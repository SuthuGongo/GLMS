namespace GLMS.Core.Domain;

/// <summary>
/// The signed agreement PDF on file for a contract (table ContractAgreements, one row per contract at most).
/// Kept out of the Contracts table because these columns describe the document, not the contract,
/// and would otherwise be a group of nullable columns on every draft contract.
/// </summary>
public class SignedAgreement
{
    // For EF Core
    private SignedAgreement() { }

    public SignedAgreement(string storedFileName, string originalFileName, long sizeBytes, DateTime uploadedUtc)
    {
        Replace(storedFileName, originalFileName, sizeBytes, uploadedUtc);
    }

    public int Id { get; private set; }
    public int ContractId { get; private set; }

    /// <summary>GUID-based name on the file server, e.g. 3f2a...e1.pdf. Unique.</summary>
    public string StoredFileName { get; private set; } = string.Empty;

    /// <summary>The name the user uploaded, used for the download.</summary>
    public string OriginalFileName { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }
    public DateTime UploadedUtc { get; private set; }

    internal void Replace(string storedFileName, string originalFileName, long sizeBytes, DateTime uploadedUtc)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
            throw new ArgumentException("Stored file name is required.", nameof(storedFileName));
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "File size cannot be negative.");

        StoredFileName = storedFileName;
        OriginalFileName = originalFileName;
        SizeBytes = sizeBytes;
        UploadedUtc = uploadedUtc;
    }
}
