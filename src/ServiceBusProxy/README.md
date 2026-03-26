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

### 1. Start Service Bus Proxy
```powershell
cd src\ServiceBusProxy
func host start --port 7075
```

### 2. Update Logic App connections.json
Update `connectionRuntimeUrl` to point to proxy (see above)

### 3. Start Logic App
```powershell
cd LogicApp\sktestlogicapp
func host start --port 7071
```

### 4. Add Test Messages to Queue
Use Azure Portal or Azure CLI to add messages to your Service Bus queue

### 5. Watch the Magic
- Logic App polls proxy every 30 seconds
- Proxy retrieves message from Azure Service Bus
- Logic App processes message

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

## Troubleshooting

**Proxy not receiving requests:**
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
