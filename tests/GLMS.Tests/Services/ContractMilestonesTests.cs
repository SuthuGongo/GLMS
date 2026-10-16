using GLMS.Core.Domain;
using GLMS.Core.Services;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Services;

/// <summary>The lifecycle timeline on the contract page.</summary>
public class ContractMilestonesTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private static ContractAuditEntry Audit(ContractStatus from, ContractStatus to, DateTime when) =>
        new() { ContractId = 1, FromStatus = from, ToStatus = to, OccurredUtc = when };

    private static List<string> Titles(IEnumerable<Milestone> m) => m.Select(x => x.Title).ToList();

    [Fact]
    public void UnsignedDraft_AsksForUpload_ActivationIsUpcoming()
    {
        var contract = TestData.Contract(ContractStatus.Draft, withAgreement: false);
        contract.CreatedUtc = Now.AddDays(-2);

        var milestones = ContractMilestones.Build(contract, Array.Empty<ContractAuditEntry>(), Now);

        Assert.Equal(new[] { "Drafted", "Upload the signed agreement", "Activate", "Expires" }, Titles(milestones));
        Assert.Equal(MilestoneState.ToDo, milestones[1].State);
        Assert.Equal(MilestoneState.Upcoming, milestones[^1].State);
    }

    [Fact]
    public void SignedDraft_AsksForActivation()
    {
        var contract = TestData.Contract(ContractStatus.Draft, withAgreement: true);

        var milestones = ContractMilestones.Build(contract, Array.Empty<ContractAuditEntry>(), Now);

        Assert.Contains(milestones, m => m.Title == "Activate the contract" && m.State == MilestoneState.ToDo);
        Assert.Contains(milestones, m => m.Title == "Signed agreement on file" && m.State == MilestoneState.Reached);
    }

    [Fact]
    public void FullHistory_IsInDateOrder_WithHoldAndReactivation()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        contract.CreatedUtc = Now.AddDays(-30);
        var audit = new[]
        {
            Audit(ContractStatus.OnHold, ContractStatus.Active, Now.AddDays(-2)),   // given out of order on purpose
            Audit(ContractStatus.Draft, ContractStatus.Active, Now.AddDays(-20)),
            Audit(ContractStatus.Active, ContractStatus.OnHold, Now.AddDays(-10)),
        };

        var titles = Titles(ContractMilestones.Build(contract, audit, Now));

        Assert.True(titles.IndexOf("Activated") < titles.IndexOf("Put on hold"));
        Assert.True(titles.IndexOf("Put on hold") < titles.IndexOf("Re-activated"));
        Assert.Equal("Expires", titles[^1]);
    }

    [Fact]
    public void SeededActiveContract_WithoutAudit_StillShowsActivatedAfterDrafted()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        contract.CreatedUtc = Now.AddDays(-30);

        var titles = Titles(ContractMilestones.Build(contract, Array.Empty<ContractAuditEntry>(), Now));

        Assert.True(titles.IndexOf("Drafted") < titles.IndexOf("Activated"));
    }

    [Fact]
    public void OverdueRequest_BecomesToDo_LinkedToRequest()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var request = TestData.Request(contract, ServiceRequestStatus.Pending, id: 7);
        request.ResponseDueUtc = Now.AddHours(-1);

        var todo = Assert.Single(ContractMilestones.Build(contract, Array.Empty<ContractAuditEntry>(), Now), m => m.RequestId == 7);

        Assert.Equal(MilestoneState.ToDo, todo.State);
        Assert.Equal("Respond to request #7", todo.Title);
    }

    [Fact]
    public void ExpiredContract_EndsWithReachedExpiry_AndNoUploadToDo()
    {
        var contract = TestData.Contract(ContractStatus.Expired, withAgreement: false);
        var expiredAt = Now.AddDays(-1);

        var milestones = ContractMilestones.Build(contract, new[] { Audit(ContractStatus.Active, ContractStatus.Expired, expiredAt) }, Now);

        var last = milestones[^1];
        Assert.Equal("Expired", last.Title);
        Assert.Equal(MilestoneState.Reached, last.State);
        Assert.Equal(expiredAt, last.Date);
        Assert.DoesNotContain(milestones, m => m.State == MilestoneState.ToDo);
    }

    [Fact]
    public void OnHoldContract_AsksForADecision()
    {
        var milestones = ContractMilestones.Build(TestData.Contract(ContractStatus.OnHold), Array.Empty<ContractAuditEntry>(), Now);
        Assert.Contains(milestones, m => m.Title == "Re-activate or expire the contract" && m.State == MilestoneState.ToDo);
    }
}
