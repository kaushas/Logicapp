# Logic Apps Quick Reference

## Local Development

### Start Logic App
```bash
cd LogicApp/sktestlogicapp
func start --port 7071
```

### Test Message Processing
```bash
# Customer message
curl -X POST http://localhost:7071/api/process-message \
  -H "Content-Type: application/json" \
  -H "x-source-topic: customerToCanonical" \
  -d '{"correlationId": "test-001", "customerId": "C001", "name": "Acme Corp"}'

# Vendor message
curl -X POST http://localhost:7071/api/process-message \
  -H "Content-Type: application/json" \
  -H "x-source-topic: vendorToCanonical" \
  -d '{"correlationId": "test-002", "vendorId": "V001", "name": "Vendor Inc"}'

# Test duplicate (send same message twice)
curl -X POST http://localhost:7071/api/process-message \
  -H "Content-Type: application/json" \
  -d '{"correlationId": "dup-test", "customerId": "C999"}'
```

## Database Queries

### Check Recent Inbox Messages
```sql
SELECT TOP 10 
    MessageId, 
    SourceTopic, 
    ReceivedAt,
    LEFT(RawPayload, 100) AS PayloadPreview
FROM Inbox 
ORDER BY ReceivedAt DESC;
```

### Check Outbox Status
```sql
SELECT * FROM vw_OutboxStatus
WHERE CreatedAt >= DATEADD(HOUR, -1, GETUTCDATE())
ORDER BY CreatedAt DESC;
```

### Find Failed Messages
```sql
SELECT 
    Id,
    MessageId,
    Destination,
    RetryCount,
    NextRetryAt,
    Error
FROM Outbox
WHERE Sent = 0 AND RetryCount > 0
ORDER BY RetryCount DESC, CreatedAt DESC;
```

### Manually Retry a Failed Message
```sql
-- Reset retry count and schedule immediate retry
UPDATE Outbox
SET NextRetryAt = GETUTCDATE(), Error = NULL
WHERE MessageId = 'your-message-id-here';
```

### Mark Message as Sent Manually
```sql
UPDATE Outbox
SET Sent = 1, SentAt = GETUTCDATE()
WHERE MessageId = 'your-message-id-here';
```

## Workflow Endpoints

### Process Message (HTTP Trigger)
- **URL**: `POST http://localhost:7071/api/process-message`
- **Headers**: 
  - `Content-Type: application/json`
  - `x-source-topic: templateName` (optional, defaults to customerToCanonical)
- **Body**: JSON payload to transform

### Outbox Processor (Recurrence)
- **Trigger**: Automatic every 1 minute
- **Manually trigger**: Navigate to workflow in Azure Portal → Run Trigger

## Template Names

Map `x-source-topic` header to Liquid template:

| Header Value | Template File |
|-------------|---------------|
| `customerToCanonical` | customerToCanonical.liquid |
| `vendorToCanonical` | vendorToCanonical.liquid |
| `customerAddressToCanonical` | customerAddressToCanonical.liquid |
| `vendorAddressToCanonical` | vendorAddressToCanonical.liquid |

## Environment Variables

### Required (local.settings.json)
```json
{
  "SQL_CONNECTION_STRING": "Server=...;Database=ProcessingDb;...",
  "SERVICEBUS_CONNECTION_STRING": "Endpoint=sb://...;...",
  "JWT_STUB_URL": "http://localhost:7073/api/generate-token",
  "MANHATTAN_PUBLISH_URL": "https://pubsub.googleapis.com/.../publish"
}
```

## Troubleshooting

### "Duplicate" Response
✅ **Expected behavior** - Message was already processed. Check Inbox table for original message.

### Message in Outbox but not sent
1. Check `vw_OutboxStatus` for retry information
2. Verify outbox-processor workflow is running
3. Check Service Bus connection string
4. Review error column in Outbox table

### Liquid transformation error
1. Verify template file exists in `Artifacts/Maps/`
2. Check template name matches header value + `.liquid`
3. Validate JSON payload structure
4. Test template in Logic Apps designer

### Database connection error
1. Verify SQL Server is running: `sqlcmd -S (localdb)\mssqllocaldb -Q "SELECT @@VERSION"`
2. Check database exists: `sqlcmd -S (localdb)\mssqllocaldb -Q "SELECT name FROM sys.databases"`
3. Run schema script if tables missing: `sqlcmd -S (localdb)\mssqllocaldb -d ProcessingDb -i database/schema/InboxOutbox.sql`

