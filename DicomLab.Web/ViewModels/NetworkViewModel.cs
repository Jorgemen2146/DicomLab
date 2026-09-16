using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DicomLab.Web.ViewModels;

public sealed class NetworkViewModel
{
    [Required, StringLength(16), RegularExpression(@"[\x21-\x5B\x5D-\x7E ]{1,16}")]
    [Display(Name = "Remote AE Title")]
    public string RemoteAe { get; set; } = "";
    [Required, StringLength(253), Display(Name = "IP / Host")]
    public string Host { get; set; } = "";
    [Range(1, 65535)]
    public int Port { get; set; }
    [Required, StringLength(16), RegularExpression(@"[\x21-\x5B\x5D-\x7E ]{1,16}")]
    [Display(Name = "Local AE Title")]
    public string LocalAe { get; set; } = "";
    [StringLength(64)] public string? PatientId { get; set; }
    [StringLength(64)] public string? PatientName { get; set; }
    [StringLength(64)] public string? StudyUid { get; set; }
    [StringLength(17)] public string? StudyDate { get; set; }
    [StringLength(64)] public string? StudyDescription { get; set; }
    [StringLength(16)] public string? Modality { get; set; }
    [StringLength(16), RegularExpression(@"[\x21-\x5B\x5D-\x7E ]{1,16}")]
    public string? DestinationAe { get; set; }
    public IFormFile? File { get; set; }
    [BindNever, ValidateNever] public string Operation { get; set; } = "Echo";
    [BindNever, ValidateNever] public string? Result { get; set; }
    [BindNever, ValidateNever] public string? Error { get; set; }
    [BindNever, ValidateNever] public string? FileName { get; set; }
    [BindNever, ValidateNever] public string? SopInstanceUid { get; set; }
    [BindNever, ValidateNever] public string? SopClassUid { get; set; }
    [BindNever, ValidateNever] public string? SavedPath { get; set; }
    [BindNever, ValidateNever] public List<StudyRow> Studies { get; set; } = [];
    [BindNever, ValidateNever] public List<MoveRow> Progress { get; set; } = [];
}

public sealed record StudyRow(string PatientName, string PatientId, string StudyDate, string Description, string Modalities, string StudyUid);
public sealed record MoveRow(string Status, int Remaining, int Completed, int Failed, int Warning);
