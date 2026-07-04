using System.Text.Json;
using System.Text.Json.Serialization;

namespace BelegOCR.Models;

// ── Datenbank-Entitäten ──────────────────────────────────────────────────────

public class DocumentTemplate
{
    public int      Id              { get; set; }
    public string   Name            { get; set; } = "";
    public string?  Description     { get; set; }
    public string   SampleImagePath { get; set; } = "";
    public string   FieldsJson      { get; set; } = "[]";   // JSON-Spalte
    public string   AnchorsJson     { get; set; } = "{}";   // JSON-Spalte: TemplateAnchors (TopLeft/BottomRight)
    public DateTime CreatedAt       { get; set; }
    public DateTime UpdatedAt       { get; set; }

    // Deserialisierter Zugriff – wird NICHT in DB gespeichert
    [JsonIgnore]
    public List<TemplateFieldDef> Fields =>
        JsonSerializer.Deserialize<List<TemplateFieldDef>>(FieldsJson) ?? [];

    [JsonIgnore]
    public TemplateAnchors Anchors =>
        JsonSerializer.Deserialize<TemplateAnchors>(AnchorsJson) ?? new();
}

/// <summary>
/// Die beiden Referenzpunkte des Templates: ein fester, garantiert wiederkehrender
/// Text oben-links und einer unten-rechts im Beleg (z.B. Firmenname im Briefkopf
/// und "Vielen Dank..."-Zeile am Fußende). Analog zu den zwei Eckpunkten, mit
/// denen z.B. iText ein Koordinatenrechteck auf einer PDF-Seite aufspannt.
///
/// Aus dem Vergleich "wo stehen diese beiden Texte im Template" vs.
/// "wo stehen sie im neu hochgeladenen Beleg" wird eine affine Korrektur
/// (Skalierung + Verschiebung) berechnet und auf alle Werte-Felder angewendet,
/// bevor die eigentliche Extraktion läuft. Ohne Anker (TopLeft/BottomRight = null)
/// wird wie bisher ohne Korrektur extrahiert.
/// </summary>
public class TemplateAnchors
{
    public AnchorPointDef? TopLeft     { get; set; }
    public AnchorPointDef? BottomRight { get; set; }
}

public class AnchorPointDef
{
    public string ExpectedText { get; set; } = "";   // z.B. "TechSolutions GmbH" oder "Vielen Dank für Ihr Vertrauen!"
    public int    X            { get; set; }
    public int    Y            { get; set; }
    public int    W            { get; set; }
    public int    H            { get; set; }
}

public class TemplateFieldDef
{
    public string  FieldName        { get; set; } = "";   // Anzeigename z.B. "Rechnungsnummer"
    public string  FieldKey         { get; set; } = "";   // Schlüssel  z.B. "invoice_no"
    public int     X                { get; set; }
    public int     Y                { get; set; }
    public int     W                { get; set; }
    public int     H                { get; set; }
    public string  Type             { get; set; } = "Text"; // Text | Number | Date | Table

    // Optionales Regex-Muster zur Plausibilitätsprüfung des erkannten Werts,
    // z.B. "^RE-\d{4}-\d{4}$" für Rechnungsnummern. Leer = keine Validierung.
    // Dient NICHT der Extraktion selbst, sondern nur als Sicherheitsnetz: wenn
    // Tesseract z.B. versehentlich das Label statt des Werts erkennt
    // ("Rechnungsnummer:" statt "RE-2024-0578"), schlägt der Pattern-Check fehl
    // und das Feld wird im UI deutlich als "unplausibel" markiert statt
    // unbemerkt einen falschen Wert in die Datenbank zu schreiben.
    public string? ValidationPattern { get; set; }
}

public class Document
{
    public int      Id               { get; set; }
    public int?     TemplateId       { get; set; }
    public string   OriginalFileName { get; set; } = "";
    public string   FilePath         { get; set; } = "";
    public string   Status           { get; set; } = "Pending"; // Pending | Processed | Error
    public string?  ExtractedJson    { get; set; }              // JSON-Spalte
    public DateTime? ProcessedAt     { get; set; }
    public DateTime CreatedAt        { get; set; }
    public string?  ErrorMessage     { get; set; }

