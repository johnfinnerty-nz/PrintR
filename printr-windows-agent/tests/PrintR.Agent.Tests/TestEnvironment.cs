using System.Runtime.CompilerServices;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
namespace PrintR.Agent.Tests;

public static class TestEnvironment
{
    [ModuleInitializer]
    public static void Initialize() => Environment.SetEnvironmentVariable("PRINTR_DATA_DIR", Path.Combine(Path.GetTempPath(), "printr-tests-" + Guid.NewGuid().ToString("N")));
}
