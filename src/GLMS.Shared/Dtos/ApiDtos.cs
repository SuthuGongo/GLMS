using System.ComponentModel.DataAnnotations;

namespace GLMS.Shared.Dtos;

// =====================================================================================================
// Request and response bodies of the GLMS Web API. The API never returns EF entities: it maps the domain
// model to these DTOs, so the database shape can change without breaking the MVC client.
// DataAnnotations here give an automatic 400 (ValidationProblemDetails) for malformed input; the full
// business rules are re-checked by the services in GLMS.Core.
// =====================================================================================================

// ------------------------------------------------------------------ Auth

public sealed class LoginRequest
{
    [Required, StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Password { get; set; } = string.Empty;
}

public sealed record LoginResponse(string AccessToken, DateTime ExpiresUtc, string Username, string DisplayName, string Role);

public sealed record CurrentUserDto(string Username, string DisplayName, string Role);

// ------------------------------------------------------------------ Clients

public class ClientDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public int ContractCount { get; set; }
}

public sealed class ClientDetailsDto : ClientDto
{
    public List<ContractDto> Contracts { get; set; } = new();
}

/// <summary>Body of POST /api/clients and PUT /api/clients/{id}.</summary>
public sealed class SaveClientRequest
{
    [Required, StringLength(ValidationLimits.ClientNameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ValidationLimits.ClientContactMaxLength)]
    public string? ContactPerson { get; set; }

    [Required, StringLength(ValidationLimits.ClientEmailMaxLength)]
    public string Email { get; set; } = string.Empty;

    [StringLength(ValidationLimits.ClientPhoneMaxLength)]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(ValidationLimits.ClientRegionMaxLength)]
    public string Region { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ Contracts

public class ContractDto
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientRegion { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public ContractStatus Status { get; set; }
    public ServiceLevel ServiceLevel { get; set; }
    public bool HasSignedAgreement { get; set; }
    public string? SignedAgreementOriginalName { get; set; }
    public DateTime? SignedAgreementUploadedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public int OpenRequestCount { get; set; }

    /// <summary>Concurrency token (SQL Server rowversion, Base64 in JSON). Send it back on PUT/PATCH.</summary>
    public byte[]? RowVersion { get; set; }

    // Presentation helpers (pure date maths shared with the domain, see ContractTerm). Not serialised.
    public int TermLengthDays() => ContractTerm.LengthDays(StartDate, EndDate);
    public double TermProgress(DateTime today) => ContractTerm.Progress(StartDate, EndDate, today);
    public int DaysRemaining(DateTime today) => ContractTerm.DaysRemaining(EndDate, today);
    public int DaysUntilStart(DateTime today) => ContractTerm.DaysUntilStart(StartDate, today);
    public bool IsWithinTerm(DateTime date) => ContractTerm.IsWithinTerm(StartDate, EndDate, date);
    public bool IsOverdueForExpiry(DateTime today) => ContractTerm.IsOverdueForExpiry(Status, EndDate, today);
}

/// <summary>GET /api/contracts/{id}: the contract plus everything the details page needs, worked out by the API.</summary>
public sealed class ContractDetailsDto : ContractDto
{
    public List<ServiceRequestDto> ServiceRequests { get; set; } = new();
    public List<AuditEntryDto> AuditTrail { get; set; } = new();
    public List<MilestoneDto> Milestones { get; set; } = new();

    /// <summary>The statuses the workflow allows next (drives the Activate / Put on hold / Expire buttons).</summary>
    public List<ContractStatus> NextStatuses { get; set; } = new();

    public bool CanRaiseRequests { get; set; }
    public List<string> RequestBlockers { get; set; } = new();

    /// <summary>The API's "today", so the page and the business rules use the same date.</summary>
    public DateTime Today { get; set; }
}

public sealed record AuditEntryDto(int Id, ContractStatus FromStatus, ContractStatus ToStatus, AuditSeverity Severity, string Message, DateTime OccurredUtc);

public sealed record MilestoneDto(string Title, string Detail, MilestoneState State, DateTime? Date, int? RequestId);

/// <summary>Query string of GET /api/contracts (filtering).</summary>
public sealed class ContractSearchQuery
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public ContractStatus? Status { get; set; }
    public ServiceLevel? ServiceLevel { get; set; }
    public int? ClientId { get; set; }

    [StringLength(100)]
    public string? Keyword { get; set; }

    public bool IsEmpty() =>
    FromDate is null && ToDate is null && Status is null && ServiceLevel is null && ClientId is null &&
        string.IsNullOrWhiteSpace(Keyword);
}

