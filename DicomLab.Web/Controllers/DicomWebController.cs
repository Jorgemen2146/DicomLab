using DicomLab.Web.Models;
using DicomLab.Web.Services.Dicom;
using DicomLab.Web.Services.DicomWeb;
using DicomLab.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DicomLab.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DicomWebController(DicomWebService service, DicomFileService files,
    IOptions<DicomWebOptions> options, ILogger<DicomWebController> logger) : Controller
{
    [HttpGet] public IActionResult Index() => View();
    [HttpGet] public IActionResult Stow() => View(Default());
    [HttpGet] public IActionResult Qido() => View(Default());
    [HttpGet] public IActionResult Wado() => View(Default());
    private DicomWebViewModel Default() => new() { BaseUrl = options.Value.BaseUrl };

    [HttpPost]
    public async Task<IActionResult> Stow(DicomWebViewModel model)
    {
        if (ModelState.IsValid) model.Result = await service.StowAsync(model.BaseUrl, model.Files, HttpContext.RequestAborted);
        return View(model);
    }

    [HttpPost] public Task<IActionResult> Qido(DicomWebViewModel model) => Query(model, "Studies");
    [HttpPost] public Task<IActionResult> Series(DicomWebViewModel model) => Query(model, "Series");
    [HttpPost] public Task<IActionResult> Instances(DicomWebViewModel model) => Query(model, "Instances");

    private async Task<IActionResult> Query(DicomWebViewModel model, string level)
    {
        model.Level = level;
        if (ModelState.IsValid)
            model.Result = await service.QueryAsync(model.BaseUrl, level, model.StudyInstanceUID, model.SeriesInstanceUID,
                new() { ["PatientName"] = model.PatientName, ["PatientID"] = model.PatientID, ["StudyDate"] = model.StudyDate,
                    ["StudyInstanceUID"] = model.StudyInstanceUID, ["AccessionNumber"] = model.AccessionNumber }, HttpContext.RequestAborted);
        return View("Qido", model);
    }

    [HttpPost]
    public async Task<IActionResult> Wado(DicomWebViewModel model, string destination = "download")
    {
        if (!ModelState.IsValid) return View(model);
        model.Result = await service.RetrieveAsync(model.BaseUrl, model.StudyInstanceUID, model.SeriesInstanceUID, model.SOPInstanceUID, HttpContext.RequestAborted);
        if (!model.Result.IsSuccess || model.Result.DicomBytes is null) return View(model);
        if (destination == "viewer")
        {
            try
            {
                HttpContext.Session.SetString("DicomLab", "active");
                var id = files.Remember(HttpContext.Session.Id, model.Result.DicomBytes, model.SOPInstanceUID + ".dcm");
                return RedirectToAction("Viewer", "Dicom", new { id });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo abrir WADO en el visor");
                model.Result.Error = "No se pudo cargar el DICOM recuperado en la memoria del visor: " + ex.Message;
                return View(model);
            }
        }
        return File(model.Result.DicomBytes, "application/dicom", model.SOPInstanceUID + ".dcm");
    }
}
