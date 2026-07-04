using System.Text.Json;
using BelegOCR.Models;
using Xunit;

namespace BelegOCR.Tests.Models;

/// <summary>
/// Tests für Document.ExtractedValues.
///
/// Hintergrund: ExtractedJson wird vom OcrService als verschachteltes JSON erzeugt,
/// z.B. {"invoice_no":{"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"}}.
/// Eine frühere Version von ExtractedValues war als Dictionary&lt;string,string&gt; typisiert
/// und warf beim Deserialisieren dieser verschachtelten Struktur eine JsonException,
/// weil System.Text.Json ein JSON-Objekt nicht direkt in einen string konvertieren kann.
/// Diese Tests sichern ab, dass ExtractedValues (Dictionary&lt;string,object&gt;) damit
/// klarkommt und bei jeglichem ungültigen JSON eine leere, aber gültige Liste liefert
/// statt eine Exception zu werfen.
/// </summary>
public class Document_ExtractedValues_Tests
{
    // ── Leere / fehlende Eingabe ────────────────────────────────────────────

    [Fact]
    public void ExtractedJson_Null_GibtLeeresDictionaryZurueck()
    {
        var doc = new Document { ExtractedJson = null };

        var result = doc.ExtractedValues;

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractedJson_LeererString_GibtLeeresDictionaryZurueck()
    {
        var doc = new Document { ExtractedJson = "" };

        Assert.Empty(doc.ExtractedValues);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ExtractedJson_NurWhitespace_GibtLeeresDictionaryZurueck(string whitespace)
    {
        var doc = new Document { ExtractedJson = whitespace };

        Assert.Empty(doc.ExtractedValues);
    }

    [Fact]
    public void ExtractedJson_LeeresJsonObjekt_GibtLeeresDictionaryZurueck()
    {
        var doc = new Document { ExtractedJson = "{}" };

        Assert.Empty(doc.ExtractedValues);
    }

    // ── Regressionstest: ursprünglicher Bug ─────────────────────────────────

    [Fact]
    public void ExtractedJson_VerschachtelteOcrStruktur_WirftKeineException()
    {
        // Genau das Format, das OcrService.ProcessDocumentAsync tatsächlich erzeugt.
        const string json = """
            {
              "invoice_no": {"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"},
              "customer_no": {"value":"KUN-10023","confidence":91.7,"fieldName":"Kundennummer"}
            }
            """;
        var doc = new Document { ExtractedJson = json };

        // Darf keine JsonException werfen (das war der ursprüngliche Bug bei
        // Dictionary<string,string>, weil ein JSON-Objekt kein gültiger string ist).
        var exception = Record.Exception(() => doc.ExtractedValues);

        Assert.Null(exception);
    }

    [Fact]
    public void ExtractedJson_VerschachtelteOcrStruktur_EnthaeltAlleSchluessel()
    {
        const string json = """
            {
              "invoice_no": {"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"},
              "customer_no": {"value":"KUN-10023","confidence":91.7,"fieldName":"Kundennummer"}
            }
            """;
        var doc = new Document { ExtractedJson = json };

        var result = doc.ExtractedValues;

        Assert.Equal(2, result.Count);
        Assert.Contains("invoice_no", result.Keys);
        Assert.Contains("customer_no", result.Keys);
    }

    [Fact]
    public void ExtractedJson_VerschachtelteOcrStruktur_WertIstJsonElement()
    {
        const string json = """{"invoice_no":{"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"}}""";
        var doc = new Document { ExtractedJson = json };

        var result = doc.ExtractedValues;

        Assert.IsType<JsonElement>(result["invoice_no"]);
    }

    // ── GetField() Hilfsmethode ──────────────────────────────────────────────

    [Fact]
    public void GetField_GueltigerSchluessel_LiestValueKonfidenzUndFieldNameKorrekt()
    {
        const string json = """{"invoice_no":{"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"}}""";
        var doc = new Document { ExtractedJson = json };

        var field = doc.ExtractedValues.GetField("invoice_no");

        Assert.NotNull(field);
        Assert.Equal("RE-2024-0578", field!.Value);
        Assert.Equal(94.2, field.Confidence);
        Assert.Equal("Rechnungsnummer", field.FieldName);
    }

    [Fact]
    public void GetField_NichtVorhandenerSchluessel_GibtNullZurueck()
    {
        const string json = """{"invoice_no":{"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"}}""";
        var doc = new Document { ExtractedJson = json };

        var field = doc.ExtractedValues.GetField("nicht_vorhanden");

        Assert.Null(field);
    }

    [Fact]
    public void GetField_GroßKleinschreibungDerPropertyNamen_WirdIgnoriert()
    {
        // Großgeschriebene Property-Namen, wie sie z.B. aus einer abweichenden
        // Serialisierungsquelle kommen könnten.
        const string json = """{"invoice_no":{"Value":"RE-2024-0578","Confidence":94.2,"FieldName":"Rechnungsnummer"}}""";
        var doc = new Document { ExtractedJson = json };

        var field = doc.ExtractedValues.GetField("invoice_no");

        Assert.NotNull(field);
        Assert.Equal("RE-2024-0578", field!.Value);
    }

    [Fact]
    public void GetField_FehlendeProperties_FuelltStandardwerteAuf()
    {
        // Nur "value" vorhanden, confidence und fieldName fehlen im JSON.
        const string json = """{"invoice_no":{"value":"RE-2024-0578"}}""";
        var doc = new Document { ExtractedJson = json };

        var field = doc.ExtractedValues.GetField("invoice_no");

        Assert.NotNull(field);
        Assert.Equal("RE-2024-0578", field!.Value);
        Assert.Equal(0, field.Confidence);   // double-Default
        Assert.Null(field.FieldName);
    }

    [Fact]
    public void GetField_WertIstKeinJsonObjektSondernString_GibtNullZurueckOhneException()
    {
        // Falls ein Feld abweichend als reiner String statt als Objekt vorliegt.
        const string json = """{"invoice_no":"RE-2024-0578"}""";
        var doc = new Document { ExtractedJson = json };

        var exception = Record.Exception(() => doc.ExtractedValues.GetField("invoice_no"));

        Assert.Null(exception);
    }

    // ── Ungültiges JSON ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("{invalid json")]      // syntaktisch kaputtes JSON
    [InlineData("not even json")]      // kein JSON
    [InlineData("[1,2,3]")]            // Array statt Objekt -> nicht in Dictionary deserialisierbar
    [InlineData("\"nur ein string\"")] // einzelner String statt Objekt
    [InlineData("12345")]              // reine Zahl statt Objekt
    public void ExtractedJson_UngueltigesOderUnpassendesJson_GibtLeeresDictionaryZurueckOhneException(string invalidJson)
    {
        var doc = new Document { ExtractedJson = invalidJson };

        var exception = Record.Exception(() => doc.ExtractedValues);

        Assert.Null(exception);
        Assert.Empty(doc.ExtractedValues);
    }

    // ── Mehrfacher Zugriff / Konsistenz ──────────────────────────────────────

    [Fact]
    public void ExtractedValues_MehrfacherZugriff_LiefertJedesMalKonsistenteAnzahlFelder()
    {
        const string json = """{"a":{"value":"1"},"b":{"value":"2"},"c":{"value":"3"}}""";
        var doc = new Document { ExtractedJson = json };

        var first  = doc.ExtractedValues;
        var second = doc.ExtractedValues;

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(3, second.Count);
    }

    [Fact]
    public void ExtractedJson_NachAenderungDerProperty_SpiegeltNeuenWert()
    {
        var doc = new Document { ExtractedJson = """{"a":{"value":"1"}}""" };
        Assert.Single(doc.ExtractedValues);

        doc.ExtractedJson = """{"a":{"value":"1"},"b":{"value":"2"}}""";

        Assert.Equal(2, doc.ExtractedValues.Count);
    }
}
