# Service Bus Proxy for Logic Apps Local Development

## Purpose

This Azure Function acts as a transparent proxy between Logic Apps workflows and Azure Service Bus queues, enabling Service Bus triggers to work reliably during local development.

## Architecture

```
Logic App (Service Bus Trigger) → http://localhost:7075/api/servicebus/{queue}/messages/head
                                              ↓
                                    ServiceBusProxy Function
                                              ↓
                                    Azure Service Bus (Cloud)
```

## Why Do We Need This?

Logic Apps Standard Service Bus triggers have limited support when running locally with `func host start`. Messages in queues may not be automatically picked up. This proxy solves that problem by:

1. **Reliable local polling**: Azure Functions Service Bus triggers work perfectly locally
2. **Mimics Service Bus API**: Exposes HTTP endpoints that match Service Bus REST API format
3. **No workflow changes**: Logic App workflows remain identical for local and Azure deployments
4. **Only connection config changes**: Just update `connectionRuntimeUrl` to point to proxy locally

## How It Works

### Local Development
- Logic App `connections.json` → `connectionRuntimeUrl: "http://localhost:7075/api/servicebus"`
- Proxy receives HTTP GET request for messages
- Proxy retrieves message from real Azure Service Bus queue
- Returns message in Service Bus format to Logic App
- Message is completed (removed from queue)

### Azure Deployment
- Logic App `connections.json` → `connectionRuntimeUrl: "https://{namespace}.servicebus.windows.net"`
- Logic App connects directly to Azure Service Bus
- Proxy not used

## Configuration

### ServiceBusProxy (this project)

**local.settings.json:**
```json
{
  "Values": {
    "SERVICEBUS_CONNECTION_STRING": "Endpoint=sb://sktestsb.servicebus.windows.net/;..."
  }
}
```

### Logic App

**local.settings.json:**
```json
{
  "Values": {
    "SERVICEBUS_CONNECTION_STRING": "Endpoint=sb://sktestsb.servicebus.windows.net/;..."
  }
}
```

**connections.json (LOCAL):**
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

**connections.json (AZURE):**
```json
{
  "managedApiConnections": {
    "servicebus": {
      "connectionRuntimeUrl": "https://sktestsb.servicebus.windows.net",
      "connectionProperties": {
        "authentication": {
          "type": "ManagedServiceIdentity",
          "audience": "https://servicebus.azure.net/"
        }
      }
    }
  }
}
```

## Running Locally

### Complete Hybrid Mode Setup

For a complete local development setup with all services, follow these steps:

#### Prerequisites
- ✅ **Azurite** - Local Azure Storage emulator  
  Install: `npm install -g azurite` or use VS Code extension
  
- ✅ **Azure Functions Core Tools** - Required for local function execution  
  Install: `npm install -g azure-functions-core-tools@4`
  
- ✅ **Azure Service Bus Namespace** - Cloud-based (accessible from local machine)
  
- ✅ **.NET SDK** - For building ServiceBusProxy  
  Download: https://dotnet.microsoft.com/download

#### Step 1: Start Azurite (Terminal 1)
```powershell
# Make sure Azurite is running (required for AzureWebJobsStorage)
azurite
```

**Expected Output:**
```
Azurite Table service is listening at http://127.0.0.1:10002
Azurite Queue service is listening at http://127.0.0.1:10001
Azurite Blob service is listening at http://127.0.0.1:10000
```

#### Step 2: Start Service Bus Proxy (Terminal 2)
```powershell
cd src\ServiceBusProxy
func host start --port 7075
```

**Expected Output:**
```
Azure Functions Core Tools
Worker process started and initialized.

Host initialized (456ms)
[7075] http://localhost:7075

Functions in ServiceBusProxy:
    ServiceBusProxyFunction: [GET,PUT] http://localhost:7075/api/servicebus/{queue}/messages/{messageId}
    SqlProxyFunction: [GET,PUT] http://localhost:7075/api/sql/{operation}
```

**Watch for in Output:**
- ✅ `Host initialized` - Proxy is ready
- ✅ `Functions in ServiceBusProxy:` - All endpoints registered
- ✅ When Logic App connects:
  ```
  [7075] GET /api/servicebus/inbound-messages/messages/head HTTP/1.1 200 - 45ms
  Executing 'ServiceBusProxyFunction' ...
  Retrieved message from queue: inbound-messages
  Executing 'ServiceBusProxyFunction' (Executed, Succeeded, 45ms)
  ```
- ⚠️ **Watch out for connection errors:**
  ```
  Azure.Messaging.ServiceBus.ServiceBusException: 'The 'SharedAccessKeyName' part of the connection string is required.'
  ```
  → Fix: Verify `SERVICEBUS_CONNECTION_STRING` in `local.settings.json`

#### Step 3: Start Logic App (Terminal 3)
```powershell
cd LogicApp\sktestlogicapp
func host start --port 7071
```

**Expected Output:**
```
Azure Functions Core Tools
Worker process started and initialized.

Host initialized (678ms)
[7071] http://localhost:7071

Workflows in sktestlogicapp:
    process-message: [Service Bus Trigger] servicebus://inbound-messages
    outbox-processor: [Recurrence Trigger] frequency: minute, interval: 1
```

