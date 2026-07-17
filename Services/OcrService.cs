using Tesseract;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.Fonts;
using BelegOCR.Models;
using System.Text.Json;
using BelegOCR.Services.Interfaces;

namespace BelegOCR.Services;

// public interface IOcrService
// {
//     /// <summary>Liest einen bestimmten Bereich aus einem Bild per OCR aus.</summary>
//     Task<(string Text, float Confidence)> ExtractRegionAsync(string imagePath, int x, int y, int w, int h);
//
//     /// <summary>Verarbeitet ein Dokument anhand eines Templates und gibt alle Felder als JSON zurück.</summary>
//     Task<string> ProcessDocumentAsync(string imagePath, List<TemplateFieldDef> fields);
//
//     /// <summary>
//     /// Verarbeitet ein Dokument mit Koordinaten-Korrektur über zwei Ankerpunkte:
//     /// sucht zuerst die Anker im Beleg, berechnet daraus Skalierung/Versatz
//     /// gegenüber dem Template und wendet diese Korrektur auf alle Feld-Koordinaten
//     /// an, bevor die eigentliche Extraktion läuft. Ohne gefundene Anker (oder
//     /// wenn anchors == null) entspricht das Verhalten ProcessDocumentAsync ohne Korrektur.
//     /// </summary>
//     Task<string> ProcessDocumentAsync(string imagePath, List<TemplateFieldDef> fields, TemplateAnchors? anchors);
//
//     /// <summary>
//     /// Sucht den erwarteten Anker-Text in einer großzügigen Suchbox um die
//     /// Template-Position. Gibt die tatsächlich gefundene Position (linke obere
//     /// Ecke der Trefferregion) zurück, oder null, wenn der Text nicht gefunden wurde.
//     /// </summary>
//     Task<(int X, int Y)?> FindAnchorAsync(string imagePath, AnchorPointDef anchor);
//
//     /// <summary>Erstellt ein annotiertes Bild mit eingezeichneten Feldern (für Vergleichsansicht).</summary>
//     Task<string> CreateAnnotatedImageAsync(string imagePath, List<TemplateFieldDef> fields,
//                                            Dictionary<string, object> extractedValues, string outputDir);
// }

public class OcrService(IWebHostEnvironment env, ILogger<OcrService> logger) : IOcrService
{
    // Tesseract-Daten liegen unter wwwroot/tessdata  (deu.traineddata + eng.traineddata)
    private readonly string _tessDataPath = Path.Combine(env.WebRootPath, "tessdata");

