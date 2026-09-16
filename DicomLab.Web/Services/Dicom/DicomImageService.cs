using DicomLab.Web.Models;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace DicomLab.Web.Services.Dicom;

public sealed class DicomImageService(DicomFileService files, IOptions<DicomLabOptions> options)
{
    public async Task<byte[]> RenderAsync(string id, string owner, int frame, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(files.GetBytes(id, owner), false);
        var file = await DicomFile.OpenAsync(input);
        if (!file.Dataset.Contains(DicomTag.PixelData)) throw new InvalidDataException("Este DICOM no contiene Pixel Data. Puedes consultar sus metadatos.");
        var frames = file.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
        if (frame < 0 || frame >= frames) throw new ArgumentOutOfRangeException(nameof(frame), "El frame solicitado no existe.");
        var rows = file.Dataset.GetSingleValue<ushort>(DicomTag.Rows);
        var columns = file.Dataset.GetSingleValue<ushort>(DicomTag.Columns);
        if (rows == 0 || columns == 0 || (long)rows * columns > options.Value.MaxFramePixels)
            throw new InvalidDataException("Las dimensiones del frame superan el límite del visor.");
        var image = new DicomImage(file.Dataset, frame) { CacheMode = CacheType.None };
        using var rendered = image.RenderImage(frame);
        using var output = new MemoryStream();
        await rendered.AsSharpImage().SaveAsPngAsync(output, cancellationToken);
        return output.ToArray();
    }
}
