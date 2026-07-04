using BelegOCR.Models;
using BelegOCR.Services;
using Xunit;

namespace BelegOCR.Tests.Services;

/// <summary>
/// Tests für AnchorTransformCalculator.Calculate – die affine Korrektur
/// (Skalierung + Verschiebung), die aus zwei Ankerpunkten berechnet wird,
/// um Template-Koordinaten an einen abweichend positionierten/skalierten
/// Beleg anzupassen.
/// </summary>
public class AnchorTransformCalculator_Tests
{
    private static AnchorPointDef Anchor(int x, int y) =>
        new() { ExpectedText = "Test", X = x, Y = y, W = 100, H = 20 };

    [Fact]
    public void Calculate_IdentischePositionen_GibtIdentityTransformZurueck()
    {
        // Beleg liegt exakt wie im Template -> keine Korrektur nötig.
        var topLeft     = Anchor(100, 100);
        var bottomRight = Anchor(900, 1000);

        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 100, 100,
            bottomRight, 900, 1000);

        Assert.Equal(1.0, transform.ScaleX, precision: 3);
        Assert.Equal(1.0, transform.ScaleY, precision: 3);
        Assert.Equal(0.0, transform.OffsetX, precision: 1);
        Assert.Equal(0.0, transform.OffsetY, precision: 1);
    }

    [Fact]
    public void Calculate_NurVerschoben_BerechnetReinenVersatzOhneSkalierung()
    {
        // Beleg ist gegenüber dem Template um (+50, +30) verschoben, gleiche Größe.
        var topLeft     = Anchor(100, 100);
        var bottomRight = Anchor(900, 1000);

        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 150, 130,
            bottomRight, 950, 1030);

        Assert.Equal(1.0, transform.ScaleX, precision: 3);
        Assert.Equal(1.0, transform.ScaleY, precision: 3);
        Assert.Equal(50.0, transform.OffsetX, precision: 1);
        Assert.Equal(30.0, transform.OffsetY, precision: 1);
    }

    [Fact]
    public void Calculate_GroessererBelegScan_BerechnetSkalierungKorrekt()
    {
        // Beleg wurde in doppelter Auflösung gescannt (Faktor 2 in beide Richtungen).
        var topLeft     = Anchor(100, 100);
        var bottomRight = Anchor(600, 600); // Distanz 500x500 im Template

        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 200, 200,
            bottomRight, 1200, 1200); // Distanz 1000x1000 im Beleg = Faktor 2

        Assert.Equal(2.0, transform.ScaleX, precision: 2);
        Assert.Equal(2.0, transform.ScaleY, precision: 2);
    }

    [Fact]
    public void Calculate_AngewendeteTransformation_TrifftErwarteteFeldposition()
    {
        // End-to-End-Check: ein Feld, das im Template bei (500,500) lag, muss nach
        // Anwendung der Transformation an der Position landen, die der Verschiebung
        // zwischen den beiden Ankern entspricht.
        var topLeft     = Anchor(100, 100);
        var bottomRight = Anchor(900, 900);

        // Beleg: alles um (+20, +10) verschoben, keine Skalierung.
        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 120, 110,
            bottomRight, 920, 910);

        var (x, y, w, h) = transform.Apply(500, 500, 200, 30);

        Assert.Equal(520, x);
        Assert.Equal(510, y);
        Assert.Equal(200, w); // Breite/Höhe unverändert bei reiner Verschiebung
        Assert.Equal(30, h);
    }

    [Fact]
    public void Calculate_AnkerZuNahBeieinanderInTemplate_GibtIdentityZurueck()
    {
        // Anker liegen im Template nur 5px auseinander -> Skalierungsberechnung
        // wäre numerisch unzuverlässig, also lieber keine Korrektur anwenden.
        var topLeft     = Anchor(100, 100);
        var bottomRight = Anchor(105, 105);

        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 100, 100,
            bottomRight, 500, 500);

        Assert.Equal(1.0, transform.ScaleX);
        Assert.Equal(1.0, transform.ScaleY);
        Assert.Equal(0.0, transform.OffsetX);
        Assert.Equal(0.0, transform.OffsetY);
    }

    [Fact]
    public void Calculate_UnplausibelGrosseSkalierung_GibtIdentityZurueckAlsSicherheitsnetz()
    {
        // Anker im Beleg liegen mehr als doppelt so weit auseinander wie im
        // Template (z.B. falsches Template zugeordnet oder Anker-Erkennung
        // hat fälschlich einen anderen Textbereich getroffen). Eine derart
        // extreme Skalierung würde alle Felder völlig falsch platzieren,
        // daher lieber unkorrigiert lassen und IsValid die Unstimmigkeit
        // aufdecken lassen.
        var topLeft     = Anchor(100, 100);
        var bottomRight = Anchor(200, 200); // Distanz 100x100 im Template

        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 100, 100,
            bottomRight, 500, 500); // Distanz 400x400 im Beleg = Faktor 4

        Assert.Equal(1.0, transform.ScaleX);
        Assert.Equal(1.0, transform.ScaleY);
    }

    [Fact]
    public void Calculate_UnterschiedlicheSkalierungXUndY_WirdGetrenntBerechnet()
    {
        // Beleg wurde nicht proportional skaliert (z.B. beim Scannen leicht
        // gestreckt) -> ScaleX und ScaleY dürfen sich unterscheiden.
        var topLeft     = Anchor(0, 0);
        var bottomRight = Anchor(500, 1000); // Distanz 500x1000

        var transform = AnchorTransformCalculator.Calculate(
            topLeft, 0, 0,
            bottomRight, 1000, 1100); // Distanz 1000x1100 -> ScaleX=2.0, ScaleY=1.1

        Assert.Equal(2.0, transform.ScaleX, precision: 2);
        Assert.Equal(1.1, transform.ScaleY, precision: 2);
    }
}
