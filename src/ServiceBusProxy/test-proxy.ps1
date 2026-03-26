# Test script for ServiceBusProxy
# Run this while the proxy is running to verify it can retrieve messages

$proxyUrl = "http://localhost:7075"
$queueName = "sbq-d365finops-customer-upserts"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing ServiceBusProxy" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Health Check
Write-Host "Test 1: Health Check" -ForegroundColor Yellow
Write-Host "GET $proxyUrl/health" -ForegroundColor Gray
try {
    $healthResponse = Invoke-RestMethod -Uri "$proxyUrl/health" -Method Get -ErrorAction Stop
    Write-Host "Success: $healthResponse" -ForegroundColor Green
} catch {
    Write-Host "Failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test 2: Retrieve Message from Queue
Write-Host "Test 2: Retrieve Message from Queue" -ForegroundColor Yellow
Write-Host "GET $proxyUrl/apim/servicebus/local/$queueName/messages/head" -ForegroundColor Gray
try {
    $messageResponse = Invoke-RestMethod -Uri "$proxyUrl/apim/servicebus/local/$queueName/messages/head" -Method Get -ErrorAction Stop
    
    if ($messageResponse) {
        Write-Host "Message retrieved!" -ForegroundColor Green
        Write-Host "  MessageId: $($messageResponse.messageId)" -ForegroundColor Green
        Write-Host "  CorrelationId: $($messageResponse.correlationId)" -ForegroundColor Green
        Write-Host "  ContentData length: $($messageResponse.contentData.Length) chars (base64)" -ForegroundColor Green
        Write-Host "  SequenceNumber: $($messageResponse.sequenceNumber)" -ForegroundColor Green
        Write-Host "  DeliveryCount: $($messageResponse.deliveryCount)" -ForegroundColor Green
        Write-Host ""
        Write-Host "Full Response:" -ForegroundColor Cyan
        $messageResponse | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor Gray
    }
} catch {
    if ($_.Exception.Response.StatusCode -eq 204) {
        Write-Host "No messages in queue (204 No Content)" -ForegroundColor Yellow
    } else {
        Write-Host "Message retrieval failed: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails) {
            Write-Host "Error details: $($_.ErrorDetails.Message)" -ForegroundColor Red
        }
    }
}
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check the proxy terminal for detailed logs" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

