# Logic Apps Implementation with Inbox/Outbox Pattern

## Overview

This implementation migrates the Azure Functions-based message processing to **Azure Logic Apps (Standard)** with inline code actions, while maintaining the same architectural patterns:

- ✅ **Inbox Pattern** - Idempotency and deduplication
- ✅ **Outbox Pattern** - Guaranteed delivery with eventual consistency  
- ✅ **Liquid Transformations** - Schema mapping using built-in Logic Apps Liquid connector
- ✅ **Retry Logic** - Exponential backoff with configurable max retries
- ✅ **Message ID Computation** - SHA256 hashing for idempotency
- ✅ **Eventual Consistency** - Asynchronous processing with separate outbox processor

## Architecture

### Components

1. **process-message** workflow - Main message ingestion and processing
2. **outbox-processor** workflow - Reliable delivery with retry logic
3. **SQL Database** - Inbox and Outbox tables for persistence
4. **Liquid Maps** - Transformation templates in `Artifacts/Maps/`
5. **Service Bus** - Canonical event publishing
6. **External APIs** - Manhattan (Google Pub/Sub) integration with JWT auth

### Flow Diagram

```
Incoming Message
      ↓
[Compute Message ID (SHA256)]
      ↓
[Query Inbox for Duplicate]
      ↓
   Duplicate? → YES → [Return 200 Duplicate]
      ↓ NO
[Insert into Inbox]
      ↓
[Determine Template Name]
      ↓
[Transform with Liquid]
      ↓
[Parse Canonical Event]
      ↓
[Insert into Outbox]
      ↓
[Try: Publish to Service Bus]
   ↓            ↓
SUCCESS      FAILURE
   ↓            ↓
[Mark Sent] [Log Error]
   ↓            ↓
[Return 200 Processed]
      ↓
  [Outbox Processor]
  (Runs every minute)
      ↓
[Retry Failed Messages]
```

## Workflows

### 1. process-message Workflow

**Trigger**: Service Bus Queue (`inbound-messages`)
- Automatically triggered when messages arrive from D365 FinOps
- Reads message body and custom properties for routing

**Actions**:
1. **Compute_Message_ID** (JavaScript Inline Code)
   - Computes SHA256 hash of raw payload for idempotency
   - Returns messageId and rawPayload

2. **Query_Inbox** (SQL Connector)
   - Checks for duplicate messages by MessageId
   - Implements deduplication

3. **Is_Duplicate** (Condition)
   - If duplicate: Logs and completes processing
   - If new: Proceeds to processing

4. **Insert_Into_Inbox** (SQL Connector)
   - Stores raw message for audit trail
   - Captures MessageId, SourceTopic, CorrelationId, RawPayload, ReceivedAt

5. **Determine_Template_Name** (Compose)
   - Reads `x-source-topic` from message properties or defaults to "customerToCanonical"

6. **Transform_With_Liquid** (Built-in Liquid Action)
   - Applies Liquid template from `Artifacts/Maps/`
   - Maps source format to canonical StandardEvent schema

7. **Parse_Canonical_Event** (Parse JSON)
   - Parses transformed JSON
   - Validates against StandardEvent schema

8. **Insert_Into_Outbox** (SQL Connector)
   - Stores message for guaranteed delivery
   - Sets Sent = false initially

9. **Try_Publish_To_Service_Bus** (Scope)
   - Attempts immediate publish to Service Bus topic
   - On success: Marks outbox record as Sent
   - On failure: Logs error (outbox processor will retry)

10. **Return_Success_Response**
   - Returns 200 with status "Processed" and messageId

### 2. outbox-processor Workflow

**Trigger**: Recurrence (Every 1 minute)

**Actions**:
1. **Get_Pending_Outbox_Messages** (SQL Stored Procedure)
   - Calls `sp_GetPendingOutboxMessages`
   - Returns up to 10 pending messages
   - Only messages ready for retry (based on NextRetryAt)

2. **For_Each_Pending_Message** (Loop with concurrency = 5)
   
   **Per Message**:
   - **Parse_Outbox_Record** - Extract message details
   - **Route_By_Destination** (Switch)
     
     **Case: ServiceBusCanonical**
     - Send to Service Bus canonical topic
     - Mark as Sent on success
     - Update retry count and error on failure
     
     **Case: ManhattanPublisher**
     - Get JWT token from stub
     - Post to Google Pub/Sub
     - Mark as Sent on success
     - Update retry count and error on failure

## Database Schema

### Inbox Table

Stores all received messages for idempotency and audit trail.

```sql
CREATE TABLE [dbo].[Inbox] (
    [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
    [MessageId] NVARCHAR(100) NOT NULL UNIQUE,
    [SourceTopic] NVARCHAR(100) NULL,
    [CorrelationId] NVARCHAR(100) NULL,
    [RawPayload] NVARCHAR(MAX) NOT NULL,
    [ReceivedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
```

**Indexes**:
- `IX_Inbox_MessageId` - Fast duplicate lookups
- `IX_Inbox_ReceivedAt` - Time-based queries

