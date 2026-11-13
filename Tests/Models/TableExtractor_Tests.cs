using BelegOCR.Models;
using BelegOCR.Services;
using Xunit;

namespace BelegOCR.Tests.Models;

/// <summary>
/// Tests für TableExtractor / TableHeaderAnalyzer mit synthetischen Tesseract-Wörtern.
///
/// Aufbau der Testtabelle (Template-Koordinaten):
///   Pos. 50–100 | Bezeichnung 100–350 | Menge 350–430 | Einzelpreis 430–530 | Gesamt 530–650
/// Im "Beleg" ist alles um (Dx, Dy) verschoben, damit die Header-Suche geprüft wird.
/// Schrifthöhe 14 px, Abstand zwischen Positionen 10 px, Zeilenumbruch in einer Zelle 3 px.
/// </summary>
public class TableExtractor_Tests
{
    private const int Dx = 10, Dy = 20, H = 14;

    private static TemplateFieldDef Template() => new()
    {
        FieldName = "Positionen",
        FieldKey  = "positions",
        Type      = "Table",
        X = 50, Y = 100, W = 600, H = 20,
        Table = new TableDef
        {
            HeaderTextTop    = 103,
            HeaderTextBottom = 117,
            BodyOffsetY      = 5,
            TotalFieldKey    = "total",
            Columns =
            [
                new() { HeaderText = "Pos.",        X1 = 50,  X2 = 100, TextX1 = 55,  Target = TableTargets.PosNo,       IsKey = true, ValidationPattern = TableHeaderAnalyzer.PatternPosNo },
                new() { HeaderText = "Bezeichnung", X1 = 100, X2 = 350, TextX1 = 105, Target = TableTargets.Description },
                new() { HeaderText = "Menge",       X1 = 350, X2 = 430, TextX1 = 360, Target = TableTargets.Quantity,    IsKey = true, ValidationPattern = TableHeaderAnalyzer.PatternQty },
                new() { HeaderText = "Einzelpreis", X1 = 430, X2 = 530, TextX1 = 440, Target = TableTargets.UnitPrice,   ValidationPattern = TableHeaderAnalyzer.PatternAmount },
                new() { HeaderText = "Gesamt",      X1 = 530, X2 = 650, TextX1 = 540, Target = TableTargets.Total,       IsKey = true, ValidationPattern = TableHeaderAnalyzer.PatternAmount }
            ]
        }
    };

    // Wörter einer Zeile; x in Template-Koordinaten, wird um Dx/Dy verschoben.
    private static IEnumerable<OcrWord> Line(int y, params (string Text, int X)[] words)
    {
        foreach (var (text, x) in words)
        {
            var cursor = x;
            foreach (var part in text.Split(' '))
            {
                yield return new OcrWord
                {
                    Text = part, X1 = cursor + Dx, Y1 = y + Dy, X2 = cursor + Dx + part.Length * 8, Y2 = y + Dy + H, Confidence = 90
                };
                cursor += part.Length * 8 + 6;
            }
        }
    }

    private static IEnumerable<OcrWord> Header() =>
        Line(103, ("Pos.", 55), ("Bezeichnung", 105), ("Menge", 360), ("Einzelpreis", 440), ("Gesamt", 540));

    private static IEnumerable<OcrWord> Pos(int y, string nr, string desc, string qty, string unit, string total) =>
        Line(y, (nr, 60), (desc, 110), (qty, 370), (unit, 450), (total, 550));

    private static ExtractedTable Run(IEnumerable<OcrWord> words, string? total = null) =>
        TableExtractor.Extract(Template(), CoordinateTransform.Identity, words.ToList(), 2000, total);

    // ── Grundfall: Schlüsselspalte + umgebrochene Beschreibung ─────────────

    [Fact]
    public void MehrzeiligeBeschreibung_WirdAnPositionAngehaengt()
    {
        var words = Header()
            .Concat(Pos(140, "1", "Wartungsvertrag", "2", "100,00", "200,00"))
            .Concat(Line(157, ("inkl. Anfahrt", 110)))
            .Concat(Pos(181, "2", "Server", "1", "1.500,00", "1.500,00"))
            .Concat(Line(198, ("Lizenz Standard", 110)))
            .Concat(Pos(222, "3", "Montage", "3", "50,00", "150,00"))
            .Concat(Line(250, ("Summe", 110), ("1.850,00", 550)));

        var table = Run(words, "1.850,00 €");

        Assert.True(table.HeaderFound);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Wartungsvertrag inkl. Anfahrt", table.Rows[0].Description);
        Assert.Equal("Server Lizenz Standard",        table.Rows[1].Description);
        Assert.Equal(2, table.Rows[1].LineCount);
        Assert.All(table.Rows, r => Assert.False(r.Uncertain));
        Assert.All(table.Rows, r => Assert.True(r.RowCheckOk));
        Assert.Equal(1500.00m, table.Rows[1].UnitPrice);
        Assert.Equal(1850.00m, table.SumOfTotals);
        Assert.True(table.SumCheckOk);
    }

