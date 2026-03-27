# Local Development Guide

## Service Bus Configuration

### 1. Update local.settings.json
This is service bus and queues in the cloud that the local logic app will connect to. The Logic App uses the `SERVICEBUS_CONNECTION_STRING` to connect to Azure Service Bus.
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
you only have to update the `connectionRuntimeUrl` to point to the local ServiceBusProxy (if using Hybrid Mode) or directly to Azure Service Bus (if using Full Cloud Mode).
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
These need to be added to the parameter file for the logic app to work. You can create them in Azure Portal or using Azure CLI.
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

## Supported Queues and Entity Types

The `process-message` workflow is designed to process multiple D365 entity types, each with its own dedicated queue. The workflow can be deployed multiple times, with each instance listening to a different queue.

### Queue Mapping

| Entity Type | Queue Name | Parameter | Use Case |
|-----|-----|-----|-----|
| **Customer** | `sbq-d365finops-customer-upserts` | `D365_CUSTOMER_QUEUE` | Customer master data create/update |
| **Customer Address** | `sbq-d365finops-customeraddress-upserts` | `D365_CUSTOMERADDRESS_QUEUE` | Shipping/billing addresses for customers |
| **Vendor** | `sbq-d365finops-vendor-upserts` | `D365_VENDOR_QUEUE` | Vendor master data create/update |
| **Vendor Address** | `sbq-d365finops-vendoraddress-upserts` | `D365_VENDORADDRESS_QUEUE` | Vendor site/location addresses |
| **Item** | `sbq-d365finops-item-upserts` | `D365_ITEM_QUEUE` | Product/inventory master data |
| **Item Unit Conversion** | `sbq-d365finops-itemunitconversion-upserts` | `D365_ITEMUNITCONVERSION_QUEUE` | Unit of measure conversions |

### Queue Configuration

Each workflow instance listens to ONE queue via the `ACTIVE_QUEUE_TYPE` parameter:

```json
"ACTIVE_QUEUE_TYPE": "customer" // Options: customer, customeraddress, vendor, vendoraddress, item, itemunitconversion
```

### Deployment Scenarios

#### Local Development (Single Queue)

For local testing, run ONE instance listening to a specific queue:

```powershell
# Start Logic App configured for Customer queue
cd LogicApp\sktestlogicapp
func host start --port 7071
```

The default `ACTIVE_QUEUE_TYPE` is **"customer"**. Update by setting an environment variable or modifying `local.settings.json`:

```json
{
  "Values": {
    "ACTIVE_QUEUE_TYPE": "customer"
  }
}
```

#### Azure Deployment (Multiple Instances)

Deploy multiple instances of the workflow, each with a different `ACTIVE_QUEUE_TYPE`:

**Instance 1: Customer**
```bash
az resource create \
  --resource-group YOUR_RG \
  --resource-type "Microsoft.Web/sites/workflows" \
  --name "logicapp/process-message-customer" \
  --properties '{
    "files": {
      "workflow.json": {...},
      "connections.json": {...}
    },
    "parameters": {
      "ACTIVE_QUEUE_TYPE": { "value": "customer" },
      "DEFAULT_SOURCE_TOPIC": { "value": "customerToCanonical" }
    }
  }'
```

**Instance 2: Vendor**
```bash
az resource create \
  --resource-group YOUR_RG \
  --resource-type "Microsoft.Web/sites/workflows" \
  --name "logicapp/process-message-vendor" \
  --properties '{
    "parameters": {
      "ACTIVE_QUEUE_TYPE": { "value": "vendor" },
      "DEFAULT_SOURCE_TOPIC": { "value": "vendorToCanonical" }
    }
  }'
```

**Instance 3: Item**
```bash
az resource create \
  --resource-group YOUR_RG \
  --resource-type "Microsoft.Web/sites/workflows" \
  --name "logicapp/process-message-item" \
  --properties '{
    "parameters": {
      "ACTIVE_QUEUE_TYPE": { "value": "item" },
      "DEFAULT_SOURCE_TOPIC": { "value": "itemToCanonical" }
    }
  }'
```

Or use the infrastructure Bicep template to define all instances:

```bicep
param logicAppDeployments array = [
  {
    name: 'process-message-customer'
    queueType: 'customer'
    sourceTopic: 'customerToCanonical'
  }
  {
    name: 'process-message-vendor'
    queueType: 'vendor'
    sourceTopic: 'vendorToCanonical'
  }
  {
    name: 'process-message-item'
    queueType: 'item'
    sourceTopic: 'itemToCanonical'
  }
  // Add more as needed...
]
```

---

## Running Locally

### Option 1: Hybrid Mode with ServiceBusProxy (Recommended for Local Development)

Hybrid mode allows you to run Logic Apps locally while connecting to the cloud Azure Service Bus, using a local proxy to bridge the gap. This is ideal for development and testing.

#### Prerequisites

