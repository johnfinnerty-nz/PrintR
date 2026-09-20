using System.Threading.Channels;

namespace PrintR.Agent;

public sealed record QueuedPrint(Guid JobId, string Path, PrintRequest Request);

public sealed class PrintQueue(JobStore jobs, IPrinterService printers, AgentSettingsStore settingsStore, ILogger<PrintQueue> logger) : BackgroundService
{
    private readonly Channel<QueuedPrint> _queue = Channel.CreateBounded<QueuedPrint>(new BoundedChannelOptions(16) { SingleReader = true });
    public bool TryEnqueue(QueuedPrint job) => _queue.Writer.TryWrite(job);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(settingsStore.Load().JobTimeoutSeconds));
                    jobs.Update(item.JobId, JobStatus.Printing, "Preparing print job");
                    var warnings = await printers.PrintAsync(item.Path, item.Request, deadline.Token, (status, message) => jobs.Update(item.JobId, status, message));
                    jobs.Update(item.JobId, JobStatus.Completed, "Submitted to printer. Check the printer queue for physical completion.", warnings);
                }
                catch (OperationCanceledException)
                { jobs.Update(item.JobId, JobStatus.Failed, "Job interrupted or timed out. Check the printer queue before retrying."); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Print job {JobId} failed", item.JobId);
                    ApiDiagnostics.RecordError(ex.Message);
                    jobs.Update(item.JobId, JobStatus.Failed, ex.Message);
                }
                finally { Cleanup(item); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var item))
            {
                jobs.Update(item.JobId, JobStatus.Cancelled, "Agent stopped before this job could start.");
                Cleanup(item);
            }
        }
    }

    private void Cleanup(QueuedPrint item)
    {
        try { SpoolCleanup.CleanupJobFolder(Path.GetDirectoryName(item.Path)!, settingsStore.Load()); }
        catch (IOException ex) { logger.LogWarning(ex, "Could not remove spool data for {JobId}", item.JobId); }
    }
}
