using pruebasdicom.Models;

namespace pruebasdicom.Services;

public sealed class DicomMoveDestinationService(DicomNodeOptions node, string directory, Action<string>? write = null) : IDisposable
{
    private readonly DicomServerService _server = new(new DicomServerOptions
    {
        Node = node,
        StorageDirectory = Path.GetFullPath(directory),
        EnableQueryRetrieve = false,
        Write = write ?? Console.WriteLine
    });

    public bool IsListening => _server.IsListening;
    public Task StartAsync() => _server.StartAsync();
    public void Dispose() => _server.Dispose();
}
