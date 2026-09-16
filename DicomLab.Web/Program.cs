using DicomLab.Web.Models;
using DicomLab.Web.Services.Dicom;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using pruebasdicom.Services;

var builder = WebApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection("Dicom").Get<DicomLabOptions>() ?? new();
if (settings.MaxUploadMb is < 1 or > 512 || settings.UploadCacheMb < settings.MaxUploadMb ||
    settings.UploadLifetimeMinutes < 1 || settings.MaxFramePixels < 1)
    throw new InvalidOperationException("La configuración de límites DICOM no es válida.");
builder.Services.Configure<DicomLabOptions>(builder.Configuration.GetSection("Dicom"));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = settings.MaxUploadMb * 1024L * 1024 + 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = settings.MaxUploadMb * 1024L * 1024 + 1024 * 1024);
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o => { o.IdleTimeout = TimeSpan.FromMinutes(settings.UploadLifetimeMinutes); o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; });
builder.Services.AddFellowOakDicom().AddImageManager<ImageSharpImageManager>();
builder.Services.AddSingleton<DicomReaderService>();
builder.Services.AddSingleton<DicomFileService>();
builder.Services.AddHostedService(p => p.GetRequiredService<DicomFileService>());
builder.Services.AddTransient<DicomImageService>();
builder.Services.AddTransient<DicomNetworkService>();
builder.Services.AddSingleton<DicomLocalServers>();
builder.Services.AddHostedService(p => p.GetRequiredService<DicomLocalServers>());

var app = builder.Build();

DicomSetupBuilder.UseServiceProvider(app.Services);
app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePages(async context =>
{
    context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
    await context.HttpContext.Response.WriteAsync(context.HttpContext.Response.StatusCode == 413
        ? "El archivo supera el tamaño de upload permitido. Vuelve al visor y selecciona un archivo más pequeño."
        : "No se pudo procesar la solicitud. Vuelve al laboratorio e intenta nuevamente.");
});

app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dicom}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