1. **Azurite** - Local Azure Storage emulator for `AzureWebJobsStorage`
   - Install: `npm install -g azurite`
   - Alternatively, VS Code extension: "Azurite"

2. **Service Bus Proxy Function** - Local HTTP proxy that bridges Service Bus API
   - Located in: `src/ServiceBusProxy/`
   - Runs on port 7075

3. **Azure Functions Core Tools** - Already required for Logic Apps

4. **Cloud Azure Service Bus Namespace** - Connection to real Service Bus in Azure (for testing with real messages)

#### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ LOCAL DEVELOPMENT (Hybrid Mode)                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  Logic App (Port 7071)                                          │
│  ├─ connections.json: connectionRuntimeUrl =                    │
│  │  "http://localhost:7075/api/servicebus"                      │
│  └─ Triggers: Service Bus connector                             │
│       │                                                           │
│       └─→ HTTP PUT/GET http://localhost:7075/api/servicebus     │
│                                                                   │
│  ServiceBusProxy Function (Port 7075)                           │
│  ├─ Receives HTTP requests from Logic App                       │
│  ├─ Translates to Service Bus REST API calls                    │
│  └─ Forwards to Azure Service Bus (Cloud)                       │
│       │                                                           │
│       └─→ HTTPS: sb://YOUR-NAMESPACE.servicebus.windows.net     │
│                                                                   │
│  Azure Service Bus (Cloud)                                      │
│  └─ Real queue: inbound-messages                                │
│                                                                   │
│  Azurite (Port 10000-10002)                                     │
│  └─ Local storage emulation (AzureWebJobsStorage)               │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

#### Step 1: Start Azurite

Azurite emulates Azure Storage locally, required for Logic Apps runtime.

```powershell
# Option A: Command line
azurite

# Option B: VS Code Extension
# Install "Azurite" extension and click "Start"

# Option C: Via npm
npx azurite
```

**Expected Output:**
```
Azurite Table service is listening at http://127.0.0.1:10002
Azurite Queue service is listening at http://127.0.0.1:10001
Azurite Blob service is listening at http://127.0.0.1:10000
```

#### Step 2: Start ServiceBusProxy Function (Port 7075)

The ServiceBusProxy acts as a bridge between your local Logic App and Azure Service Bus.

```powershell
# Navigate to ServiceBusProxy folder
cd src\ServiceBusProxy

# Build if needed
dotnet build

# Start function on port 7075
func host start --port 7075
```

**Expected Output to Monitor:**
```
Azure Functions Core Tools
Worker process started and initialized
Host initialized (X.XXXs)
[7075] http://localhost:7075

Functions in ServiceBusProxy:
    ServiceBusProxyFunction: [GET,PUT] http://localhost:7075/api/servicebus/{queue}/messages/{messageId}
    SqlProxyFunction: [GET,PUT] http://localhost:7075/api/sql/{operation}
```

**Output Indicators of Correct Operation:**
- ✅ Listening on `http://localhost:7075`
- ✅ `ServiceBusProxyFunction` endpoint registered
- ✅ `Host initialized` message (no errors)
- ✅ When logic app triggers, you'll see: `Executing 'ServiceBusProxyFunction'...`
- ✅ Watch for HTTP requests: `[7075] HTTP GET http://localhost:7075/api/servicebus/{queueName}/messages/head`
- ✅ Each successful proxy call logs: `ServiceBusProxyFunction (Executed, Succeeded, X milliseconds)`
- ⚠️ If you see connection errors, verify `SERVICEBUS_CONNECTION_STRING` in `src/ServiceBusProxy/local.settings.json`

#### Step 3: Start Logic App (Port 7071)

In a new terminal:

```powershell
# Navigate to Logic App folder
cd LogicApp\sktestlogicapp

# Start Logic App
func host start --port 7071
```

**Expected Output:**
```
Azure Functions Core Tools
Worker process started and initialized
Host initialized (X.XXXs)
[7071] http://localhost:7071

Workflows in sktestlogicapp:
    process-message: [Service Bus Trigger] servicebus://inbound-messages
    outbox-processor: [Recurrence Trigger] Every 1 minute
```

**Output Indicators of Correct Operation:**
- ✅ Both workflows loaded successfully
- ✅ Service Bus trigger shows: `servicebus://inbound-messages`
- ✅ `Host initialized` with no errors
- ✅ When a message arrives, you'll see: `Trigger Details: ServiceBus (inbound-messages)`
- ✅ Watch for workflow execution: `Started execution of workflow: process-message`
- ✅ After execution: `Execution of workflow: process-message succeeded`

#### Step 4: Verify Connections

Check that the Logic App is configured to use the local ServiceBusProxy:

