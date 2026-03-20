# Azure Logic Apps Standard - Quick Deployment

This Logic App implements an event-driven processing system with Inbox/Outbox patterns for guaranteed delivery and idempotency.

## Architecture

- **4 Workflows**:
  - `process-message` - HTTP trigger for receiving events (Customer, Vendor, Address)
  - `outbox-processor` - Scheduled processor for retry/delivery with exponential backoff
  - `message-status` - GET endpoint for 202 Accepted pattern (status polling)
  - `error-handler` - Dead-letter queue processor for permanent failures

## Quick Start

### Prerequisites

1. **Azure CLI** - [Install](https://docs.microsoft.com/cli/azure/install-azure-cli)
2. **Azure Subscription** with permissions to create resources
3. **PowerShell 7+** (for deployment script)

### Option 1: Automated Deployment (Recommended)

```powershell
# Login to Azure
az login

# Run deployment script
.\deploy-to-azure.ps1 `
  -SubscriptionId "your-subscription-id" `
  -ResourceGroupName "rg-logicapps-prod" `
  -Location "eastus" `
  -Environment "prod" `
  -SqlAdminPassword (ConvertTo-SecureString "YourSecurePassword123!" -AsPlainText -Force) `
  -JwtStubUrl "https://your-jwt-service.azurewebsites.net/api/generate-token" `
  -ManhattanPublishUrl "https://your-publisher-url"
```

This script will:
- ✅ Create all Azure resources (Logic App, SQL, Service Bus, Storage, App Insights)
- ✅ Configure API connections
- ✅ Deploy database schema
- ✅ Update connections.json with actual resource IDs
- ✅ Deploy all 4 workflows
- ✅ Configure application settings

### Option 2: Manual Deployment

See [DEPLOYMENT.md](./DEPLOYMENT.md) for detailed step-by-step instructions.

### Option 3: Infrastructure as Code

```bash
# Deploy infrastructure only
az deployment group create \
  --resource-group rg-logicapps-prod \
  --template-file infrastructure.bicep \
  --parameters @infrastructure.parameters.json

# Deploy workflows using VS Code
# Right-click > Deploy to Logic App
```

## Post-Deployment

### 1. Authorize API Connections

Navigate to Azure Portal:
- Go to Resource Group > API Connections
- Click on `sql` connection > Edit API connection > Authorize
- Click on `servicebus` connection > Edit API connection > Authorize

### 2. Get Workflow URLs

```bash
LOGIC_APP_NAME="your-logic-app-name"
RESOURCE_GROUP="rg-logicapps-prod"

# Get process-message URL
az logicapp show \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query "defaultHostName" -o tsv
```

### 3. Test Workflows

```bash
# POST - Process Customer Event
curl -X POST "https://<logic-app-url>/api/process-message/triggers/manual/invoke?..." \
  -H "Content-Type: application/json" \
  -H "x-source-topic: customer" \
  -d '{
    "customerId": "CUST001",
    "name": "John Doe",
    "email": "john@example.com"
  }'

# GET - Check Status
curl "https://<logic-app-url>/api/message-status/triggers/manual/invoke?...&messageId=<message-id>"
```

### 4. Monitor

```bash
# View Application Insights logs
az monitor app-insights query \
  --app ai-logicapps-prod \
  --resource-group rg-logicapps-prod \
  --analytics-query "traces | where message contains 'Correlation' | take 20"
```

## Configuration

### Application Settings

Set via Azure Portal or Azure CLI:

```bash
az logicapp config appsettings set \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    JWT_STUB_URL="https://your-jwt-service.azurewebsites.net/api/generate-token" \
    MANHATTAN_PUBLISH_URL="https://your-publisher-url"
