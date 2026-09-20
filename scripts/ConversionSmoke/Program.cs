using PrintR.Agent;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Length != 2) throw new ArgumentException("Usage: ConversionSmoke input-directory output-directory");
var converter = new LibreOfficeDocumentConverter(new AgentSettings("conversion-test", 8787, false), NullLogger<LibreOfficeDocumentConverter>.Instance);
if (!converter.Status.Available) throw new InvalidOperationException("LibreOffice must be installed for this integration test.");
var files = Directory.GetFiles(args[0]).Where(FileValidation.RequiresConversion).Order().ToList();
if (files.Count < 7) throw new InvalidOperationException("Expected DOCX, XLSX, PPTX, ODT, ODS, ODP and RTF fixtures.");
foreach (var path in files)
{
    var validation = FileValidation.ValidateContent(path);
    if (!validation.Ok) throw new InvalidOperationException(validation.Message);
    var output = Path.Combine(args[1], Path.GetExtension(path).TrimStart('.'));
    var result = await converter.ConvertAsync(path, output, "pdf", CancellationToken.None);
    if (!result.Success || result.OutputPath is null || new FileInfo(result.OutputPath).Length < 100)
        throw new InvalidOperationException(Path.GetFileName(path) + ": " + result.Message);
    if (!FileValidation.ValidateContent(result.OutputPath).Ok) throw new InvalidOperationException("Invalid PDF output");
    Console.WriteLine("PASS " + Path.GetFileName(path) + " -> PDF");
}
