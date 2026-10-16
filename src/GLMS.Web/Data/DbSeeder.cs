using System.Text;
using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace GLMS.Web.Data;

/// <summary>
/// Seeds demo data the first time the database is empty, so the app can be demonstrated
/// immediately. Dates are relative to "today" so the data never goes stale, and one contract is
/// deliberately left Active past its end date to show the automatic expiry worker in action.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(GlmsDbContext db, IFileStorageService files, IClock clock, CancellationToken ct = default)
    {
        if (await db.Clients.AnyAsync(ct)) return;

        var today = clock.Today;
        var now = clock.UtcNow;

        var atlantic = new Client { Name = "Atlantic Freight Partners", ContactPerson = "Thandi Mokoena", Email = "thandi@atlanticfreight.co.za", PhoneNumber = "+27 21 555 0101", Region = "Southern Africa", CreatedUtc = now };
        var rhine = new Client { Name = "Rhine Valley Imports GmbH", ContactPerson = "Lukas Becker", Email = "l.becker@rhinevalley.de", PhoneNumber = "+49 69 555 0144", Region = "Europe", CreatedUtc = now };
        var gulf = new Client { Name = "Gulf Coast Shipping LLC", ContactPerson = "Maria Alvarez", Email = "maria@gulfcoastshipping.com", PhoneNumber = "+1 713 555 0190", Region = "North America", CreatedUtc = now };
        var durban = new Client { Name = "Durban Port Distributors", ContactPerson = "Sipho Ndlovu", Email = "sipho@durbanport.co.za", PhoneNumber = "+27 31 555 0123", Region = "Southern Africa", CreatedUtc = now };

        db.Clients.AddRange(atlantic, rhine, gulf, durban);

        async Task<Contract> AddContract(Client client, string number, string title, int startOffset, int endOffset, ServiceLevel level, ContractStatus status, bool withAgreement)
        {
            var contract = new Contract
            {
                Client = client,
                ContractNumber = number,
                Title = title,
                StartDate = today.AddDays(startOffset),
                EndDate = today.AddDays(endOffset),
                ServiceLevel = level,
                CreatedUtc = now
            };

            if (withAgreement)
            {
                var pdf = BuildSamplePdf($"Signed Agreement - {number}", client.Name, title);
                using var stream = new MemoryStream(pdf);
                var stored = await files.SaveAsync(stream, $"{number}-signed-agreement.pdf", pdf.Length, ct);
                contract.AttachSignedAgreement(stored.StoredFileName, stored.OriginalFileName, now, stored.SizeBytes);
            }

            Contract.ImportStatus(contract, status); // seeding restores state; it is not a workflow event
            db.Contracts.Add(contract);
            return contract;
        }

        var c1 = await AddContract(atlantic, "TM-2026-A1001", "Cape Town - Rotterdam ocean freight", -120, 245, ServiceLevel.PriorityLogistics, ContractStatus.Active, true);
        var c2 = await AddContract(rhine, "TM-2026-B2002", "EU inbound consolidation", -60, 300, ServiceLevel.Standard, ContractStatus.Active, true);
        await AddContract(gulf, "TM-2025-C3003", "Houston cold-chain trucking", -400, -30, ServiceLevel.Standard, ContractStatus.Expired, true);
        await AddContract(durban, "TM-2026-D4004", "Durban port drayage", -90, 270, ServiceLevel.PriorityLogistics, ContractStatus.OnHold, true);
        await AddContract(atlantic, "TM-2026-A1005", "Air freight framework (awaiting signature)", 7, 372, ServiceLevel.Standard, ContractStatus.Draft, false);
        // Active but already past its end date -> the ContractExpiryWorker will expire it on start-up.
        await AddContract(durban, "TM-2026-D4006", "Seasonal warehousing (term just ended)", -200, -1, ServiceLevel.Standard, ContractStatus.Active, true);

        // One published rate, stored once and referenced by both seeded requests.
        var seedRate = new ExchangeRate
        {
            BaseCurrency = "USD", QuoteCurrency = "ZAR", Rate = 18.25m, Source = "Seed data",
            PublishedUtc = ExchangeRate.ToStoredPrecision(now.AddDays(-3))
        };
        db.ExchangeRates.Add(seedRate);

        db.ServiceRequests.AddRange(
            NewRequest(c1, "Expedited customs clearance for container MSCU 482913", 1250m, seedRate, RequestPriority.High, now.AddDays(-3)),
            NewRequest(c2, "Additional pallet storage, 40 pallets for 2 weeks", 860m, seedRate, RequestPriority.Normal, now.AddDays(-1)));

        await db.SaveChangesAsync(ct);
    }

    private static ServiceRequest NewRequest(Contract contract, string description, decimal usd, ExchangeRate rate, RequestPriority priority, DateTime createdUtc) => new()
    {
        Contract = contract,
        Description = description,
        CostUsd = usd,
        ExchangeRate = rate,
        CostZar = Math.Round(usd * rate.Rate, 2, MidpointRounding.AwayFromZero),
        Priority = priority,
        CreatedUtc = createdUtc,
        ResponseDueUtc = createdUtc.AddHours(priority == RequestPriority.Normal ? 48 : 8)
    };

    /// <summary>Builds a tiny but valid one-page PDF so seeded contracts have a real downloadable file.</summary>
    internal static byte[] BuildSamplePdf(params string[] lines)
    {
        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        var text = new StringBuilder("BT /F1 16 Tf 72 760 Td ");
        foreach (var line in lines)
            text.Append('(').Append(Escape(line)).Append(") Tj 0 -24 Td ");
        text.Append("(Sample document generated by GLMS seed data.) Tj ET");
        var content = text.ToString();

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