## Monitoring Queries

### Messages processed in last hour
```sql
SELECT 
    COUNT(*) AS TotalMessages,
    COUNT(DISTINCT SourceTopic) AS UniqueTopics
FROM Inbox
WHERE ReceivedAt >= DATEADD(HOUR, -1, GETUTCDATE());
```

### Delivery success rate
```sql
SELECT 
    COUNT(*) AS TotalOutbox,
    SUM(CASE WHEN Sent = 1 THEN 1 ELSE 0 END) AS Delivered,
    SUM(CASE WHEN Sent = 0 THEN 1 ELSE 0 END) AS Pending,
    SUM(CASE WHEN RetryCount >= 5 THEN 1 ELSE 0 END) AS Failed,
    CAST(SUM(CASE WHEN Sent = 1 THEN 1.0 ELSE 0 END) * 100 / COUNT(*) AS DECIMAL(5,2)) AS SuccessRate
FROM Outbox
WHERE CreatedAt >= DATEADD(DAY, -1, GETUTCDATE());
```

### Average processing time
```sql
SELECT 
    AVG(ProcessingTimeSeconds) AS AvgProcessingTimeSec,
    MIN(ProcessingTimeSeconds) AS MinProcessingTimeSec,
    MAX(ProcessingTimeSeconds) AS MaxProcessingTimeSec
FROM vw_OutboxStatus
WHERE Sent = 1 AND CreatedAt >= DATEADD(HOUR, -1, GETUTCDATE());
```

## Deployment

### Deploy to Azure (VS Code)
1. Open Logic App folder in VS Code
2. Right-click workflow folder → Deploy to Logic App
3. Select Azure subscription and Logic App resource
4. Workflows deploy automatically

### Deploy via Azure CLI
```bash
# Create resource group
az group create --name logic-app-rg --location eastus

# Create Logic App
az logicapp create \
  --resource-group logic-app-rg \
  --name my-logic-app \
  --storage-account mystorageacct \
  --plan my-app-service-plan

# Deploy workflows
func azure functionapp publish my-logic-app
```

### Configure Managed Identity
```bash
# Enable system-assigned managed identity
az logicapp identity assign --name my-logic-app --resource-group logic-app-rg

# Grant SQL permissions
az sql server ad-admin create \
  --resource-group logic-app-rg \
  --server-name my-sql-server \
  --display-name my-logic-app \
  --object-id $IDENTITY_PRINCIPAL_ID
```

## Performance Tuning

### Outbox Processor Concurrency
Edit `outbox-processor/workflow.json`:
```json
"runtimeConfiguration": {
  "concurrency": {
    "repetitions": 10  // Process up to 10 messages in parallel
  }
}
```

### Batch Size
Edit `sp_GetPendingOutboxMessages` call:
```json
"body": {
  "BatchSize": 20  // Retrieve up to 20 pending messages
}
```

### Recurrence Frequency
Edit outbox-processor trigger:
```json
"recurrence": {
  "frequency": "Minute",
  "interval": 5  // Run every 5 minutes instead of 1
}
```

## Common Patterns

### Test Idempotency
```bash
# Send same message 3 times
for i in {1..3}; do
  curl -X POST http://localhost:7071/api/process-message \
    -H "Content-Type: application/json" \
    -d '{"correlationId": "idem-test", "data": "test"}'
done

# Check Inbox - should only have 1 record
SELECT COUNT(*) FROM Inbox WHERE CorrelationId = 'idem-test';
```

### Simulate Failure for Retry Testing
```sql
-- Manually create outbox entry that will fail
INSERT INTO Outbox (MessageId, Destination, Payload, Sent)
VALUES ('test-retry', 'InvalidDestination', '{"test": true}', 0);

-- Watch it get retried by outbox-processor
SELECT * FROM vw_OutboxStatus WHERE MessageId = 'test-retry';
```

### Clear Test Data
```sql
-- Clear all messages older than 1 day
DELETE FROM Outbox WHERE CreatedAt < DATEADD(DAY, -1, GETUTCDATE());
DELETE FROM Inbox WHERE ReceivedAt < DATEADD(DAY, -1, GETUTCDATE());
```

---
**Last Updated**: Migration from Azure Functions to Logic Apps  
**Pattern**: Inbox/Outbox with Eventual Consistency  
**Version**: 1.0