### Outbox Table

Stores messages for guaranteed delivery with retry logic.

```sql
CREATE TABLE [dbo].[Outbox] (
    [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
    [MessageId] NVARCHAR(100) NOT NULL,
    [Destination] NVARCHAR(100) NOT NULL,
    [Payload] NVARCHAR(MAX) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [Sent] BIT NOT NULL DEFAULT 0,
    [SentAt] DATETIME2 NULL,
    [Error] NVARCHAR(MAX) NULL,
    [RetryCount] INT NOT NULL DEFAULT 0,
    [NextRetryAt] DATETIME2 NULL
);
```

**Indexes**:
- `IX_Outbox_MessageId` - Message tracking
- `IX_Outbox_Sent_CreatedAt` - Pending message queries
- `IX_Outbox_NextRetryAt` - Retry scheduling

### Stored Procedures

**sp_GetPendingOutboxMessages**
- Returns messages that need to be sent
- Filters: Sent = 0, RetryCount < 5, NextRetryAt <= NOW

**sp_UpdateOutboxRetry**
- Increments retry count
- Calculates next retry time with exponential backoff:
  - Retry 1: 1 minute
  - Retry 2: 5 minutes
  - Retry 3: 15 minutes
  - Retry 4: 1 hour
  - Retry 5: 4 hours

**View: vw_OutboxStatus**
- Monitoring view for message status
- Shows delivery state and processing time

## Liquid Templates

Located in `/LogicApp/sktestlogicapp/Artifacts/Maps/`

### Available Templates:

1. **customerToCanonical.liquid**
   - Maps customer data to StandardEvent
   - Sets eventType: "CustomerChanged"

2. **vendorToCanonical.liquid**
   - Maps vendor data to StandardEvent
   - Sets eventType: "VendorChanged"

3. **customerAddressToCanonical.liquid**
   - Maps customer address changes
   - Sets eventType: "BusinessAddressChanged"

4. **vendorAddressToCanonical.liquid**
   - Maps vendor address changes
   - Sets eventType: "BusinessAddressChanged"

### StandardEvent Schema:

```json
{
  "eventType": "string",
  "subject": "string",
  "source": "string",
  "eventTime": "ISO 8601 datetime",
  "schemaVersion": "string",
  "contentType": "string",
  "correlationId": "string",
  "data": { object }
}
```

## Setup Instructions

### Quick Start: Local Development with Hybrid Mode

For local development, we recommend using **Hybrid Mode**, which runs Logic Apps locally while connecting to cloud Azure Service Bus through a local proxy.

📖 **For detailed local development instructions, see [LOCAL-DEVELOPMENT.md](LOCAL-DEVELOPMENT.md)**

**Quick Setup:**
1. Start **Azurite** (local storage emulator): `azurite`
2. Start **ServiceBusProxy** (port 7075): `cd src\ServiceBusProxy && func host start --port 7075`
3. Start **Logic App** (port 7071): `cd LogicApp\sktestlogicapp && func host start --port 7071`

**Requirements:**
- ✅ Azurite installed (`npm install -g azurite`)
- ✅ Azure Functions Core Tools
- ✅ Azure Service Bus namespace (cloud)
- ✅ ServiceBusProxy function (`src/ServiceBusProxy/`)

**Hybrid Mode Benefits:**
- 🎯 Local testing with real Azure Service Bus
- 🐛 Full debugging in VS Code
- 📊 Monitor via Azure Portal
- 🚀 Same configuration as production (just different URLs)

---

### 1. Database Setup

Run the SQL script to create tables and stored procedures:

```bash
sqlcmd -S (localdb)\\mssqllocaldb -d ProcessingDb -i database/schema/InboxOutbox.sql
```

Or execute directly in SQL Server Management Studio:
```sql
-- Run: database/schema/InboxOutbox.sql
```

### 2. Configure Connections

Update `local.settings.json` with your connection strings:

```json
{
  "Values": {
    "SQL_CONNECTION_STRING": "Server=(localdb)\\mssqllocaldb;Database=ProcessingDb;Trusted_Connection=True;",
    "SERVICEBUS_CONNECTION_STRING": "Endpoint=sb://YOUR_NAMESPACE.servicebus.windows.net/;..."
  }
}
```

### 3. Create API Connections (Azure Portal)

For deployed Logic Apps, create managed connections:

**SQL Connection:**
```bash
az resource create \\
  --resource-group YOUR_RG \\
  --resource-type Microsoft.Web/connections \\
  --name sql \\
  --properties '{
    "api": {
      "id": "/subscriptions/SUB_ID/providers/Microsoft.Web/locations/eastus/managedApis/sql"
    },
    "parameterValues": {
      "server": "YOUR_SERVER.database.windows.net",
      "database": "ProcessingDb",
      "authType": "managedIdentity"
    }
  }'
```

**Service Bus Connection:**
```bash
az resource create \\
  --resource-group YOUR_RG \\
  --resource-type Microsoft.Web/connections \\
  --name servicebus \\
  --properties '{
    "api": {
      "id": "/subscriptions/SUB_ID/providers/Microsoft.Web/locations/eastus/managedApis/servicebus"
    },
    "parameterValues": {
      "connectionString": "YOUR_CONNECTION_STRING"
    }
  }'
```

