using GLMS.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GLMS.Web.Data;

/// <summary>
/// EF Core context for the SQL Server database.
///
/// The schema is configured entirely with the Fluent API (one IEntityTypeConfiguration per table below),
/// so the domain classes in GLMS.Core stay free of persistence attributes.
///
/// Normalised to 3NF:
///   Clients 1 --- * Contracts 1 --- * ServiceRequests * --- 1 ExchangeRates
///   Contracts 1 --- 0..1 ContractAgreements
///   Contracts 1 --- * ContractAuditEntries
/// Every non-key column depends on its own table's key only. Document details live in ContractAgreements
/// and rate details live in ExchangeRates, so neither is repeated. The one stored derived value,
/// ServiceRequests.CostZar, is kept on purpose: the brief requires the local cost to be saved, and it records
/// the amount actually quoted at the time.
/// </summary>
public class GlmsDbContext : DbContext
{
    public GlmsDbContext(DbContextOptions<GlmsDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<SignedAgreement> ContractAgreements => Set<SignedAgreement>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<ContractAuditEntry> ContractAuditEntries => Set<ContractAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GlmsDbContext).Assembly);
    }

    /// <summary>SQL for "column must be one of these enum names", e.g. [Status] IN ('Draft','Active').</summary>
    internal static string InEnum<TEnum>(string column) where TEnum : struct, Enum =>
        $"[{column}] IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"))})";
}

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Clients", t =>
        {
            t.HasCheckConstraint("CK_Clients_Name_NotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
            t.HasCheckConstraint("CK_Clients_Email_Format", "[Email] LIKE '%_@_%._%'");
        });

        b.HasKey(c => c.Id);
        b.Property(c => c.Name).IsRequired().HasMaxLength(150);
        b.Property(c => c.ContactPerson).HasMaxLength(120);
        b.Property(c => c.Email).IsRequired().HasMaxLength(256);
        b.Property(c => c.PhoneNumber).HasMaxLength(30);
        b.Property(c => c.Region).IsRequired().HasMaxLength(80);
        b.Property(c => c.CreatedUtc).HasColumnType("datetime2(0)");

        b.HasIndex(c => c.Name).IsUnique();   // no duplicate client records
        b.HasIndex(c => c.Region);

        // Client 1 --- * Contract. Restrict: a client with contracts cannot be deleted.
        b.HasMany(c => c.Contracts)
         .WithOne(c => c.Client)
         .HasForeignKey(c => c.ClientId)
         .IsRequired()
         .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("Contracts", t =>
        {
            t.HasCheckConstraint("CK_Contracts_EndAfterStart", "[EndDate] > [StartDate]");
            t.HasCheckConstraint("CK_Contracts_Status", GlmsDbContext.InEnum<ContractStatus>("Status"));
            t.HasCheckConstraint("CK_Contracts_ServiceLevel", GlmsDbContext.InEnum<ServiceLevel>("ServiceLevel"));
        });

        b.HasKey(c => c.Id);
        b.Property(c => c.ContractNumber).IsRequired().HasMaxLength(30).IsUnicode(false);
        b.HasIndex(c => c.ContractNumber).IsUnique();
        b.Property(c => c.Title).IsRequired().HasMaxLength(200);

        b.Property(c => c.StartDate).HasColumnType("date");
        b.Property(c => c.EndDate).HasColumnType("date");
        b.Property(c => c.CreatedUtc).HasColumnType("datetime2(0)");

        // Optimistic concurrency: SQL Server rowversion, checked on every UPDATE of a contract.
        b.Property(c => c.RowVersion).IsRowVersion();

        // Enums stored as readable text ("Active"), restricted by the CHECK constraints above.
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(c => c.ServiceLevel).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();

        // Indexes that support the admin search (date range + status).
        b.HasIndex(c => new { c.Status, c.EndDate });
        b.HasIndex(c => c.StartDate);

        // Contract 1 --- 0..1 SignedAgreement (its own table). Always loaded with the contract.
        b.HasOne(c => c.SignedAgreement)
         .WithOne()
         .HasForeignKey<SignedAgreement>(a => a.ContractId)
         .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.SignedAgreement).AutoInclude();

        // Runtime-only members (Observer subscribers, computed helpers) are not columns.
        b.Ignore(c => c.Observers);
        b.Ignore(c => c.HasSignedAgreement);
        b.Ignore(c => c.Display);
        b.Ignore(c => c.SignedAgreementFileName);
        b.Ignore(c => c.SignedAgreementOriginalName);
        b.Ignore(c => c.SignedAgreementUploadedUtc);

        // Contract 1 --- * ServiceRequest. Restrict: requests are financial records.
        b.HasMany(c => c.ServiceRequests)
         .WithOne(r => r.Contract)
         .HasForeignKey(r => r.ContractId)
         .IsRequired()
         .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SignedAgreementConfiguration : IEntityTypeConfiguration<SignedAgreement>
{
    public void Configure(EntityTypeBuilder<SignedAgreement> b)
    {
        b.ToTable("ContractAgreements", t =>
        {
            t.HasCheckConstraint("CK_ContractAgreements_PdfOnly", "[StoredFileName] LIKE '%.pdf'");
            t.HasCheckConstraint("CK_ContractAgreements_Size", "[SizeBytes] >= 0 AND [SizeBytes] <= 10485760");
        });

        b.HasKey(a => a.Id);
        b.HasIndex(a => a.ContractId).IsUnique();       // at most one agreement per contract
        b.Property(a => a.StoredFileName).IsRequired().HasMaxLength(64).IsUnicode(false);
        b.HasIndex(a => a.StoredFileName).IsUnique();   // a file on the server belongs to one contract only
        b.Property(a => a.OriginalFileName).IsRequired().HasMaxLength(260);
        b.Property(a => a.UploadedUtc).HasColumnType("datetime2(0)");
    }
}

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("ExchangeRates", t =>
        {
            t.HasCheckConstraint("CK_ExchangeRates_Rate_Positive", "[Rate] > 0");
            t.HasCheckConstraint("CK_ExchangeRates_Currencies", "[BaseCurrency] <> [QuoteCurrency]");
        });

        b.HasKey(r => r.Id);
        b.Property(r => r.BaseCurrency).IsRequired().HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(r => r.QuoteCurrency).IsRequired().HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(r => r.Rate).HasPrecision(18, 6);
        b.Property(r => r.Source).IsRequired().HasMaxLength(150);
        b.Property(r => r.PublishedUtc).HasColumnType("datetime2(0)");

        // Each published rate is stored once.
        b.HasIndex(r => new { r.BaseCurrency, r.QuoteCurrency, r.PublishedUtc, r.Rate }).IsUnique();
    }
}

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> b)
    {
        b.ToTable("ServiceRequests", t =>
        {
            t.HasCheckConstraint("CK_ServiceRequests_CostUsd_Positive", "[CostUsd] > 0");
            t.HasCheckConstraint("CK_ServiceRequests_CostZar_NotNegative", "[CostZar] >= 0");
            t.HasCheckConstraint("CK_ServiceRequests_DueAfterCreated", "[ResponseDueUtc] >= [CreatedUtc]");
            t.HasCheckConstraint("CK_ServiceRequests_ClosedAfterCreated", "[ClosedUtc] IS NULL OR [ClosedUtc] >= [CreatedUtc]");
            t.HasCheckConstraint("CK_ServiceRequests_Status", GlmsDbContext.InEnum<ServiceRequestStatus>("Status"));
            t.HasCheckConstraint("CK_ServiceRequests_Priority", GlmsDbContext.InEnum<RequestPriority>("Priority"));
        });

        b.HasKey(r => r.Id);
        b.Property(r => r.Description).IsRequired().HasMaxLength(1000);

        b.Property(r => r.CostUsd).HasPrecision(18, 2);
        b.Property(r => r.CostZar).HasPrecision(18, 2);

        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(r => r.Priority).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(r => r.StatusReason).HasMaxLength(300);
        b.Property(r => r.CreatedUtc).HasColumnType("datetime2(0)");
        b.Property(r => r.ResponseDueUtc).HasColumnType("datetime2(0)");
        b.Property(r => r.ClosedUtc).HasColumnType("datetime2(0)");

        // ServiceRequest * --- 1 ExchangeRate. Restrict: a rate used for costing can't be deleted.
        b.HasOne(r => r.ExchangeRate)
         .WithMany()
         .HasForeignKey(r => r.ExchangeRateId)
         .IsRequired()
         .OnDelete(DeleteBehavior.Restrict);

        b.Ignore(r => r.IsOpen);
        b.Ignore(r => r.ExchangeRateUsed);
        b.Ignore(r => r.RateSource);
        b.Ignore(r => r.RateRetrievedUtc);

        b.HasIndex(r => new { r.Status, r.ResponseDueUtc });
    }
}

