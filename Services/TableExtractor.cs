using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BelegOCR.Models;

namespace BelegOCR.Services;

/// <summary>
/// Liest eine Tabelle aus den Tesseract-Wörtern eines Belegs. Ablauf:
///   1. Header im Bild suchen (Spaltennamen per Levenshtein) → Spalten-Versatz
///   2. Rumpf-Wörter nach Y-Mitte zu physischen Zeilen gruppieren (Stoppwort beendet den Rumpf)
///   3. Wörter per größter Überlappung den Spalten zuordnen
///   4. Schlüsselspalte gefüllt (Regex erfüllt) → sichere neue Position
///   5. Übrige Zeilen per Punktesystem: aus den sicheren Positionen gelernte
///      Positions-/Umbruchabstände, Einrückung, Textmerkmale, Pos.-Nr.-Lücken
///   6. Kontrolle: Menge × Einzelpreis = Gesamt, Summe = erkannter Gesamtbetrag
///
/// Eigenständige Klasse ohne Tesseract-/Dateisystem-Abhängigkeit, damit die
/// Logik isoliert unittestbar ist (siehe TableExtractor_Tests).
/// </summary>
public static class TableExtractor
{
    // Suchbereich um den (anker-korrigierten) Header in px.
    public const int HeaderSearchMargin = 120;

    private const double HeaderMatchThreshold = 0.75;
    private const double AmountTolerance      = 0.02;

    /// <summary>
    /// Bereich, den der Aufrufer per OCR auslesen muss: Header inkl. Suchrand
    /// bis Rumpfende (bzw. Seitenende, falls keine Rumpfhöhe hinterlegt ist).
    /// </summary>
    public static (int X, int Y, int W, int H) GetSearchRegion(
        TemplateFieldDef field, CoordinateTransform transform, int imageWidth, int imageHeight)
    {
        var def = field.Table ?? new TableDef();
        var (hx, hy, hw, hh) = transform.Apply(field.X, field.Y, field.W, field.H);

        var left  = Math.Min(hx, def.Columns.Count > 0 ? transform.Apply(def.Columns.Min(c => c.X1), 0, 0, 0).X : hx);
        var right = Math.Max(hx + hw, def.Columns.Count > 0 ? transform.Apply(def.Columns.Max(c => c.X2), 0, 0, 0).X : hx + hw);

        var x = Math.Max(0, left - HeaderSearchMargin);
        var y = Math.Max(0, hy - HeaderSearchMargin);
        var bottom = def.BodyHeight > 0
            ? (int)Math.Round((def.HeaderTextBottom + def.BodyOffsetY + def.BodyHeight) * transform.ScaleY + transform.OffsetY) + HeaderSearchMargin
            : imageHeight;

        var x2 = Math.Min(imageWidth, right + HeaderSearchMargin);
        var y2 = Math.Min(imageHeight, Math.Max(bottom, hy + hh + HeaderSearchMargin));
        return (x, y, Math.Max(0, x2 - x), Math.Max(0, y2 - y));
    }

    public static ExtractedTable Extract(
        TemplateFieldDef field,
        CoordinateTransform transform,
        IReadOnlyList<OcrWord> words,
        int imageHeight,
        string? expectedTotalText = null)
    {
        var def    = field.Table ?? new TableDef();
        var result = new ExtractedTable { FieldName = field.FieldName };

        if (def.Columns.Count == 0)
        {
            result.Warnings.Add("Keine Spalten definiert.");
            return result;
        }

        var cleanWords = words.Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Height > 0).ToList();
        var lineHeight = Median(cleanWords.Select(w => (double)w.Height)) ?? 12.0;

        // ── 1. Header suchen ────────────────────────────────────────────────
        var header = LocateHeader(def, field, transform, cleanWords, lineHeight);
        result.HeaderFound = header.Found;
        if (!header.Found)
            result.Warnings.Add("Tabellenkopf nicht gefunden – Template-Position (anker-korrigiert) verwendet.");

        var keys = TableColumnKeys.Resolve(def.Columns);
        var columns = def.Columns
            .Select((c, i) => new Column(
                c,
                (int)Math.Round(c.X1 * transform.ScaleX + transform.OffsetX + header.Dx),
                (int)Math.Round(c.X2 * transform.ScaleX + transform.OffsetX + header.Dx),
                keys[i],
                c.ResolveDataType()))
            .OrderBy(c => c.X1)
            .ToList();

