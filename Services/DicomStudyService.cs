using System.Globalization;
using System.Text.RegularExpressions;
using FellowOakDicom;

namespace pruebasdicom.Services;

public sealed record StoredDicom(string Path, string SopInstanceUid, DicomDataset Metadata);

public class DicomStudyService
{
    private readonly Action<string> _write;
    public DicomStudyService(Action<string>? write = null) => _write = write ?? Console.WriteLine;
    public static readonly DicomTag[] ResultTags =
    [
        DicomTag.PatientID, DicomTag.PatientName, DicomTag.StudyDate,
        DicomTag.StudyDescription, DicomTag.StudyInstanceUID, DicomTag.ModalitiesInStudy
    ];

    public static string Value(DicomDataset dataset, DicomTag tag)
    {
        try { return dataset.GetString(tag)?.Trim() ?? ""; }
        catch (Exception) { return ""; }
    }

    public async Task<List<StoredDicom>> ScanAsync(string directory)
    {
        var result = new List<StoredDicom>();
        foreach (var path in Directory.GetFiles(directory, "*.dcm", SearchOption.AllDirectories))
        {
            try
            {
                var file = await DicomFile.OpenAsync(path, FileReadOption.SkipLargeTags);
                var studyUid = Value(file.Dataset, DicomTag.StudyInstanceUID);
                var sopUid = Value(file.Dataset, DicomTag.SOPInstanceUID);
                if (file.IsPartial || string.IsNullOrEmpty(studyUid) || string.IsNullOrEmpty(sopUid) ||
                    string.IsNullOrEmpty(Value(file.Dataset, DicomTag.SOPClassUID)))
                    throw new InvalidDataException("DICOM incompleto o sin UID de estudio/instancia/clase.");
                var metadata = new DicomDataset();
                foreach (var tag in ResultTags.Append(DicomTag.Modality))
                    metadata.AddOrUpdate(tag, Value(file.Dataset, tag));
                result.Add(new StoredDicom(path, sopUid, metadata));
            }
            catch (Exception ex) { _write($"[Índice DICOM] Omitido: {path}: {ex}"); }
        }
        return result;
    }

    public List<DicomDataset> FindStudies(List<StoredDicom> files, DicomDataset query)
    {
        ValidateDate(Value(query, DicomTag.StudyDate));
        var results = new List<DicomDataset>();
        foreach (var group in files.GroupBy(f => Value(f.Metadata, DicomTag.StudyInstanceUID)))
        {
            var study = new DicomDataset { { DicomTag.QueryRetrieveLevel, "STUDY" } };
            foreach (var tag in ResultTags.Where(t => t != DicomTag.ModalitiesInStudy))
                study.AddOrUpdate(tag, group.Select(f => Value(f.Metadata, tag)).FirstOrDefault(v => v.Length > 0) ?? "");
            var modalities = group.Select(f => Value(f.Metadata, DicomTag.Modality))
                .Where(v => v.Length > 0).Distinct().Order().ToArray();
            study.AddOrUpdate(DicomTag.ModalitiesInStudy, modalities);
            var matches = ResultTags.All(tag => tag == DicomTag.StudyDate
                ? MatchesDate(Value(study, tag), Value(query, tag))
                : tag == DicomTag.StudyInstanceUID
                    ? MatchesUid(Value(study, tag), Value(query, tag))
                    : Matches(Value(study, tag), Value(query, tag), tag == DicomTag.PatientName));
            if (matches && Matches(Value(study, DicomTag.ModalitiesInStudy), Value(query, DicomTag.Modality)))
                results.Add(study);
        }
        return results;
    }

    private static bool MatchesUid(string value, string filter) =>
        filter.Length == 0 || filter.Split('\\').Contains(value, StringComparer.Ordinal);

    private static bool Matches(string value, string filter, bool ignoreCase = false)
    {
        if (filter.Length == 0) return true;
        var options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        if (ignoreCase) options |= RegexOptions.IgnoreCase;
        return filter.Split('\\').Any(pattern => value.Split('\\').Any(candidate =>
            Regex.IsMatch(candidate, "\\A" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "\\z",
                options, TimeSpan.FromSeconds(1))));
    }

    private static void ValidateDate(string filter)
    {
        if (filter.Length == 0) return;
        var bounds = filter.Split('-');
        if (bounds.Length > 2 || bounds.All(string.IsNullOrEmpty) || bounds.Any(v => v.Length > 0 &&
            !DateOnly.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) ||
            (bounds.Length == 2 && bounds[0].Length > 0 && bounds[1].Length > 0 && string.CompareOrdinal(bounds[0], bounds[1]) > 0))
            throw new ArgumentException("StudyDate debe ser yyyyMMdd o un rango yyyyMMdd-yyyyMMdd (también abierto).");
    }

    private static bool MatchesDate(string value, string filter)
    {
        if (filter.Length == 0) return true;
        if (!DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
        var bounds = filter.Split('-');
        if (bounds.Length == 1) return value == filter;
        return (bounds[0].Length == 0 || string.CompareOrdinal(value, bounds[0]) >= 0) &&
               (bounds[1].Length == 0 || string.CompareOrdinal(value, bounds[1]) <= 0);
    }
}
