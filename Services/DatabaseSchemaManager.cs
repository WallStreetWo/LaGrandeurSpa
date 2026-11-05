using LaGrandeurSpa.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Services;

/// <summary>
/// Keeps databases created by the earlier EnsureCreated-only prototype compatible
/// with the current model. Upgrades are idempotent and versioned so an existing
/// customer database is never dropped or recreated during deployment.
/// </summary>
public sealed class DatabaseSchemaManager(
    ApplicationDbContext db,
    ILogger<DatabaseSchemaManager> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // EnsureCreated is retained only for a completely new/empty database. Existing
        // databases are upgraded below with explicit, non-destructive schema versions.
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (!db.Database.IsSqlServer())
            return;

        await EnsureMigrationTableAsync(cancellationToken);

        if (!await IsVersionAppliedAsync(1, cancellationToken))
        {
            logger.LogInformation("Applying La Grandeur database schema upgrade 1.");
            await ApplyVersion1Async(cancellationToken);
            await RecordVersionAsync(1, "Booking invoice snapshot hardening", cancellationToken);
        }

        if (!await IsVersionAppliedAsync(2, cancellationToken))
        {
            logger.LogInformation("Applying La Grandeur database schema upgrade 2.");
            await ApplyVersion2Async(cancellationToken);
            await RecordVersionAsync(2, "Booking concurrency and slot integrity", cancellationToken);
        }

        if (!await IsVersionAppliedAsync(3, cancellationToken))
        {
            logger.LogInformation("Applying La Grandeur database data upgrade 3.");
            await ApplyVersion3Async(cancellationToken);
            await RecordVersionAsync(3, "Rename spa administrator display name to Kea", cancellationToken);
        }

        await RepairAndValidateIntegrityObjectsAsync(cancellationToken);
        await ValidateSchemaAsync(cancellationToken);
    }

    private Task EnsureMigrationTableAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[AppSchemaMigrations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[AppSchemaMigrations]
                (
                    [Version] int NOT NULL CONSTRAINT [PK_AppSchemaMigrations] PRIMARY KEY,
                    [Name] nvarchar(200) NOT NULL,
                    [AppliedUtc] datetime2 NOT NULL
                );
            END
            """, cancellationToken);

    private async Task<bool> IsVersionAppliedAsync(int version, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(1) FROM [dbo].[AppSchemaMigrations] WHERE [Version] = @version";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@version";
            parameter.Value = version;
            command.Parameters.Add(parameter);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task ApplyVersion1Async(CancellationToken cancellationToken)
    {
        // SQL Server compiles an entire batch before it executes it. If a batch both
        // adds a column and then references that new column, an existing database can
        // fail with "Invalid column name" during compilation. Keep this migration in
        // one transaction, but execute the DDL/backfill/finalization as separate
        // commands so each command is compiled against the schema produced by the
        // previous command.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH('dbo.Bookings', 'InvoiceNumber') IS NULL
                    ALTER TABLE [dbo].[Bookings] ADD [InvoiceNumber] nvarchar(40) NULL;

                IF COL_LENGTH('dbo.Bookings', 'InvoiceServiceName') IS NULL
                    ALTER TABLE [dbo].[Bookings] ADD [InvoiceServiceName] nvarchar(180) NULL;

                IF COL_LENGTH('dbo.Bookings', 'InvoiceDurationMinutes') IS NULL
                    ALTER TABLE [dbo].[Bookings] ADD [InvoiceDurationMinutes] int NULL;

                IF COL_LENGTH('dbo.Bookings', 'InvoiceAmount') IS NULL
                    ALTER TABLE [dbo].[Bookings] ADD [InvoiceAmount] decimal(10,2) NULL;

                IF COL_LENGTH('dbo.Bookings', 'InvoiceIssuedUtc') IS NULL
                    ALTER TABLE [dbo].[Bookings] ADD [InvoiceIssuedUtc] datetime2 NULL;

                IF COL_LENGTH('dbo.Bookings', 'InvoiceIncludesHerbalTea') IS NULL
                    ALTER TABLE [dbo].[Bookings] ADD [InvoiceIncludesHerbalTea] bit NULL;
                """, cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE b
                SET
                    [InvoiceNumber] = CASE
                        WHEN NULLIF(LTRIM(RTRIM(b.[InvoiceNumber])), '') IS NULL
                            THEN CONCAT('INV-', REPLACE(b.[ReferenceNumber], 'LG-', ''))
                        ELSE b.[InvoiceNumber]
                    END,
                    [InvoiceServiceName] = CASE
                        WHEN NULLIF(LTRIM(RTRIM(b.[InvoiceServiceName])), '') IS NULL
                            THEN COALESCE(s.[Name], 'Treatment')
                        ELSE b.[InvoiceServiceName]
                    END,
                    [InvoiceDurationMinutes] = CASE
                        WHEN ISNULL(b.[InvoiceDurationMinutes], 0) <= 0
                            THEN COALESCE(s.[DurationMinutes], DATEDIFF(MINUTE, b.[StartLocal], b.[EndLocal]))
                        ELSE b.[InvoiceDurationMinutes]
                    END,
                    [InvoiceAmount] = CASE
                        WHEN b.[InvoiceAmount] IS NULL OR (b.[InvoiceAmount] = 0 AND ISNULL(s.[Price], 0) > 0)
                            THEN COALESCE(s.[Price], 0)
                        ELSE b.[InvoiceAmount]
                    END,
                    [InvoiceIssuedUtc] = COALESCE(b.[InvoiceIssuedUtc], b.[CreatedUtc], SYSUTCDATETIME()),
                    [InvoiceIncludesHerbalTea] = COALESCE(b.[InvoiceIncludesHerbalTea], s.[IncludesHerbalTea], 0)
                FROM [dbo].[Bookings] b
                LEFT JOIN [dbo].[SpaServices] s ON s.[Id] = b.[SpaServiceId];
                """, cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [dbo].[Bookings] ALTER COLUMN [InvoiceNumber] nvarchar(40) NOT NULL;
                ALTER TABLE [dbo].[Bookings] ALTER COLUMN [InvoiceServiceName] nvarchar(180) NOT NULL;
                ALTER TABLE [dbo].[Bookings] ALTER COLUMN [InvoiceDurationMinutes] int NOT NULL;
                ALTER TABLE [dbo].[Bookings] ALTER COLUMN [InvoiceAmount] decimal(10,2) NOT NULL;
                ALTER TABLE [dbo].[Bookings] ALTER COLUMN [InvoiceIssuedUtc] datetime2 NOT NULL;
                ALTER TABLE [dbo].[Bookings] ALTER COLUMN [InvoiceIncludesHerbalTea] bit NOT NULL;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = N'IX_Bookings_InvoiceNumber'
                      AND [object_id] = OBJECT_ID(N'[dbo].[Bookings]'))
                BEGIN
                    CREATE UNIQUE INDEX [IX_Bookings_InvoiceNumber]
                        ON [dbo].[Bookings] ([InvoiceNumber]);
                END

                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE [name] = N'FK_Bookings_SpaServices_SpaServiceId'
                      AND [parent_object_id] = OBJECT_ID(N'[dbo].[Bookings]'))
                BEGIN
                    ALTER TABLE [dbo].[Bookings]
                        DROP CONSTRAINT [FK_Bookings_SpaServices_SpaServiceId];
                END

                ALTER TABLE [dbo].[Bookings] WITH CHECK
                    ADD CONSTRAINT [FK_Bookings_SpaServices_SpaServiceId]
                    FOREIGN KEY ([SpaServiceId]) REFERENCES [dbo].[SpaServices] ([Id]);
                """, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private Task ApplyVersion2Async(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            -- Terminal bookings must never continue holding reservable time.
            DELETE bs
            FROM [dbo].[BookingSlots] bs
            INNER JOIN [dbo].[Bookings] b ON b.[Id] = bs.[BookingId]
            WHERE b.[Status] IN ('Cancelled', 'Declined');

            IF EXISTS (
                SELECT [SlotStartLocal]
                FROM [dbo].[BookingSlots]
                GROUP BY [SlotStartLocal]
                HAVING COUNT(*) > 1)
            BEGIN
                THROW 51001, 'Duplicate BookingSlot times exist. Resolve the conflicting bookings before startup can continue.', 1;
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_BookingSlots_SlotStartLocal'
                  AND [object_id] = OBJECT_ID(N'[dbo].[BookingSlots]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_BookingSlots_SlotStartLocal]
                    ON [dbo].[BookingSlots] ([SlotStartLocal]);
            END

            IF COL_LENGTH('dbo.Bookings', 'RowVersion') IS NULL
                ALTER TABLE [dbo].[Bookings] ADD [RowVersion] rowversion NOT NULL;

            COMMIT TRANSACTION;
            """, cancellationToken);


    private Task ApplyVersion3Async(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE [dbo].[AppUsers]
            SET [FullName] = N'Kea'
            WHERE [Role] = N'Admin'
              AND LTRIM(RTRIM([FullName])) = N'Mavis';
            """, cancellationToken);


    private Task RepairAndValidateIntegrityObjectsAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_BookingSlots_SlotStartLocal'
                  AND [object_id] = OBJECT_ID(N'[dbo].[BookingSlots]')
                  AND [is_unique] = 1)
            BEGIN
                IF EXISTS (
                    SELECT [SlotStartLocal]
                    FROM [dbo].[BookingSlots]
                    GROUP BY [SlotStartLocal]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51001, 'Duplicate BookingSlot times exist. Resolve the conflicting bookings before startup can continue.', 1;
                END

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = N'IX_BookingSlots_SlotStartLocal'
                      AND [object_id] = OBJECT_ID(N'[dbo].[BookingSlots]'))
                    DROP INDEX [IX_BookingSlots_SlotStartLocal] ON [dbo].[BookingSlots];

                CREATE UNIQUE INDEX [IX_BookingSlots_SlotStartLocal]
                    ON [dbo].[BookingSlots] ([SlotStartLocal]);
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_Bookings_InvoiceNumber'
                  AND [object_id] = OBJECT_ID(N'[dbo].[Bookings]')
                  AND [is_unique] = 1)
            BEGIN
                IF EXISTS (
                    SELECT [InvoiceNumber]
                    FROM [dbo].[Bookings]
                    GROUP BY [InvoiceNumber]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51002, 'Duplicate invoice numbers exist. Resolve them before startup can continue.', 1;
                END

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = N'IX_Bookings_InvoiceNumber'
                      AND [object_id] = OBJECT_ID(N'[dbo].[Bookings]'))
                    DROP INDEX [IX_Bookings_InvoiceNumber] ON [dbo].[Bookings];

                CREATE UNIQUE INDEX [IX_Bookings_InvoiceNumber]
                    ON [dbo].[Bookings] ([InvoiceNumber]);
            END

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_Bookings_ReferenceNumber'
                  AND [object_id] = OBJECT_ID(N'[dbo].[Bookings]')
                  AND [is_unique] = 1)
            BEGIN
                IF EXISTS (
                    SELECT [ReferenceNumber]
                    FROM [dbo].[Bookings]
                    GROUP BY [ReferenceNumber]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51003, 'Duplicate booking reference numbers exist. Resolve them before startup can continue.', 1;
                END

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [name] = N'IX_Bookings_ReferenceNumber'
                      AND [object_id] = OBJECT_ID(N'[dbo].[Bookings]'))
                    DROP INDEX [IX_Bookings_ReferenceNumber] ON [dbo].[Bookings];

                CREATE UNIQUE INDEX [IX_Bookings_ReferenceNumber]
                    ON [dbo].[Bookings] ([ReferenceNumber]);
            END

            COMMIT TRANSACTION;
            """, cancellationToken);

    private Task RecordVersionAsync(int version, string name, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            IF NOT EXISTS (SELECT 1 FROM [dbo].[AppSchemaMigrations] WHERE [Version] = {version})
            BEGIN
                INSERT INTO [dbo].[AppSchemaMigrations] ([Version], [Name], [AppliedUtc])
                VALUES ({version}, {name}, {DateTime.UtcNow});
            END
            """, cancellationToken);

    private async Task ValidateSchemaAsync(CancellationToken cancellationToken)
    {
        var missing = await GetMissingBookingColumnsAsync(cancellationToken);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The La Grandeur Spa database schema is incomplete. Missing Booking columns: " +
                string.Join(", ", missing));
        }
    }

    private async Task<List<string>> GetMissingBookingColumnsAsync(CancellationToken cancellationToken)
    {
        string[] required =
        [
            "ReferenceNumber", "CustomerId", "SpaServiceId", "StartLocal", "EndLocal", "Status",
            "InvoiceNumber", "InvoiceServiceName", "InvoiceDurationMinutes", "InvoiceAmount",
            "InvoiceIncludesHerbalTea", "InvoiceIssuedUtc", "RowVersion"
        ];

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT [COLUMN_NAME] FROM [INFORMATION_SCHEMA].[COLUMNS] WHERE [TABLE_SCHEMA] = 'dbo' AND [TABLE_NAME] = 'Bookings'";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                existing.Add(reader.GetString(0));

            return required.Where(x => !existing.Contains(x)).ToList();
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