        result.Columns = columns.Select(c => new ExtractedTableColumn
        {
            HeaderText = c.Def.HeaderText, Key = c.Key, Role = c.Def.Role, DataType = c.DataType,
            X1 = c.X1, X2 = c.X2
        }).ToList();

        foreach (var role in columns.Where(c => c.Def.Role != "").GroupBy(c => c.Def.Role).Where(g => g.Count() > 1))
            result.Warnings.Add($"Rolle \"{role.Key}\" ist mehrfach vergeben – Prüfungen nutzen die erste Spalte.");

        // ── 2. Rumpf + physische Zeilen ─────────────────────────────────────
        var bodyTop    = header.Bottom + (int)Math.Round(def.BodyOffsetY * transform.ScaleY);
        var bodyBottom = def.BodyHeight > 0
            ? bodyTop + (int)Math.Round(def.BodyHeight * transform.ScaleY)
            : imageHeight;

        var tableLeft  = columns.First().X1 - lineHeight;
        var tableRight = columns.Last().X2  + lineHeight;

        var bodyWords = cleanWords
            .Where(w => w.CenterY > Math.Max(bodyTop, header.Bottom) && w.CenterY < bodyBottom)
            .Where(w => w.CenterX >= tableLeft && w.CenterX <= tableRight)
            .ToList();

        var lines = GroupIntoLines(bodyWords, lineHeight);

        var stopIndex = lines.FindIndex(l => StartsWithStopWord(l, def.StopWords));
        if (stopIndex >= 0)
        {
            bodyBottom = lines[stopIndex].Top;
            lines = lines.Take(stopIndex).ToList();
        }
        else if (def.BodyHeight <= 0)
        {
            result.Warnings.Add("Kein Stoppwort gefunden – Rumpf reicht bis Seitenende.");
        }

        result.BodyTop    = bodyTop;
        result.BodyBottom = Math.Min(bodyBottom, imageHeight);

        if (lines.Count == 0)
        {
            result.Warnings.Add("Keine Zeilen im Tabellenrumpf gefunden.");
            return result;
        }

        // ── 3. Spaltenzuordnung ─────────────────────────────────────────────
        foreach (var line in lines)
            AssignCells(line, columns);

        // ── 4.+5. Zeilen → Positionen ───────────────────────────────────────
        var stats = ClassifyLines(lines, columns, lineHeight, result.Warnings);
        result.Stats = stats;

        result.Rows = BuildRows(lines, columns);

        // ── 6. Kontrolle ────────────────────────────────────────────────────
        ApplyChecks(result, expectedTotalText);

        return result;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Header-Suche
    // ════════════════════════════════════════════════════════════════════════

    private record HeaderLocation(bool Found, double Dx, int Bottom);

