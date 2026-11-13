using BelegOCR.Models;
using BelegOCR.RepositoriesSqlLite;
using BelegOCR.Services.Interfaces;

namespace BelegOCR.Services
{
 
        public class DocumentService : IDocumentService
        {
            private IDocumentRepository _documentRepository;

            public DocumentService(IDocumentRepository documentRepository)
            {
                _documentRepository = documentRepository;
            }

            public Task<int> CreateAsync(Document doc)
            {
                var val = _documentRepository.CreateAsync(doc);
                 return val;

            }

            public Task<IEnumerable<Document>> GetAllAsync()
            {
                return _documentRepository.GetAllAsync();
            }

            public Task<Document?> GetByIdAsync(int id)
            {
               var doc = _documentRepository.GetByIdAsync(id);
                return doc;
            }

            public Task UpdateStatusAsync(int id, string status, string? error = null)
            {
                return _documentRepository.UpdateStatusAsync(id, status, error);
            }

            public Task SaveExtractedAsync(int id, string extractedJson)
            {
                return _documentRepository.SaveExtractedAsync(id, extractedJson);
            }

            public Task SavePositionsAsync(int documentId, string fieldKey, IEnumerable<DocumentPosition> positions)
            {
                return _documentRepository.SavePositionsAsync(documentId, fieldKey, positions);
            }
        }
    
}
