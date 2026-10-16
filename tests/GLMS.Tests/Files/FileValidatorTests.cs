using GLMS.Core.Exceptions;
using GLMS.Core.Files;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Files;

/// <summary>"File Validation: verify that uploading a restricted file type (e.g. .exe) throws an error (only .pdf allowed)."</summary>
public class FileValidatorTests
{
    [Theory]
    [InlineData("agreement.pdf")]
    [InlineData("AGREEMENT.PDF")]
    [InlineData("Signed Agreement (final).Pdf")]
    public void ValidateMetadata_PdfFile_IsAccepted(string fileName)
    {
        var ex = Record.Exception(() => FileValidator.ValidateMetadata(fileName, 1024));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("contract.docx")]
    [InlineData("scan.jpg")]
    [InlineData("script.js")]
    [InlineData("archive.zip")]
    public void ValidateMetadata_RestrictedFileType_Throws(string fileName)
    {
        var ex = Assert.Throws<InvalidFileException>(() => FileValidator.ValidateMetadata(fileName, 1024));
        Assert.Contains("Only PDF files are allowed", ex.Message);
    }

    [Fact]
    public void ValidateMetadata_DoubleExtensionTrick_IsRejected()
    {
        // "agreement.pdf.exe" is really an .exe
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidateMetadata("agreement.pdf.exe", 1024));
    }

    [Fact]
    public void ValidateMetadata_NoExtension_IsRejected()
    {
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidateMetadata("agreement", 1024));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateMetadata_MissingFileName_Throws(string? fileName)
    {
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidateMetadata(fileName, 1024));
    }

    [Fact]
    public void ValidateMetadata_EmptyFile_Throws()
    {
        var ex = Assert.Throws<InvalidFileException>(() => FileValidator.ValidateMetadata("agreement.pdf", 0));
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void ValidateMetadata_ExactlyMaxSize_IsAccepted_OneByteMore_IsRejected()
    {
        Assert.Null(Record.Exception(() => FileValidator.ValidateMetadata("a.pdf", FileValidator.MaxFileSizeBytes)));
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidateMetadata("a.pdf", FileValidator.MaxFileSizeBytes + 1));
    }

    [Fact]
    public void ValidatePdfSignature_RealPdfHeader_IsAccepted()
    {
        Assert.Null(Record.Exception(() => FileValidator.ValidatePdfSignature(TestData.PdfBytes())));
    }

    [Fact]
    public void ValidatePdfSignature_ExeRenamedToPdf_IsRejected()
    {
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidatePdfSignature(TestData.ExeBytes()));
    }

    [Fact]
    public void ValidatePdfSignature_TooShort_IsRejected()
    {
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidatePdfSignature(new byte[] { 0x25, 0x50 }));
    }

    [Theory]
    [InlineData("ok.pdf", 10, true)]
    [InlineData("bad.exe", 10, false)]
    [InlineData("empty.pdf", 0, false)]
    public void IsAllowed_ReturnsExpected(string name, long length, bool expected)
    {
        Assert.Equal(expected, FileValidator.IsAllowed(name, length));
    }
}
