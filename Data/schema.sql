-- ============================================================
-- BelegOCR – SQL Server Schema (JSON-Variante)
-- ============================================================

CREATE TABLE dbo.DocumentTemplates (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(200)   NOT NULL,
    Description     NVARCHAR(500)   NULL,
    SampleImagePath NVARCHAR(500)   NOT NULL,
    -- Alle Felddefinitionen als JSON gespeichert
    -- Beispiel: [{"FieldName":"Rechnungsnummer","FieldKey":"invoice_no","X":120,"Y":80,"W":200,"H":30,"Type":"Text"}]
    FieldsJson      NVARCHAR(MAX)   NOT NULL DEFAULT '[]',
    CreatedAt       DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2       NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE dbo.Documents (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    TemplateId       INT             NULL REFERENCES dbo.DocumentTemplates(Id),
    OriginalFileName NVARCHAR(300)   NOT NULL,
    FilePath         NVARCHAR(500)   NOT NULL,
    Status           NVARCHAR(50)    NOT NULL DEFAULT 'Pending',
    -- Extrahierte Werte als JSON-Objekt
    -- Beispiel: {"invoice_no":"RE-2024-001","customer_no":"KD-4711"}
    ExtractedJson    NVARCHAR(MAX)   NULL,
    ProcessedAt      DATETIME2       NULL,
    CreatedAt        DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
    ErrorMessage     NVARCHAR(MAX)   NULL
);
