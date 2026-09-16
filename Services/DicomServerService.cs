using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;
using pruebasdicom.Models;
using DicomServerOptions = pruebasdicom.Models.DicomServerOptions;

namespace pruebasdicom.Services;

public sealed class DicomServerService : IDisposable
{
    private readonly DicomServerOptions _options;
    public DicomServerService(string receivedDirectory) : this(new DicomServerOptions { StorageDirectory = Path.GetFullPath(receivedDirectory) }) { }
    public DicomServerService(DicomServerOptions options) => _options = options;
    public const string Host = "127.0.0.1";
    public const int Port = 11112;
    public const string AeTitle = "LOCAL_PACS";
    private IDicomServer? _server;
    public bool IsListening => _server?.IsListening == true;

    public async Task StartAsync()
    {
        if (IsListening) { _options.Write("El SCP ya está escuchando."); return; }
        _server?.Dispose();
        _server = null;
        Directory.CreateDirectory(_options.StorageDirectory);
        _options.Write("Starting DICOM SCP...");
        try
        {
            _server = DicomServerFactory.Create<LocalDicomScp>(_options.Node.Host, _options.Node.Port, userState: _options);
            for (var attempt = 0; attempt < 100 && !_server.IsListening && _server.Exception is null; attempt++)
                await Task.Delay(50);
            if (_server.Exception is not null) throw _server.Exception;
            if (!_server.IsListening) throw new IOException("El SCP no pudo empezar a escuchar.");
            _options.Write($"Listening on {_options.Node.Host}:{_options.Node.Port} — AE Title: {_options.Node.AeTitle}");
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }
}

public partial class LocalDicomScp : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCStoreProvider,
    IDicomCFindProvider, IDicomCMoveProvider
{
    private DicomServerOptions NodeOptions => (DicomServerOptions)UserState;
    public LocalDicomScp(INetworkStream stream, Encoding fallbackEncoding, ILogger logger,
        DicomServiceDependencies dependencies) : base(stream, fallbackEncoding, logger, dependencies) { }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        if (association.CalledAE != NodeOptions.Node.AeTitle)
            return SendAssociationRejectAsync(DicomRejectResult.Permanent,
                DicomRejectSource.ServiceUser, DicomRejectReason.CalledAENotRecognized);
        NodeOptions.Write($"Association: {association.CallingAE} -> {association.CalledAE}");
        foreach (var context in association.PresentationContexts)
        {
            if (context.AbstractSyntax == DicomUID.Verification ||
                context.AbstractSyntax.StorageCategory != DicomStorageCategory.None ||
                (NodeOptions.EnableQueryRetrieve && (context.AbstractSyntax == DicomUID.StudyRootQueryRetrieveInformationModelFind ||
                    context.AbstractSyntax == DicomUID.StudyRootQueryRetrieveInformationModelMove)))
                context.AcceptTransferSyntaxes(context.GetTransferSyntaxes().ToArray());
            else context.SetResult(DicomPresentationContextResult.RejectAbstractSyntaxNotSupported);
        }
        return SendAssociationAcceptAsync(association);
    }

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
    {
        NodeOptions.Write("C-ECHO received by SCP");
        return Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
    }

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        NodeOptions.Write($"[C-STORE SCP - {NodeOptions.Node.AeTitle}] C-STORE received");
        NodeOptions.Write($"Received SOP Instance UID: {request.SOPInstanceUID.UID}");
        try
        {
            var directory = NodeOptions.StorageDirectory;
            var uid = new string(request.SOPInstanceUID.UID.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
            if (uid.Length > 64) uid = uid[..64];
            var path = Path.Combine(directory, $"{uid}_{Guid.NewGuid():N}.dcm");
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await request.File.SaveAsync(output);
            NodeOptions.Write($"Saved: {path}");
            NodeOptions.Write("Verificación del DICOM guardado:");
            await new DicomReaderService(NodeOptions.Write).ReadAsync(path);
            return new DicomCStoreResponse(request, DicomStatus.Success);
        }
        catch (Exception ex)
        {
            NodeOptions.Write($"Error al guardar/verificar C-STORE: {ex.Message}");
            return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
        }
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        NodeOptions.Write($"Error al recibir C-STORE: {e.Message}");
        return Task.CompletedTask;
    }
    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) =>
        NodeOptions.Write($"Asociación abortada: {source}, {reason}");
    public void OnConnectionClosed(Exception exception)
    {
        if (exception is not null) NodeOptions.Write($"Conexión cerrada con error: {exception.Message}");
    }
}
