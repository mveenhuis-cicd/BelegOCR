using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BelegOCR.RepositoriesSqlLite;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BelegOCR.Tests
{
    public class DocumentRepositoryTests
    {
        [Fact]
        public async Task SaveExtractedAsync_UpdatesDocument()
        {
            // Arrange - create temp sqlite file and schema
            var dbPath = Path.Combine(Path.GetTempPath(), $"belegocr_test_{Guid.NewGuid()}.db");
            var connectionString = $"Data Source={dbPath}";

            try
            {
                using (var conn = new SqliteConnection(connectionString))
                {
                    conn.Open();

                    // create Documents table (minimal columns used by test)
                    conn.Execute(@"CREATE TABLE Documents
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TemplateId INTEGER,
                        OriginalFileName TEXT NOT NULL,
                        FilePath TEXT NOT NULL,
                        Status TEXT NOT NULL DEFAULT 'Pending',
                        ExtractedJson TEXT,
                        ProcessedAt TEXT,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        ErrorMessage TEXT
                    );");

                    // insert a document and get id
                    var id = conn.ExecuteScalar<long>("INSERT INTO Documents (TemplateId, OriginalFileName, FilePath, Status) VALUES (NULL, @ofn, @fp, 'Pending'); SELECT last_insert_rowid();",
                        new { ofn = "test.pdf", fp = "uploads/test.pdf" });

                    // build configuration with connection string
                    var config = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string>
                        {
                            ["ConnectionStrings:SqlLite"] = connectionString
                        })
                        .Build();

                    var gate = new SqlLiteWriterGate();
                    var repo = new DocumentRepository(config, gate);

                    var extractedJson = "{\"field\":\"value\"}";

                    // Act
                    await repo.SaveExtractedAsync((int)id, extractedJson);

                    // Assert - read back from DB
                    var row = conn.QuerySingle(@"SELECT ExtractedJson, Status, ProcessedAt FROM Documents WHERE Id = @id", new { id });

                    Assert.Equal(extractedJson, (string)row.ExtractedJson);
                    Assert.Equal("Processed", (string)row.Status);
                    Assert.False(string.IsNullOrEmpty((string)row.ProcessedAt));
                }
            }
            finally
            {
                // cleanup
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }
    }
}
