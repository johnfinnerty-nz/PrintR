using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class JobStoreTests
{
    [Fact]
    public void Tracks_Job_Status()
    {
        var store = new JobStore();
        var job = store.AddQueued(null, "test.txt", "txt");
        store.Update(job.JobId, JobStatus.Completed, "Done", ["mock"]);
        var updated = store.Get(job.JobId);
        Assert.NotNull(updated);
        Assert.Equal(JobStatus.Completed, updated!.Status);
        Assert.Equal("Done", updated.Message);
        Assert.Single(updated.Warnings);
    }
}
