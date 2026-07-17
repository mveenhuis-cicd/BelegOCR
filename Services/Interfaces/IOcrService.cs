using BelegOCR.Models;

namespace BelegOCR.Services.Interfaces;

public interface IOcrService
{
    /// <summary>Liest einen bestimmten Bereich aus einem Bild per OCR aus.</summary>
    Task<(string Text, float Confidence)> ExtractRegionAsync(string imagePath, int x, int y, int w, int h);

    /// <summary>Verarbeitet ein Dokument anhand eines Templates und gibt alle Felder als JSON zurück.</summary>
    Task<string> ProcessDocumentAsync(string imagePath, List<TemplateFieldDef> fields);

    /// <summary>
    /// Verarbeitet ein Dokument mit Koordinaten-Korrektur über zwei Ankerpunkte:
    /// sucht zuerst die Anker im Beleg, berechnet daraus Skalierung/Versatz
    /// gegenüber dem Template und wendet diese Korrektur auf alle Feld-Koordinaten
    /// an, bevor die eigentliche Extraktion läuft. Ohne gefundene Anker (oder
    /// wenn anchors == null) entspricht das Verhalten ProcessDocumentAsync ohne Korrektur.
    /// </summary>
    Task<string> ProcessDocumentAsync(string imagePath, List<TemplateFieldDef> fields, TemplateAnchors? anchors);

    /// <summary>
    /// Sucht den erwarteten Anker-Text in einer großzügigen Suchbox um die
    /// Template-Position. Gibt die tatsächlich gefundene Position (linke obere
    /// Ecke der Trefferregion) zurück, oder null, wenn der Text nicht gefunden wurde.
    /// </summary>
    Task<(int X, int Y)?> FindAnchorAsync(string imagePath, AnchorPointDef anchor);

    /// <summary>Erstellt ein annotiertes Bild mit eingezeichneten Feldern (für Vergleichsansicht).</summary>
    Task<string> CreateAnnotatedImageAsync(string imagePath, List<TemplateFieldDef> fields,
        Dictionary<string, object> extractedValues, string outputDir);
}
