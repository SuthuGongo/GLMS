using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;

namespace GLMS.Core.Domain;

/// <summary>
/// A freight contract between TechMove and a client.
///
/// OBSERVER PATTERN (Part 1, section 2.2) - Contract is the <b>Subject</b>.
/// It keeps a list of <see cref="IContractObserver"/> subscribers and notifies them from
/// <see cref="ChangeStatus"/>, so the status can only ever change through one method and
/// every change is broadcast, no matter which controller, worker or test triggered it.
/// Observers are supplied from outside (dependency injection) - Contract never references
/// a concrete observer class.
/// </summary>
public class Contract
{
    private readonly List<IContractObserver> _observers = new();

    private static readonly Dictionary<ContractStatus, ContractStatus[]> AllowedTransitions = new()
    {
        [ContractStatus.Draft] = new[] { ContractStatus.Active },
        [ContractStatus.Active] = new[] { ContractStatus.OnHold, ContractStatus.Expired },
        [ContractStatus.OnHold] = new[] { ContractStatus.Active, ContractStatus.Expired },
        [ContractStatus.Expired] = Array.Empty<ContractStatus>() 
    };

    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Private setter: the only way to change status is <see cref="ChangeStatus"/>.</summary>
    public ContractStatus Status { get; private set; } = ContractStatus.Draft;

    public ServiceLevel ServiceLevel { get; set; }

    // ---- Signed agreement (file handling). Stored in its own table (ContractAgreements), 1 : 0..1.
    // Only the generated file NAME lives in the DB; the bytes live on the file server (disk).
    public SignedAgreement? SignedAgreement { get; private set; }

    // Convenience read-only views of the agreement (not columns).
    public string? SignedAgreementFileName => SignedAgreement?.StoredFileName;
    public string? SignedAgreementOriginalName => SignedAgreement?.OriginalFileName;
    public DateTime? SignedAgreementUploadedUtc => SignedAgreement?.UploadedUtc;

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Concurrency token (SQL Server rowversion). Changes on every save, so if two people edit the same
    /// contract the second save is detected and rejected instead of silently overwriting the first.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();

    public bool HasSignedAgreement => !string.IsNullOrWhiteSpace(SignedAgreementFileName);

    public IReadOnlyCollection<IContractObserver> Observers => _observers.AsReadOnly();

    // Observer: subject API
    public void Subscribe(IContractObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        if (!_observers.Contains(observer))
        {
            _observers.Add(observer);
        }
    }
    public void Unsubscribe(IContractObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Moves the contract to <paramref name="newStatus"/> and notifies every subscribed observer.
    /// Throws if the transition is not part of the workflow.
    /// </summary>
    public void ChangeStatus(ContractStatus newStatus)
    {
        if (newStatus == Status)
        {
            return; // no change -> no notification
        }

        if (!CanTransition(Status, newStatus))
        {
            throw new InvalidStatusTransitionException(
                $"Contract {Display} cannot move from {Status} to {newStatus}.");
        }

        if (newStatus == ContractStatus.Active && !HasSignedAgreement)
        {
            throw new BusinessRuleException(
                "A contract cannot be activated until its signed agreement (PDF) has been uploaded.");
        }

        var previous = Status;
        Status = newStatus;

        // Copy so an observer may safely unsubscribe itself while being notified.
        foreach (var observer in _observers.ToList())
        {
            observer.OnStatusChanged(this, previous);
        }
    }

    public static bool CanTransition(ContractStatus from, ContractStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<ContractStatus> NextStatuses(ContractStatus from) =>
        AllowedTransitions.TryGetValue(from, out var targets) ? targets : Array.Empty<ContractStatus>();

    // Other behaviour

    public void AttachSignedAgreement(string storedFileName, string originalFileName, DateTime uploadedUtc, long sizeBytes = 0)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
            throw new ArgumentException("Stored file name is required.", nameof(storedFileName));

        if (Status == ContractStatus.Expired)
            throw new BusinessRuleException("The signed agreement of an expired contract cannot be replaced.");

        var original = string.IsNullOrWhiteSpace(originalFileName) ? "signed-agreement.pdf" : originalFileName;

        // Update the existing row in place (one agreement per contract), or create it the first time.
        if (SignedAgreement is null)
            SignedAgreement = new SignedAgreement(storedFileName, original, sizeBytes, uploadedUtc);
        else
            SignedAgreement.Replace(storedFileName, original, sizeBytes, uploadedUtc);
    }

    /// <summary>True when <paramref name="date"/> falls inside the contract term (inclusive).</summary>
    public bool IsWithinTerm(DateTime date) => date.Date >= StartDate.Date && date.Date <= EndDate.Date;

    /// <summary>True when the term has ended but the status has not caught up yet.</summary>
    public bool IsOverdueForExpiry(DateTime today) =>
        (Status == ContractStatus.Active || Status == ContractStatus.OnHold) && EndDate.Date < today.Date;

    public string Display => string.IsNullOrWhiteSpace(ContractNumber) ? $"#{Id}" : ContractNumber;

    // ------------------------------------------------------------------ Term progress (drawn as a "route" in the UI)

    /// <summary>Total length of the contract term in days.</summary>
    public int TermLengthDays() => Math.Max(0, (EndDate.Date - StartDate.Date).Days);

    /// <summary>How far through its term the contract is on <paramref name="today"/>, from 0 (not started) to 1 (ended).</summary>
    public double TermProgress(DateTime today)
    {
        var total = TermLengthDays();
        if (total == 0) return today.Date >= EndDate.Date ? 1 : 0;
        var elapsed = (today.Date - StartDate.Date).TotalDays;
        return Math.Clamp(elapsed / total, 0, 1);
    }

    /// <summary>Days until the term ends (0 on the last day or after it).</summary>
    public int DaysRemaining(DateTime today) => Math.Max(0, (EndDate.Date - today.Date).Days);

    /// <summary>Days until the term starts (0 once it has started).</summary>
    public int DaysUntilStart(DateTime today) => Math.Max(0, (StartDate.Date - today.Date).Days);

    /// <summary>
    /// Used only by data seeding / data import to restore a persisted status without
    /// replaying the workflow. Normal code must use <see cref="ChangeStatus"/>.
    /// </summary>
    internal void RestoreStatus(ContractStatus status) => Status = status;

    /// <summary>Public entry point for importers/seeders (kept explicit so misuse is easy to spot in review).</summary>
    public static void ImportStatus(Contract contract, ContractStatus status) => contract.RestoreStatus(status);
}
