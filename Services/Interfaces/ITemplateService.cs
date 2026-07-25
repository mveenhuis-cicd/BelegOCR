using BelegOCR.Models;

namespace BelegOCR.Services.Interfaces
{
    public interface ITemplateService
    {
        Task<IEnumerable<DocumentTemplate>> GetAllAsync();
        Task<DocumentTemplate?> GetByIdAsync(int id);
        Task<int> CreateAsync(DocumentTemplate t);
        Task UpdateAsync(DocumentTemplate t);
        Task DeleteAsync(int id);
        Task SaveFieldsAsync(int templateId, string fieldsJson);
    }
}
