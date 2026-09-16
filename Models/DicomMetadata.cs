namespace pruebasdicom.Models;

public sealed record DicomTagValue(string Tag, string Name, string Vr, string Value, int Depth);
public sealed record DicomMetadata(IReadOnlyDictionary<string, string> MainTags, string TransferSyntax,
    int TagCount, IReadOnlyList<DicomTagValue> Tags);
