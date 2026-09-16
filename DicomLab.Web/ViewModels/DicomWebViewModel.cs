using System.ComponentModel.DataAnnotations;
using DicomLab.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DicomLab.Web.ViewModels;

public sealed class DicomWebViewModel
{
    [Required, StringLength(2048), Display(Name = "DICOMweb URL")]
    public string BaseUrl { get; set; } = "";
    [StringLength(64)] public string? PatientName { get; set; }
    [StringLength(64)] public string? PatientID { get; set; }
    [StringLength(17)] public string? StudyDate { get; set; }
    [StringLength(64)] public string? StudyInstanceUID { get; set; }
    [StringLength(64)] public string? SeriesInstanceUID { get; set; }
    [StringLength(64)] public string? SOPInstanceUID { get; set; }
    [StringLength(16)] public string? AccessionNumber { get; set; }
    public List<IFormFile> Files { get; set; } = [];
    [BindNever, ValidateNever] public string Level { get; set; } = "Studies";
    [BindNever, ValidateNever] public DicomWebResult? Result { get; set; }
}
