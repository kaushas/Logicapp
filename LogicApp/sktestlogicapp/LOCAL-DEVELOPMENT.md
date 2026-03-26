# Local Development Guide

## Service Bus Configuration

### 1. Update local.settings.json

The Logic App now uses **Service Bus trigger** (not HTTP) to receive messages from D365 FinOps.

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "node",
    "SERVICEBUS_CONNECTION_STRING": "Endpoint=sb://YOUR-NAMESPACE.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YOUR_KEY_HERE"
  }
}
```

### 2. Configure Service Bus Connection

Update [connections.json](connections.json) for local development:

```json
{
  "managedApiConnections": {
    "servicebus": {
      "api": {
        "id": "/subscriptions/{subscription-id}/providers/Microsoft.Web/locations/{location}/managedApis/servicebus"
      },
      "connection": {
        "id": "/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.Web/connections/servicebus"
      },
      "connectionRuntimeUrl": "{your-connection-runtime-url}",
      "authentication": {
        "type": "Raw",
        "scheme": "Key",
        "parameter": "@appsetting('SERVICEBUS_CONNECTION_STRING')"
      }
    }
  }
}
```

### 3. Create Service Bus Queue

Create a queue named `inbound-messages` in your Service Bus namespace:

```bash
# Using Azure CLI
az servicebus queue create \
  --resource-group YOUR_RG \
  --namespace-name YOUR_NAMESPACE \
  --name inbound-messages
```

Or use Azure Portal:
1. Go to your Service Bus namespace
2. Click "Queues" → "+ Queue"
3. Name: `inbound-messages`
4. Max delivery count: 10
5. Lock duration: 5 minutes

## Running Locally

### Start the Logic App

```powershell
# Navigate to Logic App folder
cd LogicApp\sktestlogicapp

# Start with Azure Functions Core Tools
func host start --port 7071
```

Or use VS Code Task: **"Start Logic App (Port 7071)"**

### Send Test Message to Service Bus

#### Option 1: Using Azure CLI

```bash
# Send message with custom properties
az servicebus queue message send \
  --resource-group YOUR_RG \
  --namespace-name YOUR_NAMESPACE \
  --queue-name inbound-messages \
  --body '{"orderId":"12345","customerName":"John Doe"}' \
  --properties x-source-topic=customerToCanonical x-correlation-id=test-123
```

#### Option 2: Using Service Bus Explorer in Azure Portal

1. Go to Service Bus namespace → Queues → `inbound-messages`
2. Click "Service Bus Explorer"
3. Click "Send Messages"
4. Add message body:
   ```json
   {
     "orderId": "12345",
     "customerName": "John Doe",
     "email": "john@example.com"
   }
   ```
5. Add Custom Properties:
   - `x-source-topic`: `customerToCanonical`
   - `x-correlation-id`: `test-correlation-123`
6. Click "Send"

#### Option 3: Using PowerShell Script

```powershell
# Install Azure PowerShell module if needed
# Install-Module -Name Az.ServiceBus

# Send test message
$connectionString = "Endpoint=sb://YOUR-NAMESPACE.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YOUR_KEY"
$queueName = "inbound-messages"

# Message body
$messageBody = @{
    orderId = "12345"
    customerName = "John Doe"
    email = "john@example.com"
} | ConvertTo-Json

# Custom properties
$properties = @{
    "x-source-topic" = "customerToCanonical"
    "x-correlation-id" = "test-correlation-123"
}

# Send message (requires Azure.Messaging.ServiceBus)
Write-Host "Send message via Azure Portal Service Bus Explorer or Azure CLI"
Write-Host "Message: $messageBody"
Write-Host "Properties: $($properties | ConvertTo-Json)"
```

#### Option 4: Using .NET Console App

```csharp
using Azure.Messaging.ServiceBus;

var connectionString = "YOUR_CONNECTION_STRING";
var queueName = "inbound-messages";

await using var client = new ServiceBusClient(connectionString);
var sender = client.CreateSender(queueName);

var message = new ServiceBusMessage("""
{
    "orderId": "12345",
    "customerName": "John Doe",
    "email": "john@example.com"
}
""")
{
    ApplicationProperties =
    {
        ["x-source-topic"] = "customerToCanonical",
        ["x-correlation-id"] = "test-correlation-123"
    }
};

