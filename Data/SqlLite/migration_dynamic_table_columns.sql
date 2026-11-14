-- Umstellung DocumentPositions von festen Spalten (PosNo, Description, Quantity,
-- UnitPrice, Total) auf dynamische Werte in ValuesJson.
-- Wird von DocumentRepository.SavePositionsAsync automatisch ausgeführt, falls die
-- Spalte ValuesJson fehlt. Die alten Spalten bleiben (nullable, ungenutzt) erhalten.
ALTER TABLE DocumentPositions ADD COLUMN ValuesJson TEXT NOT NULL DEFAULT '{}';

UPDATE DocumentPositions
SET ValuesJson = json_object(
        'pos_no',      PosNo,
        'description', Description,
        'quantity',    Quantity,
        'unit_price',  UnitPrice,
        'total',       Total);
