-- ============================================================
-- BelegOCR – SQLite Seed-Script
-- Das Template wird nur angelegt, wenn es noch nicht existiert.
-- ============================================================

INSERT INTO DocumentTemplates
(
    Name,
    Description,
    SampleImagePath,
    FieldsJson
)
SELECT
    'TechSolutions Rechnung',

    'Standardrechnung TechSolutions GmbH – RE-YYYY-NNNN',

    'lieferschein.png',

'[
  {"FieldName":"Rechnungsnummer","FieldKey":"invoice_no","X":900,"Y":295,"W":240,"H":34,"Type":"Text"},
  {"FieldName":"Kundennummer","FieldKey":"customer_no","X":900,"Y":335,"W":200,"H":34,"Type":"Text"},
  {"FieldName":"Rechnungsdatum","FieldKey":"invoice_date","X":900,"Y":375,"W":200,"H":34,"Type":"Date"},
  {"FieldName":"Fälligkeitsdatum","FieldKey":"due_date","X":900,"Y":415,"W":200,"H":34,"Type":"Date"},
  {"FieldName":"Lieferdatum","FieldKey":"delivery_date","X":900,"Y":455,"W":200,"H":34,"Type":"Date"},
  {"FieldName":"Empfänger Firma","FieldKey":"recipient_company","X":60,"Y":295,"W":360,"H":34,"Type":"Text"},
  {"FieldName":"Empfänger Name","FieldKey":"recipient_name","X":60,"Y":330,"W":360,"H":34,"Type":"Text"},
  {"FieldName":"Empfänger Adresse","FieldKey":"recipient_address","X":60,"Y":365,"W":360,"H":34,"Type":"Text"},
  {"FieldName":"Zwischensumme","FieldKey":"subtotal_net","X":1100,"Y":870,"W":220,"H":34,"Type":"Number"},
  {"FieldName":"USt. 19%","FieldKey":"vat","X":1100,"Y":910,"W":220,"H":34,"Type":"Number"},
  {"FieldName":"Gesamtbetrag","FieldKey":"total_gross","X":1100,"Y":955,"W":220,"H":40,"Type":"Number"},
  {"FieldName":"IBAN","FieldKey":"iban","X":220,"Y":1020,"W":380,"H":34,"Type":"Text"},
  {"FieldName":"BIC","FieldKey":"bic","X":930,"Y":1020,"W":240,"H":34,"Type":"Text"}
]'
WHERE NOT EXISTS
(
    SELECT 1
    FROM DocumentTemplates
    WHERE Name='TechSolutions Rechnung'
);