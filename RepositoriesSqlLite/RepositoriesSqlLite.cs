using BelegOCR.Models;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Xml;
using Microsoft.Data.SqlClient;

namespace BelegOCR.RepositoriesSqlLite;

// ── Interfaces ───────────────────────────────────────────────────────────────

public interface ITemplateRepository
{
    Task<IEnumerable<DocumentTemplate>> GetAllAsync();
    Task<DocumentTemplate?> GetByIdAsync(int id);
    Task<int> CreateAsync(DocumentTemplate t);
    Task UpdateAsync(DocumentTemplate t);
    Task DeleteAsync(int id);
    Task SaveFieldsAsync(int templateId, string fieldsJson);
}

public interface IDocumentRepository
{
    Task<IEnumerable<Document>> GetAllAsync();
    Task<Document?> GetByIdAsync(int id);
    Task<int> CreateAsync(Document doc);
    Task UpdateStatusAsync(int id, string status, string? error = null);
    Task SaveExtractedAsync(int id, string extractedJson);
    Task SavePositionsAsync(int documentId, string fieldKey, IEnumerable<DocumentPosition> positions);
}

// ── TemplateRepository ───────────────────────────────────────────────────────

public class TemplateRepository(IConfiguration cfg, SqlLiteWriterGate sqlLiteWriterGate) : ITemplateRepository
{
    private Connections.Connections _connections = new Connections.Connections(cfg);
    // private SqliteConnection ConnSqlLite() =>
    //     new(cfg.GetConnectionString("SqlLite"));
    private SqliteConnection ConnSqlLite() => _connections.ConnSqlLite;

    public async Task<IEnumerable<DocumentTemplate>> GetAllAsync()
    {
        using var db = ConnSqlLite();

        return await db.QueryAsync<DocumentTemplate>(
            "SELECT Id, Name, Description, SampleImagePath, FieldsJson, CreatedAt, UpdatedAt " +
            "FROM DocumentTemplates ORDER BY CreatedAt DESC");
    }

    public async Task<DocumentTemplate?> GetByIdAsync(int id)
    {
        using var db = ConnSqlLite();
        return await db.QueryFirstOrDefaultAsync<DocumentTemplate>(
            "SELECT Id, Name, Description, SampleImagePath, FieldsJson, CreatedAt, UpdatedAt " +
            "FROM DocumentTemplates WHERE Id = @id",
            new { id });
    }


    public Task<int> CreateAsync(DocumentTemplate t) =>
    sqlLiteWriterGate.WriteAsync<int>(async () =>
    {
        using var db = ConnSqlLite();
        return await db.ExecuteScalarAsync<int>("""
            INSERT INTO DocumentTemplates
                (Name, Description, SampleImagePath, FieldsJson)
            VALUES
                (@Name, @Description, @SampleImagePath, @FieldsJson)
            RETURNING Id
            """,
            new
            {
                t.Name,
                t.Description,
                t.SampleImagePath,
                t.FieldsJson
            });
    });

    public async Task UpdateAsync(DocumentTemplate t) =>
    await sqlLiteWriterGate.WriteAsync(async () => {
        {
            using var db = ConnSqlLite();

            return await db.ExecuteAsync("""
            UPDATE DocumentTemplates
            SET Name = @Name,
                Description = @Description,
                UpdatedAt = datetime('now')
            WHERE Id = @Id
            """,
                 new
                 {
                     t.Name,
                     t.Description,
                     t.Id
                 }
                 );
        }
    });


    public Task DeleteAsync(int id) =>
     sqlLiteWriterGate.WriteAsync<DocumentTemplate>(async () =>
     {
         using var db = ConnSqlLite();

         await db.ExecuteAsync(
             "DELETE FROM DocumentTemplates WHERE Id = @id",
             new { id });
     });

    // Nur die JSON-Felder-Spalte aktualisieren (nach Canvas-Bearbeitung)
    public Task SaveFieldsAsync(int templateId, string fieldsJson) =>
        sqlLiteWriterGate.WriteAsync<DocumentTemplate>(async () =>
        {
            using var db = ConnSqlLite();

            await db.ExecuteAsync("""
            UPDATE DocumentTemplates
            SET FieldsJson = @fieldsJson,
                UpdatedAt = datetime('now')
            WHERE Id = @templateId
            """,
                new
                {
                    fieldsJson,
                    templateId
                });
        });
}
// ── DocumentRepository ───────────────────────────────────────────────────────

public class DocumentRepository(IConfiguration cfg, SqlLiteWriterGate sqlLiteWriterGate) : IDocumentRepository
{
    private SqliteConnection ConnSqlLite() =>
        new(cfg.GetConnectionString("SqlLite"));

    public async Task<IEnumerable<Document>> GetAllAsync()
    {
        using var db = ConnSqlLite();

        return await db.QueryAsync<Document>("""
        SELECT Id,
               TemplateId,
               OriginalFileName,
               FilePath,
               Status,
               ExtractedJson,
               ProcessedAt,
               CreatedAt,
               ErrorMessage
        FROM Documents
        ORDER BY CreatedAt DESC
        """);
    }

