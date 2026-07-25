using AspNetCoreGeneratedDocument;
using BelegOCR.Models;
using BelegOCR.RepositoriesSqlLite;
using BelegOCR.Services.Interfaces;

namespace BelegOCR.Services
{
    public class TemplateService : ITemplateService
    {
        private ITemplateRepository _templateRepository;

        public TemplateService(ITemplateRepository templateRepository)
        {
            _templateRepository = templateRepository;
        }
        public Task<int> CreateAsync(DocumentTemplate t)
        {
           return _templateRepository.CreateAsync(t);
        }

        public Task DeleteAsync(int id)
        {
            return _templateRepository.DeleteAsync(id);
        }

        public Task<IEnumerable<DocumentTemplate>> GetAllAsync()
        {
            return _templateRepository.GetAllAsync();
        }

        public Task<DocumentTemplate?> GetByIdAsync(int id)
        {
            return _templateRepository.GetByIdAsync(id);
        }

        public Task SaveFieldsAsync(int templateId, string fieldsJson)
        {
            return _templateRepository.SaveFieldsAsync(templateId, fieldsJson);
        }

        public Task UpdateAsync(DocumentTemplate t)
        {
            return _templateRepository.UpdateAsync(t);
        }
    }
}
