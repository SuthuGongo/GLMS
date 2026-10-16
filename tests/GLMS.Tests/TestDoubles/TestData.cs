using System.Text;
using GLMS.Core.Domain;
using GLMS.Core.Patterns.Strategy;

namespace GLMS.Tests.TestDoubles;

/// <summary>Builders for test objects. Today in tests is 2026-06-15 (see FakeClock).</summary>
public static class TestData
{
    public static readonly DateTime Today = new(2026, 6, 15);

    public static Contract Contract(
        ContractStatus status = ContractStatus.Active,
        ServiceLevel level = ServiceLevel.Standard,
        DateTime? start = null,
        DateTime? end = null,
        bool withAgreement = true,
        int id = 1,
        string? title = null,
        Client? client = null)
    {
        var contract = new Contract
        {
            Id = id,
            ContractNumber = $"TM-2026-{id:D4}",
            Title = title ?? "Ocean freight",
            ClientId = client?.Id ?? 1,
            Client = client ?? new Client { Id = 1, Name = "Atlantic Freight Partners", Region = "Africa" },
            StartDate = start ?? Today.AddMonths(-3),
            EndDate = end ?? Today.AddMonths(9),
            ServiceLevel = level
        };
        if (withAgreement) contract.AttachSignedAgreement("0123456789abcdef0123456789abcdef.pdf", "signed.pdf", Today);
        GLMS.Core.Domain.Contract.ImportStatus(contract, status);
        return contract;
    }

    public static ServiceRequest Request(Contract contract, ServiceRequestStatus status = ServiceRequestStatus.Pending, int id = 1)
    {
        var request = new ServiceRequest
        {
            Id = id, ContractId = contract.Id, Contract = contract, Description = "Work", CostUsd = 100, CostZar = 1800,
            ExchangeRate = new ExchangeRate { Id = 1, Rate = 18m, Source = "Test", PublishedUtc = Today }
        };
        ServiceRequest.ImportStatus(request, status);
        contract.ServiceRequests.Add(request);
        return request;
    }

    public static CurrencyConversion Conversion(decimal usd, decimal rate = 18.50m) =>
        new("USD", "ZAR", usd, rate, CurrencyCalculator.Convert(usd, rate), "Stub", Today, false);

    /// <summary>A minimal file that looks like a complete PDF: "%PDF-" header and "%%EOF" trailer.</summary>
    public static byte[] PdfBytes(int size = 64)
    {
        var bytes = new byte[Math.Max(size, 16)];
        Array.Fill(bytes, (byte)' ');
        Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("%%EOF\n").CopyTo(bytes, bytes.Length - 6);
        return bytes;
    }

    /// <summary>Has a PDF header but was cut off before the end (no "%%EOF").</summary>
    public static byte[] TruncatedPdfBytes(int size = 64)
    {
        var bytes = new byte[Math.Max(size, 16)];
        Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Bytes that look like a Windows executable ("MZ" header).</summary>
    public static byte[] ExeBytes() => new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

    public const string RatesJson = """
        {
          "result": "success",
          "provider": "https://www.exchangerate-api.com",
          "time_last_update_unix": 1781481600,
          "base_code": "USD",
          "rates": { "USD": 1, "EUR": 0.9187, "ZAR": 18.4523 }
        }
        """;
}