    [Fact]
    public void HeaderVersatz_WirdAufSpaltenUebertragen()
    {
        var table = Run(Header().Concat(Pos(140, "1", "Test", "1", "1,00", "1,00")));

        Assert.Equal(50 + Dx, table.Columns[0].X1);
        Assert.Equal(650 + Dx, table.Columns[^1].X2);
    }

    [Fact]
    public void Stoppwort_BeendetRumpf_AberNichtBeiWortanfang()
    {
        var words = Header()
            .Concat(Pos(140, "1", "Gesamtpaket Wartung", "1", "10,00", "10,00"))
            .Concat(Pos(164, "2", "Kabel", "1", "5,00", "5,00"))
            .Concat(Line(192, ("Gesamt netto", 110), ("15,00", 550)))
            .Concat(Pos(216, "9", "Fußzeile", "1", "1,00", "1,00"));

        var table = Run(words);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Gesamtpaket Wartung", table.Rows[0].Description);
    }

    // ── Unsichere Fälle über gelernte Abstände ─────────────────────────────

    [Fact]
    public void ZeileOhneZahlen_MitPositionsabstand_WirdNeuePosition()
    {
        var words = Header()
            .Concat(Pos(140, "", "Wartung", "1", "10,00", "10,00"))
            .Concat(Line(157, ("und Pflege", 110)))                      // sicherer Umbruch (klein)
            .Concat(Pos(181, "", "Kabel", "2", "5,00", "10,00"))
            .Concat(Line(205, ("Hinweis Lieferung", 110)))               // Positionsabstand, keine Zahlen
            .Concat(Pos(229, "", "Stecker", "1", "2,00", "2,00"));

        var table = Run(words);

        Assert.Equal(4, table.Rows.Count);
        Assert.Equal("Wartung und Pflege", table.Rows[0].Description);
        Assert.Equal("Hinweis Lieferung",  table.Rows[2].Description);
        Assert.False(table.Rows[2].Uncertain);
        Assert.NotNull(table.Stats!.PositionGap);
        Assert.NotNull(table.Stats.LineBreakGap);
    }

    [Fact]
    public void ZeileZwischenDenAbstaenden_WirdAlsUnsicherMarkiert()
    {
        var words = Header()
            .Concat(Pos(140, "", "Wartung", "1", "10,00", "10,00"))
            .Concat(Line(157, ("und Pflege", 110)))
            .Concat(Pos(181, "", "Kabel", "2", "5,00", "10,00"))
            .Concat(Line(201, ("Zubehör Set", 110)))                     // 6 px: genau dazwischen
            .Concat(Pos(225, "", "Stecker", "1", "2,00", "2,00"));

        var table = Run(words);

        Assert.Contains(table.Rows, r => r.Uncertain);
    }

    [Fact]
    public void Silbentrennung_WirdZusammengefuegt()
    {
        var words = Header()
            .Concat(Pos(140, "1", "Netzwerkkonfigu-", "1", "80,00", "80,00"))
            .Concat(Line(157, ("ration vor Ort", 110)))
            .Concat(Pos(181, "2", "Daten-", "1", "20,00", "20,00"))
            .Concat(Line(198, ("Management", 110)))
            .Concat(Pos(222, "3", "Kabel", "1", "1,00", "1,00"));

        var table = Run(words);

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Netzwerkkonfiguration vor Ort", table.Rows[0].Description);
        Assert.Equal("Daten-Management",              table.Rows[1].Description);
    }

    // ── Pos.-Nr.-Folge ─────────────────────────────────────────────────────

