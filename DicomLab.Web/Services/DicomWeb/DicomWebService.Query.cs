using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DicomLab.Web.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace DicomLab.Web.Services.DicomWeb;

public sealed partial class DicomWebService
{
    public async Task<DicomWebResult> QueryAsync(string baseUrl, string level, string? studyUid, string? seriesUid,
        Dictionary<string, string?> filters, CancellationToken token)
    {
        var result = new DicomWebResult();
        try
        {
            var path = level switch
            {
                "Studies" => "studies",
                "Series" => $"studies/{Uid(studyUid)}/series",
                "Instances" => $"studies/{Uid(studyUid)}/series/{Uid(seriesUid)}/instances",
                _ => throw new ArgumentException("Nivel QIDO no válido.")
            };
            var allowed = new[] { "PatientName", "PatientID", "StudyDate", "StudyInstanceUID", "AccessionNumber" };
            var query = filters.Where(f => level == "Studies" && allowed.Contains(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
                .ToDictionary(f => f.Key, f => f.Value);
            query["includefield"] = level == "Studies" ? "00081030" : level == "Series" ? "0008103E" : "00080016";
            using var request = Request(HttpMethod.Get, new Uri(QueryHelpers.AddQueryString(Endpoint(baseUrl, path).AbsoluteUri, query)), "application/dicom+json");
            await SendAsync(request, result, async (response, ct) =>
            {
                if (response.StatusCode == HttpStatusCode.NoContent) { result.Body = ""; return; }
                var bytes = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(ct), 4 * 1024 * 1024, ct);
                result.Body = Text(bytes);
                if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/dicom+json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("QIDO debe devolver application/dicom+json.");
                var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(bytes)
                    ?? throw new JsonException("Se esperaba un array de objetos DICOM JSON.");
                foreach (var row in rows)
                {
                    if (row is null || row.Any(p => !Regex.IsMatch(p.Key, "\\A[0-9A-F]{8}\\z") ||
                        p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("vr", out var vr) || vr.ValueKind != JsonValueKind.String))
                        throw new JsonException("La respuesta no contiene atributos DICOM JSON válidos.");
                    var parsed = new DicomJsonRow(row);
                    foreach (var tag in new[] { "00100010", "00100020", "00080020", "00081030", "00080050", "0020000D",
                        "0020000E", "00200011", "00080060", "0008103E", "00080018", "00080016", "00200013" })
                        _ = parsed.Value(tag);
                    result.Rows.Add(parsed);
                }
            }, token);
        }
        catch (Exception ex) { result.Rows.Clear(); Failure(result, ex, token); }
        return result;
    }

    private static string Uid(string? uid)
    {
        if (uid is null || uid.Length > 64 || !Regex.IsMatch(uid, "\\A[0-9]+(?:\\.[0-9]+)+\\z"))
            throw new ArgumentException("El UID debe contener componentes numéricos separados por puntos (máximo 64 caracteres).");
        return uid;
    }
}
