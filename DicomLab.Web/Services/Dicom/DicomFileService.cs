using DicomLab.Web.Models;
using DicomLab.Web.ViewModels;
using FellowOakDicom;
using Microsoft.Extensions.Options;
using pruebasdicom.Services;

namespace DicomLab.Web.Services.Dicom;

public sealed class DicomFileService(IOptions<DicomLabOptions> options, DicomReaderService reader) : BackgroundService
{
    private sealed record Upload(string Owner, byte[] Bytes, DateTime Expires);
    private readonly Dictionary<string, Upload> _uploads = new();
    private readonly object _gate = new();
    private readonly DicomLabOptions _options = options.Value;

    public async Task<byte[]> ReadUploadAsync(IFormFile? upload, CancellationToken cancellationToken)
    {
        var limit = _options.MaxUploadMb * 1024L * 1024;
        if (upload is null || upload.Length == 0) throw new InvalidDataException("Selecciona un archivo DICOM.");
        if (upload.Length > limit) throw new InvalidDataException($"El archivo supera el límite de {_options.MaxUploadMb} MB.");
        await using var input = upload.OpenReadStream();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("El archivo supera el tamaño permitido.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        output.Position = 0;
        var dicom = await DicomFile.OpenAsync(output);
        if (dicom.IsPartial || !dicom.Dataset.Contains(DicomTag.SOPClassUID) || !dicom.Dataset.Contains(DicomTag.SOPInstanceUID))
            throw new InvalidDataException("El archivo está incompleto o no contiene una instancia DICOM válida.");
        return output.ToArray();
    }

    public string Remember(string owner, byte[] bytes)
    {
        lock (_gate)
        {
            RemoveExpired();
            var capacity = _options.UploadCacheMb * 1024L * 1024;
            if (bytes.LongLength > capacity) throw new InvalidDataException("El archivo supera la capacidad del visor.");
            while (_uploads.Values.Sum(u => u.Bytes.LongLength) + bytes.LongLength > capacity)
                _uploads.Remove(_uploads.MinBy(p => p.Value.Expires).Key);
            var id = Guid.NewGuid().ToString("N");
            _uploads.Add(id, new(owner, bytes, DateTime.UtcNow.AddMinutes(_options.UploadLifetimeMinutes)));
            return id;
        }
    }

    public byte[] GetBytes(string id, string owner)
    {
        lock (_gate)
        {
            RemoveExpired();
            if (!_uploads.TryGetValue(id, out var upload) || upload.Owner != owner)
                throw new FileNotFoundException("El archivo no está disponible o expiró. Vuelve a cargarlo.");
            _uploads[id] = upload with { Expires = DateTime.UtcNow.AddMinutes(_options.UploadLifetimeMinutes) };
            return upload.Bytes;
        }
    }

    public async Task<ViewerViewModel> DescribeAsync(string id, string owner, int frame)
    {
        using var stream = new MemoryStream(GetBytes(id, owner), false);
        var file = await DicomFile.OpenAsync(stream);
        var frames = Math.Max(1, file.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1));
        if (frame < 0 || frame >= frames) throw new ArgumentOutOfRangeException(nameof(frame), "El frame solicitado no existe.");
        return new ViewerViewModel
        {
            Id = id, Metadata = reader.GetMetadata(file), Frame = frame, Frames = frames,
            HasPixelData = file.Dataset.Contains(DicomTag.PixelData), MaxUploadMb = _options.MaxUploadMb
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                lock (_gate) RemoveExpired();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void RemoveExpired()
    {
        foreach (var id in _uploads.Where(p => p.Value.Expires <= DateTime.UtcNow).Select(p => p.Key).ToArray())
            _uploads.Remove(id);
    }
}
