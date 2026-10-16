using System.Text.RegularExpressions;
using GLMS.Core.Abstractions;
using GLMS.Core.Exceptions;

namespace GLMS.Core.Files;

public sealed partial class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A storage folder is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, long declaredLength, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // 1. Name / extension / size rules (throws InvalidFileException).
        FileValidator.ValidateMetadata(originalFileName, declaredLength);

        // 2. Read the header and check it is a real PDF BEFORE anything touches the disk.
        var header = new byte[FileValidator.SignatureLength];
        var headerRead = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        FileValidator.ValidatePdfSignature(header.AsSpan(0, headerRead));

        // 3. Save under a collision-proof name.
        var storedName = $"{Guid.NewGuid():N}{FileValidator.AllowedExtension}";
        var fullPath = Path.Combine(_rootPath, storedName);
        long written = 0;

        try
        {
            await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await target.WriteAsync(header.AsMemory(0, headerRead), ct).ConfigureAwait(false);
                written += headerRead;

                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > FileValidator.MaxFileSizeBytes)
                        throw new InvalidFileException("The file is too large. The maximum size is 10 MB.");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            // 4. The whole file is on disk: confirm it really is a complete PDF.
            await using (var check = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                var tailLength = (int)Math.Min(FileValidator.TrailerSearchBytes, check.Length);
                check.Seek(-tailLength, SeekOrigin.End);
                var tail = new byte[tailLength];
                await check.ReadExactlyAsync(tail, ct).ConfigureAwait(false);
                FileValidator.ValidatePdfTrailer(tail);
            }
        }
        catch
        {
            // Never leave half-written or rejected files behind.
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }

        return new StoredFile(storedName, Path.GetFileName(originalFileName.Trim()), written);
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken ct = default)
    {
        var path = ResolveSafePath(storedFileName);
        if (!File.Exists(path))
            throw new NotFoundException("The signed agreement file could not be found on the file server.");

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public bool Exists(string storedFileName) =>
        IsValidStoredName(storedFileName) && File.Exists(Path.Combine(_rootPath, storedFileName));

    public void Delete(string storedFileName)
    {
        if (!IsValidStoredName(storedFileName)) return;
        var path = Path.Combine(_rootPath, storedFileName);
        if (File.Exists(path)) File.Delete(path);
    }

    private string ResolveSafePath(string storedFileName)
    {
        // Only names this service generated are accepted - blocks "../../appsettings.json" style attacks.
        if (!IsValidStoredName(storedFileName))
            throw new InvalidFileException("Invalid file reference.");
        return Path.Combine(_rootPath, storedFileName);
    }

    public static bool IsValidStoredName(string? storedFileName) =>
        !string.IsNullOrEmpty(storedFileName) && StoredNameRegex().IsMatch(storedFileName);

    [GeneratedRegex("^[a-f0-9]{32}\\.pdf$")]
    private static partial Regex StoredNameRegex();
}
