using BelegOCR.Models;
using BelegOCR.RepositoriesSqlLite;
using BelegOCR.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace BelegOCR.Controllers;

public class TemplateController(
    IWebHostEnvironment env,
    ITemplateService templateService,
    ILogger<TemplateController> logger) : Controller
{
    private string UploadsPath => Path.Combine(env.WebRootPath, "uploads");


    // GET /Template
    public async Task<IActionResult> Index()
    {
        var templates = await templateService.GetAllAsync();
        return View(templates);
    }

    // GET /Template/Create
    public IActionResult Create() => View(new DocumentTemplate());

    //POST /Template/Create  – Bild hochladen +Template anlegen
       [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DocumentTemplate model, IFormFile sampleImage)
    {
        if (sampleImage == null || sampleImage.Length == 0)
        {
            ModelState.AddModelError("", "Bitte ein Beispielbild hochladen.");
            return View(model);
        }

        Directory.CreateDirectory(UploadsPath);

        var ext = Path.GetExtension(sampleImage.FileName);
        var fileName = $"template_{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(UploadsPath, fileName);

        await using (var fs = new FileStream(filePath, FileMode.Create))
            await sampleImage.CopyToAsync(fs);

        model.SampleImagePath = fileName;
        model.FieldsJson = "[]";

        var id = await templateService.CreateAsync(model);
        return RedirectToAction(nameof(Edit), new { id });
    }

    // GET /Template/Edit/5  – Canvas-Editor
    public async Task<IActionResult> Edit(int id)
    {
        var template = await templateService.GetByIdAsync(id);
        if (template == null) return NotFound();

        var imgPath = Path.Combine(UploadsPath, template.SampleImagePath);
        var (w, h) = GetImageDimensions(imgPath);

        var vm = new TemplateEditorViewModel
        {
            Template = template,
            ImageUrl = $"/uploads/{template.SampleImagePath}",
            ImageWidth = w,
            ImageHeight = h
        };
        return View(vm);
    }

    // POST /Template/SaveFields  – Canvas-Felder als JSON speichern
    [HttpPost]
    public async Task<IActionResult> SaveFields([FromBody] SaveFieldsRequest req)
    {
        if (req.TemplateId <= 0) return BadRequest("Ungültige Template-ID");

        var json = JsonSerializer.Serialize(req.Fields);
        await templateService.SaveFieldsAsync(req.TemplateId, json);

        return Ok(new { success = true, message = "Template gespeichert." });
    }

    // POST /Template/Delete/5
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await templateService.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }

    // ── Hilfsmethoden ────────────────────────────────────────────────────────

    private static (int w, int h) GetImageDimensions(string path)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(path);
            return (info?.Width ?? 800, info?.Height ?? 1100);
        }
        catch { return (800, 1100); }
    }
}

public class SaveFieldsRequest
    {
        public int TemplateId { get; set; }
        public List<TemplateFieldDef> Fields { get; set; } = [];
    }