/// <summary>Body of POST /api/contracts (JSON) and PUT /api/contracts/{id}.</summary>
public class SaveContractRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a client.")]
    public int ClientId { get; set; }

    [Required, StringLength(ValidationLimits.ContractTitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [EnumDataType(typeof(ServiceLevel))]
    public ServiceLevel ServiceLevel { get; set; } = ServiceLevel.Standard;

    /// <summary>PUT only: the version the user edited (optimistic concurrency, 409 when stale).</summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>Body of PATCH /api/contracts/{id}/status.</summary>
public sealed class ChangeContractStatusRequest
{
    [Required, EnumDataType(typeof(ContractStatus))]
    public ContractStatus? Status { get; set; }

    public byte[]? RowVersion { get; set; }
}

// ------------------------------------------------------------------ Service requests

public sealed class ServiceRequestDto
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public ServiceLevel ServiceLevel { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal CostUsd { get; set; }
    public decimal CostZar { get; set; }
    public decimal ExchangeRateUsed { get; set; }
    public string RateSource { get; set; } = string.Empty;
    public DateTime RateRetrievedUtc { get; set; }
    public ServiceRequestStatus Status { get; set; }
    public RequestPriority Priority { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ResponseDueUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }
    public string? StatusReason { get; set; }

    public bool IsOpen => RequestStatusRules.IsOpen(Status);
}

/// <summary>Body of POST /api/service-requests. The ZAR cost is always calculated by the API.</summary>
public sealed class CreateServiceRequestRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an active contract.")]
    public int ContractId { get; set; }

    [Required, StringLength(ValidationLimits.RequestDescriptionMaxLength, MinimumLength = ValidationLimits.RequestDescriptionMinLength)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "10000000", ErrorMessage = "Enter a USD amount between 0.01 and 10,000,000.",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal CostUsd { get; set; }

    public bool IsUrgent { get; set; }
}

/// <summary>Body of PATCH /api/service-requests/{id}/status: InProgress (start), Completed or Cancelled.</summary>
public sealed class ChangeRequestStatusRequest
{
    [Required, EnumDataType(typeof(ServiceRequestStatus))]
    public ServiceRequestStatus? Status { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

// ------------------------------------------------------------------ Rates and dashboard

public sealed record RateQuoteDto(string FromCurrency, string ToCurrency, decimal Rate, DateTime RetrievedUtc, string Source, bool IsStale);

public sealed record RequestSummaryDto(int Overdue, int DueSoon, int OnTrack, int Paused, decimal ValueThisMonthZar);

public sealed record WorkItemDto(WorkItemKind Kind, WorkItemUrgency Urgency, ContractDto Contract, ServiceRequestDto? Request);

/// <summary>GET /api/dashboard: the home page, built in one call.</summary>
public sealed class DashboardDto
{
    public DateTime Today { get; set; }
    public DateTime UtcNow { get; set; }
    public List<WorkItemDto> WorkItems { get; set; } = new();
    public List<ContractDto> Contracts { get; set; } = new();
    public int TotalContracts { get; set; }
    public int ContractsOpenForWork { get; set; }
    public RequestSummaryDto Requests { get; set; } = new(0, 0, 0, 0, 0m);
    public RateQuoteDto? UsdToZar { get; set; }
    public string? RateError { get; set; }
}
