using DicomLab.Web.Models;
using Microsoft.Extensions.Options;
using pruebasdicom.Services;

namespace DicomLab.Web.Services.Dicom;

public sealed class DicomLocalServers : IHostedService, IDisposable
{
    private readonly DicomServerService _pacs;
    private readonly DicomMoveDestinationService _destination;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string ReceivedDirectory { get; }
    public string MovedDirectory { get; }
    public bool PacsListening => _pacs.IsListening;
    public bool DestinationListening => _destination.IsListening;

    public DicomLocalServers(IOptions<DicomLabOptions> options, IWebHostEnvironment environment, ILogger<DicomLocalServers> logger)
    {
        var config = options.Value;
        ReceivedDirectory = Path.GetFullPath(config.ReceivedDirectory, environment.ContentRootPath);
        MovedDirectory = Path.GetFullPath(config.MovedDirectory, environment.ContentRootPath);
        void Write(string message) => logger.LogInformation("{DicomMessage}", message);
        _pacs = new DicomServerService(new pruebasdicom.Models.DicomServerOptions
        {
            Node = config.Pacs, StorageDirectory = ReceivedDirectory, Write = Write,
            MoveDestinations = new(StringComparer.Ordinal) { [config.MoveDestination.AeTitle] = config.MoveDestination }
        });
        _destination = new DicomMoveDestinationService(config.MoveDestination, MovedDirectory, Write);
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
    public Task StopAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _pacs.Dispose(); _destination.Dispose(); }
}
