# Quick Start: Testing Logic App with Service Bus Proxy

## Step 1: Start Service Bus Proxy (Terminal 1)
```powershell
cd C:\code\repos\BatoryLogicApps\src\ServiceBusProxy
func host start --port 7075
```

**Wait for these startup messages:**
```
ServiceBusProxyFunction: [GET] http://localhost:7075/apim/servicebus/local/{queueName}/messages/head
HealthCheck: [GET] http://localhost:7075/health
```

## Step 2: Test Proxy Independently (Optional but Recommended)

In a new terminal, run the test script:
```powershell
cd C:\code\repos\BatoryLogicApps\src\ServiceBusProxy
.\test-proxy.ps1
```

**Expected output:**
- ✓ Health check passed
- ✓ Message retrieved OR No messages in queue

**Check proxy terminal for detailed logs:**
```
[PROXY] HTTP Request Received
[PROXY] URL: http://localhost:7075/apim/servicebus/local/sbq-d365finops-customer-upserts/messages/head
[PROXY] Queue Name: 'sbq-d365finops-customer-upserts'
[PROXY] Creating Service Bus receiver...
[PROXY] ✓ Message Retrieved!
[PROXY]   MessageId: xxx
[PROXY]   Size: 1234 bytes
[PROXY] ✓ Message completed successfully
[PROXY] Returning message to Logic App (200 OK)
```

## Step 3: Start Logic App (Terminal 2)
```powershell
cd C:\code\repos\BatoryLogicApps\LogicApp\sktestlogicapp
func host start --port 7071
```

**Wait for:**
```
Workflow 'process-message' started successfully
```

## Step 4: Watch the Logs

### Proxy Terminal (Every 30s)
You should see polling requests:
```
[PROXY] HTTP Request Received
[PROXY] Queue Name: 'sbq-d365finops-customer-upserts'
[PROXY] Message Retrieved! MessageId: xxx
```

### Logic App Terminal
You should see workflow executions:
```
Workflow 'process-message' execution started
Log_Trigger_Received: Service Bus trigger fired
Log_Message_Decoded: Message decoded successfully
```

## Step 5: Verify Processing

Check your SQL database:
```sql
-- Check Inbox table
SELECT TOP 10 * FROM [dbo].[Inbox] ORDER BY ReceivedAt DESC

-- Check Outbox table
SELECT TOP 10 * FROM [dbo].[Outbox] ORDER BY CreatedAt DESC
```

## Troubleshooting

### Proxy not receiving requests from Logic App
**Symptom:** Proxy terminal shows no activity (no "[PROXY] HTTP Request Received" logs)

**Checks:**
1. Verify proxy is running: `curl http://localhost:7075/health`
2. Check Logic App validation errors in startup logs
3. Verify [connections.json](LogicApp/sktestlogicapp/connections.json#L31) has `"connectionRuntimeUrl": "http://localhost:7075/apim/servicebus/local"`
4. Look for "Workflow validation and creation failed" errors

### Proxy returns "No messages in queue"
**Symptom:** `[PROXY] No messages available in queue`

**Checks:**
1. Verify message exists in Azure Portal → Service Bus → Queue
2. Check queue name matches exactly (case-sensitive)
3. Verify Service Bus connection string in [local.settings.json](src/ServiceBusProxy/local.settings.json)

### Proxy throws exception
**Symptom:** `[PROXY] ❌ ERROR` logs with stack trace

**Common causes:**
- Invalid Service Bus connection string
- Queue doesn't exist
- Permissions issue (SharedAccessKey needs Manage/Listen rights)
- Network connectivity to Azure

### Logic App not polling
**Symptom:** Logic App starts but no workflow executions

**Checks:**
1. Check for workflow validation errors in startup
2. Verify Service Bus connection in connections.json
3. Check parameters.json has D365_CUSTOMER_QUEUE defined
4. Look in Logic App logs for "WorkflowDispatcher" polling messages

## Success Indicators

✅ Proxy logs show "[PROXY] HTTP Request Received" every 30 seconds  
✅ Proxy logs show "[PROXY] ✓ Message Retrieved!" when messages exist  
✅ Logic App logs show "TriggerFired" and "MessageDecoded"  
✅ SQL Inbox table has new row with MessageId  
✅ SQL Outbox table has canonical event with eventType  
✅ Message removed from Azure Service Bus queue (check in Portal)
