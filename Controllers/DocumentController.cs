using Microsoft.AspNetCore.Mvc;
using BelegOCR.Models;

using System.Text.Json;
using BelegOCR.Services.Interfaces;


namespace BelegOCR.Controllers;

public class DocumentController(
   
    ITemplateService templateService,
    IDocumentService documentService,
    IOcrService          ocrService,
    IWebHostEnvironment  env,
    ILogger<DocumentController> logger) : Controller
{
    private string UploadsPath => Path.Combine(env.WebRootPath, "uploads");

    // GET /Document
    public async Task<IActionResult> Index()
    {
        var docs      = await documentService.GetAllAsync();
        var templates = await templateService.GetAllAsync();
        return View(new DocumentListViewModel
        {
            Documents = docs.ToList(),
            Templates = templates.ToList()
        });
    }

    // GET /Document/Upload
    public async Task<IActionResult> Upload()
    {
        var templates = await templateService.GetAllAsync();
        ViewBag.Templates = templates;
        return View();
    }

    // POST /Document/Upload  – Beleg hochladen + sofort verarbeiten
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, int? templateId)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("", "Bitte eine Datei auswählen.");
            ViewBag.Templates = await templateService.GetAllAsync();
            return View();
        }

        Directory.CreateDirectory(UploadsPath);

        var ext      = Path.GetExtension(file.FileName);
        var fileName = $"doc_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(UploadsPath, fileName);

        await using (var fs = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(fs);

        var doc = new Document
        {
            OriginalFileName = file.FileName,
            FilePath         = fileName,
            TemplateId       = templateId,
            Status           = "Pending"
        };

        var docId = await documentService.CreateAsync(doc);

        // Sofort verarbeiten wenn Template vorhanden
        if (templateId.HasValue)
        {
            await ProcessDocumentInternalAsync(docId, filePath, templateId.Value);
            return RedirectToAction(nameof(Compare), new { id = docId });
        }

        return RedirectToAction(nameof(Index));
    }

    // GET /Document/Compare/5  – Original vs. annotiertes Bild
    public async Task<IActionResult> Compare(int id)
    {
        var doc = await documentService.GetByIdAsync(id);
        if (doc == null) return NotFound();

        DocumentTemplate? template = null;
        var fields = new List<TemplateFieldDef>();

        if (doc.TemplateId.HasValue)
        {
            template = await templateService.GetByIdAsync(doc.TemplateId.Value);
            fields   = template?.Fields ?? [];
        }

        // Annotiertes Bild erzeugen falls noch nicht vorhanden
        var annotatedName = $"ann_{doc.FilePath}";
        var annotatedPath = Path.Combine(UploadsPath, annotatedName);

        if (!System.IO.File.Exists(annotatedPath) && fields.Count > 0)
        {
            var srcPath = Path.Combine(UploadsPath, doc.FilePath);
            annotatedName = await ocrService.CreateAnnotatedImageAsync(
                srcPath, fields, doc.ExtractedValues, UploadsPath);
        }

        var vm = new DocumentCompareViewModel
        {
            Document          = doc,
            Template          = template,
            Fields            = fields,
            OriginalImageUrl  = $"/uploads/{doc.FilePath}",
            AnnotatedImageUrl = $"/uploads/{annotatedName}"
        };

        return View(vm);
    }

    // POST /Document/Process/5  – Nachträgliche Verarbeitung mit Template
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(int id, int templateId)
    {
        var doc = await documentService.GetByIdAsync(id);
        if (doc == null) return NotFound();

        var filePath = Path.Combine(UploadsPath, doc.FilePath);
        await ProcessDocumentInternalAsync(id, filePath, templateId);

        return RedirectToAction(nameof(Compare), new { id });
    }

    // GET /Document/Details/5  – Extrahierte Felder als Tabelle
    public async Task<IActionResult> Details(int id)
    {
        var doc = await documentService.GetByIdAsync(id);
        if (doc == null) return NotFound();

        if (doc.TemplateId.HasValue)
            ViewBag.Template = await templateService.GetByIdAsync(doc.TemplateId.Value);

        return View(doc);
    }

    // ── Interne Verarbeitung ─────────────────────────────────────────────────

    private async Task ProcessDocumentInternalAsync(int docId, string filePath, int templateId)
    {
        try
        {
            var template = await templateService.GetByIdAsync(templateId);
            if (template == null) return;

            var extractedJson = await ocrService.ProcessDocumentAsync(filePath, template.Fields);
            await documentService.SaveExtractedAsync(docId, extractedJson);

            // TemplateId am Dokument setzen
            var doc = await documentService.GetByIdAsync(docId);
            if (doc != null && doc.TemplateId == null)
            {
                // kleines Update via Status-Methode reicht nicht – direktes SQL über Repo
                await documentService.UpdateStatusAsync(docId, "Processed");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fehler beim Verarbeiten von Dokument {Id}", docId);
            await documentService.UpdateStatusAsync(docId, "Error", ex.Message);
        }
    }
}