    // Deserialisierter Zugriff – wird NICHT in DB gespeichert.
    // ExtractedJson sieht so aus:
    // {"invoice_no":{"value":"RE-2024-0578","confidence":94.2,"fieldName":"Rechnungsnummer"}, ...}
    // Dictionary<string, object> statt eines festen Typs, damit auch abweichende
    // oder zukünftig erweiterte JSON-Strukturen ohne JsonException deserialisiert werden.
    // Werte kommen als System.Text.Json.JsonElement zurück.
    [JsonIgnore]
    public Dictionary<string, object> ExtractedValues
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExtractedJson))
                return [];

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(
                    ExtractedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (JsonException)
            {
                // Ungültiges JSON führt nicht zu einer Exception in der View,
                // sondern zu einer leeren, aber gültigen Liste.
                return [];
            }
        }
    }
}

/// <summary>
/// Einzelner extrahierter Feldwert inkl. OCR-Konfidenz, wie er vom OcrService
/// pro Feld in ExtractedJson abgelegt wird. Wird genutzt, um ein JsonElement
/// aus ExtractedValues typsicher auszulesen (siehe ExtractedFieldValueExtensions).
/// </summary>
public class ExtractedFieldValue
{
    public string? Value      { get; set; }
    public double  Confidence { get; set; }
    public string? FieldName  { get; set; }
    public string? Error      { get; set; }

    // null  = kein ValidationPattern im Template hinterlegt (keine Prüfung durchgeführt)
    // true  = Wert entspricht dem ValidationPattern
    // false = Wert entspricht NICHT dem Muster -> wahrscheinlich falsches Feld erkannt
    //         (z.B. Label statt Wert, oder OCR hat Nachbarbereich erfasst)
    public bool?   IsValid    { get; set; }
}

/// <summary>
/// Berechnete affine Korrektur (Skalierung + Verschiebung, ohne Rotation),
/// die aus zwei gefundenen Ankerpunkten ermittelt wird und auf alle
/// Feld-Koordinaten angewendet wird, bevor die Werte-Extraktion läuft.
/// </summary>
public class CoordinateTransform
{
    public double ScaleX  { get; set; } = 1.0;
    public double ScaleY  { get; set; } = 1.0;
    public double OffsetX { get; set; } = 0.0;
    public double OffsetY { get; set; } = 0.0;

    /// <summary>Wendet die Transformation auf eine Template-Koordinate an.</summary>
    public (int X, int Y, int W, int H) Apply(int x, int y, int w, int h) => (
        X: (int)Math.Round(x * ScaleX + OffsetX),
        Y: (int)Math.Round(y * ScaleY + OffsetY),
        W: (int)Math.Round(w * ScaleX),
        H: (int)Math.Round(h * ScaleY)
    );

    public static readonly CoordinateTransform Identity = new();
}

/// <summary>
/// Hilfsmethoden, um aus Document.ExtractedValues (Dictionary&lt;string, object&gt;,
/// Werte als JsonElement) typsicher ein ExtractedFieldValue zu lesen, ohne dass
/// Aufrufer JsonElement-Handling duplizieren müssen.
/// </summary>
public static class ExtractedValuesExtensions
{
    public static ExtractedFieldValue? GetField(this Dictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var raw))
            return null;

        if (raw is JsonElement element)
        {
            try
            {
                return JsonSerializer.Deserialize<ExtractedFieldValue>(
                    element.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return raw as ExtractedFieldValue;
    }
}

// ── ViewModels ───────────────────────────────────────────────────────────────

public class TemplateEditorViewModel
{
    public DocumentTemplate Template   { get; set; } = new();
    public string           ImageUrl   { get; set; } = "";
    public int              ImageWidth { get; set; }
    public int              ImageHeight{ get; set; }
}

public class DocumentCompareViewModel
{
    public Document          Document         { get; set; } = new();
    public DocumentTemplate? Template         { get; set; }
    public string            OriginalImageUrl { get; set; } = "";
    public string            AnnotatedImageUrl{ get; set; } = "";
    // Felder mit Original-Koordinaten für die Overlay-Ansicht
    public List<TemplateFieldDef> Fields      { get; set; } = [];
}

public class DocumentListViewModel
{
    public List<Document>         Documents  { get; set; } = [];
    public List<DocumentTemplate> Templates  { get; set; } = [];
}
