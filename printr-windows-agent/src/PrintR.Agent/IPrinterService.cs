namespace PrintR.Agent;

public interface IPrinterService
{
    IReadOnlyList<PrinterInfo> GetPrinters();
    Task<IReadOnlyList<string>> PrintAsync(string path, PrintRequest request, CancellationToken cancellationToken, Action<JobStatus, string>? updateStatus = null);
}
