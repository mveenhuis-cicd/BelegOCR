-- Tabellenpositionen eines Belegs (Ergebnis der Tabellenerkennung, Feld-Typ "Table").
-- Wird von DocumentRepository.SavePositionsAsync zusätzlich per
-- CREATE TABLE IF NOT EXISTS angelegt, falls die Migration nicht ausgeführt wurde.
CREATE TABLE IF NOT EXISTS DocumentPositions
(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    DocumentId  INTEGER NOT NULL,
    FieldKey    TEXT    NOT NULL,
    RowNo       INTEGER NOT NULL,
    PosNo       TEXT,
    Description TEXT,
    Quantity    NUMERIC,
    UnitPrice   NUMERIC,
    Total       NUMERIC,
    Uncertain   INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(DocumentId) REFERENCES Documents(Id)
);