    private static HeaderLocation LocateHeader(
        TableDef def, TemplateFieldDef field, CoordinateTransform t, List<OcrWord> words, double lineHeight)
    {
        var expectedTop    = def.HeaderTextTop    > 0 ? def.HeaderTextTop    : field.Y;
        var expectedBottom = def.HeaderTextBottom > 0 ? def.HeaderTextBottom : field.Y + field.H;
        var expTopT        = expectedTop * t.ScaleY + t.OffsetY;
        var headerHeightT  = (expectedBottom - expectedTop) * t.ScaleY;

        var fallback = new HeaderLocation(false, 0, (int)Math.Round(expTopT + headerHeightT));

        var namedColumns = def.Columns.Where(c => !string.IsNullOrWhiteSpace(c.HeaderText)).ToList();
        if (namedColumns.Count == 0)
            return fallback;

        var candidates = words
            .Where(w => w.CenterY >= expTopT - HeaderSearchMargin &&
                        w.CenterY <= expTopT + headerHeightT + HeaderSearchMargin)
            .ToList();

        (int Matches, double Score, double Dist, double Dx, int Top)? best = null;

        foreach (var line in GroupIntoLines(candidates, lineHeight))
        {
            var dxs    = new List<double>();
            var score  = 0.0;
            var ws     = line.Words;

            foreach (var col in namedColumns)
            {
                var target   = NormalizeKey(col.HeaderText);
                var bestSim  = 0.0;
                OcrWord? bestStart = null;

                // Mehrwortige Überschriften: Läufe aus 1–3 benachbarten Wörtern vergleichen.
                for (var i = 0; i < ws.Count; i++)
                {
                    var sb = new StringBuilder();
                    for (var len = 1; len <= 3 && i + len <= ws.Count; len++)
                    {
                        sb.Append(NormalizeKey(ws[i + len - 1].Text));
                        var sim = Similarity(sb.ToString(), target);
                        if (sim > bestSim) { bestSim = sim; bestStart = ws[i]; }
                    }
                }

                if (bestSim >= HeaderMatchThreshold && bestStart != null)
                {
                    score += bestSim;
                    dxs.Add(bestStart.X1 - (col.TextX1 * t.ScaleX + t.OffsetX));
                }
            }

            if (dxs.Count == 0) continue;

            var dist = Math.Abs(line.Top - expTopT);
            if (best == null ||
                dxs.Count > best.Value.Matches ||
                (dxs.Count == best.Value.Matches && dist < best.Value.Dist))
            {
                best = (dxs.Count, score, dist, Median(dxs) ?? 0, line.Top);
            }
        }

        // Mindestens die Hälfte der Spaltennamen muss in einer Zeile gefunden werden,
        // sonst ist die Gefahr groß, eine beliebige Textzeile für den Header zu halten.
        var required = Math.Max(1, (int)Math.Ceiling(namedColumns.Count / 2.0));
        if (best == null || best.Value.Matches < required)
            return fallback;

        return new HeaderLocation(true, best.Value.Dx, (int)Math.Round(best.Value.Top + headerHeightT));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Zeilen + Spalten
    // ════════════════════════════════════════════════════════════════════════

    private sealed record Column(TableColumnDef Def, int X1, int X2, string Key, string DataType);

    private sealed class Line
    {
        public List<OcrWord> Words { get; } = [];
        public int Top    => Words.Min(w => w.Y1);
        public int Bottom => Words.Max(w => w.Y2);
        public double CenterY => Words.Average(w => w.CenterY);
        public string Text => string.Join(" ", Words.Select(w => w.Text));

        public string[] Cells       { get; set; } = [];
        public int[]    CellLeft    { get; set; } = [];   // linke Kante des ersten Worts pro Spalte
        public bool     IsKey       { get; set; }
        public bool     NewPosition { get; set; }
        public bool     Uncertain   { get; set; }
        public string?  Reason      { get; set; }
        public int      Score       { get; set; }
        public double?  GapRatio    { get; set; }
        public int?     PosNumber   { get; set; }
    }

    private static List<Line> GroupIntoLines(List<OcrWord> words, double lineHeight)
    {
        var lines = new List<Line>();
        foreach (var w in words.OrderBy(w => w.CenterY))
        {
            var current = lines.Count > 0 ? lines[^1] : null;
            if (current != null && Math.Abs(w.CenterY - current.CenterY) <= lineHeight * 0.5)
                current.Words.Add(w);
            else
            {
                var line = new Line();
                line.Words.Add(w);
                lines.Add(line);
            }
        }
        foreach (var l in lines)
            l.Words.Sort((a, b) => a.X1.CompareTo(b.X1));
        return lines;
    }

    private static void AssignCells(Line line, List<Column> columns)
    {
        var cells = new List<OcrWord>[columns.Count];
        for (var i = 0; i < cells.Length; i++) cells[i] = [];

        foreach (var w in line.Words)
        {
            var bestIdx     = -1;
            var bestOverlap = 0;
            for (var i = 0; i < columns.Count; i++)
            {
                var overlap = Math.Min(w.X2, columns[i].X2) - Math.Max(w.X1, columns[i].X1);
                if (overlap > bestOverlap) { bestOverlap = overlap; bestIdx = i; }
            }

            // Kein Überlapp (Wort liegt in einer Lücke/am Rand) → nächstgelegene Spalte.
            if (bestIdx < 0)
                bestIdx = Enumerable.Range(0, columns.Count)
                    .OrderBy(i => Math.Abs(w.CenterX - (columns[i].X1 + columns[i].X2) / 2.0))
                    .First();

            cells[bestIdx].Add(w);
        }

        line.Cells    = cells.Select(c => string.Join(" ", c.Select(w => w.Text))).ToArray();
        line.CellLeft = cells.Select(c => c.Count > 0 ? c.Min(w => w.X1) : -1).ToArray();
    }

    private static bool StartsWithStopWord(Line line, List<string> stopWords)
    {
        var text = NormalizeText(line.Text);
        foreach (var stop in stopWords)
        {
            var s = NormalizeText(stop);
            if (s.Length == 0 || !text.StartsWith(s, StringComparison.Ordinal)) continue;
            // Ganzes Wort: "Gesamt 1.234,00" ja, "Gesamtpaket Wartung" nein.
            if (text.Length == s.Length || !char.IsLetter(text[s.Length]))
                return true;
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Klassifizierung: neue Position oder Fortsetzung
    // ════════════════════════════════════════════════════════════════════════

    private static TableLayoutStats ClassifyLines(
        List<Line> lines, List<Column> columns, double lineHeight, List<string> warnings)
    {
        var descIdx  = columns.FindIndex(c => c.Def.Role == TableRoles.Description);
        var posNoIdx = columns.FindIndex(c => c.Def.Role == TableRoles.PosNo);
        var hasKey   = columns.Any(c => c.Def.IsKey);

        if (!hasKey)
            warnings.Add("Keine Schlüsselspalte definiert – Positionen werden nur über das Layout getrennt.");

        // Sichere Fälle: Schlüsselspalte gefüllt und Regex erfüllt.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            line.IsKey = Enumerable.Range(0, columns.Count).Any(c =>
                columns[c].Def.IsKey && IsValidCell(line.Cells[c], columns[c].Def));

            if (posNoIdx >= 0 && line.IsKey)
                line.PosNumber = ParsePosNumber(line.Cells[posNoIdx]);

            if (i > 0)
                line.GapRatio = (line.Top - lines[i - 1].Bottom) / lineHeight;
        }

        // ── Lernen aus den sicheren Fällen ──────────────────────────────────
        var positionGaps = lines.Skip(1).Where(l => l.IsKey).Select(l => l.GapRatio!.Value).ToList();

        // Sichere Umbrüche: Nicht-Schlüssel-Zeilen, die eindeutig weitergeführter Text sind
        // (klein beginnend / vorherige Zeile endet mit "-" oder ","). Fehlen solche, gelten
        // alle Nicht-Schlüssel-Abstände als Umbruchabstände – der Median ist robust gegen
        // die wenigen echten Positionen darunter.
        var breakGaps = new List<double>();
        var otherGaps = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].IsKey) continue;
            otherGaps.Add(lines[i].GapRatio!.Value);
            if (TextSaysContinuation(lines[i - 1], lines[i], descIdx))
                breakGaps.Add(lines[i].GapRatio!.Value);
        }
        if (breakGaps.Count == 0) breakGaps = otherGaps;

        var posGap   = positionGaps.Count > 0 ? Median(positionGaps) : null;
        var breakGap = breakGaps.Count    > 0 ? Median(breakGaps)    : null;

        // Fallback ohne sichere Positionen: Median über den gesamten Rumpf.
        var fallback  = posGap == null;
        var allMedian = Median(lines.Skip(1).Select(l => l.GapRatio!.Value));

        // Typische linke Kante der Beschreibung bei sicheren Positionen (Einrückung).
        double? descStart = descIdx >= 0
            ? Median(lines.Where(l => l.IsKey && l.CellLeft[descIdx] >= 0).Select(l => (double)l.CellLeft[descIdx]))
            : null;

        // ── Punktesystem für die übrigen Zeilen ─────────────────────────────
        var seenPosition = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.IsKey)
            {
                line.NewPosition = true;
                seenPosition = true;
                continue;
            }

            if (!seenPosition)
            {
                // Vor der ersten Position gibt es nichts zum Anhängen.
                line.NewPosition = true;
                line.Uncertain   = hasKey;
                line.Reason      = hasKey ? "Zeile ohne Schlüsselwert vor der ersten sicheren Position" : null;
                line.Score       = 2;
                seenPosition     = true;
                continue;
            }

            var prev  = lines[i - 1];
            var score = 0;
            var r     = line.GapRatio!.Value;

            // Abstand: näher am Positions- oder am Umbruchabstand?
            if (posGap != null && breakGap != null && Math.Abs(posGap.Value - breakGap.Value) >= 0.15)
            {
                var t = (r - breakGap.Value) / (posGap.Value - breakGap.Value);
                if (t >= 0.65) score += 2;
                else if (t <= 0.35) score -= 2;
            }
            else if (posGap != null && posGap.Value >= 0.1)
            {
                if (r >= posGap.Value * 0.75) score += 2;
                else if (r <= posGap.Value * 0.5) score -= 2;
            }
            else if (allMedian != null)
            {
                if (r > Math.Max(allMedian.Value * 1.5, allMedian.Value + 0.2)) score += 2;
                else if (r <= allMedian.Value * 1.1) score -= 1;
            }

            // Einrückung gegenüber dem Beschreibungsbeginn sicherer Positionen.
            if (descIdx >= 0 && descStart != null && line.CellLeft[descIdx] >= 0 &&
                line.CellLeft[descIdx] > descStart.Value + Math.Max(lineHeight * 0.8, 8))
                score -= 1;

            // Textmerkmale.
            var prevText = TextForSignals(prev, descIdx);
            var curText  = TextForSignals(line, descIdx);
            if (prevText.EndsWith('-') || prevText.EndsWith(',')) score -= 2;
            var first = curText.FirstOrDefault(char.IsLetterOrDigit);
            if (char.IsLower(first)) score -= 2;
            if (prevText.EndsWith('.') && char.IsUpper(first)) score += 1;

            // Weitere Wertspalten (z.B. Einzelpreis) sinnvoll gefüllt → eher eigene Position.
            if (Enumerable.Range(0, columns.Count).Any(c =>
                    !columns[c].Def.IsKey && c != descIdx &&
                    !string.IsNullOrWhiteSpace(columns[c].Def.ValidationPattern) &&
                    IsValidCell(line.Cells[c], columns[c].Def)))
                score += 1;

            line.Score       = score;
            line.NewPosition = score >= 2;
            line.Uncertain   = Math.Abs(score) <= 1;
            line.Reason      = line.Uncertain ? $"Layout-Merkmale nicht eindeutig (Punkte {score})" : null;
        }

        ApplyPosNumberGaps(lines, warnings);

        return new TableLayoutStats
        {
            LineHeight     = Math.Round(lineHeight, 1),
            PositionGap    = posGap   != null ? Math.Round(posGap.Value, 2)   : null,
            LineBreakGap   = breakGap != null ? Math.Round(breakGap.Value, 2) : null,
            FallbackMedian = fallback,
            SureLines      = lines.Count(l => l.IsKey),
            UncertainLines = lines.Count(l => l.Uncertain)
        };
    }

    /// <summary>
    /// Harte Information aus der Pos.-Nr.-Folge: zwischen Pos. 2 und Pos. 4 liegt genau
    /// eine weitere Position, zwischen 2 und 3 keine. Überschreibt das Punktesystem.
    /// </summary>
    private static void ApplyPosNumberGaps(List<Line> lines, List<string> warnings)
    {
        var keyIdx = Enumerable.Range(0, lines.Count).Where(i => lines[i].PosNumber != null).ToList();

        for (var k = 0; k + 1 < keyIdx.Count; k++)
        {
            int a = keyIdx[k], b = keyIdx[k + 1];
            var between = Enumerable.Range(a + 1, b - a - 1).Where(i => !lines[i].IsKey).ToList();
            if (between.Count == 0 || between.Count != b - a - 1) continue; // dazwischen weitere Schlüsselzeilen ohne Nr.

            var missing = lines[b].PosNumber!.Value - lines[a].PosNumber!.Value - 1;
            if (missing < 0) continue;

            if (missing > between.Count)
            {
                warnings.Add($"Pos.-Nr.-Lücke zwischen {lines[a].PosNumber} und {lines[b].PosNumber} nicht auflösbar.");
                continue;
            }

            var chosen = between
                .OrderByDescending(i => lines[i].Score)
                .ThenByDescending(i => lines[i].GapRatio)
                .Take(missing)
                .ToHashSet();

            foreach (var i in between)
            {
                var line = lines[i];
                line.NewPosition = chosen.Contains(i);
                line.Uncertain   = line.NewPosition && line.Score < 2;
                line.Reason      = line.Uncertain ? "Aus Lücke in der Pos.-Nr.-Folge abgeleitet" : null;
            }
        }
    }

    private static bool TextSaysContinuation(Line prev, Line cur, int descIdx)
    {
        var p = TextForSignals(prev, descIdx);
        var first = TextForSignals(cur, descIdx).FirstOrDefault(char.IsLetterOrDigit);
        return p.EndsWith('-') || p.EndsWith(',') || char.IsLower(first);
    }

    private static string TextForSignals(Line line, int descIdx) =>
        (descIdx >= 0 && !string.IsNullOrWhiteSpace(line.Cells[descIdx]) ? line.Cells[descIdx] : line.Text).Trim();

    private static bool IsValidCell(string cell, TableColumnDef col) =>
        !string.IsNullOrWhiteSpace(cell) &&
        FieldValueValidator.Validate(cell.Trim(), col.ValidationPattern, col.HeaderText) != false;

    private static int? ParsePosNumber(string cell)
    {
        var m = Regex.Match(cell ?? "", @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Positionen bauen + Kontrolle
    // ════════════════════════════════════════════════════════════════════════

    private static List<ExtractedTableRow> BuildRows(List<Line> lines, List<Column> columns)
    {
        var rows = new List<ExtractedTableRow>();
        string[]? cells = null;
        ExtractedTableRow? row = null;
        var confs = new List<double>();

        void Flush()
        {
            if (row == null || cells == null) return;
            for (var c = 0; c < columns.Count; c++)
            {
                row.Cells[columns[c].Key]  = cells[c];
                row.Values[columns[c].Key] = ParseValue(cells[c], columns[c].DataType);
            }
            row.Confidence = confs.Count > 0 ? Math.Round(confs.Average(), 1) : 0;
            rows.Add(row);
        }

        foreach (var line in lines)
        {
            if (line.NewPosition || row == null)
            {
                Flush();
                row   = new ExtractedTableRow { RowNo = rows.Count + 1, Y1 = line.Top };
                cells = new string[columns.Count];
                Array.Fill(cells, "");
                confs = [];
            }

            for (var c = 0; c < columns.Count; c++)
                cells![c] = AppendText(cells[c], line.Cells[c]);

            confs.AddRange(line.Words.Select(w => (double)w.Confidence));
            row.LineCount++;
            row.Y2 = line.Bottom;
            if (line.Uncertain)
            {
                row.Uncertain       = true;
                row.UncertainReason ??= line.Reason;
            }
        }
        Flush();
        return rows;
    }

    private static void ApplyChecks(ExtractedTable result, string? expectedTotalText)
    {
        var qtyKey   = result.KeyOf(TableRoles.Quantity);
        var priceKey = result.KeyOf(TableRoles.UnitPrice);
        var totalKey = result.KeyOf(TableRoles.Total);

        foreach (var row in result.Rows)
        {
            var qty   = row.GetDecimal(qtyKey);
            var price = row.GetDecimal(priceKey);
            var total = row.GetDecimal(totalKey);
            if (qty != null && price != null && total != null)
                row.RowCheckOk = Math.Abs(qty.Value * price.Value - total.Value) <= (decimal)AmountTolerance;
        }

        var totals = result.Rows.Select(r => r.GetDecimal(totalKey)).OfType<decimal>().ToList();
        if (totals.Count > 0)
            result.SumOfTotals = totals.Sum();

        result.ExpectedTotal = ParseAmount(expectedTotalText);
        if (result.SumOfTotals != null && result.ExpectedTotal != null)
        {
            result.SumCheckOk = Math.Abs(result.SumOfTotals.Value - result.ExpectedTotal.Value) <= (decimal)AmountTolerance;
            if (result.SumCheckOk == false)
                result.Warnings.Add($"Summe der Positionen ({result.SumOfTotals:N2}) weicht vom Gesamtbetrag ({result.ExpectedTotal:N2}) ab.");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Hilfsfunktionen (public für Tests / Header-Analyse)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hängt eine Folgezeile an: Silbentrennung "Liefe-" + "rung" → "Lieferung",
    /// Bindestrich-Komposita "Daten-" + "Management" bleiben "Daten-Management".
    /// </summary>
    public static string AppendText(string existing, string next)
    {
        existing = existing.TrimEnd();
        next     = next.Trim();
        if (existing.Length == 0) return next;
        if (next.Length == 0)     return existing;

        if (existing.Length > 1 && existing[^1] == '-' && char.IsLetter(existing[^2]))
            return char.IsLower(next[0]) ? existing[..^1] + next : existing + next;

        return existing + " " + next;
    }

    /// <summary>
    /// Parst deutsche und englische Beträge: "1.234,56 €", "12,50", "1,234.56", "-3,00", "3,00-".
    /// "1.234" gilt als Tausenderpunkt (deutsches Format).
    /// </summary>
    public static decimal? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Tausendergruppen (".", ",", "'", " ") nur mit genau 3 Ziffern, damit z.B.
        // "2 12,50" nicht zu 212,50 zusammengezogen wird.
        var m = Regex.Match(text, @"-?\d+(?:[.,' ]\d{3})*(?:[.,]\d+)?");
        if (!m.Success) return null;

        var s = m.Value.Replace(" ", "").Replace("'", "");
        var negative = s.StartsWith('-') || Regex.IsMatch(text, @"\d\s*-\s*$");
        s = s.TrimStart('-');

        var lastComma = s.LastIndexOf(',');
        var lastDot   = s.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            var dec = lastComma > lastDot ? ',' : '.';
            var grp = dec == ',' ? '.' : ',';
            s = s.Replace(grp.ToString(), "").Replace(dec, '.');
        }
        else if (lastComma >= 0)
        {
            s = s.Replace(',', '.');
            if (s.Count(ch => ch == '.') > 1) s = s.Replace(".", "");
        }
        else if (lastDot >= 0)
        {
            // Nur Punkte: genau 3 Ziffern danach → Tausenderpunkt
            var groups = s.Split('.');
            if (groups.Length > 2 || groups[^1].Length == 3)
                s = s.Replace(".", "");
        }

        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return null;
        return negative ? -value : value;
    }

    /// <summary>Levenshtein-Ähnlichkeit 0..1.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;

        var prev = new int[b.Length + 1];
        var cur  = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }

    /// <summary>Kleinbuchstaben, nur Buchstaben/Ziffern (für Header-Vergleich).</summary>
    public static string NormalizeKey(string text) =>
        new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string NormalizeText(string text) =>
        Regex.Replace(new string(text.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray()), @"\s+", " ").Trim();

    /// <summary>
    /// Rohtext einer Zelle → Wert passend zum Datentyp der Spalte:
    /// Text → string, Number/Amount → decimal, Date → "yyyy-MM-dd". Leer oder
    /// nicht parsebar → null (der Rohtext bleibt in ExtractedTableRow.Cells erhalten).
    /// </summary>
    public static object? ParseValue(string? raw, string dataType)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return dataType switch
        {
            TableDataTypes.Number or TableDataTypes.Amount => ParseAmount(s),
            TableDataTypes.Date                            => ParseDate(s)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _                                              => s
        };
    }

    private static readonly string[] DateFormats =
        ["d.M.yyyy", "d.M.yy", "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy"];

    /// <summary>Parst "05.03.2026", "5.3.26", "2026-03-05" (erstes Datum im Text).</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[./-]\d{1,2}[./-]\d{2,4}");
        if (!m.Success) return null;
        return DateTime.TryParseExact(m.Value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    private static double? Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0) return null;
        var mid = list.Count / 2;
        return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2.0;
    }
}

