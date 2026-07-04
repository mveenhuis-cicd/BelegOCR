-- ============================================================
-- Demo-Template: TechSolutions GmbH Rechnung
-- Bildgröße: 1448 x 1086 px
-- ============================================================

INSERT INTO dbo.DocumentTemplates (Name, Description, SampleImagePath, FieldsJson)
VALUES (
    N'TechSolutions Rechnung',
    N'Standardrechnung TechSolutions GmbH – RE-YYYY-NNNN',
    N'lieferschein.png',
    N'[
  {"FieldName":"Rechnungsnummer",  "FieldKey":"invoice_no",        "X":900,  "Y":295,  "W":240, "H":34, "Type":"Text"},
  {"FieldName":"Kundennummer",     "FieldKey":"customer_no",       "X":900,  "Y":335,  "W":200, "H":34, "Type":"Text"},
  {"FieldName":"Rechnungsdatum",   "FieldKey":"invoice_date",      "X":900,  "Y":375,  "W":200, "H":34, "Type":"Date"},
  {"FieldName":"Fälligkeitsdatum", "FieldKey":"due_date",          "X":900,  "Y":415,  "W":200, "H":34, "Type":"Date"},
  {"FieldName":"Lieferdatum",      "FieldKey":"delivery_date",     "X":900,  "Y":455,  "W":200, "H":34, "Type":"Date"},
  {"FieldName":"Empfänger Firma",  "FieldKey":"recipient_company", "X":60,   "Y":295,  "W":360, "H":34, "Type":"Text"},
  {"FieldName":"Empfänger Name",   "FieldKey":"recipient_name",    "X":60,   "Y":330,  "W":360, "H":34, "Type":"Text"},
  {"FieldName":"Empfänger Adresse","FieldKey":"recipient_address", "X":60,   "Y":365,  "W":360, "H":34, "Type":"Text"},
  {"FieldName":"Zwischensumme",    "FieldKey":"subtotal_net",      "X":1100, "Y":870,  "W":220, "H":34, "Type":"Number"},
  {"FieldName":"USt. 19%",         "FieldKey":"vat",               "X":1100, "Y":910,  "W":220, "H":34, "Type":"Number"},
  {"FieldName":"Gesamtbetrag",     "FieldKey":"total_gross",       "X":1100, "Y":955,  "W":220, "H":40, "Type":"Number"},
  {"FieldName":"IBAN",             "FieldKey":"iban",              "X":220,  "Y":1020, "W":380, "H":34, "Type":"Text"},
  {"FieldName":"BIC",              "FieldKey":"bic",               "X":930,  "Y":1020, "W":240, "H":34, "Type":"Text"}
]'
);

-- Prüfen:
SELECT Id, Name,
       JSON_VALUE(FieldsJson, '$[0].FieldName') AS ErstesField,
       JSON_QUERY(FieldsJson, '$')              AS AlleFelder
FROM dbo.DocumentTemplates
WHERE Name = N'TechSolutions Rechnung';

-- Einzelne Felder per OPENJSON abfragen:
SELECT f.FieldName, f.FieldKey, f.X, f.Y, f.W, f.H, f.Type
FROM dbo.DocumentTemplates t
CROSS APPLY OPENJSON(t.FieldsJson)
  WITH (
    FieldName NVARCHAR(100),
    FieldKey  NVARCHAR(100),
    X INT, Y INT, W INT, H INT,
    Type NVARCHAR(50)
  ) f
WHERE t.Name = N'TechSolutions Rechnung';
