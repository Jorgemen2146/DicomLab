using FellowOakDicom;
using pruebasdicom.Models;

namespace pruebasdicom.Services;

public class DicomReaderService
{
    private readonly Action<string> _write;
    public DicomReaderService(Action<string>? write = null) => _write = write ?? Console.WriteLine;
    private static readonly DicomTag[] MainTags =
    [
        DicomTag.PatientName, DicomTag.PatientID, DicomTag.PatientBirthDate,
        DicomTag.StudyDate, DicomTag.StudyDescription, DicomTag.StudyInstanceUID,
        DicomTag.SeriesInstanceUID, DicomTag.SOPInstanceUID, DicomTag.SOPClassUID,
        DicomTag.Modality, DicomTag.Manufacturer
    ];

    public static string ValidatePath(string path)
    {
        path = Path.GetFullPath(path);
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("La ruta está dentro de un ZIP. Extrae primero los archivos y configura una ruta normal del filesystem.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontró el DICOM: {path}. Revisa la ruta y extrae primero el ZIP si corresponde.");
        return path;
    }

    public async Task ReadAsync(string path)
    {
        var file = await DicomFile.OpenAsync(ValidatePath(path));
        _write($"\nDICOM: {path}");
        foreach (var tag in MainTags)
        {
            string value;
            try { value = file.Dataset.GetString(tag); }
            catch (Exception) { value = "N/A"; }
            _write($"{tag.DictionaryEntry.Keyword}: {(string.IsNullOrWhiteSpace(value) ? "N/A" : value)}");
        }
        _write($"Transfer Syntax: {file.Dataset.InternalTransferSyntax}");
        _write($"Cantidad aproximada de tags (nivel principal): {file.Dataset.Count()}");
    }

    public DicomMetadata GetMetadata(DicomFile file)
    {
        var main = MainTags.Concat(new[] { DicomTag.Rows, DicomTag.Columns, DicomTag.NumberOfFrames,
            DicomTag.WindowCenter, DicomTag.WindowWidth, DicomTag.BitsAllocated, DicomTag.PhotometricInterpretation })
            .ToDictionary(t => t.DictionaryEntry.Keyword, t =>
            {
                var value = DicomStudyService.Value(file.Dataset, t);
                return string.IsNullOrWhiteSpace(value) ? "N/A" : value;
            });
        var tags = new List<DicomTagValue>();
        CollectTags(file.Dataset, tags, 0);
        return new DicomMetadata(main, file.Dataset.InternalTransferSyntax.ToString(), file.Dataset.Count(), tags);
    }

    private static void CollectTags(DicomDataset dataset, List<DicomTagValue> tags, int depth)
    {
        foreach (var item in dataset)
        {
            if (tags.Count >= 10000) return;
            var name = item.Tag.DictionaryEntry.Keyword;
            var tag = $"({item.Tag.Group:x4},{item.Tag.Element:x4})";
            var value = "";
            try
            {
                value = item switch
                {
                    DicomSequence sequence => $"[Secuencia: {sequence.Items.Count} items]",
                    DicomFragmentSequence fragments => $"[Binary Pixel Data: {fragments.Fragments.Count} fragmentos]",
                    DicomElement element when item.Tag == DicomTag.PixelData => $"[Binary Pixel Data: {element.Buffer.Size} bytes]",
                    DicomElement element when element.ValueRepresentation.Code is "OB" or "OW" or "OF" or "OD" or "OL" or "OV" or "UN" => $"[Binario: {element.Buffer.Size} bytes]",
                    DicomElement element => string.Join("\\", Enumerable.Range(0, Math.Min(element.Count, 16)).Select(i => element.Get<string>(i))) +
                        (element.Count > 16 ? " [más valores omitidos]" : ""),
                    _ => $"[{item.GetType().Name}]"
                };
                if (value.Length > 300) value = value[..300] + "...";
            }
            catch (Exception) { value = "[No representable como texto]"; }
            tags.Add(new(tag, name, item.ValueRepresentation.Code, value, depth));
            if (item is DicomSequence seq && depth < 16)
            {
                for (var i = 0; i < seq.Items.Count && tags.Count < 10000; i++)
                {
                    tags.Add(new("", $"Item {i + 1}", "", "", depth + 1));
                    CollectTags(seq.Items[i], tags, depth + 1);
                }
            }
        }
    }

    public async Task DumpAsync(string path)
    {
        var file = await DicomFile.OpenAsync(ValidatePath(path));
        DumpDataset(file.Dataset, 0);
    }

    private void DumpDataset(DicomDataset dataset, int depth)
    {
        var indent = new string(' ', depth * 2);
        foreach (var item in dataset)
        {
            var label = $"{indent}({item.Tag.Group:x4},{item.Tag.Element:x4}) {item.Tag.DictionaryEntry.Keyword}";
            try
            {
                if (item is DicomSequence sequence)
                {
                    _write($"{label} = [Secuencia: {sequence.Items.Count} items]");
                    if (depth >= 16) { _write($"{indent}  [Límite de profundidad]"); continue; }
                    for (var i = 0; i < sequence.Items.Count; i++)
                    {
                        _write($"{indent}  Item {i + 1}:");
                        DumpDataset(sequence.Items[i], depth + 1);
                    }
                }
                else if (item is DicomFragmentSequence fragments)
                    _write($"{label} = [Datos encapsulados: {fragments.Fragments.Count} fragmentos]");
                else if (item is DicomElement element)
                {
                    var vr = element.ValueRepresentation.Code;
                    if (item.Tag == DicomTag.PixelData || vr is "OB" or "OW" or "OF" or "OD" or "OL" or "OV" or "UN")
                        _write($"{label} = [Datos binarios: {element.Buffer.Size} bytes]");
                    else
                    {
                        var values = Enumerable.Range(0, Math.Min(element.Count, 16)).Select(i => element.Get<string>(i));
                        var value = string.Join("\\", values);
                        if (value.Length > 300) value = value[..300] + "...";
                        _write($"{label} = {value}{(element.Count > 16 ? " [más valores omitidos]" : "")}");
                    }
                }
                else _write($"{label} = [{item.GetType().Name}]");
            }
            catch (Exception ex) { _write($"{label} = [No representable como texto: {ex.Message}]"); }
        }
    }
}
