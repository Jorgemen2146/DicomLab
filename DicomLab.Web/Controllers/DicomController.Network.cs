using DicomLab.Web.Services.Dicom;
using DicomLab.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DicomLab.Web.Controllers;

public partial class DicomController
{
    [HttpGet] public IActionResult Echo() => NetworkPage("Echo");
    [HttpGet] public IActionResult Find() => NetworkPage("Find");
    [HttpGet] public IActionResult Move(string? studyUid) => NetworkPage("Move", studyUid);
    [HttpGet] public IActionResult Store() => NetworkPage("Store");

    [HttpPost] public Task<IActionResult> Echo(NetworkViewModel model, [FromServices] DicomNetworkService network) =>
        RunNetworkAsync("Echo", model, () => network.EchoAsync(model, HttpContext.RequestAborted));
    [HttpPost] public Task<IActionResult> Find(NetworkViewModel model, [FromServices] DicomNetworkService network) =>
        RunNetworkAsync("Find", model, () => network.FindAsync(model, HttpContext.RequestAborted));
    [HttpPost] public Task<IActionResult> Move(NetworkViewModel model, [FromServices] DicomNetworkService network) =>
        RunNetworkAsync("Move", model, () => network.MoveAsync(model, HttpContext.RequestAborted));
    [HttpPost] public Task<IActionResult> Store(NetworkViewModel model, [FromServices] DicomNetworkService network) =>
        RunNetworkAsync("Store", model, () => network.StoreAsync(model, HttpContext.RequestAborted));

    [HttpPost]
    public async Task<IActionResult> StartServer(bool destination, [FromServices] DicomLocalServers servers)
    {
        try
        {
            await servers.StartNodeAsync(destination, HttpContext.RequestAborted);
            TempData["ServerMessage"] = destination ? "MOVE_DESTINATION está escuchando." : "LOCAL_PACS está escuchando.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo iniciar el SCP");
            TempData["ServerError"] = "No se pudo iniciar el SCP. Comprueba que la consola u otra aplicación no esté usando ese puerto.";
        }
        return RedirectToAction(nameof(Index));
    }

    private IActionResult NetworkPage(string operation, string? studyUid = null) => View("Network", new NetworkViewModel
    {
        Operation = operation, RemoteAe = options.Value.Pacs.AeTitle, Host = options.Value.Pacs.Host,
        Port = options.Value.Pacs.Port, LocalAe = options.Value.LocalAeTitle,
        DestinationAe = options.Value.MoveDestination.AeTitle, StudyUid = studyUid
    });

    private async Task<IActionResult> RunNetworkAsync(string operation, NetworkViewModel model, Func<Task> run)
    {
        model.Operation = operation;
        if (ModelState.IsValid)
        {
            try { await run(); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en C-{Operation} hacia {Host}:{Port}", operation, model.Host, model.Port);
                model.Error = ex is OperationCanceledException
                    ? "La operación fue cancelada o superó el tiempo de espera."
                    : $"No se pudo completar C-{operation.ToUpperInvariant()}: {ex.Message}";
            }
        }
        return View("Network", model);
    }
}
