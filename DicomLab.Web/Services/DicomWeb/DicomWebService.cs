using System.Net.Http.Headers;
using System.Text;
using DicomLab.Web.Models;
using DicomLab.Web.Services.Dicom;
using FellowOakDicom;
using Microsoft.Extensions.Options;
using pruebasdicom.Services;

namespace DicomLab.Web.Services.DicomWeb;

public sealed partial class DicomWebService(HttpClient http, DicomFileService files, IOptions<DicomWebOptions> options,
    IOptions<DicomLabOptions> dicomOptions, ILogger<DicomWebService> logger)
{
    private static Uri Endpoint(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Indica una URL base HTTP/HTTPS sin credenciales, query ni fragmento.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/" + path);
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string accept)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.ParseAdd(accept);
        return request;
    }

    public async Task<DicomWebResult> StowAsync(string baseUrl, IReadOnlyList<IFormFile> uploads, CancellationToken token)
    {
        var result = new DicomWebResult();
        try
        {
            if (uploads.Count == 0) throw new InvalidDataException("Selecciona al menos un archivo DICOM.");
            if (uploads.Count > 100 || uploads.Sum(f => f.Length) > dicomOptions.Value.MaxUploadMb * 1024L * 1024)
                throw new InvalidDataException($"Máximo 100 archivos y {dicomOptions.Value.MaxUploadMb} MB en total.");
            using var request = Request(HttpMethod.Post, Endpoint(baseUrl, "studies"), "application/dicom+json");
            var multipart = new MultipartContent("related", "dicom-" + Guid.NewGuid().ToString("N"));
            request.Content = multipart;
            multipart.Headers.ContentType!.Parameters.Add(new NameValueHeaderValue("type", "\"application/dicom\""));
            foreach (var upload in uploads)
            {
                var bytes = await files.ReadUploadAsync(upload, token);
                using var parseStream = new MemoryStream(bytes, false);
                var dicom = await DicomFile.OpenAsync(parseStream);
                result.SopInstanceUids.Add(DicomStudyService.Value(dicom.Dataset, DicomTag.SOPInstanceUID));
                var part = new StreamContent(new MemoryStream(bytes, false));
                part.Headers.ContentType = new MediaTypeHeaderValue("application/dicom");
                multipart.Add(part);
            }
            await SendAsync(request, result, async (response, ct) =>
            {
                result.Body = Text(await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(ct), 4 * 1024 * 1024, ct));
            }, token);
        }
        catch (Exception ex) { Failure(result, ex, token); }
        return result;
    }

    private async Task SendAsync(HttpRequestMessage request, DicomWebResult result,
        Func<HttpResponseMessage, CancellationToken, Task> readSuccess, CancellationToken token)
    {
        result.Request = $"{request.Method} {request.RequestUri}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        result.Status = (int)response.StatusCode;
        result.Reason = response.ReasonPhrase;
        result.ContentType = response.Content.Headers.ContentType?.ToString();
        logger.LogInformation("DICOMweb {Method}: HTTP {Status}", request.Method, result.Status);
        if (!response.IsSuccessStatusCode)
        {
            result.Error = $"El servidor respondió HTTP {result.Status} {result.Reason}.";
            result.Body = Text(await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), 4 * 1024 * 1024, timeout.Token));
            return;
        }
        await readSuccess(response, timeout.Token);
    }

    private void Failure(DicomWebResult result, Exception ex, CancellationToken token)
    {
        logger.LogWarning(ex, "Falló la operación DICOMweb");
        result.Error = ex is OperationCanceledException
            ? token.IsCancellationRequested ? "Solicitud cancelada." : "El servidor superó el tiempo de espera."
            : $"No se pudo completar la operación: {ex.Message}";
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > maxBytes) throw new InvalidDataException("La respuesta supera el límite de tamaño del laboratorio.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return output.ToArray();
    }

    private static string Text(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 64000 ? text[..64000] + "\n[Respuesta abreviada]" : text;
    }
}