    public async Task<(string Text, float Confidence)> ExtractRegionAsync(
        string imagePath, int x, int y, int w, int h)
    {
        return await Task.Run(() =>
        {
            // Häufigste Ursache für "Wert bleibt leer": tessdata-Ordner fehlt oder
            // enthält keine .traineddata-Dateien. Das wurde bisher von der catch-Klausel
            // unten stillschweigend verschluckt und nur ins Server-Log geschrieben.
            if (!Directory.Exists(_tessDataPath) ||
                !Directory.EnumerateFiles(_tessDataPath, "*.traineddata").Any())
            {
                logger.LogError(
                    "Tesseract-Sprachdaten fehlen unter '{Path}'. " +
                    "deu.traineddata (und optional eng.traineddata) müssen dort liegen, " +
                    "siehe https://github.com/tesseract-ocr/tessdata", _tessDataPath);
                throw new InvalidOperationException(
                    $"OCR-Sprachdaten nicht gefunden unter '{_tessDataPath}'. " +
                    "Bitte deu.traineddata in wwwroot/tessdata ablegen.");
            }

            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Belegbild nicht gefunden.", imagePath);

            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "deu+eng", EngineMode.Default);
                using var img    = Pix.LoadFromFile(imagePath);

                // Region darf nicht über die Bildgrenzen hinausragen, sonst liefert
                // Tesseract häufig kommentarlos einen leeren Text zurück.
                var clampedW = Math.Min(w, img.Width  - x);
                var clampedH = Math.Min(h, img.Height - y);

                if (x < 0 || y < 0 || clampedW <= 0 || clampedH <= 0)
                {
                    logger.LogWarning(
                        "Region ({X},{Y},{W},{H}) liegt außerhalb des Bildes ({ImgW}x{ImgH}) – " +
                        "Feld liefert leeren Wert. Template-Koordinaten prüfen.",
                        x, y, w, h, img.Width, img.Height);
                    return ("", 0f);
                }

                var rect = new Rect(x, y, clampedW, clampedH);
                using var page = engine.Process(img, rect);

                var text = page.GetText()?.Trim() ?? "";
                var conf = page.GetMeanConfidence();

                if (string.IsNullOrWhiteSpace(text))
                    logger.LogWarning(
                        "OCR lieferte leeren Text für Region ({X},{Y},{W},{H}). " +
                        "Mögliche Ursachen: Region trifft keinen Text, Bildqualität zu niedrig, " +
                        "oder falsche Koordinaten im Template.", x, y, w, h);

                return (text, conf);
            }
            catch (Exception ex) when (ex is not InvalidOperationException and not FileNotFoundException)
            {
                logger.LogError(ex, "OCR fehlgeschlagen für Region ({X},{Y},{W},{H})", x, y, w, h);
                return ("", 0f);
            }
        });
    }

    /// <summary>
    /// Sucht den erwarteten Anker-Text in einer Suchbox um die Template-Position.
    /// Die Suchbox ist deutlich größer als die ursprüngliche Anker-Markierung
    /// (siehe SearchMargin), damit auch ein verschobener Beleg den Anker noch
    /// im Suchbereich hat. Nutzt Tesseract's Zeilen-Iterator, um die tatsächliche
    /// Position der gefundenen Textzeile zu ermitteln statt nur deren Inhalt.
    /// </summary>
    public async Task<(int X, int Y)?> FindAnchorAsync(string imagePath, AnchorPointDef anchor)
    {
        return await Task.Run(() =>
        {
            const int searchMargin = 150; // px in jede Richtung um die Template-Position

            if (!Directory.Exists(_tessDataPath) ||
                !Directory.EnumerateFiles(_tessDataPath, "*.traineddata").Any())
            {
                logger.LogError("Tesseract-Sprachdaten fehlen unter '{Path}' – Anker-Suche nicht möglich.", _tessDataPath);
                return null;
            }

            if (!File.Exists(imagePath))
            {
                logger.LogError("Belegbild nicht gefunden: {Path}", imagePath);
                return null;
            }

            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "deu+eng", EngineMode.Default);
                using var img    = Pix.LoadFromFile(imagePath);

                var searchX = Math.Max(0, anchor.X - searchMargin);
                var searchY = Math.Max(0, anchor.Y - searchMargin);
                var searchW = Math.Min(anchor.W + 2 * searchMargin, img.Width  - searchX);
                var searchH = Math.Min(anchor.H + 2 * searchMargin, img.Height - searchY);

                if (searchW <= 0 || searchH <= 0)
                {
                    logger.LogWarning("Suchbereich für Anker '{Text}' liegt außerhalb des Bildes.", anchor.ExpectedText);
                    return null;
                }

                var searchRect = new Rect(searchX, searchY, searchW, searchH);
                using var page = engine.Process(img, searchRect, PageSegMode.SparseText);
                using var iter = page.GetIterator();

                var normalizedExpected = NormalizeForComparison(anchor.ExpectedText);
                if (string.IsNullOrEmpty(normalizedExpected))
                    return null;

                iter.Begin();
                do
                {
                    var lineText = iter.GetText(PageIteratorLevel.TextLine);
                    if (string.IsNullOrWhiteSpace(lineText))
                        continue;

                    var normalizedLine = NormalizeForComparison(lineText);

                    // Enthält statt exakter Gleichheit, da Tesseract Zeilenumbrüche
                    // und Nachbartext im SparseText-Modus mit ins Ergebnis ziehen kann.
                    if (normalizedLine.Contains(normalizedExpected))
                    {
                        if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                        {
                            logger.LogInformation(
                                "Anker '{Text}' gefunden bei ({X},{Y}) statt Template-Position ({TplX},{TplY}).",
                                anchor.ExpectedText, rect.X1, rect.Y1, anchor.X, anchor.Y);
                            return ((int X, int Y)?)(rect.X1, rect.Y1);
                        }
                    }
                } while (iter.Next(PageIteratorLevel.TextLine));

                logger.LogWarning(
                    "Anker-Text '{Text}' nicht im Suchbereich um ({X},{Y}) gefunden – " +
                    "Koordinaten-Korrektur entfällt für diesen Anker.",
                    anchor.ExpectedText, anchor.X, anchor.Y);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fehler bei der Anker-Suche für '{Text}'", anchor.ExpectedText);
                return null;
            }
        });
    }

    /// <summary>
    /// Normalisiert Text für den Anker-Vergleich: Groß-/Kleinschreibung und
    /// mehrfache Leerzeichen ignorieren, da OCR-Erkennung hier leicht abweichen
    /// kann, ohne dass es sich um einen falschen Treffer handelt.
    /// </summary>
    private static string NormalizeForComparison(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");

    public async Task<string> ProcessDocumentAsync(string imagePath, List<TemplateFieldDef> fields) =>
        await ProcessDocumentAsync(imagePath, fields, anchors: null);

    public async Task<string> ProcessDocumentAsync(string imagePath, List<TemplateFieldDef> fields, TemplateAnchors? anchors)
    {
        var transform = await ResolveTransformAsync(imagePath, anchors);
        var result = new Dictionary<string, object>();

        foreach (var field in fields)
        {
            try
            {
                // Template-Koordinaten erst per Transformation an die tatsächliche
                // Lage im hochgeladenen Beleg anpassen (Identity, falls keine
                // Anker hinterlegt sind oder sie nicht gefunden wurden).
                var (tx, ty, tw, th) = transform.Apply(field.X, field.Y, field.W, field.H);

                var (text, confidence) = await ExtractRegionAsync(imagePath, tx, ty, tw, th);
                var isValid = ValidateAgainstPattern(text, field.ValidationPattern, field.FieldKey);

                result[field.FieldKey] = new
                {
                    value      = text,
                    confidence = Math.Round(confidence * 100, 1),
                    fieldName  = field.FieldName,
                    isValid    = isValid
                };
            }
            catch (Exception ex)
            {
                // Fehler bei einem einzelnen Feld (z.B. Tessdata fehlt) soll nicht dazu führen,
                // dass alle anderen Felder ebenfalls leer bleiben bzw. die ganze Verarbeitung abbricht.
                logger.LogError(ex, "Feld '{FieldKey}' konnte nicht extrahiert werden", field.FieldKey);
                result[field.FieldKey] = new
                {
                    value      = (string?)null,
                    confidence = 0,
                    fieldName  = field.FieldName,
                    error      = ex.Message,
                    isValid    = (bool?)null
                };
            }
        }

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Sucht beide Template-Anker im hochgeladenen Beleg und berechnet daraus
    /// die Koordinaten-Korrektur. Gibt CoordinateTransform.Identity zurück
    /// (= keine Korrektur), wenn keine Anker hinterlegt sind oder einer der
    /// beiden nicht gefunden werden konnte – in dem Fall wird wie bisher mit
    /// den unveränderten Template-Koordinaten extrahiert.
    /// </summary>
    private async Task<CoordinateTransform> ResolveTransformAsync(string imagePath, TemplateAnchors? anchors)
    {
        if (anchors?.TopLeft == null || anchors.BottomRight == null)
            return CoordinateTransform.Identity;

        var topLeftFound     = await FindAnchorAsync(imagePath, anchors.TopLeft);
        var bottomRightFound = await FindAnchorAsync(imagePath, anchors.BottomRight);

        if (topLeftFound == null || bottomRightFound == null)
        {
            logger.LogWarning(
                "Koordinaten-Korrektur übersprungen: TopLeft {TopLeftFound}, BottomRight {BottomRightFound} gefunden.",
                topLeftFound != null ? "gefunden" : "NICHT gefunden",
                bottomRightFound != null ? "gefunden" : "NICHT gefunden");
            return CoordinateTransform.Identity;
        }

        var transform = AnchorTransformCalculator.Calculate(
            anchors.TopLeft,     topLeftFound.Value.X,     topLeftFound.Value.Y,
            anchors.BottomRight, bottomRightFound.Value.X, bottomRightFound.Value.Y);

        logger.LogInformation(
            "Koordinaten-Korrektur angewendet: ScaleX={ScaleX:F3} ScaleY={ScaleY:F3} OffsetX={OffsetX:F1} OffsetY={OffsetY:F1}",
            transform.ScaleX, transform.ScaleY, transform.OffsetX, transform.OffsetY);

        return transform;
    }

    /// <summary>
    /// Prüft den erkannten Text gegen das optionale ValidationPattern des Felds.
    /// Delegiert an FieldValueValidator (eigene Klasse, damit die Logik ohne
    /// Tesseract/Dateisystem-Abhängigkeit unittestbar ist).
    /// </summary>
    private bool? ValidateAgainstPattern(string text, string? pattern, string fieldKey) =>
        FieldValueValidator.Validate(text, pattern, fieldKey, logger);

    public async Task<string> CreateAnnotatedImageAsync(
        string imagePath,
        List<TemplateFieldDef> fields,
        Dictionary<string, object> extractedValues,
        string outputDir)
    {
        return await Task.Run(() =>
        {
            using var image = SixLabors.ImageSharp.Image.Load(imagePath);

            // Schriftart für die Wert-Beschriftung im Bild.
            // FirstOrDefault() statt fixem Namen, da auf schlanken Server/Container-Images
            // ggf. keine bestimmte Schriftart (z.B. "Arial") existiert.
            var hasFontFamily = SystemFonts.Families.Any();
            Font? font = hasFontFamily
                ? SystemFonts.Families.First().CreateFont(12, FontStyle.Bold)
                : null;

            if (!hasFontFamily)
                logger.LogWarning("Keine Systemschriftart gefunden – extrahierte Werte werden im annotierten Bild nicht als Text angezeigt.");

            image.Mutate(ctx =>
            {
                foreach (var field in fields)
                {
                    var rect  = new RectangleF(field.X, field.Y, field.W, field.H);
                    var color = SixLabors.ImageSharp.Color.FromRgba(0, 120, 215, 80);
                    var border= SixLabors.ImageSharp.Color.FromRgb(0, 120, 215);

                    // Halbtransparente Fläche + Rahmen
                    ctx.Fill(color, rect);
                    ctx.Draw(border, 2f, rect);

                    // Extrahierten Wert (falls vorhanden) unterhalb des Rahmens einblenden
                    var extracted = extractedValues.GetField(field.FieldKey);
                    if (font != null && extracted != null && !string.IsNullOrWhiteSpace(extracted.Value))
                    {
                        var label = $"{field.FieldName}: {extracted.Value}";
                        var labelPos = new PointF(field.X, field.Y + field.H + 2);
                        ctx.DrawText(label, font, SixLabors.ImageSharp.Color.FromRgb(0, 90, 160), labelPos);
                    }
                }
            });

            var fileName   = $"annotated_{Path.GetFileNameWithoutExtension(imagePath)}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            var outputPath = Path.Combine(outputDir, fileName);
            image.Save(outputPath, new PngEncoder());

            return fileName;
        });
    }
}

/// <summary>
/// Validiert einen extrahierten OCR-Text gegen ein optionales Regex-Muster.
/// Eigenständige, von Tesseract/Dateisystem unabhängige Klasse, damit die
/// Validierungslogik isoliert unittestbar ist (siehe FieldValueValidator_Tests).
/// </summary>
public static class FieldValueValidator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gibt null zurück, wenn kein Pattern hinterlegt ist (= keine Prüfung gewünscht),
    /// true/false je nach Übereinstimmung, oder null bei ungültigem/zu komplexem Pattern
    /// (Timeout-Schutz gegen ReDoS durch ein vom User eingegebenes Regex).
    /// </summary>
    public static bool? Validate(string text, string? pattern, string fieldKey, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                text ?? "", pattern,
                System.Text.RegularExpressions.RegexOptions.None,
                RegexTimeout);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            logger?.LogWarning("ValidationPattern für Feld '{FieldKey}' hat Timeout ausgelöst – Muster vermutlich zu komplex.", fieldKey);
            return null;
        }
        catch (ArgumentException ex)
        {
            // Ungültiges Regex-Muster (z.B. unausgeglichene Klammern) soll die
            // Extraktion nicht zum Absturz bringen, nur die Validierung entfällt.
            logger?.LogWarning(ex, "Ungültiges ValidationPattern für Feld '{FieldKey}': '{Pattern}'", fieldKey, pattern);
            return null;
        }
    }
}
