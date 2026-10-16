using GLMS.Core.Exceptions;

namespace GLMS.Core.Files;
public static class FileValidator
{
    public const string AllowedExtension = ".pdf";
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const int MaxFileNameLength = 200;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PdfEndMarker = "%%EOF"u8.ToArray();
    /// <summary>Real PDFs end with "%%EOF"; the spec allows a little trailing whitespace, so the last 1 KB is searched.</summary>
    public const int TrailerSearchBytes = 1024;

    public static int SignatureLength => PdfSignature.Length;
    /// <summary>Validates name and size. Throws <see cref="InvalidFileException"/> on any problem.</summary>
    public static void ValidateMetadata(string? fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidFileException("Please choose a file to upload.");

        var name = Path.GetFileName(fileName.Trim());
        if (name.Length > MaxFileNameLength)
            throw new InvalidFileException($"The file name is too long (maximum {MaxFileNameLength} characters).");

        // Path.GetExtension returns the LAST extension: "contract.pdf.exe" -> ".exe" (rejected).
        var extension = Path.GetExtension(name);
        if (!string.Equals(extension, AllowedExtension, StringComparison.OrdinalIgnoreCase))
        {
            var shown = string.IsNullOrEmpty(extension) ? "a file with no extension" : $"'{extension}' files";
            throw new InvalidFileException($"Only PDF files are allowed - {shown} cannot be uploaded.");
        }

        if (length <= 0)
            throw new InvalidFileException("The uploaded file is empty.");

        if (length > MaxFileSizeBytes)
            throw new InvalidFileException($"The file is too large. The maximum size is {MaxFileSizeBytes / (1024 * 1024)} MB.");
    }

    /// <summary>Checks the first bytes of the file really are a PDF header.</summary>
    public static void ValidatePdfSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length < PdfSignature.Length || !header[..PdfSignature.Length].SequenceEqual(PdfSignature))
            throw new InvalidFileException("The file is not a valid PDF document (its content does not match the .pdf extension).");
    }
    public static void ValidatePdfTrailer(ReadOnlySpan<byte> tail)
    {
        if (tail.IndexOf(PdfEndMarker) < 0)
            throw new InvalidFileException("The PDF appears to be incomplete or damaged (it has no end-of-file marker). Please export it again and re-upload.");
    }

    public static bool IsAllowed(string? fileName, long length)
    {
        try
        {
            ValidateMetadata(fileName, length);
            return true;
        }
        catch (InvalidFileException)
        {
            return false;
        }
    }
}