/// <summary>
/// Ermittelt im Template-Editor aus den OCR-Wörtern des Header-Frames die Spalten:
/// Wörter mit kleinem Abstand (bzw. übereinander bei zweizeiligen Überschriften)
/// werden zu einem Spaltennamen zusammengefasst, die Spaltengrenze liegt in der
/// Mitte der Lücke zwischen zwei Überschriften.
/// </summary>
public static class TableHeaderAnalyzer
{
    public static TableDef BuildColumns(IReadOnlyList<OcrWord> words, int frameX, int frameY, int frameW, int frameH)
    {
        var def = new TableDef { HeaderTextTop = frameY, HeaderTextBottom = frameY + frameH };
        var ws  = words.Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Height > 0)
                       .OrderBy(w => w.X1).ToList();
        if (ws.Count == 0)
            return def;

        def.HeaderTextTop    = ws.Min(w => w.Y1);
        def.HeaderTextBottom = ws.Max(w => w.Y2);

        // Wortabstand innerhalb eines Spaltennamens ist deutlich kleiner als die
        // Lücke zwischen zwei Spalten; Richtwert: 0,8 × Schrifthöhe.
        var heights   = ws.Select(w => w.Height).OrderBy(h => h).ToList();
        var maxGap    = heights[heights.Count / 2] * 0.8;

