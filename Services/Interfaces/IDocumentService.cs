using BelegOCR.Models;

namespace BelegOCR.Services.Interfaces
{
    public interface IDocumentService
    {
        Task<IEnumerable<Document>> GetAllAsync();
        Task<Document?> GetByIdAsync(int id);
        Task<int> CreateAsync(Document doc);
        Task UpdateStatusAsync(int id, string status, string? error = null);
        Task SaveExtractedAsync(int id, string extractedJson);
    }
    
}
