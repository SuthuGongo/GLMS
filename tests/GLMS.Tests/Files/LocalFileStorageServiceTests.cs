using GLMS.Core.Exceptions;
using GLMS.Core.Files;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Files;

/// <summary>Uses a real temporary folder to prove files are saved to disk (the simulated file server).</summary>
public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "glms-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests() => _storage = new LocalFileStorageService(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static MemoryStream Stream(byte[] bytes) => new(bytes);

    [Fact]
    public async Task Save_Pdf_WritesFileToDisk_UnderGuidName()
    {
        var bytes = TestData.PdfBytes(500);

        var stored = await _storage.SaveAsync(Stream(bytes), "Signed Agreement.pdf", bytes.Length);

        Assert.Matches("^[a-f0-9]{32}\\.pdf$", stored.StoredFileName);
        Assert.Equal("Signed Agreement.pdf", stored.OriginalFileName);
        Assert.Equal(500, stored.SizeBytes);
        Assert.True(File.Exists(Path.Combine(_root, stored.StoredFileName)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, stored.StoredFileName)));
    }

    [Fact]
    public async Task Save_SameNameTwice_DoesNotOverwrite()
    {
        var a = await _storage.SaveAsync(Stream(TestData.PdfBytes()), "agreement.pdf", 64);
        var b = await _storage.SaveAsync(Stream(TestData.PdfBytes()), "agreement.pdf", 64);

        Assert.NotEqual(a.StoredFileName, b.StoredFileName);
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public async Task Save_ExeFile_Throws_AndNothingIsWritten()
    {
        await Assert.ThrowsAsync<InvalidFileException>(() =>
            _storage.SaveAsync(Stream(TestData.ExeBytes()), "malware.exe", 8));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Save_ExeRenamedToPdf_Throws_AndNothingIsWritten()
    {
        await Assert.ThrowsAsync<InvalidFileException>(() =>
            _storage.SaveAsync(Stream(TestData.ExeBytes()), "invoice.pdf", 8));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task OpenRead_ReturnsTheSavedContent()
    {
        var bytes = TestData.PdfBytes(128);
        var stored = await _storage.SaveAsync(Stream(bytes), "a.pdf", bytes.Length);

        await using var read = await _storage.OpenReadAsync(stored.StoredFileName);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);

        Assert.Equal(bytes, copy.ToArray());
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("..\\..\\secret.pdf")]
    [InlineData("C:/Windows/notepad.exe")]
    [InlineData("not-a-guid.pdf")]
    public async Task OpenRead_PathTraversalOrUnknownName_IsRejected(string name)
    {
        await Assert.ThrowsAsync<InvalidFileException>(() => _storage.OpenReadAsync(name));
    }

    [Fact]
    public async Task OpenRead_MissingFile_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _storage.OpenReadAsync("0123456789abcdef0123456789abcdef.pdf"));
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var stored = await _storage.SaveAsync(Stream(TestData.PdfBytes()), "a.pdf", 64);

        _storage.Delete(stored.StoredFileName);

        Assert.False(_storage.Exists(stored.StoredFileName));
    }
}
