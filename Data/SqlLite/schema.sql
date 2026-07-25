-- ============================================================
-- BelegOCR – SQLite Schema (JSON-Variante)
-- ============================================================

CREATE TABLE DocumentTemplates
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    Name TEXT NOT NULL,

    Description TEXT,

    SampleImagePath TEXT NOT NULL,

    FieldsJson TEXT NOT NULL DEFAULT '[]',

    CreatedAt TEXT NOT NULL
              DEFAULT CURRENT_TIMESTAMP,

    UpdatedAt TEXT NOT NULL
              DEFAULT CURRENT_TIMESTAMP
);


CREATE TABLE Documents
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    TemplateId INTEGER,

    OriginalFileName TEXT NOT NULL,

    FilePath TEXT NOT NULL,

    Status TEXT NOT NULL
           DEFAULT 'Pending',

    ExtractedJson TEXT,

    ProcessedAt TEXT,

    CreatedAt TEXT NOT NULL
              DEFAULT CURRENT_TIMESTAMP,

    ErrorMessage TEXT,

    FOREIGN KEY(TemplateId)
        REFERENCES DocumentTemplates(Id)
);