        var clusters = new List<List<OcrWord>>();
        var right    = int.MinValue;
        foreach (var w in ws)
        {
            if (clusters.Count == 0 || w.X1 - right > maxGap)
            {
                clusters.Add([]);
                right = int.MinValue;
            }
            clusters[^1].Add(w);
            right = Math.Max(right, w.X2);
        }

        for (var i = 0; i < clusters.Count; i++)
        {
            var c = clusters[i];
            // Text zeilenweise (oben→unten, links→rechts) zusammensetzen.
            var text = "";
            foreach (var w in c.OrderBy(w => Math.Round(w.CenterY / Math.Max(1, heights[heights.Count / 2]))).ThenBy(w => w.X1))
                text = TableExtractor.AppendText(text, w.Text);

            var x1 = i == 0 ? Math.Min(frameX, c.Min(w => w.X1))
                            : (clusters[i - 1].Max(w => w.X2) + c.Min(w => w.X1)) / 2;
            var x2 = i == clusters.Count - 1 ? Math.Max(frameX + frameW, c.Max(w => w.X2))
                            : (c.Max(w => w.X2) + clusters[i + 1].Min(w => w.X1)) / 2;

            var (role, dataType, isKey, pattern) = GuessColumn(text);
            def.Columns.Add(new TableColumnDef
            {
                HeaderText        = text,
                X1                = x1,
                X2                = x2,
                TextX1            = c.Min(w => w.X1),
                Role              = role,
                DataType          = dataType,
                IsKey             = isKey,
                ValidationPattern = pattern
            });
        }