await sender.SendMessageAsync(message);
Console.WriteLine("Message sent!");
```

## Message Properties

The Logic App reads custom properties from Service Bus messages:

| Property | Required | Default | Description |
|----------|----------|---------|-------------|
| `x-source-topic` | No | `customerToCanonical` | Liquid template name for transformation |
| `x-correlation-id` | No | Auto-generated GUID | End-to-end correlation ID |

## Liquid Template Routing

Based on `x-source-topic` property, the workflow selects a Liquid template:

| Property Value | Template File |
|----------------|---------------|
| `customerToCanonical` | `Artifacts/Maps/customerToCanonical.liquid` |
| `vendorToCanonical` | `Artifacts/Maps/vendorToCanonical.liquid` |
| `customerAddressToCanonical` | `Artifacts/Maps/customerAddressToCanonical.liquid` |
| `vendorAddressToCanonical` | `Artifacts/Maps/vendorAddressToCanonical.liquid` |

## Monitoring

### View Run History

1. Open Azure Portal
2. Go to Logic App → Runs
3. Filter by status, trigger time
4. Click on a run to see detailed execution

### Local Debugging

When running locally with `func host start`, you'll see:
- Trigger polling logs
- Message received events
- Workflow execution steps
- SQL queries
- Service Bus publish events

### Check Database

```sql
-- View received messages
SELECT TOP 10 * FROM [dbo].[Inbox] ORDER BY ReceivedAt DESC;

-- View pending outbox messages
SELECT * FROM [dbo].[Outbox] WHERE Sent = 0;

-- View retry information
SELECT 
    MessageId, 
    Destination, 
    RetryCount, 
    LastAttemptAt, 
    NextRetryAt,
    ErrorMessage
FROM [dbo].[Outbox] 
WHERE Sent = 0 AND RetryCount > 0;
```

## Troubleshooting

### Logic App not triggering

1. **Check Service Bus connection**
   ```bash
   # Verify connection string
   az servicebus namespace show \
     --resource-group YOUR_RG \
     --name YOUR_NAMESPACE
   ```

2. **Verify queue exists**
   ```bash
   az servicebus queue show \
     --resource-group YOUR_RG \
     --namespace-name YOUR_NAMESPACE \
     --name inbound-messages
   ```

3. **Check local.settings.json**
   - Ensure `SERVICEBUS_CONNECTION_STRING` is set
   - Verify connection string format

4. **Check connections.json**
   - Ensure `servicebus` connection is configured
   - Verify authentication type

### Message not processing

1. **Check message format**
   - Ensure valid JSON in message body
   - Verify custom properties are set

2. **View Logic App logs**
   ```powershell
   # In terminal where func host start is running
   # Look for errors in console output
   ```

3. **Check SQL database**
   ```sql
   -- Check if message reached Inbox
   SELECT * FROM [dbo].[Inbox] WHERE MessageId = 'YOUR_MESSAGE_ID';
   ```

4. **Enable detailed logging**
   In [host.json](host.json):
   ```json
   {
     "version": "2.0",
     "logging": {
       "logLevel": {
         "default": "Debug"
       }
     }
   }
   ```

### Database connection issues

1. **Ensure SQL Server is running**
   ```powershell
   # For LocalDB
   sqllocaldb info mssqllocaldb
   sqllocaldb start mssqllocaldb
   ```

2. **Verify connection string**
   ```powershell
   # Test connection
   sqlcmd -S "(localdb)\mssqllocaldb" -d ProcessingDb -Q "SELECT @@VERSION"
   ```

3. **Run migrations**
   ```powershell
   cd src\LogicAppProcessor
   dotnet ef database update
   ```

## Architecture Benefits

✅ **Automatic Message Processing** - No more manual HTTP calls  
✅ **Built-in Retry** - Service Bus handles message retry if workflow fails  
✅ **Scaling** - Multiple workflow instances can process messages in parallel  
✅ **Dead Letter Queue** - Failed messages automatically move to DLQ  
✅ **Peek-Lock Pattern** - Messages locked during processing, released on failure  
✅ **Session Support** - Can enable sessions for ordered processing  

## Differences from HTTP Trigger

| Aspect | HTTP Trigger | Service Bus Trigger |
|--------|--------------|---------------------|
| **Invocation** | Manual HTTP POST | Automatic on message arrival |
| **Response** | HTTP 200/202/500 | Workflow success/failure |
| **Retry** | Caller responsibility | Service Bus handles retry |
| **Scaling** | Based on HTTP load | Based on queue depth |
| **Testing** | curl/Postman | Send message to queue |
| **Correlation** | HTTP headers | Message properties |
| **Local Dev** | Easier (no SB needed) | Requires Service Bus connection |

## Next Steps

- [ ] Configure Production Service Bus namespace
- [ ] Set up Azure Monitor alerts
- [ ] Configure autoscaling rules
- [ ] Enable Application Insights integration
- [ ] Set up CI/CD pipeline with service bus connection parameters
