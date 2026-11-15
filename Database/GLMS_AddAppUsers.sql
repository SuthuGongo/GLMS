-- Part 3 migration 20261111120000_AddAppUsers (Users table for JWT sign-in).
-- The API applies this automatically at start-up (Database.MigrateAsync). This script is for review / manual use.
-- Regenerate with:  dotnet ef migrations script 20261013150156_InitialCreate 20261111120000_AddAppUsers --project src/GLMS.Api -o Database/GLMS_AddAppUsers.sql

BEGIN TRANSACTION;
GO

CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Username] varchar(50) NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [PasswordHash] varchar(256) NOT NULL,
    [Role] varchar(30) NOT NULL,
    [CreatedUtc] datetime2(0) NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Users_Role] CHECK ([Role] IN ('Admin', 'LogisticsManager'))
);
GO

CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261111120000_AddAppUsers', N'8.0.11');
GO

COMMIT;
GO