    [Fact]
    public void PosNrLuecke_ErzwingtVersteckteNeuePosition()
    {
        var words = Header()
            .Concat(Pos(140, "1", "Wartung", "1", "10,00", "10,00"))
            .Concat(Line(157, ("und Pflege", 110)))
            .Concat(Pos(181, "2", "Kabel", "1", "5,00", "5,00"))
            .Concat(Line(198, ("Versand pauschal", 110)))                // Umbruchabstand, aber Pos. 3 fehlt
            .Concat(Pos(222, "4", "Stecker", "1", "2,00", "2,00"));

        var table = Run(words);

        Assert.Equal(4, table.Rows.Count);
        Assert.Equal("Versand pauschal", table.Rows[2].Description);
        Assert.True(table.Rows[2].Uncertain);
        Assert.Contains("Pos.-Nr.", table.Rows[2].UncertainReason);
    }

    [Fact]
    public void LueckenloseePosNr_ErzwingtFortsetzung()
    {
        var words = Header()
            .Concat(Pos(140, "1", "Wartung", "1", "10,00", "10,00"))
            .Concat(Line(157, ("und Pflege", 110)))
            .Concat(Pos(181, "2", "Kabel", "1", "5,00", "5,00"))
            .Concat(Line(205, ("Farbe Schwarz", 110)))                   // Positionsabstand, aber 2 → 3
            .Concat(Pos(229, "3", "Stecker", "1", "2,00", "2,00"));

        var table = Run(words);

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Kabel Farbe Schwarz", table.Rows[1].Description);
        Assert.False(table.Rows[1].Uncertain);
    }

    // ── Kontrolle ──────────────────────────────────────────────────────────

    [Fact]
    public void FalscheZeilensumme_UndAbweichenderGesamtbetrag_WerdenErkannt()
    {
        var words = Header()
            .Concat(Pos(140, "1", "Wartung", "2", "10,00", "25,00"))
            .Concat(Pos(164, "2", "Kabel", "1", "5,00", "5,00"));

        var table = Run(words, "99,00");

        Assert.False(table.Rows[0].RowCheckOk);
        Assert.True(table.Rows[1].RowCheckOk);
        Assert.False(table.SumCheckOk);
        Assert.Contains(table.Warnings, w => w.Contains("weicht"));
    }

    // ── Hilfsfunktionen ────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.234,56 €", 1234.56)]
    [InlineData("12,50",      12.50)]
    [InlineData("1,234.56",   1234.56)]
    [InlineData("12.50",      12.50)]
    [InlineData("1.000",      1000)]
    [InlineData("-3,00",      -3.00)]
    [InlineData("3,00-",      -3.00)]
    [InlineData("2 Stk",      2)]
    [InlineData("€ 99,90",    99.90)]
    public void ParseAmount_ErkenntFormate(string text, double expected) =>
        Assert.Equal((decimal)expected, TableExtractor.ParseAmount(text));

    [Fact]
    public void ParseAmount_OhneZahl_GibtNull() =>
        Assert.Null(TableExtractor.ParseAmount("Pauschal"));

    [Fact]
    public void HeaderAnalyzer_FasstMehrwortigeUndZweizeiligeUeberschriftenZusammen()
    {
        static OcrWord W(string t, int x, int y) =>
            new() { Text = t, X1 = x, Y1 = y, X2 = x + t.Length * 8, Y2 = y + H };

        var words = new List<OcrWord>
        {
            W("Pos.", 55, 103),
            W("Artikel", 105, 103), W("Bezeichnung", 167, 103),   // 6 px Wortabstand → eine Spalte
            W("Menge", 360, 103),
            W("Einzel-", 440, 103), W("preis", 440, 119),         // zweizeilig
            W("Gesamt", 540, 103)
        };

        var def = TableHeaderAnalyzer.BuildColumns(words, 50, 100, 600, 36);

        Assert.Equal(new[] { "Pos.", "Artikel Bezeichnung", "Menge", "Einzelpreis", "Gesamt" },
                     def.Columns.Select(c => c.HeaderText));
        Assert.Equal(50,  def.Columns[0].X1);
        Assert.Equal(650, def.Columns[^1].X2);
        // Grenze = Mitte der Lücke: "Pos." endet bei 87, "Artikel" beginnt bei 105
        Assert.Equal(96, def.Columns[0].X2);
        Assert.Equal(def.Columns[0].X2, def.Columns[1].X1);
        Assert.Equal(TableTargets.Description, def.Columns[1].Target);
        Assert.Equal(TableTargets.UnitPrice,   def.Columns[3].Target);
        Assert.True(def.Columns[4].IsKey);
    }
}
