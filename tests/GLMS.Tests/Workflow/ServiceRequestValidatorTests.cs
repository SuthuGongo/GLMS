using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Workflow;
public class ServiceRequestValidatorTests
{
    private readonly ServiceRequestValidator _validator = new(new FakeClock());

    private static NewServiceRequest Input(decimal cost = 100m, string description = "Customs clearance") =>
        new(1, description, cost);

    [Fact]
    public void ActiveContractWithinTerm_IsValid()
    {
        Assert.Empty(_validator.Validate(TestData.Contract(ContractStatus.Active), Input()));
    }

    [Fact]
    public void ExpiredContract_IsRejected()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            _validator.EnsureValid(TestData.Contract(ContractStatus.Expired), Input()));
        Assert.Contains("EXPIRED", ex.Message);
    }

    [Fact]
    public void OnHoldContract_IsRejected()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            _validator.EnsureValid(TestData.Contract(ContractStatus.OnHold), Input()));
        Assert.Contains("ON HOLD", ex.Message);
    }

    [Fact]
    public void DraftContract_IsRejected()
    {
        Assert.Throws<BusinessRuleException>(() => _validator.EnsureValid(TestData.Contract(ContractStatus.Draft), Input()));
    }

    [Fact]
    public void ActiveContract_WhoseEndDateHasPassed_IsRejected()
    {
        var contract = TestData.Contract(ContractStatus.Active, start: TestData.Today.AddYears(-1), end: TestData.Today.AddDays(-1));
        Assert.False(_validator.CanRaiseRequests(contract));
    }

    [Fact]
    public void ActiveContract_NotStartedYet_IsRejected()
    {
        var contract = TestData.Contract(ContractStatus.Active, start: TestData.Today.AddDays(1), end: TestData.Today.AddYears(1));
        Assert.False(_validator.CanRaiseRequests(contract));
    }

    [Fact]
    public void ActiveContract_OnFirstAndLastDayOfTerm_IsAccepted()
    {
        Assert.True(_validator.CanRaiseRequests(TestData.Contract(ContractStatus.Active, start: TestData.Today, end: TestData.Today.AddDays(30))));
        Assert.True(_validator.CanRaiseRequests(TestData.Contract(ContractStatus.Active, start: TestData.Today.AddDays(-30), end: TestData.Today)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-50")]
    [InlineData("10000000.01")]
    [InlineData("10.555")]
    public void InvalidCost_IsRejected(string cost)
    {
        var errors = _validator.Validate(TestData.Contract(), Input(decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Single(errors);
        Assert.Contains("cost", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyDescription_IsRejected(string description)
    {
        Assert.NotEmpty(_validator.Validate(TestData.Contract(), Input(description: description)));
    }

    [Fact]
    public void TooLongDescription_IsRejected()
    {
        var text = new string('x', ServiceRequestValidator.MaxDescriptionLength + 1);
        Assert.NotEmpty(_validator.Validate(TestData.Contract(), Input(description: text)));
    }

    [Fact]
    public void NullContract_AndNullInput_ReportErrors_InsteadOfCrashing()
    {
        var errors = _validator.Validate(null, null);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void MultipleProblems_AreAllReported()
    {
        var errors = _validator.Validate(TestData.Contract(ContractStatus.Expired), Input(cost: 0, description: ""));
        Assert.Equal(3, errors.Count);
    }

    // ------------------------------------------------------------------ Observer behaviour

    [Fact]
    public void Observer_ContractPutOnHold_HoldsOpenRequests_LeavesClosedOnesAlone()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var pending = TestData.Request(contract, ServiceRequestStatus.Pending, 1);
        var inProgress = TestData.Request(contract, ServiceRequestStatus.InProgress, 2);
        var completed = TestData.Request(contract, ServiceRequestStatus.Completed, 3);
        contract.Subscribe(_validator);

        contract.ChangeStatus(ContractStatus.OnHold);

        Assert.Equal(ServiceRequestStatus.OnHold, pending.Status);
        Assert.Equal(ServiceRequestStatus.OnHold, inProgress.Status);
        Assert.Equal(ServiceRequestStatus.Completed, completed.Status);
        Assert.Contains("on hold", pending.StatusReason);
    }

    [Fact]
    public void Observer_ContractExpires_CancelsOpenRequests()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var pending = TestData.Request(contract, ServiceRequestStatus.Pending, 1);
        var completed = TestData.Request(contract, ServiceRequestStatus.Completed, 2);
        contract.Subscribe(_validator);

        contract.ChangeStatus(ContractStatus.Expired);

        Assert.Equal(ServiceRequestStatus.Cancelled, pending.Status);
        Assert.NotNull(pending.ClosedUtc);
        Assert.Equal(ServiceRequestStatus.Completed, completed.Status);
    }

    [Fact]
    public void Observer_ContractReactivated_ResumesHeldRequests()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var request = TestData.Request(contract, ServiceRequestStatus.Pending);
        contract.Subscribe(_validator);

        contract.ChangeStatus(ContractStatus.OnHold);
        contract.ChangeStatus(ContractStatus.Active);

        Assert.Equal(ServiceRequestStatus.Pending, request.Status);
    }
}