**Watch for in Output:**
- ✅ `Host initialized` - Logic App is ready
- ✅ `Workflows in sktestlogicapp:` - All workflows loaded
- ✅ Process-message shows Service Bus trigger: `servicebus://inbound-messages`
- ✅ When a message arrives:
  ```
  Trigger Details: ServiceBus (inbound-messages)
  Started execution of workflow: process-message
  Workflow: process-message started at X:XX:XX
  ```
- ✅ Successful actions show:
  ```
  Action 'Compute_Message_ID' succeeded
  Action 'Query_Inbox' succeeded
  Action 'Transform_With_Liquid' succeeded
  ```

### 1. Start Service Bus Proxy (Port 7075)

```powershell
cd src\ServiceBusProxy
func host start --port 7075
```

✅ **Key Output Indicators:**
- `Host initialized` with no errors
- Endpoint shows: `[7075] http://localhost:7075`
- Three functions registered: `ServiceBusProxyFunction`, `SqlProxyFunction`

⚠️ **Common Issues:**
| Error | Fix |
|-------|-----|
| `Port 7075 already in use` | Kill process: `netstat -ano \| findstr :7075` → `taskkill /PID [PID] /F` |
| `ServiceBusException: connection string is invalid` | Check `local.settings.json` has correct `SERVICEBUS_CONNECTION_STRING` |
| `Host did not start` | Ensure `.NET SDK` is installed: `dotnet --version` |

### 2. Update Logic App connections.json
Update `connectionRuntimeUrl` to point to proxy (see above)

### 3. Start Logic App
```powershell
cd LogicApp\sktestlogicapp
func host start --port 7071
```

### 4. Add Test Messages to Queue
Use Azure Portal or Azure CLI to add messages to your Service Bus queue

### 5. Monitor Proxy Output

The ServiceBusProxy runs on **port 7075** and acts as the bridge between the local Logic App and Azure Service Bus.

#### Normal Operation Output (When Logic App Polls for Messages)

```
[7075] GET /api/servicebus/inbound-messages/messages/head HTTP/1.1 200 - 45ms
[7075] Executing 'ServiceBusProxyFunction' (Reason='(null)', Id=abc123)
[7075] Retrieved message from queue: inbound-messages (MessageId: d365-msg-001)
[7075] Executing 'ServiceBusProxyFunction' (Executed, Succeeded, 45ms)
```

**What to notice:**
- ✅ `HTTP/1.1 200` - Success (message returned)
- ✅ `Retrieved message from queue` - Message found and extracted
- ✅ Timing: Typically 40-100ms per request
- ✅ `Executed, Succeeded` - No errors

#### When Message is Marked Complete

```
[7075] PUT /api/servicebus/inbound-messages/messages/d365-msg-001 HTTP/1.1 200 - 32ms
[7075] Executing 'ServiceBusProxyFunction' (Reason='Delete', Id=def456)
[7075] Message marked as complete: d365-msg-001
[7075] Executing 'ServiceBusProxyFunction' (Executed, Succeeded, 32ms)
```

**What to notice:**
- ✅ `PUT` request (completing/deleting message)
- ✅ `HTTP/1.1 200` - Successful completion
- ✅ Message is now removed from the queue
- ✅ Logic App workflow will continue to next step

#### When No Messages Available

```
[7075] GET /api/servicebus/inbound-messages/messages/head HTTP/1.1 204 - 15ms
[7075] Executing 'ServiceBusProxyFunction' (Reason='(null)', Id=ghi789)
[7075] No message available in queue: inbound-messages
[7075] Executing 'ServiceBusProxyFunction' (Executed, Succeeded, 15ms)
```

**What to notice:**
- ✅ `HTTP/1.1 204` - No Content (queue empty)
- ✅ `No message available` - Expected behavior when queue is empty
- ✅ Logic App will wait and poll again after 30 seconds

#### Connection Issues (Watch Out!)

```
[7075] GET /api/servicebus/inbound-messages/messages/head HTTP/1.1 401 - 156ms
[7075] Executing 'ServiceBusProxyFunction' (Reason='(null)', Id=jkl012)
[7075] Azure.Messaging.ServiceBus.ServiceBusException: 'The 'SharedAccessKeyName' part of the connection string is required.'
[7075] Executing 'ServiceBusProxyFunction' (Executed, Failed, 156ms)
```

**What to do:**
- ⚠️ `HTTP/1.1 401` - Authentication failed
- ⚠️ Check `src/ServiceBusProxy/local.settings.json` for `SERVICEBUS_CONNECTION_STRING`
- ⚠️ Verify the connection string has all parts:
  ```
  Endpoint=sb://YOUR-NS.servicebus.windows.net/;
  SharedAccessKeyName=RootManageSharedAccessKey;
  SharedAccessKey=YOUR_KEY
  ```

#### Troubleshooting Output Patterns

