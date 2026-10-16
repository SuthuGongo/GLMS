IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Clients] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [ContactPerson] nvarchar(120) NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [PhoneNumber] nvarchar(30) NOT NULL,
    [Region] nvarchar(80) NOT NULL,
    [CreatedUtc] datetime2(0) NOT NULL,
    CONSTRAINT [PK_Clients] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Clients_Email_Format] CHECK ([Email] LIKE '%_@_%._%'),
    CONSTRAINT [CK_Clients_Name_NotBlank] CHECK (LEN(LTRIM(RTRIM([Name]))) > 0)
);
GO

CREATE TABLE [ExchangeRates] (
    [Id] int NOT NULL IDENTITY,
    [BaseCurrency] char(3) NOT NULL,
    [QuoteCurrency] char(3) NOT NULL,
    [Rate] decimal(18,6) NOT NULL,
    [Source] nvarchar(150) NOT NULL,
    [PublishedUtc] datetime2(0) NOT NULL,
    CONSTRAINT [PK_ExchangeRates] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ExchangeRates_Currencies] CHECK ([BaseCurrency] <> [QuoteCurrency]),
    CONSTRAINT [CK_ExchangeRates_Rate_Positive] CHECK ([Rate] > 0)
);
GO

CREATE TABLE [Contracts] (
    [Id] int NOT NULL IDENTITY,
    [ContractNumber] varchar(30) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [ClientId] int NOT NULL,
    [StartDate] date NOT NULL,
    [EndDate] date NOT NULL,
    [Status] varchar(20) NOT NULL,
    [ServiceLevel] varchar(30) NOT NULL,
    [CreatedUtc] datetime2(0) NOT NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_Contracts] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Contracts_EndAfterStart] CHECK ([EndDate] > [StartDate]),
    CONSTRAINT [CK_Contracts_ServiceLevel] CHECK ([ServiceLevel] IN ('Standard', 'PriorityLogistics')),
    CONSTRAINT [CK_Contracts_Status] CHECK ([Status] IN ('Draft', 'Active', 'OnHold', 'Expired')),
    CONSTRAINT [FK_Contracts_Clients_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Clients] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ContractAgreements] (
    [Id] int NOT NULL IDENTITY,
    [ContractId] int NOT NULL,
    [StoredFileName] varchar(64) NOT NULL,
    [OriginalFileName] nvarchar(260) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [UploadedUtc] datetime2(0) NOT NULL,
    CONSTRAINT [PK_ContractAgreements] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ContractAgreements_PdfOnly] CHECK ([StoredFileName] LIKE '%.pdf'),
    CONSTRAINT [CK_ContractAgreements_Size] CHECK ([SizeBytes] >= 0 AND [SizeBytes] <= 10485760),
    CONSTRAINT [FK_ContractAgreements_Contracts_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Contracts] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [ContractAuditEntries] (
    [Id] int NOT NULL IDENTITY,
    [ContractId] int NOT NULL,
    [FromStatus] varchar(20) NOT NULL,
    [ToStatus] varchar(20) NOT NULL,
    [Severity] varchar(20) NOT NULL,
    [Message] nvarchar(500) NOT NULL,
    [OccurredUtc] datetime2(0) NOT NULL,
    CONSTRAINT [PK_ContractAuditEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ContractAuditEntries_FromStatus] CHECK ([FromStatus] IN ('Draft', 'Active', 'OnHold', 'Expired')),
    CONSTRAINT [CK_ContractAuditEntries_Severity] CHECK ([Severity] IN ('Information', 'Warning', 'Critical')),
    CONSTRAINT [CK_ContractAuditEntries_StatusChanged] CHECK ([FromStatus] <> [ToStatus]),
    CONSTRAINT [CK_ContractAuditEntries_ToStatus] CHECK ([ToStatus] IN ('Draft', 'Active', 'OnHold', 'Expired')),
    CONSTRAINT [FK_ContractAuditEntries_Contracts_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Contracts] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [ServiceRequests] (
    [Id] int NOT NULL IDENTITY,
    [ContractId] int NOT NULL,
    [Description] nvarchar(1000) NOT NULL,
    [CostUsd] decimal(18,2) NOT NULL,
    [CostZar] decimal(18,2) NOT NULL,
    [ExchangeRateId] int NOT NULL,
    [Status] varchar(20) NOT NULL,
    [Priority] varchar(20) NOT NULL,
    [CreatedUtc] datetime2(0) NOT NULL,
    [ResponseDueUtc] datetime2(0) NOT NULL,
    [ClosedUtc] datetime2(0) NULL,
    [StatusReason] nvarchar(300) NULL,
    CONSTRAINT [PK_ServiceRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ServiceRequests_ClosedAfterCreated] CHECK ([ClosedUtc] IS NULL OR [ClosedUtc] >= [CreatedUtc]),
    CONSTRAINT [CK_ServiceRequests_CostUsd_Positive] CHECK ([CostUsd] > 0),
    CONSTRAINT [CK_ServiceRequests_CostZar_NotNegative] CHECK ([CostZar] >= 0),
    CONSTRAINT [CK_ServiceRequests_DueAfterCreated] CHECK ([ResponseDueUtc] >= [CreatedUtc]),
    CONSTRAINT [CK_ServiceRequests_Priority] CHECK ([Priority] IN ('Normal', 'High', 'Critical')),
    CONSTRAINT [CK_ServiceRequests_Status] CHECK ([Status] IN ('Pending', 'InProgress', 'OnHold', 'Completed', 'Cancelled')),
    CONSTRAINT [FK_ServiceRequests_Contracts_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Contracts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ServiceRequests_ExchangeRates_ExchangeRateId] FOREIGN KEY ([ExchangeRateId]) REFERENCES [ExchangeRates] ([Id]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [IX_Clients_Name] ON [Clients] ([Name]);
GO

CREATE INDEX [IX_Clients_Region] ON [Clients] ([Region]);
GO

CREATE UNIQUE INDEX [IX_ContractAgreements_ContractId] ON [ContractAgreements] ([ContractId]);
GO

CREATE UNIQUE INDEX [IX_ContractAgreements_StoredFileName] ON [ContractAgreements] ([StoredFileName]);
GO

CREATE INDEX [IX_ContractAuditEntries_ContractId_OccurredUtc] ON [ContractAuditEntries] ([ContractId], [OccurredUtc]);
GO

CREATE INDEX [IX_Contracts_ClientId] ON [Contracts] ([ClientId]);
GO

CREATE UNIQUE INDEX [IX_Contracts_ContractNumber] ON [Contracts] ([ContractNumber]);
GO

CREATE INDEX [IX_Contracts_StartDate] ON [Contracts] ([StartDate]);
GO

CREATE INDEX [IX_Contracts_Status_EndDate] ON [Contracts] ([Status], [EndDate]);
GO

CREATE UNIQUE INDEX [IX_ExchangeRates_BaseCurrency_QuoteCurrency_PublishedUtc_Rate] ON [ExchangeRates] ([BaseCurrency], [QuoteCurrency], [PublishedUtc], [Rate]);
GO

CREATE INDEX [IX_ServiceRequests_ContractId] ON [ServiceRequests] ([ContractId]);
GO

CREATE INDEX [IX_ServiceRequests_ExchangeRateId] ON [ServiceRequests] ([ExchangeRateId]);
GO

CREATE INDEX [IX_ServiceRequests_Status_ResponseDueUtc] ON [ServiceRequests] ([Status], [ResponseDueUtc]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261013150156_InitialCreate', N'8.0.11');
GO

COMMIT;
GO

