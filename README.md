# BelegOCR – ASP.NET Core 9 MVC

Belegdaten (Rechnungsnummer, Kundennummer etc.) per Canvas-Template markieren
und per Tesseract OCR automatisch extrahieren. Speicherung in SQL Server via Dapper + JSON.

---

## Projektstruktur

```
BelegOCR/
├── Controllers/
│   ├── TemplateController.cs   – Template anlegen, Canvas-Felder speichern
│   └── DocumentController.cs  – Beleg hochladen, verarbeiten, vergleichen
├── Models/
│   └── Models.cs               – Entitäten + ViewModels (FieldsJson / ExtractedJson)
├── Repositories/
│   └── Repositories.cs         – Dapper-Repositories (ITemplateRepo, IDocumentRepo)
├── Services/
│   └── OcrService.cs           – Tesseract 5, Bildannotierung via ImageSharp
├── Views/
│   ├── Template/
│   │   ├── Index.cshtml        – Template-Liste
│   │   ├── Create.cshtml       – Bild hochladen
│   │   └── Edit.cshtml         – Canvas-Editor (Felder ziehen, benennen, speichern)
│   └── Document/
│       ├── Index.cshtml        – Beleg-Liste
│       ├── Upload.cshtml       – Beleg + Template auswählen
│       ├── Compare.cshtml      – Original vs. annotiertes Overlay nebeneinander
│       └── Details.cshtml      – Extrahierte Felder + JSON-Rohdaten
├── Data/
│   └── schema.sql              – SQL Server Tabellen
├── wwwroot/
│   ├── css/site.css
│   ├── uploads/                – hochgeladene Bilder + Belege
│   └── tessdata/               – Tesseract Sprachdaten (deu.traineddata)
└── Program.cs
```

---

## Setup

### 1. SQL Server

```sql
-- schema.sql ausführen (Data/schema.sql)
```

### 2. Tesseract Sprachdaten

```
wwwroot/tessdata/deu.traineddata   -- https://github.com/tesseract-ocr/tessdata
wwwroot/tessdata/eng.traineddata   -- (optional für gemischte Dokumente)
```

### 3. Connection String

`appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Server=.;Database=BelegOCR;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

### 4. Starten

```bash
dotnet restore
dotnet run
```

---

## Workflow

```
1. Template anlegen  →  /Template/Create
   Bild hochladen (Beispielrechnung als PNG/JPG)

2. Canvas-Editor     →  /Template/Edit/{id}
   Rechteck über "Rechnungsnummer" ziehen → Namen vergeben → Speichern

3. Beleg hochladen   →  /Document/Upload
   Belegbild + Template auswählen → automatische OCR-Extraktion

4. Vergleichsansicht →  /Document/Compare/{id}
   Original | Template-Overlay + extrahierte Werte

5. Details           →  /Document/Details/{id}
   Alle Felder mit Konfidenzwert + JSON-Rohdaten
```

---

## Datenbankschema (vereinfacht)

```
DocumentTemplates
  Id | Name | SampleImagePath | FieldsJson (JSON-Array) | CreatedAt | UpdatedAt

Documents
  Id | TemplateId | OriginalFileName | FilePath | Status | ExtractedJson (JSON-Objekt) | ProcessedAt
```

### FieldsJson Beispiel
```json
[
  {"FieldName":"Rechnungsnummer","FieldKey":"invoice_no","X":120,"Y":80,"W":200,"H":30,"Type":"Text"},
  {"FieldName":"Kundennummer",   "FieldKey":"customer_no","X":120,"Y":120,"W":150,"H":30,"Type":"Text"},
  {"FieldName":"Datum",          "FieldKey":"date",       "X":400,"Y":80,"W":120,"H":30,"Type":"Date"}
]
```

### ExtractedJson Beispiel
```json
{
  "invoice_no":  {"value":"RE-2024-0042","confidence":94.2,"fieldName":"Rechnungsnummer"},
  "customer_no": {"value":"KD-4711",     "confidence":91.7,"fieldName":"Kundennummer"},
  "date":        {"value":"15.01.2024",  "confidence":96.1,"fieldName":"Datum"}
}
```

---

## Pakete

| Paket | Version | Zweck |
|---|---|---|
| Dapper | 2.1.35 | SQL Server Datenzugriff |
| Microsoft.Data.SqlClient | 5.2.2 | SQL Server Treiber |
| Tesseract | 5.2.0 | OCR Engine |
| SixLabors.ImageSharp | 3.1.5 | Bildverarbeitung |
| SixLabors.ImageSharp.Drawing | 2.1.4 | Annotierungen zeichnen |
