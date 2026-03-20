# Logic Apps Best Practices Implementation Plan

## Current Issues

### ❌ Missing/Incorrect Implementations

1. **No explicit retry policies** - Currently relying on default connector behavior
2. **No logging actions** - No Application Insights integration
3. **No Correlation ID propagation** - Missing across actions
4. **Incomplete error handling** - Basic Scope usage but not comprehensive
5. **Non-idempotent actions may retry** - No differentiation
6. **No terminal error detection** - 4xx errors will retry unnecessarily

### ✅ Current Workflows (2 of 4)

1. ✅ **process-message** - Main ingestion
2. ✅ **outbox-processor** - Retry delivery
3. ❌ **Missing**: Status query endpoint (202 Accepted pattern)
4. ❌ **Missing**: Compensating transaction handler

## Recommended 4 Workflows

Based on best practices and your original Functions architecture:

### 1. **process-message** (HTTP Trigger)
- **Purpose**: Accept incoming messages, validate, deduplicate, transform, queue
- **Pattern**: Returns 202 Accepted immediately after queuing
- **State**: Stateless (state in SQL Inbox/Outbox)
- **Retry**: Disabled for database inserts (idempotent by MessageId)

### 2. **outbox-processor** (Recurrence Trigger)  
- **Purpose**: Process pending outbox messages with retry logic
- **Pattern**: Pull from outbox, retry with exponential backoff
- **State**: Stateless (state in SQL Outbox table)
- **Retry**: Enabled for Service Bus/HTTP, disabled for SQL updates

### 3. **message-status** (HTTP Trigger)
- **Purpose**: Query message processing status
- **Pattern**: GET endpoint with MessageId parameter
- **State**: Stateless (queries SQL)
- **Retry**: Enabled for SQL queries (idempotent)

### 4. **error-handler** (Manual/Event Trigger)
- **Purpose**: Handle failed messages after max retries
- **Pattern**: Compensating transactions, alerting, dead-letter
- **State**: Stateless (updates SQL, sends alerts)
- **Retry**: Minimal (terminal actions)

## Required Fixes

### Retry Policy Configuration

```json
{
  "retryPolicy": {
    "type": "exponential",
    "count": 4,
    "interval": "PT10S",
    "maximumInterval": "PT1H",
    "minimumInterval": "PT5S"
  }
}
```

**Apply to:**
- ✅ Service Bus sends (transient failures)
- ✅ HTTP calls to external APIs (transient failures)
- ✅ SQL queries/selects (transient failures)
- ❌ SQL inserts/updates (idempotent, single attempt)
- ❌ Compute Message ID (deterministic, no retry)
- ❌ Transform actions (deterministic, no retry)

### Terminal Error Handling

Stop retries on:
- 400 Bad Request
- 401 Unauthorized  
- 403 Forbidden
- 404 Not Found
- 422 Unprocessable Entity

Only retry on:
- 408 Request Timeout
- 429 Too Many Requests
- 500 Internal Server Error
- 502 Bad Gateway
- 503 Service Unavailable
- 504 Gateway Timeout

### Logging Requirements

Every action should log:
- Correlation ID
- Message ID
- Action name
- Status (Success/Failure)
- Duration
- Error details (if failed)

### Correlation ID Propagation

```
HTTP Header: x-correlation-id
  ↓
Process Message Workflow
  ↓
Outbox Record (CorrelationId column)
  ↓
Service Bus Message Property
  ↓
Downstream Systems
```

## Implementation Priority

1. **High**: Add explicit retry policies to all actions
2. **High**: Add correlation ID tracking throughout
3. **High**: Add Application Insights logging
4. **Medium**: Create message-status workflow
5. **Medium**: Improve error handling with terminal error detection
6. **Low**: Create error-handler workflow for compensating transactions

## Next Steps

1. Update process-message workflow with retry policies + logging
2. Update outbox-processor workflow with retry policies + logging
3. Create message-status workflow
4. Create error-handler workflow
5. Update documentation with best practices compliance

Would you like me to implement these fixes?