public class ContractAuditEntryConfiguration : IEntityTypeConfiguration<ContractAuditEntry>
{
    public void Configure(EntityTypeBuilder<ContractAuditEntry> b)
    {
        b.ToTable("ContractAuditEntries", t =>
        {
            t.HasCheckConstraint("CK_ContractAuditEntries_FromStatus", GlmsDbContext.InEnum<ContractStatus>("FromStatus"));
            t.HasCheckConstraint("CK_ContractAuditEntries_ToStatus", GlmsDbContext.InEnum<ContractStatus>("ToStatus"));
            t.HasCheckConstraint("CK_ContractAuditEntries_Severity", GlmsDbContext.InEnum<AuditSeverity>("Severity"));
            t.HasCheckConstraint("CK_ContractAuditEntries_StatusChanged", "[FromStatus] <> [ToStatus]");
        });

        b.HasKey(a => a.Id);
        b.Property(a => a.FromStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(a => a.ToStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(a => a.Severity).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(a => a.Message).IsRequired().HasMaxLength(500);
        b.Property(a => a.OccurredUtc).HasColumnType("datetime2(0)");
        b.HasIndex(a => new { a.ContractId, a.OccurredUtc });

        b.HasOne<Contract>()
         .WithMany()
         .HasForeignKey(a => a.ContractId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