| Output Pattern | Meaning | Solution |
|----------------|---------|----------|
| All `204 No Content` | Queue is empty | Add messages to Service Bus queue |
| All `401 Unauthorized` | Bad connection string | Verify and update `SERVICEBUS_CONNECTION_STRING` |
| `Connection timeout` | Can't reach Service Bus | Check network/firewall to Service Bus |
| Long delays (>500ms) | Network latency or Service Bus throttling | Normal; usually Service Bus is busy |
| `No 'GET' requests appearing` | Logic App not calling proxy | Check Logic App connections.json setup |

#### Performance Baseline

**Expected timings per request:**
- ✅ Normal message retrieval: **30-80ms**
- ✅ No message (204): **10-30ms**
- ✅ Message completion (PUT): **20-60ms**

If you see consistently >500ms, it might indicate:
- Network latency to Azure
- Service Bus throttling (slow down request rate)
- Local machine resource constraints

---

### Watch the Magic

- Logic App polls proxy every 30 seconds
- Proxy retrieves message from Azure Service Bus
- Logic App processes message using workflows

## API Endpoints

### GET /api/servicebus/{queueName}/messages/head
Retrieves a single message from the specified queue.

**Query Parameters:**
- `queueType`: Main or DeadLetter (default: Main)

**Response (200 OK):**
```json
{
  "contentData": "eyJkYXRhQXJlYUlkIjoiVVNNRiIsLi4u",
  "contentType": "application/json",
  "messageId": "msg-123",
  "properties": {
    "CorrelationId": "corr-456"
  },
  "correlationId": "corr-456",
  "sequenceNumber": 12345
}
```

**Response (204 No Content):**
No messages available in queue.

### GET /api/health
Health check endpoint.

## Deployment

**This proxy is only needed for local development.** Do not deploy to Azure.

In Azure:
- Logic Apps connect directly to Service Bus
- Service Bus triggers work natively
- No proxy required

## Troubleshooting Hybrid Mode

### ServiceBusProxy Won't Start

```
error: listening on port 7075 failed: address already in use
```

**Solution:**
```powershell
# Find process using port 7075
netstat -ano | findstr :7075

# Kill the process (replace PID)
taskkill /PID 12345 /F
```

### Proxy Can't Connect to Service Bus

```
Azure.Messaging.ServiceBus.ServiceBusException: 'The 'SharedAccessKeyName' part of the connection string is required.'
```

**Solution:**
1. Verify `SERVICEBUS_CONNECTION_STRING` in `src/ServiceBusProxy/local.settings.json`
2. Get correct connection string:
   ```powershell
   az servicebus namespace authorization-rule keys list \
     --resource-group YOUR_RG \
     --namespace-name YOUR_NAMESPACE \
     --name RootManageSharedAccessKey \
     --query "primaryConnectionString" -o tsv
   ```
3. Paste into `local.settings.json`

### Logic App Not Receiving Messages from Proxy

**Check:**
1. Verify `Logic App connections.json` has: `"connectionRuntimeUrl": "http://localhost:7075/api/servicebus"`
2. Verify `AzureWebJobsStorage` in Logic App points to Azurite: `"UseDevelopmentStorage=true"`
3. Watch proxy output - should see GET requests from Logic App

### Messages Stuck in Queue

```
[7075] GET /api/servicebus/inbound-messages/messages/head HTTP/1.1 200 - 45ms
[7075] Retrieved message from queue
[7075] Executing 'ServiceBusProxyFunction' (Executed, Failed, X milliseconds)
```

**Solution:**
1. Check Logic App workflow logs for errors after retrieval
2. If Logic App crashes, message may be abandoned (depends on max delivery count)
3. Check `Outbox` table for processing errors

### Proxy not receiving requests from Logic App

**Check:**
1. Is Logic App running? `func host start --port 7071`
2. Check Logic App logs for Service Bus connection errors
3. Verify firewall allows `localhost:7075` connections
4. Restart both services

### Hybrid Proxy vs Azure Deployment

| Issue | Hybrid (Local) | Azure |
|-------|---|---|
| ServiceBusProxy needed? | YES (port 7075) | NO |
| Azurite needed? | YES | NO |
| Local Logic App? | YES | NO |
| Connection URLs | `http://localhost:7075/api/servicebus` | `https://ns.servicebus.windows.net` |
| Best for | Development & Testing | Production |

---

### Proxy not receiving requests:
- Check Logic App connections.json has correct connectionRuntimeUrl
- Verify proxy is running on port 7075
- Check firewall/network settings

**Messages not appearing:**
- Verify SERVICEBUS_CONNECTION_STRING is correct
- Check messages actually exist in Azure Service Bus queue (Azure Portal)
- Check proxy logs for errors

**Logic App errors:**
- Ensure message format returned by proxy matches Service Bus API
- Check for JSON serialization issues in logs

## Benefits

✅ Test Service Bus triggers locally without Azure deployment  
✅ No workflow code changes between local and Azure  
✅ Use real Azure Service Bus queues for testing  
✅ Message deduplication and idempotency fully tested  
✅ Liquid transformations tested with real D365 messages  
✅ Complete end-to-end testing locally