    public async Task<Document?> GetByIdAsync(int id)
    {
        using var db = ConnSqlLite();

        return await db.QueryFirstOrDefaultAsync<Document>("""
        SELECT Id,
               TemplateId,
               OriginalFileName,
               FilePath,
               Status,
               ExtractedJson,
               ProcessedAt,
               CreatedAt,
               ErrorMessage
        FROM Documents
        WHERE Id = @id
        """,
            new { id });
    }

    public Task<int> CreateAsync(Document doc) =>
        sqlLiteWriterGate.WriteAsync(async () =>
        {
            using var db = ConnSqlLite();

            return await db.ExecuteScalarAsync<int>("""
            INSERT INTO Documents
                (TemplateId, OriginalFileName, FilePath, Status)            
            VALUES
                (@TemplateId, @OriginalFileName, @FilePath, @Status)
            RETURNING Id
            """,
                new
                {
                    doc.TemplateId,
                    doc.OriginalFileName,
                    doc.FilePath,
                    doc.Status
                });
        });

    public Task UpdateStatusAsync(
        int id,
        string status,
        string? error = null) =>
        sqlLiteWriterGate.WriteAsync<Document>(async () =>
        {
            using var db = ConnSqlLite();

            await db.ExecuteAsync("""
            UPDATE Documents
            SET Status = @status,
                ErrorMessage = @error
            WHERE Id = @id
            """,
                new
                {
                    status,
                    error,
                    id
                });
        });

    // Extrahierte Werte als JSON speichern + Status auf Processed setzen
    public Task SaveExtractedAsync(
        int id,
        string extractedJson) =>
        sqlLiteWriterGate.WriteAsync<Document>(async () =>
        {
            using var db = ConnSqlLite();

            await db.ExecuteAsync("""
            UPDATE Documents
            SET ExtractedJson = @extractedJson,
                Status = 'Processed',
                ProcessedAt = datetime('now')
            WHERE Id = @id
            """,
                new
                {
                    extractedJson,
                    id
                });
        });

    // Tabelle wird bei bestehenden Datenbanken (ohne migration_add_document_positions.sql)
    // beim ersten Zugriff angelegt.
    private const string EnsurePositionsTable = """
        CREATE TABLE IF NOT EXISTS DocumentPositions
        (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            DocumentId  INTEGER NOT NULL,
            FieldKey    TEXT    NOT NULL,
            RowNo       INTEGER NOT NULL,
            ValuesJson  TEXT    NOT NULL DEFAULT '{}',
            Uncertain   INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY(DocumentId) REFERENCES Documents(Id)
        );
        """;

    // Bestehende Tabellen aus der Zeit fester Spalten (PosNo, Description, Quantity,
    // UnitPrice, Total) um ValuesJson ergänzen. Die alten Spalten bleiben ungenutzt
    // stehen (nullable), damit keine Daten verloren gehen.
    private static async Task EnsurePositionsSchemaAsync(SqliteConnection db)
    {
        await db.ExecuteAsync(EnsurePositionsTable);
        var columns = (await db.QueryAsync<string>("SELECT name FROM pragma_table_info('DocumentPositions')"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (columns.Contains("ValuesJson"))
            return;

        await db.ExecuteAsync("ALTER TABLE DocumentPositions ADD COLUMN ValuesJson TEXT NOT NULL DEFAULT '{}'");

        string[] legacy = ["PosNo", "Description", "Quantity", "UnitPrice", "Total"];
        if (legacy.All(columns.Contains))
            await db.ExecuteAsync("""
            UPDATE DocumentPositions
            SET ValuesJson = json_object(
                    'pos_no', PosNo, 'description', Description, 'quantity', Quantity,
                    'unit_price', UnitPrice, 'total', Total)
            """);
    }

    // Positionen einer Tabelle ersetzen (bei erneuter Verarbeitung alte Zeilen verwerfen)
    public Task SavePositionsAsync(
        int documentId,
        string fieldKey,
        IEnumerable<DocumentPosition> positions) =>
        sqlLiteWriterGate.WriteAsync<DocumentPosition>(async () =>
        {
            using var db = ConnSqlLite();
            await db.OpenAsync();
            await EnsurePositionsSchemaAsync(db);

            using var tx = db.BeginTransaction();

            await db.ExecuteAsync(
                "DELETE FROM DocumentPositions WHERE DocumentId = @documentId AND FieldKey = @fieldKey",
                new { documentId, fieldKey }, tx);

            await db.ExecuteAsync("""
            INSERT INTO DocumentPositions
                (DocumentId, FieldKey, RowNo, ValuesJson, Uncertain)
            VALUES
                (@DocumentId, @FieldKey, @RowNo, @ValuesJson, @Uncertain)
            """,
                positions.Select(p => new
                {
                    DocumentId = documentId,
                    FieldKey   = fieldKey,
                    p.RowNo,
                    p.ValuesJson,
                    p.Uncertain
                }), tx);

            tx.Commit();
        });
}