using DicomLab.Web.Models;
using FellowOakDicom;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace DicomLab.Web.Services.DicomWeb;

public sealed partial class DicomWebService
{
    public async Task<DicomWebResult> RetrieveAsync(string baseUrl, string? studyUid, string? seriesUid, string? instanceUid, CancellationToken token)
    {
        var result = new DicomWebResult();
        try
        {
            var path = $"studies/{Uid(studyUid)}/series/{Uid(seriesUid)}/instances/{Uid(instanceUid)}";
            using var request = Request(HttpMethod.Get, Endpoint(baseUrl, path), "multipart/related; type=\"application/dicom\"; transfer-syntax=*");
            await SendAsync(request, result, async (response, ct) =>
            {
                var bytes = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(ct), options.Value.MaxResponseMb * 1024L * 1024, ct);
                var type = MediaTypeHeaderValue.Parse(response.Content.Headers.ContentType?.ToString()
                    ?? throw new InvalidDataException("WADO no devolvió Content-Type."));
                if (string.Equals(type.MediaType.Value, "multipart/related", StringComparison.OrdinalIgnoreCase))
                {
                    var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
                    if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 200)
                        throw new InvalidDataException("Boundary multipart ausente o inválido.");
                    var relatedType = type.Parameters.FirstOrDefault(p => p.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
                    if (!string.Equals(HeaderUtilities.RemoveQuotes(relatedType?.Value ?? default).Value, "application/dicom", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("WADO multipart debe contener application/dicom.");
                    using var multipartStream = new MemoryStream(bytes, false);
                    var reader = new MultipartReader(boundary, multipartStream);
                    var section = await reader.ReadNextSectionAsync(ct) ?? throw new InvalidDataException("Respuesta multipart vacía.");
                    if (!MediaTypeHeaderValue.TryParse(section.ContentType, out var partType) ||
                        !string.Equals(partType.MediaType.Value, "application/dicom", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La parte WADO no es application/dicom.");
                    bytes = await ReadBoundedAsync(section.Body, options.Value.MaxResponseMb * 1024L * 1024, ct);
                    if (await reader.ReadNextSectionAsync(ct) is not null)
                        throw new InvalidDataException("Se esperaba exactamente una instancia DICOM.");
                }
                else if (!string.Equals(type.MediaType.Value, "application/dicom", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("WADO debe devolver DICOM binario o multipart/related con DICOM.");

                using var stream = new MemoryStream(bytes, false);
                var upload = new FormFile(stream, 0, bytes.Length, "file", instanceUid + ".dcm");
                await files.ReadUploadAsync(upload, ct);
                stream.Position = 0;
                var dicom = await DicomFile.OpenAsync(stream);
                if (dicom.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") != studyUid ||
                    dicom.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "") != seriesUid ||
                    dicom.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "") != instanceUid)
                    throw new InvalidDataException("Los UIDs del DICOM recibido no coinciden con la instancia solicitada.");
                result.DicomBytes = bytes;
                result.SopInstanceUids.Add(instanceUid!);
            }, token);
        }
        catch (Exception ex) { result.DicomBytes = null; Failure(result, ex, token); }
        return result;
    }
}
