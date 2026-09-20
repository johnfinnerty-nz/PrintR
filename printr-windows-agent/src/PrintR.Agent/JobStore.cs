using System.Collections.Concurrent;

namespace PrintR.Agent;

public sealed class JobStore
{
    private static readonly object FileGate = new();
    private readonly string _path;
    private readonly ConcurrentDictionary<Guid, PrintJob> _jobs = new();

    public JobStore()
    {
        var dir = AgentPaths.DataDirectory;
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "recent-jobs.json");
        Load();
    }

    public PrintJob AddQueued(string? printerName, string fileName, string fileType)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob(Guid.NewGuid(), JobStatus.Queued, "Print job accepted", printerName, fileName, fileType, [], null, now, null, now);
        _jobs[job.JobId] = job;
        Save();
        return job;
    }

    public PrintJob? Get(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public IReadOnlyList<PrintJob> Recent() => _jobs.Values.OrderByDescending(j => j.CreatedAt).Take(20).ToList();

    public void Update(Guid id, JobStatus status, string message, IReadOnlyList<string>? warnings = null)
    {
        _jobs.AddOrUpdate(
            id,
            _ => throw new InvalidOperationException("Job does not exist."),
            (_, current) => current with
            {
                Status = status,
                Message = message,
                ErrorMessage = status == JobStatus.Failed ? message : current.ErrorMessage,
                CompletedAt = status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled ? DateTimeOffset.UtcNow : current.CompletedAt,
                Warnings = warnings ?? current.Warnings,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        Save();
    }

    private void Load()
    {
        lock (FileGate)
        {
            if (!File.Exists(_path)) return;
            List<PrintJob> jobs;
            try { jobs = System.Text.Json.JsonSerializer.Deserialize<List<PrintJob>>(File.ReadAllText(_path)) ?? []; }
            catch (System.Text.Json.JsonException) { File.Move(_path, _path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); return; }
            foreach (var job in jobs.Take(50))
            {
                _jobs[job.JobId] = job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled ? job :
                    job with { Status = JobStatus.Failed, Message = "Agent restarted before completion. Check the printer queue before retrying.", ErrorMessage = "Interrupted by restart", CompletedAt = DateTimeOffset.UtcNow };
            }
        }
    }

    private void Save()
    {
        lock (FileGate)
        {
            var jobs = Recent().Take(50).ToList();
            AgentPaths.WritePrivateText(_path, System.Text.Json.JsonSerializer.Serialize(jobs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
