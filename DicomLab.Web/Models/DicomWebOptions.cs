namespace DicomLab.Web.Models;

public sealed class DicomWebOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8042/dicom-web/";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxResponseMb { get; set; } = 100;
}
