using GLMS.Core.Domain;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Shared.Dtos;

namespace GLMS.Api.Mapping;

/// <summary>
/// Turns domain objects into the DTOs the API returns. Entities never leave the API, so the JSON contract
/// (GLMS.Shared) stays stable even if the database model changes, and EF navigation cycles can't leak into JSON.
/// </summary>
public static class DtoMapper
{
    public static ClientDto ToDto(this Client c) => Fill(new ClientDto(), c);

    public static ClientDetailsDto ToDetailsDto(this Client c)
    {
        var dto = Fill(new ClientDetailsDto(), c);
        dto.Contracts = c.Contracts
            .OrderByDescending(x => x.StartDate)
            .Select(x => x.ToDto(c))
            .ToList();
        return dto;
    }

    private static T Fill<T>(T dto, Client c) where T : ClientDto
    {
        dto.Id = c.Id;
        dto.Name = c.Name;
        dto.ContactPerson = c.ContactPerson;
        dto.Email = c.Email;
        dto.PhoneNumber = c.PhoneNumber;
        dto.Region = c.Region;
        dto.CreatedUtc = c.CreatedUtc;
        dto.ContractCount = c.Contracts.Count;
        return dto;
    }

    /// <param name="c">The contract to map.</param>
    /// <param name="owner">Optional client to use when the contract's Client navigation was not loaded.</param>
    public static ContractDto ToDto(this Contract c, Client? owner = null) => Fill(new ContractDto(), c, owner);

    public static T Fill<T>(T dto, Contract c, Client? owner = null) where T : ContractDto
    {
        var client = c.Client ?? owner;
        dto.Id = c.Id;
        dto.ContractNumber = c.ContractNumber;
        dto.Title = c.Title;
        dto.ClientId = c.ClientId;
        dto.ClientName = client?.Name ?? string.Empty;
        dto.ClientRegion = client?.Region ?? string.Empty;
        dto.StartDate = DateOnlyValue(c.StartDate);
        dto.EndDate = DateOnlyValue(c.EndDate);
        dto.Status = c.Status;
        dto.ServiceLevel = c.ServiceLevel;
        dto.HasSignedAgreement = c.HasSignedAgreement;
        dto.SignedAgreementOriginalName = c.SignedAgreementOriginalName;
        dto.SignedAgreementUploadedUtc = c.SignedAgreementUploadedUtc;
        dto.CreatedUtc = c.CreatedUtc;
        dto.OpenRequestCount = c.ServiceRequests.Count(r => r.IsOpen);
        dto.RowVersion = c.RowVersion;
        return dto;
    }
    /// <param name="r">The service request to map.</param>
    /// <param name="contract">Optional parent contract when the request's Contract navigation was not loaded.</param>
    public static ServiceRequestDto ToDto(this ServiceRequest r, Contract? contract = null)
    {
        var parent = r.Contract ?? contract;
        return new ServiceRequestDto
        {
            Id = r.Id,
            ContractId = r.ContractId,
            ContractNumber = parent?.ContractNumber ?? string.Empty,
            ClientName = parent?.Client?.Name ?? string.Empty,
            ServiceLevel = parent?.ServiceLevel ?? default,
            Description = r.Description,
            CostUsd = r.CostUsd,
            CostZar = r.CostZar,
            ExchangeRateUsed = r.ExchangeRateUsed,
            RateSource = r.RateSource,
            RateRetrievedUtc = r.RateRetrievedUtc,
            Status = r.Status,
            Priority = r.Priority,
            CreatedUtc = r.CreatedUtc,
            ResponseDueUtc = r.ResponseDueUtc,
            ClosedUtc = r.ClosedUtc,
            StatusReason = r.StatusReason
        };
    }

    /// <summary>Calendar dates travel as "2026-11-11T00:00:00" (no time zone), so no client can shift them by a day.</summary>
    public static DateTime DateOnlyValue(DateTime date) => DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

    public static AuditEntryDto ToDto(this ContractAuditEntry a) =>
        new(a.Id, a.FromStatus, a.ToStatus, a.Severity, a.Message, a.OccurredUtc);

    public static MilestoneDto ToDto(this Milestone m) => new(m.Title, m.Detail, m.State, m.Date, m.RequestId);

    public static RateQuoteDto ToDto(this ExchangeRateQuote q) =>
        new(q.FromCurrency, q.ToCurrency, q.Rate, q.RetrievedUtc, q.Source, q.IsStale);

    public static RequestSummaryDto ToDto(this RequestSummary s) =>
        new(s.Overdue, s.DueSoon, s.OnTrack, s.Paused, s.ValueThisMonthZar);

    public static WorkItemDto ToDto(this WorkItem w) =>
        new(w.Kind, w.Urgency, w.Contract.ToDto(), w.Request?.ToDto(w.Contract));
}
