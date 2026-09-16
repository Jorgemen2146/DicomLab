using DicomLab.Web.Models;
using Microsoft.Extensions.Options;
using pruebasdicom.Services;
using pruebasdicom.Models;

namespace DicomLab.Web.Services.Dicom;

public sealed class DicomLocalServers : IHostedService, IDisposable
{
    private readonly DicomServerService _pacs;
    private readonly DicomMoveDestinationService _destination;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DicomLabOptions _config;
    public event Action<DicomStoredFile>? FileStored;
    public string ReceivedDirectory { get; }
    public string MovedDirectory { get; }
    public bool PacsListening => _pacs.IsListening;
    public bool DestinationListening => _destination.IsListening;

    public DicomLocalServers(IOptions<DicomLabOptions> options, IWebHostEnvironment environment, ILogger<DicomLocalServers> logger)
    {
        var config = options.Value;
        _config = config;
        ReceivedDirectory = Path.GetFullPath(config.ReceivedDirectory, environment.ContentRootPath);
        MovedDirectory = Path.GetFullPath(config.MovedDirectory, environment.ContentRootPath);
        void Write(string message) => logger.LogInformation("{DicomMessage}", message);
        _pacs = new DicomServerService(new pruebasdicom.Models.DicomServerOptions
        {
            Node = config.Pacs, StorageDirectory = ReceivedDirectory, Write = Write,
            OnFileStored = receipt => FileStored?.Invoke(receipt),
            MoveDestinations = new(StringComparer.Ordinal) { [config.MoveDestination.AeTitle] = config.MoveDestination }
        });
        _destination = new DicomMoveDestinationService(config.MoveDestination, MovedDirectory, Write,
            receipt => FileStored?.Invoke(receipt));
    }

    public async Task StartNodeAsync(bool destination, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (destination) await _destination.StartAsync();
            else await _pacs.StartAsync();
        }
        finally { _gate.Release(); }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public bool IsManagedDestination(DicomNodeOptions node) =>
        (PacsListening && Matches(node, _config.Pacs)) ||
        (DestinationListening && Matches(node, _config.MoveDestination));

    private static bool Matches(DicomNodeOptions requested, DicomNodeOptions configured) =>
        requested.AeTitle == configured.AeTitle && requested.Port == configured.Port &&
        string.Equals(NormalizeHost(requested.Host), NormalizeHost(configured.Host), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;
    public Task StopAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _pacs.Dispose(); _destination.Dispose(); }
}