### 4. Start Logic App Locally

```bash
cd LogicApp/sktestlogicapp
func start
```

The Logic App will be available at:
- **process-message**: `http://localhost:7071/api/process-message` (POST)
- **outbox-processor**: Runs automatically every minute

### 5. Test the Workflow

```bash
curl -X POST http://localhost:7071/api/process-message \\
  -H "Content-Type: application/json" \\
  -H "x-source-topic: customerToCanonical" \\
  -d '{
    "correlationId": "test-123",
    "customerId": "C001",
    "name": "Acme Corp"
  }'
```

Expected response:
```json
{
  "status": "Processed",
  "messageId": "abc123...",
  "eventType": "CustomerChanged"
}
```

## Key Differences from Azure Functions

| Aspect | Azure Functions | Logic Apps |
|--------|----------------|------------|
| **Code Execution** | C# compiled code | Inline JavaScript/visual workflow |
| **Transaction Guarantees** | ACID with EF Core transactions | Eventual consistency (async) |
| **Liquid Transformation** | DotLiquid library | Built-in Liquid connector |
| **Retry Logic** | Manual implementation | Built-in with exponential backoff |
| **Monitoring** | Application Insights | Logic Apps run history + App Insights |
| **State Management** | Stateless (DB-backed) | Stateful workflows available |
| **Hosting** | Consumption/Premium/Dedicated | Standard (App Service Plan) |

## Eventual Consistency Model

Unlike the Functions implementation which used EF Core transactions for ACID guarantees, this Logic Apps implementation uses **eventual consistency**:

1. **Inbox write** happens first (may succeed even if outbox fails)
2. **Outbox write** happens second (captured for delivery)
3. **First publish attempt** happens immediately after outbox write
4. **Retry mechanism** handles failures via outbox-processor workflow

### Trade-offs:

✅ **Pros:**
- Simpler implementation with visual workflows
- Built-in retry and error handling
- No need for EF Core or complex transaction management
- Better observability with Logic Apps run history

⚠️ **Cons:**
- No ACID guarantees across Inbox + Outbox writes
- Small risk of inbox write succeeding but outbox failing (very rare)
- Messages might be processed twice in extreme failure scenarios (should be idempotent anyway)

## Monitoring

### View Outbox Status

```sql
SELECT * FROM vw_OutboxStatus
WHERE Sent = 0
ORDER BY CreatedAt DESC;
```

### Check Failed Messages

```sql
SELECT Id, MessageId, Destination, RetryCount, Error
FROM Outbox
WHERE RetryCount >= 5 AND Sent = 0;
```

### Monitor Logic Apps

- **Azure Portal** → Logic App → Run History
- **Application Insights** → Live Metrics
- **Log Analytics** → Custom Queries

## Troubleshooting

### Message Not Processing

1. Check Inbox table for duplicate MessageId
2. Verify Liquid template exists in Artifacts/Maps/
3. Check Logic App run history for errors
4. Validate SQL connection in connections.json

### Outbox Messages Stuck

1. Check `vw_OutboxStatus` for retry counts
2. Verify Service Bus connection is valid
3. Check outbox-processor workflow is enabled
4. Review error messages in Outbox table

### Transformation Errors

1. Validate JSON payload structure
2. Test Liquid template in Logic Apps designer
3. Check template file name matches x-source-topic header
4. Verify StandardEvent schema compliance

## Migration Checklist

- [x] Database schema created (Inbox, Outbox tables)
- [x] Stored procedures deployed
- [x] Liquid templates copied to Artifacts/Maps/
- [x] process-message workflow created
- [x] outbox-processor workflow created
- [x] connections.json configured
- [x] local.settings.json updated
- [ ] Test message processing locally
- [ ] Deploy to Azure
- [ ] Configure managed identity for SQL
- [ ] Set up Application Insights
- [ ] Configure alerts and monitoring
- [ ] Update Function app clients to call Logic App endpoint

## Next Steps

1. **Test Locally**: Run both workflows and send test messages
2. **Deploy to Azure**: Use VS Code Azure Logic Apps extension or Azure CLI
3. **Configure Managed Identity**: Enable MI for Logic App and grant SQL permissions
4. **Set Up Monitoring**: Configure Application Insights and alerts
5. **Update Integrations**: Point upstream systems to new Logic App endpoint
6. **Decommission Functions**: Once stable, retire the Azure Functions implementation

## Support

For issues or questions:
- Check Logic Apps run history for detailed execution logs
- Query `vw_OutboxStatus` for message delivery status
- Review Application Insights for performance metrics
- See troubleshooting section above

---

**Architecture Pattern**: Inbox/Outbox with Eventual Consistency  
**Technology**: Azure Logic Apps (Standard), SQL Server, Service Bus  
**Transformation**: Liquid Templates  
**Delivery Guarantee**: At-least-once with retry
