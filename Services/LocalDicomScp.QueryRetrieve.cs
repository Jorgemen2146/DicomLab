using System.Threading.Channels;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace pruebasdicom.Services;

public partial class LocalDicomScp
{
    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        NodeOptions.Write($"[C-FIND SCP]\nCalling AE: {Association.CallingAE}\nQuery Level: {DicomStudyService.Value(request.Dataset, DicomTag.QueryRetrieveLevel)}");
        foreach (var tag in DicomStudyService.ResultTags)
            NodeOptions.Write($"{tag.DictionaryEntry.Keyword}: {DicomStudyService.Value(request.Dataset, tag)}");
        if (!NodeOptions.EnableQueryRetrieve || request.Level != DicomQueryRetrieveLevel.Study)
        {
            yield return new DicomCFindResponse(request, DicomStatus.QueryRetrieveIdentifierDoesNotMatchSOPClass);
            yield break;
        }

        List<DicomDataset> results = [];
        DicomStatus? error = null;
        try
        {
            var studies = new DicomStudyService(NodeOptions.Write);
            results = studies.FindStudies(await studies.ScanAsync(NodeOptions.StorageDirectory), request.Dataset);
        }
        catch (Exception ex)
        {
            NodeOptions.Write($"[C-FIND SCP] {ex.Message}");
            error = DicomStatus.QueryRetrieveUnableToProcess;
        }
        if (error is not null)
        {
            yield return new DicomCFindResponse(request, error);
            yield break;
        }
        foreach (var result in results)
            yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = result };
        yield return new DicomCFindResponse(request, DicomStatus.Success);
    }

    public async IAsyncEnumerable<DicomCMoveResponse> OnCMoveRequestAsync(DicomCMoveRequest request)
    {
        var studyUid = DicomStudyService.Value(request.Dataset, DicomTag.StudyInstanceUID);
        NodeOptions.Write($"[C-MOVE SCP] C-MOVE received\nCalling AE: {Association.CallingAE}\nDestination AE: {request.DestinationAE}\nStudyInstanceUID: {studyUid}");
        if (!NodeOptions.EnableQueryRetrieve || request.Level != DicomQueryRetrieveLevel.Study || string.IsNullOrEmpty(studyUid))
        {
            yield return new DicomCMoveResponse(request, DicomStatus.QueryRetrieveIdentifierDoesNotMatchSOPClass);
            yield break;
        }
        if (!NodeOptions.MoveDestinations.TryGetValue(request.DestinationAE, out var destination))
        {
            NodeOptions.Write($"[C-MOVE SCP] No se pudo resolver el destino: {request.DestinationAE}");
            yield return new DicomCMoveResponse(request, DicomStatus.QueryRetrieveMoveDestinationUnknown);
            yield break;
        }

        List<StoredDicom> instances = [];
        DicomStatus? error = null;
        try
        {
            instances = (await new DicomStudyService(NodeOptions.Write).ScanAsync(NodeOptions.StorageDirectory))
                .Where(f => DicomStudyService.Value(f.Metadata, DicomTag.StudyInstanceUID) == studyUid)
                .DistinctBy(f => f.SopInstanceUid).ToList();
        }
        catch (Exception ex)
        {
            NodeOptions.Write($"[C-MOVE SCP] {ex.Message}");
            error = DicomStatus.QueryRetrieveUnableToCalculateNumberOfMatches;
        }
        if (error is not null || instances.Count > ushort.MaxValue)
        {
            yield return new DicomCMoveResponse(request, error ?? DicomStatus.QueryRetrieveUnableToPerformSuboperations);
            yield break;
        }
        NodeOptions.Write($"Instances found: {instances.Count}");
        if (instances.Count == 0)
        {
            yield return new DicomCMoveResponse(request, DicomStatus.Success) { Completed = 0, Failures = 0, Warnings = 0 };
            yield break;
        }

        var updates = Channel.CreateUnbounded<(string Uid, DicomStatus Status)>();
        var completed = 0;
        var warnings = 0;
        var failedUids = new List<string>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        yield return Progress(DicomStatus.Pending);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var sending = SendInstancesAsync();
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync())
            {
                if (!handled.Add(update.Uid)) continue;
                if (update.Status.State == DicomState.Success) completed++;
                else if (update.Status.State == DicomState.Warning) warnings++;
                else failedUids.Add(update.Uid);
                yield return Progress(DicomStatus.Pending);
            }
            await sending;
        }
        finally
        {
            timeout.Cancel();
            await sending;
        }
        foreach (var instance in instances.Where(i => !handled.Contains(i.SopInstanceUid)))
            failedUids.Add(instance.SopInstanceUid);
        var finalStatus = failedUids.Count == instances.Count
            ? DicomStatus.QueryRetrieveUnableToPerformSuboperations
            : failedUids.Count == 0 && warnings == 0
                ? DicomStatus.Success : DicomStatus.QueryRetrieveSubOpsOneOrMoreFailures;
        var final = Progress(finalStatus);
        final.Command.Remove(DicomTag.NumberOfRemainingSuboperations);
        if (failedUids.Count > 0)
            final.Dataset = new DicomDataset { { DicomTag.FailedSOPInstanceUIDList, failedUids.ToArray() } };
        yield return final;

        DicomCMoveResponse Progress(DicomStatus status) => new(request, status)
        {
            Remaining = instances.Count - completed - warnings - failedUids.Count,
            Completed = completed,
            Failures = failedUids.Count,
            Warnings = warnings
        };

        async Task SendInstancesAsync()
        {
            try
            {
                NodeOptions.Write($"[C-STORE SCU - C-MOVE]\nConnecting: {destination.AeTitle}\n{destination.Host}:{destination.Port}");
                var client = DicomClientFactory.Create(destination.Host, destination.Port, false, NodeOptions.Node.AeTitle, destination.AeTitle);
                for (var i = 0; i < instances.Count; i++)
                {
                    var instance = instances[i];
                    try
                    {
                        var file = await DicomFile.OpenAsync(instance.Path);
                        var store = new DicomCStoreRequest(file);
                        store.Command.AddOrUpdate(DicomTag.MoveOriginatorApplicationEntityTitle, Association.CallingAE);
                        store.Command.AddOrUpdate(DicomTag.MoveOriginatorMessageID, request.MessageID);
                        store.OnResponseReceived += (_, response) =>
                        {
                            NodeOptions.Write($"[C-STORE SCU - C-MOVE] {instance.SopInstanceUid}: {response.Status}");
                            updates.Writer.TryWrite((instance.SopInstanceUid, response.Status));
                        };
                        NodeOptions.Write($"Sending C-STORE {i + 1}/{instances.Count}");
                        await client.AddRequestAsync(store);
                    }
                    catch (Exception ex)
                    {
                        NodeOptions.Write($"[C-STORE SCU - C-MOVE] {instance.Path}: {ex.Message}");
                        updates.Writer.TryWrite((instance.SopInstanceUid, DicomStatus.ProcessingFailure));
                    }
                }
                await client.SendAsync(timeout.Token);
            }
            catch (Exception ex) { NodeOptions.Write($"[C-STORE SCU - C-MOVE] Error: {ex.Message}"); }
            finally { updates.Writer.TryComplete(); }
        }
    }
}
