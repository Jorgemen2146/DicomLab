using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using pruebasdicom.Models;

namespace pruebasdicom.Services;

public class DicomClientService
{
    private readonly DicomNodeOptions _pacs;
    private readonly string _callingAe;
    private readonly Action<string> _write;
    public DicomClientService(DicomNodeOptions? pacs = null, string callingAe = CallingAe, Action<string>? write = null)
    {
        _pacs = pacs ?? new(DicomServerService.AeTitle, DicomServerService.Host, DicomServerService.Port);
        _callingAe = callingAe;
        _write = write ?? Console.WriteLine;
    }
    public const string CallingAe = "DICOM_CLIENT";
    private IDicomClient CreateClient() => DicomClientFactory.Create(
        _pacs.Host, _pacs.Port, false, _callingAe, _pacs.AeTitle);

    public async Task<DicomStatus> EchoAsync(CancellationToken cancellationToken = default)
    {
        _write("Sending C-ECHO...");
        var client = CreateClient();
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, response) =>
        {
            status = response.Status;
            _write($"C-ECHO Response: {response.Status}");
        };
        await client.AddRequestAsync(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendAsync(timeout.Token);
        EnsureSuccess(status);
        return status!;
    }

    public async Task<DicomStatus> StoreAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = await DicomFile.OpenAsync(DicomReaderService.ValidatePath(path));
        return await StoreAsync(file, cancellationToken);
    }

    public async Task<DicomStatus> StoreAsync(DicomFile file, CancellationToken cancellationToken = default)
    {
        var request = new DicomCStoreRequest(file);
        _write("Sending C-STORE...");
        _write($"SOP Instance UID: {request.SOPInstanceUID.UID}");
        var client = CreateClient();
        DicomStatus? status = null;
        request.OnResponseReceived += (_, response) =>
        {
            status = response.Status;
            _write($"C-STORE Response: {response.Status}");
        };
        await client.AddRequestAsync(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await client.SendAsync(timeout.Token);
        EnsureSuccess(status);
        return status!;
    }

    public async Task<List<DicomDataset>> FindAsync(string patientId = "", string patientName = "",
        string studyUid = "", string studyDate = "", string studyDescription = "", string modality = "", CancellationToken cancellationToken = default)
    {
        var request = new DicomCFindRequest(DicomUID.StudyRootQueryRetrieveInformationModelFind, DicomQueryRetrieveLevel.Study);
        request.Dataset.AddOrUpdate(DicomTag.PatientID, patientId);
        request.Dataset.AddOrUpdate(DicomTag.PatientName, patientName);
        request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyUid);
        request.Dataset.AddOrUpdate(DicomTag.StudyDate, studyDate);
        request.Dataset.AddOrUpdate(DicomTag.StudyDescription, studyDescription);
        request.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, modality);
        var results = new List<DicomDataset>();
        DicomStatus? final = null;
        request.OnResponseReceived += (_, response) =>
        {
            _write($"C-FIND Response: {response.Status}");
            if (response.Status.State == DicomState.Pending && response.Dataset is not null)
            {
                results.Add(response.Dataset);
                foreach (var tag in DicomStudyService.ResultTags)
                {
                    var value = DicomStudyService.Value(response.Dataset, tag);
                    _write($"{tag.DictionaryEntry.Keyword}: {(value.Length == 0 ? "N/A" : value)}");
                }
                _write("");
            }
            else final = response.Status;
        };
        var client = CreateClient();
        await client.AddRequestAsync(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await client.SendAsync(timeout.Token);
        EnsureSuccess(final);
        _write($"Estudios encontrados: {results.Count}");
        return results;
    }

    public async Task<DicomCMoveResponse> MoveAsync(string studyUid, string destinationAe,
        Action<DicomCMoveResponse>? onResponse = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyUid)) throw new ArgumentException("StudyInstanceUID es obligatorio.");
        var request = new DicomCMoveRequest(destinationAe, studyUid.Trim());
        DicomCMoveResponse? final = null;
        request.OnResponseReceived += (_, response) =>
        {
            var statusText = response.Status.Code == DicomStatus.QueryRetrieveSubOpsOneOrMoreFailures.Code
                ? "Warning [B000: una o más suboperaciones con fallos o advertencias]" : response.Status.ToString();
            _write($"C-MOVE Response: {statusText}\nRemaining: {response.Remaining}\nCompleted: {response.Completed}\nFailed: {response.Failures}\nWarning: {response.Warnings}");
            if (response.Status.State != DicomState.Pending) final = response;
            onResponse?.Invoke(response);
        };
        var client = CreateClient();
        await client.AddRequestAsync(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        await client.SendAsync(timeout.Token);
        if (final is null) throw new IOException("No se recibió respuesta final C-MOVE.");
        return final;
    }

    private static void EnsureSuccess(DicomStatus? status)
    {
        if (status is null) throw new IOException("No se recibió una respuesta DICOM del SCP.");
        if (status.State != DicomState.Success) throw new IOException($"El SCP respondió: {status}");
    }
}
