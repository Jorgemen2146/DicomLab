namespace pruebasdicom.Models;

public sealed record DicomNodeOptions(string AeTitle, string Host, int Port);

public sealed class DicomServerOptions
{
    public Action<string> Write { get; init; } = Console.WriteLine;
    public DicomNodeOptions Node { get; init; } = new("LOCAL_PACS", "127.0.0.1", 11112);
    public string StorageDirectory { get; init; } = Path.GetFullPath("ReceivedDicoms");
    public bool EnableQueryRetrieve { get; init; } = true;
    public Dictionary<string, DicomNodeOptions> MoveDestinations { get; init; } = new(StringComparer.Ordinal);
}