**LogicApp/sktestlogicapp/connections.json.local:**
```json
{
  "managedApiConnections": {
    "servicebus": {
      "connectionRuntimeUrl": "http://localhost:7075/api/servicebus",
      "authentication": {
        "type": "Raw",
        "scheme": "Key",
        "parameter": "@appsetting('SERVICEBUS_CONNECTION_STRING')"
      }
    }
  }
}
```

**LogicApp/sktestlogicapp/local.settings.json:**
```json
{
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "SERVICEBUS_CONNECTION_STRING": "Endpoint=sb://YOUR-NAMESPACE.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YOUR_KEY"
  }
}
```

#### Step 5: Use VS Code Compound Task (Recommended)

Or start all services in one command using the provided task:

```powershell
# In VS Code Terminal, press Ctrl+Shift+P
# Run: "Tasks: Run Task"
# Select: "Start All Services"
```

This will start:
- Logic App (Port 7071)
- LogicAppProcessor (Port 7072)
- JwtAuthStub (Port 7073)

You'll need to start Azurite and ServiceBusProxy separately in different terminals.

#### Monitoring Hybrid Mode Operations

**Watch the ServiceBusProxy output for:**

1. **Message Head Requests**
   ```
   [7075] GET /api/servicebus/inbound-messages/messages/head HTTP/1.1 200 - 45ms
   Executing 'ServiceBusProxyFunction'...
   Retrieved message from queue: inbound-messages (MessageId: abc123...)
   Executing 'ServiceBusProxyFunction' (Executed, Succeeded, 45ms)
   ```
   → This indicates the proxy is successfully polling Service Bus and returning messages

2. **Message Completion**
   ```
   [7075] PUT /api/servicebus/inbound-messages/messages/abc123 HTTP/1.1 200 - 32ms
   Message marked as complete: abc123
   ```
   → This indicates the proxy successfully removed the message from the queue

3. **Connection Errors** (indicates misconfiguration)
   ```
   Azure.Messaging.ServiceBus.ServiceBusException: 'The 'SharedAccessKeyName' part of the connection string is required.'
   ```
   → Fix: Update `SERVICEBUS_CONNECTION_STRING` in `src/ServiceBusProxy/local.settings.json`

**Watch the Logic App output for:**

1. **Trigger Execution**
   ```
   Trigger Details: ServiceBus (inbound-messages)
   Started execution of workflow: process-message
   Workflow: process-message started
   ```

2. **Workflow Actions**
   ```
   Action 'Compute_Message_ID' (JavaScript inline code) succeeded
   Action 'Query_Inbox' (SQL) succeeded
   Action 'Transform_With_Liquid' succeeded
   Action 'Insert_Into_Outbox' (SQL) succeeded
   ```

3. **Service Bus Publishing**
   ```
   Action 'Try_Publish_To_Service_Bus' succeeded
   Message published to Service Bus topic: canonical-events
   ```

4. **Completion**
   ```
   Execution of workflow: process-message succeeded (2345ms)
   Response: {"status": "Processed", "messageId": "abc123..."}
   ```

#### Troubleshooting Hybrid Mode

| Issue | Symptom | Solution |
|-------|---------|----------|
| ServiceBusProxy not connecting to Service Bus | `UnauthorizedException` in proxy output | Verify `SERVICEBUS_CONNECTION_STRING` in `src/ServiceBusProxy/local.settings.json` with `az servicebus namespace authorization-rule keys list` |
| Logic App not triggering | No messages being polled, proxy shows no activity | Check `connections.json` has `connectionRuntimeUrl: "http://localhost:7075/api/servicebus"` |
| Azurite connection issues | `Error connecting to storage` | Ensure Azurite is running on port 10000-10002 and `local.settings.json` has `"AzureWebJobsStorage": "UseDevelopmentStorage=true"` |
| Port already in use | `Unable to bind to port 7075` | Kill process: `netstat -ano \| findstr :7075` then `taskkill /PID [PID] /F` |
| Messages not being removed from queue | Messages keep being reprocessed | Check ServiceBusProxy output for successful `PUT` requests for message completion |

#### Key Differences: Hybrid Mode vs. Azure Deployment

| Aspect | Hybrid Mode | Azure |
|--------|------------|-------|
| Logic App location | Local (`localhost:7071`) | Azure (`https://logic-app.azurewebsites.net`) |
| Service Bus proxy | Local ServiceBusProxy (`localhost:7075`) | Direct cloud connection |
| Storage | Azurite emulator | Azure Storage Account |
| Connection setup | `connections.json.local` | `connections.json` (Azure) |
| Connection URL | `http://localhost:7075/api/servicebus` | `https://sktestsb.servicebus.windows.net` |
| Best for | Development, testing, debugging | Production deployment |

### Option 2: Full Cloud Mode (No Local Services)

If you prefer not to run ServiceBusProxy locally, skip ServiceBusProxy and run Logic App directly against Azure Service Bus. However, this approach has limitations with Service Bus triggers in local development.

### Start the Logic App (Simple Mode - No ServiceBusProxy)

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
