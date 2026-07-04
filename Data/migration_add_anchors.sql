-- ============================================================
-- Migration: AnchorsJson-Spalte nachträglich ergänzen
-- Nur ausführen, wenn dbo.DocumentTemplates bereits existiert
-- (d.h. schema.sql wurde schon vorher einmal ausgeführt).
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.DocumentTemplates') AND name = 'AnchorsJson'
)
BEGIN
    ALTER TABLE dbo.DocumentTemplates
        ADD AnchorsJson NVARCHAR(MAX) NOT NULL DEFAULT '{}';

    PRINT 'Spalte AnchorsJson hinzugefügt.';
END
ELSE
BEGIN
    PRINT 'Spalte AnchorsJson existiert bereits – keine Änderung nötig.';
END
