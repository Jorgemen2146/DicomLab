using pruebasdicom.Models;

namespace DicomLab.Web.Models;

public sealed class DicomLabOptions
{
    public string LocalAeTitle { get; set; } = "DICOM_CLIENT";
    public DicomNodeOptions Pacs { get; set; } = new("LOCAL_PACS", "127.0.0.1", 11112);
    public DicomNodeOptions MoveDestination { get; set; } = new("MOVE_DESTINATION", "127.0.0.1", 11113);
    public string ReceivedDirectory { get; set; } = "../ReceivedDicoms";
    public string MovedDirectory { get; set; } = "../MovedDicoms";
    public int MaxUploadMb { get; set; } = 100;
    public int UploadCacheMb { get; set; } = 256;
    public int UploadLifetimeMinutes { get; set; } = 20;
    public int MaxFramePixels { get; set; } = 16777216;
}
