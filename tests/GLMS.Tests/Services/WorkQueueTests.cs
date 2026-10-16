using GLMS.Core.Domain;
using GLMS.Core.Services;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Services;

/// <summary>The home page "To do" list, request summary and contract term progress.</summary>
public class WorkQueueTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = Now.Date;

    private static ServiceRequest RequestDue(Contract contract, DateTime dueUtc, ServiceRequestStatus status = ServiceRequestStatus.Pending, int id = 1)
    {
        var r = TestData.Request(contract, status, id);
        r.ResponseDueUtc = dueUtc;
        return r;
    }

    [Fact]
    public void OverdueRequest_IsCritical_AndListedFirst()
    {
        var onHold = TestData.Contract(ContractStatus.OnHold, id: 1);
        var active = TestData.Contract(ContractStatus.Active, id: 2);
        var overdue = RequestDue(active, Now.AddHours(-3));

        var items = WorkQueue.Build(new[] { onHold, active }, new[] { overdue }, Now, Today);

        Assert.Equal(WorkItemKind.OverdueRequest, items[0].Kind);
        Assert.Equal(WorkItemUrgency.Critical, items[0].Urgency);
        Assert.Same(overdue, items[0].Request);
        Assert.Equal(WorkItemKind.ContractOnHold, items[1].Kind);
    }

    [Fact]
    public void RequestDueWithin24Hours_IsWarning()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var item = Assert.Single(WorkQueue.Build(new[] { contract }, new[] { RequestDue(contract, Now.AddHours(5)) }, Now, Today));
        Assert.Equal(WorkItemKind.RequestDueSoon, item.Kind);
    }

    [Fact]
    public void RequestDueLater_IsNotListed()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        Assert.Empty(WorkQueue.Build(new[] { contract }, new[] { RequestDue(contract, Now.AddHours(30)) }, Now, Today));
    }

    [Theory]
    [InlineData(ServiceRequestStatus.OnHold)]
    [InlineData(ServiceRequestStatus.Completed)]
    [InlineData(ServiceRequestStatus.Cancelled)]
    public void PausedOrClosedRequest_PastDueTime_IsNotOverdue(ServiceRequestStatus status)
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var request = RequestDue(contract, Now.AddHours(-5), status);

        Assert.False(request.IsOverdue(Now));
        Assert.DoesNotContain(WorkQueue.Build(new[] { contract }, new[] { request }, Now, Today), i => i.Kind == WorkItemKind.OverdueRequest);
    }

    [Fact]
    public void DraftWithoutPdf_AsksForSignature_DraftWithPdf_AsksForActivation()
    {
        var unsigned = TestData.Contract(ContractStatus.Draft, withAgreement: false, id: 1);
        var signed = TestData.Contract(ContractStatus.Draft, withAgreement: true, id: 2);

        var items = WorkQueue.Build(new[] { unsigned, signed }, Array.Empty<ServiceRequest>(), Now, Today);

        Assert.Contains(items, i => i.Kind == WorkItemKind.AwaitingSignature && i.Contract == unsigned);
        Assert.Contains(items, i => i.Kind == WorkItemKind.ReadyToActivate && i.Contract == signed);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void ActiveContract_EndingWithin30Days_IsFlagged(int daysLeft, bool flagged)
    {
        var contract = TestData.Contract(ContractStatus.Active, start: Today.AddYears(-1), end: Today.AddDays(daysLeft));
        var items = WorkQueue.Build(new[] { contract }, Array.Empty<ServiceRequest>(), Now, Today);
        Assert.Equal(flagged, items.Any(i => i.Kind == WorkItemKind.ContractEndingSoon));
    }

    [Fact]
    public void ActiveContract_PastEndDate_IsAwaitingExpiry_NotEndingSoon()
    {
        var contract = TestData.Contract(ContractStatus.Active, start: Today.AddYears(-1), end: Today.AddDays(-1));
        var item = Assert.Single(WorkQueue.Build(new[] { contract }, Array.Empty<ServiceRequest>(), Now, Today));
        Assert.Equal(WorkItemKind.AwaitingExpiry, item.Kind);
    }

    [Fact]
    public void ExpiredAndHealthyActiveContracts_CreateNoWork()
    {
        var items = WorkQueue.Build(new[] { TestData.Contract(ContractStatus.Expired, id: 1), TestData.Contract(ContractStatus.Active, id: 2) },
            Array.Empty<ServiceRequest>(), Now, Today);
        Assert.Empty(items);
    }

    [Fact]
    public void ItemsOfSameUrgency_AreOrderedByDate()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var later = RequestDue(contract, Now.AddHours(-1), id: 1);
        var earlier = RequestDue(contract, Now.AddHours(-9), id: 2);

        var items = WorkQueue.Build(new[] { contract }, new[] { later, earlier }, Now, Today);

        Assert.Same(earlier, items[0].Request);
        Assert.Same(later, items[1].Request);
    }

    [Fact]
    public void RequestSummary_CountsEachBucketOnce_AndSumsThisMonthsValue()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var overdue = RequestDue(contract, Now.AddHours(-1), id: 1);
        var dueSoon = RequestDue(contract, Now.AddHours(2), id: 2);
        var onTrack = RequestDue(contract, Now.AddDays(3), id: 3);
        var paused = RequestDue(contract, Now.AddHours(-10), ServiceRequestStatus.OnHold, id: 4);
        var cancelled = RequestDue(contract, Now.AddHours(-10), ServiceRequestStatus.Cancelled, id: 5);
        var lastMonth = RequestDue(contract, Now.AddDays(5), ServiceRequestStatus.Completed, id: 6);
        foreach (var r in new[] { overdue, dueSoon, onTrack, paused, cancelled }) { r.CreatedUtc = Now.AddDays(-1); r.CostZar = 100m; }
        lastMonth.CreatedUtc = Now.AddMonths(-1); lastMonth.CostZar = 999m;

        var summary = RequestSummary.Build(new[] { overdue, dueSoon, onTrack, paused, cancelled, lastMonth }, Now);

        Assert.Equal(1, summary.Overdue);
        Assert.Equal(1, summary.DueSoon);
        Assert.Equal(1, summary.OnTrack);
        Assert.Equal(1, summary.Paused);
        Assert.Equal(400m, summary.ValueThisMonthZar); // 4 non-cancelled requests this month
    }

    // ------------------------------------------------------------------ Contract term progress

    [Theory]
    [InlineData(-10, 0.0)]   // not started
    [InlineData(0, 0.0)]     // first day
    [InlineData(50, 0.5)]    // halfway through a 100-day term
    [InlineData(100, 1.0)]   // last day
    [InlineData(150, 1.0)]   // after the end
    public void TermProgress_IsClampedBetweenZeroAndOne(int dayOffset, double expected)
    {
        var contract = TestData.Contract(start: Today, end: Today.AddDays(100));
        Assert.Equal(expected, contract.TermProgress(Today.AddDays(dayOffset)), 3);
    }

    [Fact]
    public void DaysRemaining_AndDaysUntilStart_NeverGoNegative()
    {
        var contract = TestData.Contract(start: Today.AddDays(7), end: Today.AddDays(40));

        Assert.Equal(40, contract.DaysRemaining(Today));
        Assert.Equal(7, contract.DaysUntilStart(Today));
        Assert.Equal(0, contract.DaysRemaining(Today.AddDays(60)));
        Assert.Equal(0, contract.DaysUntilStart(Today.AddDays(10)));
    }
}
