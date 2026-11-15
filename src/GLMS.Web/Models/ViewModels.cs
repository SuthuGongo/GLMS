using System.ComponentModel.DataAnnotations;
using GLMS.Shared;
using GLMS.Shared.Dtos;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GLMS.Web.Models;

// View models carry DataAnnotations for CLIENT-SIDE validation (jQuery unobtrusive) and early feedback.
// The authoritative rules run on the SERVER behind the Web API (GLMS.Core services), so a user who
// bypasses the browser still cannot break a business rule. Data shown on the pages comes from the API DTOs.

public class DashboardViewModel
{
    public DateTime Today { get; set; }
    public DateTime LocalNow { get; set; }
    public IReadOnlyList<WorkItemDto> WorkItems { get; set; } = Array.Empty<WorkItemDto>();
    public IReadOnlyList<ContractDto> Contracts { get; set; } = Array.Empty<ContractDto>();
    public int TotalContracts { get; set; }
    public int ContractsOpenForWork { get; set; }
    public RequestSummaryDto Requests { get; set; } = new(0, 0, 0, 0, 0m);
    public RateQuoteDto? UsdToZar { get; set; }
    public string? RateError { get; set; }
}

/// <summary>One contract row with its term line (used on Home and the Contracts list).</summary>
public sealed record ContractRowModel(ContractDto Contract, DateTime Today);

// ------------------------------------------------------------------ Sign-in

public class LoginViewModel
{
    [Required, StringLength(50), Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(100), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

// ------------------------------------------------------------------ Clients

public class ClientFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(ValidationLimits.ClientNameMaxLength), Display(Name = "Client name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(ValidationLimits.ClientContactMaxLength), Display(Name = "Contact person")]
    public string? ContactPerson { get; set; }

    [Required, StringLength(ValidationLimits.ClientEmailMaxLength)]
    [RegularExpression(ValidationLimits.EmailPattern, ErrorMessage = "Enter a valid email address, for example name@company.co.za.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(ValidationLimits.ClientPhoneMaxLength), Display(Name = "Phone number")]
    [RegularExpression(ValidationLimits.PhonePattern, ErrorMessage = "Use digits, spaces, brackets, dashes and an optional leading +.")]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(ValidationLimits.ClientRegionMaxLength)]
    public string Region { get; set; } = string.Empty;

    public SaveClientRequest ToRequest() => new()
    {
        Name = Name,
        ContactPerson = ContactPerson,
        Email = Email,
        PhoneNumber = PhoneNumber,
        Region = Region
    };

    public static ClientFormViewModel From(ClientDto c) => new()
    {
        Id = c.Id, Name = c.Name, ContactPerson = c.ContactPerson, Email = c.Email, PhoneNumber = c.PhoneNumber, Region = c.Region
    };
}

// ------------------------------------------------------------------ Contracts

public class ContractFormViewModel
{
    public int Id { get; set; }

    [Required, Display(Name = "Client")]
    [Range(1, int.MaxValue, ErrorMessage = "Choose a client.")]
    public int ClientId { get; set; }

    [Required, StringLength(ValidationLimits.ContractTitleMaxLength), Display(Name = "Contract title")]
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

    /// <summary>The version of the contract the user opened (concurrency check on save, 409 from the API if stale).</summary>
    public byte[]? RowVersion { get; set; }
    public IEnumerable<SelectListItem> Clients { get; set; } = Array.Empty<SelectListItem>();

    public SaveContractRequest ToRequest() => new()
    {
        ClientId = ClientId,
        Title = Title,
        StartDate = StartDate,
        EndDate = EndDate,
        ServiceLevel = ServiceLevel,
        RowVersion = RowVersion
    };

    public static ContractFormViewModel From(ContractDto c) => new()
    {
        Id = c.Id,
        ClientId = c.ClientId,
        Title = c.Title,
        StartDate = c.StartDate,
        EndDate = c.EndDate,
        ServiceLevel = c.ServiceLevel,
        CurrentStatus = c.Status,
        RowVersion = c.RowVersion
    };
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

    public IReadOnlyList<ContractDto> Results { get; set; } = Array.Empty<ContractDto>();
    public IEnumerable<SelectListItem> Clients { get; set; } = Array.Empty<SelectListItem>();
    public DateTime Today { get; set; } = DateTime.Today;

    public ContractSearchQuery ToQuery() => new()
    {
        FromDate = FromDate,
        ToDate = ToDate,
        Status = Status,
        ServiceLevel = ServiceLevel,
        ClientId = ClientId,
        Keyword = Keyword
    };

    public bool HasFilters => !ToQuery().IsEmpty;
}

public class ContractDetailsViewModel
{
    public required ContractDetailsDto Contract { get; init; }
    public IReadOnlyList<AuditEntryDto> AuditTrail => Contract.AuditTrail;
    public IReadOnlyList<ContractStatus> NextStatuses => Contract.NextStatuses;
    public bool CanRaiseRequests => Contract.CanRaiseRequests;
    public IReadOnlyList<string> RequestBlockers => Contract.RequestBlockers;
    public IReadOnlyList<MilestoneDto> Milestones => Contract.Milestones;
    public DateTime Today => Contract.Today;
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

    [Required, StringLength(ValidationLimits.RequestDescriptionMaxLength, MinimumLength = ValidationLimits.RequestDescriptionMinLength), DataType(DataType.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Required, Display(Name = "Cost (USD)")]
    [Range(typeof(decimal), "0.01", "10000000", ErrorMessage = "Enter a USD amount between 0.01 and 10,000,000.", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal? CostUsd { get; set; }

    [Display(Name = "Urgent (Priority Logistics contracts only)")]
    public bool IsUrgent { get; set; }

    public IEnumerable<SelectListItem> Contracts { get; set; } = Array.Empty<SelectListItem>();
    public IReadOnlyDictionary<int, string> ContractServiceLevels { get; set; } = new Dictionary<int, string>();
    public RateQuoteDto? Quote { get; set; }
    public string? RateError { get; set; }

    public CreateServiceRequestRequest ToRequest() => new()
    {
        ContractId = ContractId,
        Description = Description,
        CostUsd = CostUsd ?? 0m,
        IsUrgent = IsUrgent
    };
}

public class ServiceRequestIndexViewModel
{
    public ServiceRequestStatus? Status { get; set; }
    public IReadOnlyList<ServiceRequestDto> Requests { get; set; } = Array.Empty<ServiceRequestDto>();
}
