using BelegOCR.Models;
using Dapper;
using Microsoft.Data.Sqlite;

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
}

// ── TemplateRepository ───────────────────────────────────────────────────────

public class TemplateRepository(IConfiguration cfg) : ITemplateRepository
{
    private SqliteConnection ConnSqlLite() =>
        new(cfg.GetConnectionString("SqlLite"));

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

    public async Task<int> CreateAsync(DocumentTemplate t)
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
    }

    public async Task UpdateAsync(DocumentTemplate t)
    {
        using var db = ConnSqlLite();

        await db.ExecuteAsync("""
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
            });
    }

    public async Task DeleteAsync(int id)
    {
        using var db = ConnSqlLite();

        await db.ExecuteAsync(
            "DELETE FROM DocumentTemplates WHERE Id = @id",
            new { id });
    }

    // Nur die JSON-Felder-Spalte aktualisieren (nach Canvas-Bearbeitung)
    public async Task SaveFieldsAsync(int templateId, string fieldsJson)
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
    }
}

// ── DocumentRepository ───────────────────────────────────────────────────────

public class DocumentRepository(IConfiguration cfg) : IDocumentRepository
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

    public async Task<int> CreateAsync(Document doc)
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
    }

    public async Task UpdateStatusAsync(
        int id,
        string status,
        string? error = null)
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
    }

    // Extrahierte Werte als JSON speichern + Status auf Processed setzen
    public async Task SaveExtractedAsync(
        int id,
        string extractedJson)
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
    }
}