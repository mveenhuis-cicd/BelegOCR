using BelegOCR.Models;

namespace BelegOCR.Services;

/// <summary>
/// Berechnet aus zwei Ankerpunkten (oben-links, unten-rechts) eine affine
/// Korrektur (Skalierung + Verschiebung), die Template-Koordinaten auf die
/// tatsächliche Position im neu hochgeladenen Beleg abbildet.
///
/// Prinzip analog zu zwei Eckpunkten, die ein Koordinatenrechteck aufspannen
/// (wie bei PDF-Bibliotheken üblich): die Distanz zwischen den beiden Ankern
/// im Template wird mit der Distanz zwischen denselben beiden Ankern im
/// tatsächlichen Beleg verglichen. Daraus ergeben sich Skalierungsfaktor
/// (Beleg evtl. in anderer Auflösung gescannt) und Versatz (Beleg evtl.
/// verschoben/anders zugeschnitten).
///
/// Eigenständige Klasse ohne Tesseract-/Dateisystem-Abhängigkeit, damit die
/// reine Rechenlogik isoliert unittestbar ist.
/// </summary>
public static class AnchorTransformCalculator
{
    /// <summary>
    /// Rückfall, wenn nur einer der beiden Anker im Beleg gefunden wurde (z.B.
    /// Beleg oben/unten beschnitten, Fußzeile fehlt): reine Verschiebung ohne
    /// Skalierung, abgeleitet aus dem einen gefundenen Anker. Deckt den häufigen
    /// Fall "Beleg verschoben/zugeschnitten, aber gleiche Auflösung" ab.
    /// </summary>
    public static CoordinateTransform CalculateOffsetOnly(AnchorPointDef template, int foundX, int foundY) =>
        new()
        {
            OffsetX = foundX - template.X,
            OffsetY = foundY - template.Y
        };

    /// <summary>
    /// Berechnet die Korrektur aus den Template-Ankerpositionen und den im
    /// aktuellen Beleg tatsächlich gefundenen Positionen derselben zwei Texte.
    /// Gibt CoordinateTransform.Identity zurück (keine Korrektur), wenn nicht
    /// genug Informationen vorhanden sind oder die Anker in beiden Richtungen
    /// zu nah beieinander liegen, um eine sinnvolle Skalierung zu berechnen.
    /// </summary>
    public static CoordinateTransform Calculate(
        AnchorPointDef templateTopLeft,     int foundTopLeftX,     int foundTopLeftY,
        AnchorPointDef templateBottomRight, int foundBottomRightX, int foundBottomRightY)
    {
        // Referenzpunkt jeweils die linke obere Ecke der Anker-Box selbst.
        double templateDx = templateBottomRight.X - templateTopLeft.X;
        double templateDy = templateBottomRight.Y - templateTopLeft.Y;
        double foundDx     = foundBottomRightX - foundTopLeftX;
        double foundDy     = foundBottomRightY - foundTopLeftY;

        // Mindestabstand, unterhalb dessen eine Skalierungsberechnung numerisch
        // unzuverlässig würde (Division durch sehr kleine Zahl).
        const double minDistance = 20.0;

        bool xUsable = Math.Abs(templateDx) >= minDistance;
        bool yUsable = Math.Abs(templateDy) >= minDistance;

        if (!xUsable && !yUsable)
            return CoordinateTransform.Identity;

        // Liegen die Anker z.B. linksbündig untereinander, ist der X-Abstand zu
        // klein. Dann die Skalierung der anderen Achse übernehmen – beim Zoomen
        // eines Belegs skalieren beide Achsen gleich.
        double scaleX = xUsable ? foundDx / templateDx : foundDy / templateDy;
        double scaleY = yUsable ? foundDy / templateDy : scaleX;

        // Plausibilitätsgrenze: Faktoren außerhalb [0.4, 2.5] gelten als falsch
        // zugeordnetes Template bzw. falscher Anker-Treffer, nicht als normale
        // Scan-/Zoom-Abweichung. Negative Werte (gespiegelt) ebenso.
        const double minScale = 0.4;
        const double maxScale = 2.5;
        if (scaleX < minScale || scaleX > maxScale ||
            scaleY < minScale || scaleY > maxScale)
            return CoordinateTransform.Identity;

        // Bei reinem Zoom sind ScaleX und ScaleY fast gleich. Weichen sie mehr
        // als 5 % ab, wurde vermutlich ein Anker falsch zugeordnet.
        const double maxAxisDeviation = 0.05;
        bool uncertain = Math.Abs(scaleX - scaleY) / Math.Max(scaleX, scaleY) > maxAxisDeviation;

        // Plausibler Zoom: beide Achsen auf denselben Faktor setzen, damit Felder
        // exakt proportional mitschrumpfen/-wachsen und kleine OCR-Ungenauigkeiten
        // an den Anker-Boxen die Feld-Boxen nicht verzerren. Bei großer Abweichung
        // (unsicher) bleiben die getrennten Werte stehen, damit sie im Log sichtbar sind.
        if (!uncertain)
            scaleX = scaleY = (scaleX + scaleY) / 2;

        // Versatz: wo der TopLeft-Anker im Beleg tatsächlich liegt, verglichen
        // mit wo er (skaliert) im Template läge.
        double offsetX = foundTopLeftX - templateTopLeft.X * scaleX;
        double offsetY = foundTopLeftY - templateTopLeft.Y * scaleY;

        return new CoordinateTransform
        {
            ScaleX  = scaleX,
            ScaleY  = scaleY,
            OffsetX = offsetX,
            OffsetY     = offsetY,
            IsUncertain = uncertain
        };
    }
}
