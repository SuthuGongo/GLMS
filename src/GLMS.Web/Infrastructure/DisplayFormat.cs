using System.Globalization;
using GLMS.Core.Domain;
using GLMS.Core.Services;

namespace GLMS.Web.Infrastructure;

/// <summary>Formatting helpers used by the Razor views.</summary>
public static class DisplayFormat
{
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-GB");

    public static string Zar(decimal amount) => "R " + amount.ToString("N2", Money);
    public static string Usd(decimal amount) => "$ " + amount.ToString("N2", Money);
    public static string Rate(decimal rate) => rate.ToString("0.0000##", CultureInfo.InvariantCulture);
    public static string Date(DateTime date) => date.ToString("dd MMM yyyy", Money);
    public static string DateTime(System.DateTime utc) => ToLocal(utc).ToString("d MMM yyyy, HH:mm", Money);

    /// <summary>
    /// Timestamps are stored in UTC, but SQL Server hands them back without a "Kind",
    /// so treat Unspecified as UTC before converting to the viewer's local time.
    /// </summary>
    public static System.DateTime ToLocal(System.DateTime utc) =>
        (utc.Kind == DateTimeKind.Local ? utc : System.DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToLocalTime();

    public static string Label(ServiceLevel level) => level switch
    {
        ServiceLevel.PriorityLogistics => "Priority Logistics",
        _ => "Standard"
    };

    public static string Label(ContractStatus status) => status == ContractStatus.OnHold ? "On hold" : status.ToString();

    public static string Label(ServiceRequestStatus status) => status switch
    {
        ServiceRequestStatus.InProgress => "In progress",
        ServiceRequestStatus.OnHold => "On hold",
        _ => status.ToString()
    };

    public static string BadgeClass(ContractStatus status) => status switch
    {
        ContractStatus.Active => "badge-status badge-active",
        ContractStatus.OnHold => "badge-status badge-hold",
        ContractStatus.Expired => "badge-status badge-expired",
        _ => "badge-status badge-draft"
    };

    public static string BadgeClass(ServiceRequestStatus status) => status switch
    {
        ServiceRequestStatus.Pending => "badge-status badge-draft",
        ServiceRequestStatus.InProgress => "badge-status badge-active",
        ServiceRequestStatus.OnHold => "badge-status badge-hold",
        ServiceRequestStatus.Completed => "badge-status badge-done",
        _ => "badge-status badge-expired"
    };

    public static string BadgeClass(RequestPriority priority) => priority switch
    {
        RequestPriority.Critical => "badge-status badge-expired",
        RequestPriority.High => "badge-status badge-hold",
        _ => "badge-status badge-draft"
    };

    public static string BadgeClass(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => "badge-status badge-expired",
        AuditSeverity.Warning => "badge-status badge-hold",
        _ => "badge-status badge-draft"
    };

    // ------------------------------------------------------------------ New UI helpers

    /// <summary>Colour tone class (sets --c) for a contract status.</summary>
    public static string Tone(ContractStatus status) => status switch
    {
        ContractStatus.Active => "c-ok",
        ContractStatus.OnHold => "c-warn",
        ContractStatus.Expired => "c-bad",
        _ => "c-idle"
    };

    public static string Tone(WorkItemUrgency urgency) => urgency switch
    {
        WorkItemUrgency.Critical => "c-bad",
        WorkItemUrgency.Warning => "c-warn",
        _ => "c-todo"
    };

    public static string ShortDate(System.DateTime date) => date.ToString("d MMM yyyy", Money);

    /// <summary>"9 Oct at 18:00" in local time.</summary>
    public static string DueText(System.DateTime utc) => ToLocal(utc).ToString("d MMM 'at' HH:mm", Money);

    public static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    public static string Greeting(System.DateTime local) => local.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening"
    };

    public static string Shorten(string? text, int max = 70)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }
}
