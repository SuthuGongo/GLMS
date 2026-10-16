using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Workflow;

public class ComplianceLoggerTests
{
    private readonly InMemoryAuditSink _sink = new();
    private readonly FakeClock _clock = new();

    [Theory]
    [InlineData(ContractStatus.Active, ContractStatus.Expired, AuditSeverity.Critical)]
    [InlineData(ContractStatus.Active, ContractStatus.OnHold, AuditSeverity.Warning)]
    [InlineData(ContractStatus.Draft, ContractStatus.Active, AuditSeverity.Information)]
    public void StatusChange_WritesAuditEntry_WithMatchingSeverity(ContractStatus from, ContractStatus to, AuditSeverity severity)
    {
        var contract = TestData.Contract(from, id: 42);
        contract.Subscribe(new ComplianceLogger(_sink, _clock));

        contract.ChangeStatus(to);

        var entry = Assert.Single(_sink.Entries);
        Assert.Equal(42, entry.ContractId);
        Assert.Equal(from, entry.FromStatus);
        Assert.Equal(to, entry.ToStatus);
        Assert.Equal(severity, entry.Severity);
        Assert.Equal(_clock.UtcNow, entry.OccurredUtc);
        Assert.Contains(contract.ContractNumber, entry.Message);
    }

    [Fact]
    public void BothObservers_ReactToTheSameChange()
    {
        var contract = TestData.Contract(ContractStatus.Active);
        var request = TestData.Request(contract);
        contract.Subscribe(new ServiceRequestValidator(_clock));
        contract.Subscribe(new ComplianceLogger(_sink, _clock));

        contract.ChangeStatus(ContractStatus.Expired);

        Assert.Equal(ServiceRequestStatus.Cancelled, request.Status);
        Assert.Single(_sink.Entries);
    }
}

public class ServiceRequestEntityTests
{
    private readonly DateTime _now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HappyPath_Pending_InProgress_Completed()
    {
        var request = TestData.Request(TestData.Contract());

        request.Start();
        request.Complete(_now);

        Assert.Equal(ServiceRequestStatus.Completed, request.Status);
        Assert.Equal(_now, request.ClosedUtc);
        Assert.False(request.IsOpen);
    }

    [Fact]
    public void Complete_WithoutStarting_Throws()
    {
        var request = TestData.Request(TestData.Contract());
        Assert.Throws<InvalidStatusTransitionException>(() => request.Complete(_now));
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        var request = TestData.Request(TestData.Contract());
        request.Start();
        Assert.Throws<InvalidStatusTransitionException>(() => request.Start());
    }

    [Theory]
    [InlineData(ServiceRequestStatus.Completed)]
    [InlineData(ServiceRequestStatus.Cancelled)]
    public void Cancel_ClosedRequest_Throws(ServiceRequestStatus status)
    {
        var request = TestData.Request(TestData.Contract(), status);
        Assert.Throws<InvalidStatusTransitionException>(() => request.Cancel("no", _now));
    }

    [Fact]
    public void Cancel_WithBlankReason_UsesDefaultReason()
    {
        var request = TestData.Request(TestData.Contract());
        request.Cancel("  ", _now);
        Assert.Equal("Cancelled by user", request.StatusReason);
    }
}
