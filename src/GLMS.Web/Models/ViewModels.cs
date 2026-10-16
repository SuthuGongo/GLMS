using System.ComponentModel.DataAnnotations;
using GLMS.Core.Domain;
using GLMS.Core.Files;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Querying;
using GLMS.Core.Services;
using GLMS.Core.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GLMS.Web.Models;

// View models carry DataAnnotations for CLIENT-SIDE validation (jQuery unobtrusive).
// The authoritative rules are re-checked SERVER-SIDE in GLMS.Core services, so a user who
// bypasses the browser still cannot break a business rule.

public class DashboardViewModel
{
    public DateTime Today { get; set; }
    public DateTime LocalNow { get; set; }
    public IReadOnlyList<WorkItem> WorkItems { get; set; } = Array.Empty<WorkItem>();
    public IReadOnlyList<Contract> Contracts { get; set; } = Array.Empty<Contract>();
    public int TotalContracts { get; set; }
    public int ContractsOpenForWork { get; set; }
    public RequestSummary Requests { get; set; } = new(0, 0, 0, 0, 0m);
    public ExchangeRateQuote? UsdToZar { get; set; }
    public string? RateError { get; set; }
}

/// <summary>One contract row with its term line (used on Home and the Contracts list).</summary>
public sealed record ContractRowModel(Contract Contract, DateTime Today);

// ------------------------------------------------------------------ Clients

public class ClientFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(ClientValidator.NameMaxLength), Display(Name = "Client name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(ClientValidator.ContactMaxLength), Display(Name = "Contact person")]
    public string? ContactPerson { get; set; }

    [Required, StringLength(ClientValidator.EmailMaxLength)]
    [RegularExpression(ClientValidator.EmailPattern, ErrorMessage = "Enter a valid email address, for example name@company.co.za.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(ClientValidator.PhoneMaxLength), Display(Name = "Phone number")]
    [RegularExpression(@"^\+?[0-9 ()\-]{6,30}$", ErrorMessage = "Use digits, spaces, brackets, dashes and an optional leading +.")]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(ClientValidator.RegionMaxLength)]
    public string Region { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ Contracts

public class ContractFormViewModel
{
    public int Id { get; set; }

    [Required, Display(Name = "Client")]
    [Range(1, int.MaxValue, ErrorMessage = "Choose a client.")]
    public int ClientId { get; set; }

    [Required, StringLength(ContractValidator.TitleMaxLength), Display(Name = "Contract title")]
    public string Title { get; set; } = string.Empty;

    [Required, DataType(DataType.Date), Display(Name = "Start date")]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [Required, DataType(DataType.Date), Display(Name = "End date")]
    [DateAfter(nameof(StartDate), "Start date")]
    public DateTime EndDate { get; set; } = DateTime.Today.AddYears(1);

    [Required, Display(Name = "Service level (SLA)")]
    public ServiceLevel ServiceLevel { get; set; } = ServiceLevel.Standard;

    [PdfFile, Display(Name = "Signed agreement (PDF)")]
    [RequiredIfTrue(nameof(ActivateNow), "Upload the signed agreement to activate the contract now, or untick \"Activate immediately\".")]
    public IFormFile? SignedAgreement { get; set; }

    [Display(Name = "Activate immediately (requires the signed PDF)")]
    public bool ActivateNow { get; set; }

    public ContractStatus? CurrentStatus { get; set; }

    /// <summary>The version of the contract the user opened (concurrency check on save).</summary>
    public byte[]? RowVersion { get; set; }
    public IEnumerable<SelectListItem> Clients { get; set; } = Array.Empty<SelectListItem>();
}

public class ContractSearchViewModel
{
    [DataType(DataType.Date), Display(Name = "From")]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date), Display(Name = "To")]
    public DateTime? ToDate { get; set; }

    public ContractStatus? Status { get; set; }

    [Display(Name = "SLA")]
    public ServiceLevel? ServiceLevel { get; set; }

    [Display(Name = "Client")]
    public int? ClientId { get; set; }

    [StringLength(100), Display(Name = "Keyword")]
    public string? Keyword { get; set; }

    public IReadOnlyList<Contract> Results { get; set; } = Array.Empty<Contract>();
    public IEnumerable<SelectListItem> Clients { get; set; } = Array.Empty<SelectListItem>();
    public DateTime Today { get; set; } = DateTime.Today;

    public ContractSearchCriteria ToCriteria() => new()
    {
        FromDate = FromDate,
        ToDate = ToDate,
        Status = Status,
        ServiceLevel = ServiceLevel,
        ClientId = ClientId,
        Keyword = Keyword
    };

    public bool HasFilters => ToCriteria().IsEmpty == false;
}

public class ContractDetailsViewModel
{
    public required Contract Contract { get; init; }
    public IReadOnlyList<ContractAuditEntry> AuditTrail { get; init; } = Array.Empty<ContractAuditEntry>();
    public IReadOnlyList<ContractStatus> NextStatuses { get; init; } = Array.Empty<ContractStatus>();
    public bool CanRaiseRequests { get; init; }
    public IReadOnlyList<string> RequestBlockers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Milestone> Milestones { get; init; } = Array.Empty<Milestone>();
    public DateTime Today { get; init; }
}

public class UploadAgreementViewModel
{
    public int ContractId { get; set; }

    [Required(ErrorMessage = "Choose a PDF file to upload."), PdfFile, Display(Name = "Signed agreement (PDF)")]
    public IFormFile? SignedAgreement { get; set; }
}

// ------------------------------------------------------------------ Service requests

public class ServiceRequestCreateViewModel
{
    [Required, Display(Name = "Contract")]
    [Range(1, int.MaxValue, ErrorMessage = "Choose an active contract.")]
    public int ContractId { get; set; }

    [Required, StringLength(ServiceRequestValidator.MaxDescriptionLength, MinimumLength = 5), DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Required, Display(Name = "Cost (USD)")]
    [Range(typeof(decimal), "0.01", "10000000", ErrorMessage = "Enter a USD amount between 0.01 and 10,000,000.", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal? CostUsd { get; set; }

    [Display(Name = "Urgent (Priority Logistics contracts only)")]
    public bool IsUrgent { get; set; }

    public IEnumerable<SelectListItem> Contracts { get; set; } = Array.Empty<SelectListItem>();
    public IReadOnlyDictionary<int, string> ContractServiceLevels { get; set; } = new Dictionary<int, string>();
    public ExchangeRateQuote? Quote { get; set; }
    public string? RateError { get; set; }
}

public class ServiceRequestIndexViewModel
{
    public ServiceRequestStatus? Status { get; set; }
    public IReadOnlyList<ServiceRequest> Requests { get; set; } = Array.Empty<ServiceRequest>();
}
