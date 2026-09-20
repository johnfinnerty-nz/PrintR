using System.Diagnostics;

namespace PrintR.Agent;

public static class ProcessRunner
{
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["LC_ALL"] = "C";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var result = await output;
            var message = await error;
            if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} failed ({process.ExitCode}): {message[..Math.Min(message.Length, 500)]}");
            return result.Trim();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            throw;
        }
    }
}
