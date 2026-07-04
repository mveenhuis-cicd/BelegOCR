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
    /// Berechnet die Korrektur aus den Template-Ankerpositionen und den im
    /// aktuellen Beleg tatsächlich gefundenen Positionen derselben zwei Texte.
    /// Gibt CoordinateTransform.Identity zurück (keine Korrektur), wenn nicht
    /// genug Informationen vorhanden sind oder die Anker zu nah beieinander
    /// liegen, um eine sinnvolle Skalierung zu berechnen.
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
        // unzuverlässig würde (Division durch sehr kleine Zahl). 20px ist großzügig
        // gewählt, da Anker typischerweise weit auseinander liegen (Kopf vs. Fuß).
        const double minDistance = 20.0;

        if (Math.Abs(templateDx) < minDistance || Math.Abs(templateDy) < minDistance)
            return CoordinateTransform.Identity;

        double scaleX = foundDx / templateDx;
        double scaleY = foundDy / templateDy;

        // Plausibilitätsgrenze: eine Skalierung außerhalb [1-deviation, 1+deviation]
        // gilt als unplausibel (z.B. falsch zugeordnetes Template), nicht als
        // normale Scan-Abweichung. deviation=1.5 erlaubt bis Faktor 2.5 (z.B. ein
        // deutlich höher aufgelöster Scan), lehnt aber z.B. Faktor 4+ ab. Negative
        // oder Null-Skalierung (gespiegelt/entartet) wird in jedem Fall verworfen.
        const double maxScaleDeviation = 1.5;
        if (scaleX <= 0 || scaleY <= 0 ||
            scaleX < (1 - maxScaleDeviation) || scaleX > (1 + maxScaleDeviation) ||
            scaleY < (1 - maxScaleDeviation) || scaleY > (1 + maxScaleDeviation))
            return CoordinateTransform.Identity;

        // Versatz: wo der TopLeft-Anker im Beleg tatsächlich liegt, verglichen
        // mit wo er (skaliert) im Template läge.
        double offsetX = foundTopLeftX - templateTopLeft.X * scaleX;
        double offsetY = foundTopLeftY - templateTopLeft.Y * scaleY;

        return new CoordinateTransform
        {
            ScaleX  = scaleX,
            ScaleY  = scaleY,
            OffsetX = offsetX,
            OffsetY = offsetY
        };
    }
}
