using DicomLab.Web.Models;
using DicomLab.Web.Services.Dicom;
using DicomLab.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DicomLab.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public partial class DicomController(DicomFileService files, DicomImageService images,
    IOptions<DicomLabOptions> options, ILogger<DicomController> logger) : Controller
{
    private string Owner
    {
        get
        {
            HttpContext.Session.SetString("DicomLab", "active");
            return HttpContext.Session.Id;
        }
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Viewer(string? id, int frame = 0)
    {
        if (string.IsNullOrEmpty(id)) return View(new ViewerViewModel { MaxUploadMb = options.Value.MaxUploadMb });
        try { return View(await files.DescribeAsync(id, Owner, frame)); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo leer el DICOM del visor");
            return View(new ViewerViewModel { Error = "No se pudo abrir el archivo o frame. Vuelve a cargar el DICOM.", MaxUploadMb = options.Value.MaxUploadMb });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Viewer(IFormFile? file)
    {
        try
        {
            var bytes = await files.ReadUploadAsync(file, HttpContext.RequestAborted);
            return RedirectToAction(nameof(Viewer), new { id = files.Remember(Owner, bytes, file?.FileName) });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error validando upload DICOM");
            return View(new ViewerViewModel { Error = "No se pudo cargar el archivo. Comprueba que sea un DICOM válido y respete el límite de tamaño.", MaxUploadMb = options.Value.MaxUploadMb });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Image(string id, int frame = 0)
    {
        try { return File(await images.RenderAsync(id, Owner, frame, HttpContext.RequestAborted), "image/png"); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error renderizando frame {Frame}", frame);
            return Problem(statusCode: 422, title: "No se pudo renderizar este frame. El archivo pudo expirar, no contener píxeles o requerir una sintaxis/codec no disponible. Los metadatos siguen disponibles.");
        }
    }
}
