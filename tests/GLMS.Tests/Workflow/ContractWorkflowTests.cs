using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Workflow;

/// <summary>Contract status workflow + Observer pattern (Contract is the Subject).</summary>
public class ContractWorkflowTests
{
    [Theory]
    [InlineData(ContractStatus.Draft, ContractStatus.Active)]
    [InlineData(ContractStatus.Active, ContractStatus.OnHold)]
    [InlineData(ContractStatus.Active, ContractStatus.Expired)]
    [InlineData(ContractStatus.OnHold, ContractStatus.Active)]
    [InlineData(ContractStatus.OnHold, ContractStatus.Expired)]
    public void ChangeStatus_AllowedTransition_Succeeds(ContractStatus from, ContractStatus to)
    {
        var contract = TestData.Contract(from);

        contract.ChangeStatus(to);

        Assert.Equal(to, contract.Status);
    }

    [Theory]
    [InlineData(ContractStatus.Expired, ContractStatus.Active)]
    [InlineData(ContractStatus.Expired, ContractStatus.OnHold)]
    [InlineData(ContractStatus.Expired, ContractStatus.Draft)]
    [InlineData(ContractStatus.Draft, ContractStatus.Expired)]
    [InlineData(ContractStatus.Draft, ContractStatus.OnHold)]
    [InlineData(ContractStatus.Active, ContractStatus.Draft)]
    public void ChangeStatus_IllegalTransition_Throws_AndStatusUnchanged(ContractStatus from, ContractStatus to)
    {
        var contract = TestData.Contract(from);

        Assert.Throws<InvalidStatusTransitionException>(() => contract.ChangeStatus(to));
        Assert.Equal(from, contract.Status);
    }

    [Fact]
    public void Activate_WithoutSignedAgreement_Throws()
    {
        var contract = TestData.Contract(ContractStatus.Draft, withAgreement: false);

        var ex = Assert.Throws<BusinessRuleException>(() => contract.ChangeStatus(ContractStatus.Active));
        Assert.Contains("signed agreement", ex.Message);
        Assert.Equal(ContractStatus.Draft, contract.Status);
    }

    [Fact]
    public void ChangeStatus_NotifiesEverySubscribedObserver_WithPreviousStatus()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var first = new RecordingObserver();
        var second = new RecordingObserver();
        contract.Subscribe(first);
        contract.Subscribe(second);

        contract.ChangeStatus(ContractStatus.OnHold);

        Assert.Equal((ContractStatus.Active, ContractStatus.OnHold), Assert.Single(first.Notifications));
        Assert.Equal((ContractStatus.Active, ContractStatus.OnHold), Assert.Single(second.Notifications));
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_DoesNotNotify()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var observer = new RecordingObserver();
        contract.Subscribe(observer);

        contract.ChangeStatus(ContractStatus.Active);

        Assert.Empty(observer.Notifications);
    }

    [Fact]
    public void FailedTransition_DoesNotNotifyObservers()
    {
        var contract = TestData.Contract(ContractStatus.Expired);
        var observer = new RecordingObserver();
        contract.Subscribe(observer);

        Assert.Throws<InvalidStatusTransitionException>(() => contract.ChangeStatus(ContractStatus.Active));
        Assert.Empty(observer.Notifications);
    }

    [Fact]
    public void Subscribe_SameObserverTwice_IsNotifiedOnce()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var observer = new RecordingObserver();
        contract.Subscribe(observer);
        contract.Subscribe(observer);

        contract.ChangeStatus(ContractStatus.Expired);

        Assert.Single(observer.Notifications);
    }

    [Fact]
    public void Unsubscribe_StopsNotifications()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var observer = new RecordingObserver();
        contract.Subscribe(observer);
        contract.Unsubscribe(observer);

        contract.ChangeStatus(ContractStatus.OnHold);

        Assert.Empty(observer.Notifications);
    }

    [Fact]
    public void Subscribe_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TestData.Contract().Subscribe(null!));
    }

    [Fact]
    public void AttachSignedAgreement_OnExpiredContract_Throws()
    {
        var contract = TestData.Contract(ContractStatus.Expired);
        Assert.Throws<BusinessRuleException>(() => contract.AttachSignedAgreement("abc.pdf", "x.pdf", TestData.Today));
    }

    [Theory]
    [InlineData(ContractStatus.Active, -1, true)]
    [InlineData(ContractStatus.OnHold, -1, true)]
    [InlineData(ContractStatus.Active, 0, false)]   // ends today -> still valid today
    [InlineData(ContractStatus.Draft, -1, false)]
    [InlineData(ContractStatus.Expired, -1, false)]
    public void IsOverdueForExpiry_DependsOnStatusAndEndDate(ContractStatus status, int endOffsetDays, bool expected)
    {
        var contract = TestData.Contract(status, start: TestData.Today.AddYears(-1), end: TestData.Today.AddDays(endOffsetDays));
        Assert.Equal(expected, contract.IsOverdueForExpiry(TestData.Today));
    }
}
