-- Database schema for Inbox/Outbox Pattern
-- This script creates tables for idempotency and reliable delivery
-- with eventual consistency guarantees

-- Create Inbox table for deduplication and audit trail
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Inbox]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Inbox] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [MessageId] NVARCHAR(100) NOT NULL UNIQUE,
        [SourceTopic] NVARCHAR(100) NULL,
        [CorrelationId] NVARCHAR(100) NULL,
        [RawPayload] NVARCHAR(MAX) NOT NULL,
        [ReceivedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        
        INDEX IX_Inbox_MessageId NONCLUSTERED ([MessageId]),
        INDEX IX_Inbox_ReceivedAt NONCLUSTERED ([ReceivedAt])
    );
    
    PRINT 'Inbox table created successfully';
END
GO

-- Create Outbox table for guaranteed delivery pattern
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Outbox]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Outbox] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [MessageId] NVARCHAR(100) NOT NULL,
        [Destination] NVARCHAR(100) NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [Sent] BIT NOT NULL DEFAULT 0,
        [SentAt] DATETIME2 NULL,
        [Error] NVARCHAR(MAX) NULL,
        [RetryCount] INT NOT NULL DEFAULT 0,
        [NextRetryAt] DATETIME2 NULL,
        
        INDEX IX_Outbox_MessageId NONCLUSTERED ([MessageId]),
        INDEX IX_Outbox_Sent_CreatedAt NONCLUSTERED ([Sent], [CreatedAt]),
        INDEX IX_Outbox_NextRetryAt NONCLUSTERED ([NextRetryAt]) WHERE [Sent] = 0
    );
    
    PRINT 'Outbox table created successfully';
END
GO

-- Create stored procedure for querying pending outbox messages
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[sp_GetPendingOutboxMessages]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[sp_GetPendingOutboxMessages]
GO

CREATE PROCEDURE [dbo].[sp_GetPendingOutboxMessages]
    @BatchSize INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT TOP (@BatchSize)
        [Id],
        [MessageId],
        [Destination],
        [Payload],
        [CreatedAt],
        [RetryCount],
        [Error]
    FROM [dbo].[Outbox]
    WHERE [Sent] = 0 
        AND ([NextRetryAt] IS NULL OR [NextRetryAt] <= GETUTCDATE())
        AND [RetryCount] < 5  -- Max 5 retries
    ORDER BY [CreatedAt] ASC;
END
GO

PRINT 'Stored procedure sp_GetPendingOutboxMessages created successfully';
GO

-- Create stored procedure for updating outbox retry information
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[sp_UpdateOutboxRetry]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[sp_UpdateOutboxRetry]
GO

CREATE PROCEDURE [dbo].[sp_UpdateOutboxRetry]
    @Id BIGINT,
    @Error NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @RetryCount INT;
    
    SELECT @RetryCount = [RetryCount] FROM [dbo].[Outbox] WHERE [Id] = @Id;
    
    -- Exponential backoff: 1min, 5min, 15min, 1hr, 4hr
    DECLARE @NextRetryMinutes INT;
    SET @NextRetryMinutes = CASE 
        WHEN @RetryCount = 0 THEN 1
        WHEN @RetryCount = 1 THEN 5
        WHEN @RetryCount = 2 THEN 15
        WHEN @RetryCount = 3 THEN 60
        ELSE 240
    END;
    
    UPDATE [dbo].[Outbox]
    SET 
        [RetryCount] = [RetryCount] + 1,
        [Error] = COALESCE(@Error, [Error]),
        [NextRetryAt] = DATEADD(MINUTE, @NextRetryMinutes, GETUTCDATE())
    WHERE [Id] = @Id;
END
GO

PRINT 'Stored procedure sp_UpdateOutboxRetry created successfully';
GO

-- Optional: Create view for monitoring
IF EXISTS (SELECT * FROM sys.views WHERE object_id = OBJECT_ID(N'[dbo].[vw_OutboxStatus]'))
    DROP VIEW [dbo].[vw_OutboxStatus]
GO

CREATE VIEW [dbo].[vw_OutboxStatus]
AS
SELECT 
    [Id],
    [MessageId],
    [Destination],
    [CreatedAt],
    [Sent],
    [SentAt],
    [RetryCount],
    [NextRetryAt],
    CASE 
        WHEN [Sent] = 1 THEN 'Delivered'
        WHEN [RetryCount] >= 5 THEN 'Failed - Max Retries'
        WHEN [NextRetryAt] IS NOT NULL AND [NextRetryAt] > GETUTCDATE() THEN 'Waiting for Retry'
        ELSE 'Pending'
    END AS [Status],
    [Error],
    DATEDIFF(SECOND, [CreatedAt], COALESCE([SentAt], GETUTCDATE())) AS [ProcessingTimeSeconds]
FROM [dbo].[Outbox];
GO

PRINT 'View vw_OutboxStatus created successfully';
GO

PRINT 'Database schema setup complete!';
PRINT 'Tables: Inbox, Outbox';
PRINT 'Stored Procedures: sp_GetPendingOutboxMessages, sp_UpdateOutboxRetry';
PRINT 'Views: vw_OutboxStatus';