        // Schlüssel vorbelegen (im Editor änderbar).
        var keys = TableColumnKeys.Resolve(def.Columns);
        for (var i = 0; i < def.Columns.Count; i++)
            def.Columns[i].Key = keys[i];

        return def;
    }

    public const string PatternPosNo  = @"^\d{1,4}[.)]?$";
    public const string PatternQty    = @"^\d{1,6}([.,]\d{1,3})?(\s*[A-Za-zÄÖÜäöü.]{1,6})?$";
    public const string PatternAmount = @"^-?(€\s*)?\d{1,3}([.,' ]?\d{3})*[.,]\d{2}(\s*(€|EUR|-))?$";

    /// <summary>Vorbelegung von Rolle, Datentyp, Schlüsselspalte und Regex anhand des Spaltennamens.</summary>
    public static (string Role, string DataType, bool IsKey, string? Pattern) GuessColumn(string headerText)
    {
        const string text = TableDataTypes.Text, num = TableDataTypes.Number, amt = TableDataTypes.Amount;

        var h = TableExtractor.NormalizeKey(headerText);
        if (h.StartsWith("artikelnr") || h.StartsWith("artnr"))                  return ("", text, false, null);
        if (h.Contains("datum") || h == "date")                                  return ("", TableDataTypes.Date, false, null);
        if (h.StartsWith("pos") || h is "nr" or "lfdnr")                         return (TableRoles.PosNo, text, true, PatternPosNo);
        if (h.StartsWith("meng") || h.StartsWith("anz") || h is "stk" or "qty")  return (TableRoles.Quantity, num, true, PatternQty);
        if (h.StartsWith("einzel") || h.StartsWith("ep") || h.Contains("preisst") || h.Contains("stückpreis") || h == "preis")
                                                                                 return (TableRoles.UnitPrice, amt, false, PatternAmount);
        if (h.StartsWith("gesamt") || h.Contains("betrag") || h.StartsWith("summe") || h.StartsWith("total"))
                                                                                 return (TableRoles.Total, amt, true, PatternAmount);
        if (h.StartsWith("mwst") || h.StartsWith("ust") || h.Contains("rabatt")) return ("", num, false, null);
        if (h.Contains("bezeichnung") || h.Contains("beschreibung") || h.StartsWith("artikel") ||
            h.Contains("leistung") || h.StartsWith("text") || h.StartsWith("produkt"))
                                                                                 return (TableRoles.Description, text, false, null);
        return ("", text, false, null);
    }
}
