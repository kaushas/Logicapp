# Test script: Get message from proxy and POST to Logic App HTTP trigger
# This tests the workflow logic independently of the Service Bus connector

$proxyUrl = "http://localhost:7075"
$logicAppUrl = "http://localhost:7071/runtime/webhooks/workflow/api/management/workflows/process-message/triggers/manual/invoke?api-version=2020-05-01-preview&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=PLACEHOLDER"
$queueName = "sbq-d365finops-customer-upserts"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test: Manual Workflow Execution" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Get message from proxy
Write-Host "Step 1: Retrieving message from proxy..." -ForegroundColor Yellow
Write-Host "GET $proxyUrl/apim/servicebus/local/$queueName/messages/head" -ForegroundColor Gray

try {
    $message = Invoke-RestMethod -Uri "$proxyUrl/apim/servicebus/local/$queueName/messages/head" -Method Get -ErrorAction Stop
    
    if (-not $message) {
        Write-Host "No messages in queue" -ForegroundColor Yellow
        exit 0
    }
    
    Write-Host "Message retrieved!" -ForegroundColor Green
    Write-Host "  MessageId: $($message.messageId)" -ForegroundColor Green
    Write-Host "  ContentData length: $($message.contentData.Length) chars" -ForegroundColor Green
    Write-Host ""
    
} catch {
    Write-Host "Failed to retrieve message: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 2: POST message to Logic App HTTP trigger
Write-Host "Step 2: Posting message to Logic App..." -ForegroundColor Yellow
Write-Host "POST $logicAppUrl" -ForegroundColor Gray
Write-Host ""

# Enable HTTP trigger by setting environment variable
$env:USE_HTTP_TRIGGER_FOR_LOCAL = "true"

try {
    $response = Invoke-RestMethod -Uri $logicAppUrl `
        -Method Post `
        -Body ($message | ConvertTo-Json -Depth 10) `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    Write-Host "Workflow triggered successfully!" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor Gray
    
} catch {
    Write-Host "Failed to trigger workflow: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host "Error details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check SQL Inbox table for new record" -ForegroundColor Cyan
Write-Host "MessageId: $($message.messageId)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