```

### Parameters

Edit [parameters.json](./parameters.json) before deployment:

```json
{
  "JWT_STUB_URL": { "type": "String", "value": "https://..." },
  "MANHATTAN_PUBLISH_URL": { "type": "String", "value": "https://..." }
}
```

## Workflows

### process-message (HTTP POST)

**Purpose**: Receive events and store in Inbox (idempotency) and Outbox (guaranteed delivery)

**Supported Topics** (via `x-source-topic` header):
- `customer` - Customer events
- `vendor` - Vendor events
- `customerAddress` - Customer address events
- `vendorAddress` - Vendor address events

**Response**: 202 Accepted with `messageId` and `statusUrl`

### outbox-processor (Recurrence: 1 minute)

**Purpose**: Process pending Outbox messages with exponential backoff

**Retry Schedule**:
- Attempt 1: Immediate
- Attempt 2: +1 min
- Attempt 3: +5 min
- Attempt 4: +15 min
- Attempt 5: +1 hour
- Attempt 6+: +4 hours (until moved to dead-letter)

### message-status (HTTP GET)

**Purpose**: Query event processing status

**Query Param**: `messageId`

**Responses**:
- `NotFound` - Message not in system
- `Received` - In Inbox, pending transform
- `Processing` - In Outbox, delivery in progress
- `Sent` - Successfully delivered
- `Unknown` - Status cannot be determined

### error-handler (Recurrence: 5 minutes)

**Purpose**: Handle permanent failures (5+ retries)

**Actions**:
1. Query Outbox for messages with `RetryCount >= 5`
2. Move to Service Bus dead-letter queue: `dead-letter-events`
3. Send alert to operations: Manhattan Publisher

## Best Practices Implemented

✅ **1. Explicit Retry Policies** - All actions have defined retry behavior  
✅ **2. Correlation ID Propagation** - End-to-end tracking via `x-correlation-id`  
✅ **3. Application Insights Logging** - Structured logging with Compose actions  
✅ **4. Idempotency** - SHA256 MessageId prevents duplicate processing  
✅ **5. 202 Accepted Pattern** - Async processing with status endpoint  
✅ **6. Compensating Transactions** - Dead-letter queue for permanent failures  
✅ **7. Terminal Error Detection** - 4xx errors stop retries immediately  
✅ **8. Stateless Design** - All state in SQL (Inbox/Outbox tables)  
✅ **9. Eventual Consistency** - Inbox/Outbox pattern guarantees delivery  

## Troubleshooting

### Workflows Not Loading

Check API connections are authorized:
```bash
az resource show --ids "/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/connections/sql"
```

### SQL Connection Errors

1. Verify firewall rules allow Azure services
2. Check Managed Identity has permissions
3. Validate connection string in App Settings

### Service Bus Send Failures

1. Verify topics exist: `canonical-events`, `dead-letter-events`
2. Check Managed Identity has "Azure Service Bus Data Sender" role
3. Validate connection string

## Monitoring Queries (Application Insights)

```kusto
// Correlation ID tracking
traces
| where message contains "Correlation_ID"
| project timestamp, message, operation_Name
| order by timestamp desc

// Error tracking
traces
| where severityLevel >= 3
| project timestamp, message, severityLevel
| order by timestamp desc

// Performance
requests
| summarize avg(duration), percentile(duration, 95) by name
```

## Cost Estimation

- **Logic App (WS1)**: ~$75/month (includes 2 vCPU, 8GB RAM, 250GB storage)
- **SQL Database (S0)**: ~$15/month
- **Service Bus (Standard)**: ~$10/month
- **Storage Account**: ~$2/month
- **Application Insights**: ~$5/month (first 5GB free)

**Total**: ~$107/month (excludes data transfer and excessive executions)

## Support

- **Documentation**: [DEPLOYMENT.md](./DEPLOYMENT.md)
- **Best Practices**: [BEST_PRACTICES_IMPLEMENTATION.md](./BEST_PRACTICES_IMPLEMENTATION.md)
- **Database Schema**: [../../database/schema/InboxOutbox.sql](../../database/schema/InboxOutbox.sql)

## License

Proprietary - Internal Use Only
