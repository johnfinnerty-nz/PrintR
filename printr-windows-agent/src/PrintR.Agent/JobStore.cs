using System.Collections.Concurrent;

namespace PrintR.Agent;

public sealed class JobStore
{
    private static readonly object FileGate = new();
    private readonly string _path;
    private readonly ConcurrentDictionary<Guid, PrintJob> _jobs = new();

    public JobStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintR Agent");
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
            var jobs = System.Text.Json.JsonSerializer.Deserialize<List<PrintJob>>(File.ReadAllText(_path)) ?? [];
            foreach (var job in jobs.Take(50))
            {
                _jobs[job.JobId] = job;
            }
        }
    }

    private void Save()
    {
        lock (FileGate)
        {
            var jobs = Recent().Take(50).ToList();
            File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(jobs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
