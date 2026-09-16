using DicomLab.Web.ViewModels;
using FellowOakDicom;
using pruebasdicom.Models;
using pruebasdicom.Services;

namespace DicomLab.Web.Services.Dicom;

public sealed class DicomNetworkService(ILogger<DicomNetworkService> logger, DicomFileService files)
{
    private DicomClientService Client(NetworkViewModel model) => new(
        new DicomNodeOptions(model.RemoteAe.Trim(), model.Host.Trim(), model.Port), model.LocalAe.Trim(),
        message => logger.LogInformation("{DicomMessage}", message));

    public async Task EchoAsync(NetworkViewModel model, CancellationToken token)
    {
        var status = await Client(model).EchoAsync(token);
        model.Result = $"C-ECHO: {status} (0x{status.Code:X4})";
    }

    public async Task FindAsync(NetworkViewModel model, CancellationToken token)
    {
        var datasets = await Client(model).FindAsync(model.PatientId ?? "", model.PatientName ?? "", model.StudyUid ?? "",
            model.StudyDate ?? "", model.StudyDescription ?? "", model.Modality ?? "", token);
        model.Studies = datasets.Select(d => new StudyRow(
            DicomStudyService.Value(d, DicomTag.PatientName), DicomStudyService.Value(d, DicomTag.PatientID),
            DicomStudyService.Value(d, DicomTag.StudyDate), DicomStudyService.Value(d, DicomTag.StudyDescription),
            DicomStudyService.Value(d, DicomTag.ModalitiesInStudy), DicomStudyService.Value(d, DicomTag.StudyInstanceUID))).ToList();
        model.Result = $"C-FIND: Success. Estudios encontrados: {model.Studies.Count}.";
    }

    public async Task MoveAsync(NetworkViewModel model, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model.StudyUid) || string.IsNullOrWhiteSpace(model.DestinationAe))
            throw new ArgumentException("StudyInstanceUID y Destination AE son obligatorios.");
        var final = await Client(model).MoveAsync(model.StudyUid, model.DestinationAe.Trim(), response =>
            model.Progress.Add(new($"{response.Status.State} (0x{response.Status.Code:X4})", response.Remaining,
                response.Completed, response.Failures, response.Warnings)), token);
        model.Result = $"C-MOVE: {final.Status.State} (0x{final.Status.Code:X4}). Completed: {final.Completed}, Failed: {final.Failures}, Warning: {final.Warnings}.";
        if (final.Status.State != FellowOakDicom.Network.DicomState.Success)
            model.Error = $"La recuperación no finalizó con Success: {final.Status}. Comprueba la configuración y el estado del destino.";
    }

    public async Task StoreAsync(NetworkViewModel model, CancellationToken token)
    {
        var bytes = await files.ReadUploadAsync(model.File, token);
        using var stream = new MemoryStream(bytes, false);
        var dicom = await DicomFile.OpenAsync(stream);
        var status = await Client(model).StoreAsync(dicom, token);
        model.Result = $"C-STORE: {status} (0x{status.Code:X4}). SOP Instance UID: {DicomStudyService.Value(dicom.Dataset, DicomTag.SOPInstanceUID)}";
    }
}
