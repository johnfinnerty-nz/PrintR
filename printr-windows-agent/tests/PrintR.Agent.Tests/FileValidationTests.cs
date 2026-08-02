using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class FileValidationTests
{
    [Fact]
    public void Rejects_Path_Traversal_Filenames()
    {
        Assert.Equal("invoice.pdf", FileValidation.SanitizeFileName(@"..\..\invoice.pdf"));
        Assert.Equal("proposal.docx", FileValidation.SanitizeFileName(@"..\..\proposal.docx"));
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Accepts_Supported_Files(string name, string mime)
    {
        var result = FileValidation.Validate(name, mime, 12);
        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public void Rejects_Macro_Enabled_Office_Files()
    {
        foreach (var name in new[] { "macro.docm", "template.dotm", "sheet.xlsm", "slides.pptm" })
        {
            var result = FileValidation.Validate(name, "application/octet-stream", 12);
            Assert.False(result.Ok);
            Assert.Contains("Macro-enabled", result.Message);
        }
    }

    [Fact]
    public void Rejects_Unknown_Office_Files()
    {
        var result = FileValidation.Validate("legacy.doc", "application/msword", 12);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Rejects_Large_Files()
    {
        var result = FileValidation.Validate("huge.pdf", "application/pdf", FileValidation.MaxUploadBytes + 1);
        Assert.False(result.Ok);
    }
}
