using GLMS.Core.Domain;
using GLMS.Core.Querying;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Querying;

/// <summary>LINQ search/filter by date range and status (the same IQueryable code EF Core translates to SQL).</summary>
public class ContractSearchTests
{
    private static readonly Client Atlantic = new() { Id = 1, Name = "Atlantic Freight" };
    private static readonly Client Rhine = new() { Id = 2, Name = "Rhine Valley Imports" };

    // Jan-Jun active, Apr-Dec on hold, 2025 expired, Jul-Dec draft
    private readonly List<Contract> _contracts = new()
    {
        TestData.Contract(ContractStatus.Active, start: new(2026, 1, 1), end: new(2026, 6, 30), id: 1, title: "Ocean freight", client: Atlantic),
        TestData.Contract(ContractStatus.OnHold, ServiceLevel.PriorityLogistics, new(2026, 4, 1), new(2026, 12, 31), id: 2, title: "Port drayage", client: Rhine),
        TestData.Contract(ContractStatus.Expired, start: new(2025, 1, 1), end: new(2025, 12, 31), id: 3, title: "Cold chain", client: Atlantic),
        TestData.Contract(ContractStatus.Draft, start: new(2026, 7, 1), end: new(2026, 12, 31), id: 4, title: "Air freight", client: Rhine, withAgreement: false),
    };

    private List<int> Search(ContractSearchCriteria criteria) =>
        _contracts.AsQueryable().ApplySearch(criteria).Select(c => c.Id).ToList();

    [Fact]
    public void NoCriteria_ReturnsAll_NewestFirst()
    {
        Assert.Equal(new[] { 4, 2, 1, 3 }, Search(new ContractSearchCriteria()));
    }

    [Fact]
    public void NullCriteria_ReturnsAll()
    {
        Assert.Equal(4, _contracts.AsQueryable().ApplySearch(null).Count());
    }

    [Theory]
    [InlineData(ContractStatus.Active, 1)]
    [InlineData(ContractStatus.OnHold, 2)]
    [InlineData(ContractStatus.Expired, 3)]
    [InlineData(ContractStatus.Draft, 4)]
    public void FilterByStatus_ReturnsOnlyThatStatus(ContractStatus status, int expectedId)
    {
        Assert.Equal(new[] { expectedId }, Search(new ContractSearchCriteria { Status = status }));
    }

    [Fact]
    public void FilterByDateRange_ReturnsContractsOverlappingTheRange()
    {
        // May 2026 overlaps #1 (Jan-Jun) and #2 (Apr-Dec), not #3 (2025) or #4 (from July)
        var ids = Search(new ContractSearchCriteria { FromDate = new(2026, 5, 1), ToDate = new(2026, 5, 31) });
        Assert.Equal(new[] { 2, 1 }, ids);
    }

    [Fact]
    public void FilterByDateRange_BoundaryDaysAreInclusive()
    {
        // #1 ends on 30 June, #4 starts on 1 July
        Assert.Equal(new[] { 2, 1 }, Search(new ContractSearchCriteria { FromDate = new(2026, 6, 30), ToDate = new(2026, 6, 30) }));
        Assert.Equal(new[] { 4, 2 }, Search(new ContractSearchCriteria { FromDate = new(2026, 7, 1), ToDate = new(2026, 7, 1) }));
    }

    [Fact]
    public void FilterByFromDateOnly_ExcludesContractsThatEndedBefore()
    {
        Assert.DoesNotContain(3, Search(new ContractSearchCriteria { FromDate = new(2026, 1, 1) }));
    }

    [Fact]
    public void FilterByDateRangeAndStatus_Combined()
    {
        var ids = Search(new ContractSearchCriteria { FromDate = new(2026, 1, 1), ToDate = new(2026, 12, 31), Status = ContractStatus.Active });
        Assert.Equal(new[] { 1 }, ids);
    }

    [Fact]
    public void FilterByClientAndServiceLevel()
    {
        Assert.Equal(new[] { 2 }, Search(new ContractSearchCriteria { ClientId = 2, ServiceLevel = ServiceLevel.PriorityLogistics }));
    }

    [Theory]
    [InlineData("rhine", new[] { 4, 2 })]   // client name, case-insensitive
    [InlineData("FREIGHT", new[] { 4, 1, 3 })] // title + client name
    [InlineData("tm-2026-0003", new[] { 3 })]  // contract number
    [InlineData("nothing-matches", new int[0])]
    public void FilterByKeyword_IsCaseInsensitive(string keyword, int[] expected)
    {
        Assert.Equal(expected, Search(new ContractSearchCriteria { Keyword = keyword }));
    }

    [Fact]
    public void FromDateAfterToDate_IsRejected()
    {
        var criteria = new ContractSearchCriteria { FromDate = new(2026, 12, 1), ToDate = new(2026, 1, 1) };
        Assert.NotEmpty(criteria.Validate());
        Assert.Throws<ArgumentException>(() => Search(criteria));
    }

    [Fact]
    public void WhereOverdueForExpiry_FindsActiveOrOnHoldContractsPastTheirEndDate()
    {
        var ids = _contracts.AsQueryable().WhereOverdueForExpiry(new DateTime(2026, 7, 15)).Select(c => c.Id).ToList();
        Assert.Equal(new[] { 1 }, ids); // #1 ended 30 June and is still Active
    }

    [Fact]
    public void WhereOpenForRequests_ReturnsOnlyActiveContractsWithinTerm()
    {
        var ids = _contracts.AsQueryable().WhereOpenForRequests(new DateTime(2026, 5, 1)).Select(c => c.Id).ToList();
        Assert.Equal(new[] { 1 }, ids);
    }
}
