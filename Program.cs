using BelegOCR.Repositories;
using BelegOCR.Services;
using BelegOCR.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Dapper Repositories
builder.Services.AddScoped<ITemplateRepository, TemplateRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();

// Services
builder.Services.AddScoped<IOcrService, OcrService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Document}/{action=Index}/{id?}");

app.Run();
