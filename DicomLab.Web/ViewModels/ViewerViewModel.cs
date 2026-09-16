using pruebasdicom.Models;

namespace DicomLab.Web.ViewModels;

public sealed class ViewerViewModel
{
    public string? Id { get; set; }
    public string? FileName { get; set; }
    public string? Error { get; set; }
    public DicomMetadata? Metadata { get; set; }
    public int Frame { get; set; }
    public int Frames { get; set; } = 1;
    public bool HasPixelData { get; set; }
    public int MaxUploadMb { get; set; }
}
