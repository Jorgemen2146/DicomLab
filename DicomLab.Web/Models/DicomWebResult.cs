using System.Text.Json;

namespace DicomLab.Web.Models;

public sealed class DicomWebResult
{
    public string Request { get; set; } = "";
    public int? Status { get; set; }
    public string? Reason { get; set; }
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public string? Error { get; set; }
    public List<string> SopInstanceUids { get; set; } = [];
    public List<DicomJsonRow> Rows { get; set; } = [];
    public byte[]? DicomBytes { get; set; }
    public bool IsSuccess => Status is >= 200 and < 300 && Error is null;
}

public sealed class DicomJsonRow
{
    private readonly Dictionary<string, JsonElement> _attributes;
    public DicomJsonRow(Dictionary<string, JsonElement> attributes) => _attributes = attributes;
    public string Value(string tag)
    {
        if (!_attributes.TryGetValue(tag, out var attribute) || !attribute.TryGetProperty("Value", out var values)) return "";
        if (values.ValueKind != JsonValueKind.Array) throw new JsonException("Value debe ser un array DICOM JSON.");
        return string.Join("\\", values.EnumerateArray().Select(value =>
        {
            if (value.ValueKind == JsonValueKind.Object)
                return string.Join("=", new[] { "Alphabetic", "Ideographic", "Phonetic" }
                    .Select(key => value.TryGetProperty(key, out var component) ? component.GetString() ?? "" : "")).TrimEnd('=');
            return value.ValueKind == JsonValueKind.Null ? "" : value.ToString();
        }));
    }
}
