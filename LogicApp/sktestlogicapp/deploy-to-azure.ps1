# Azure Logic Apps Standard Deployment Script
# This script deploys the Logic App infrastructure and workflows to Azure

param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$true)]
    [string]$Location,
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "prod",
    
    [Parameter(Mandatory=$true)]
    [securestring]$SqlAdminPassword,
    
    [Parameter(Mandatory=$false)]
    [string]$JwtStubUrl = "",
    
    [Parameter(Mandatory=$false)]
    [string]$ManhattanPublishUrl = ""
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Azure Logic Apps Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Login and set subscription
Write-Host "Setting Azure subscription..." -ForegroundColor Yellow
az account set --subscription $SubscriptionId

# Create resource group if not exists
Write-Host "Creating resource group: $ResourceGroupName..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location --output none

# Deploy infrastructure
Write-Host "Deploying infrastructure (Bicep)..." -ForegroundColor Yellow
Write-Host "This may take 5-10 minutes..." -ForegroundColor Gray

$sqlAdminPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword))

$deploymentOutput = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file infrastructure.bicep `
    --parameters environment=$Environment `
                 location=$Location `
                 sqlAdminLogin="sqladmin" `
                 sqlAdminPassword="$sqlAdminPasswordPlain" `
                 jwtStubUrl="$JwtStubUrl" `
                 manhattanPublishUrl="$ManhattanPublishUrl" `
    --output json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Error "Infrastructure deployment failed!"
    exit 1
}

Write-Host "Infrastructure deployed successfully!" -ForegroundColor Green

# Extract outputs
$outputs = $deploymentOutput.properties.outputs
$logicAppName = $outputs.logicAppName.value
$sqlServerFqdn = $outputs.sqlServerFqdn.value
$sqlDatabaseName = $outputs.sqlDatabaseName.value
$sqlConnectionId = $outputs.sqlConnectionId.value
$serviceBusConnectionId = $outputs.serviceBusConnectionId.value

Write-Host ""
Write-Host "Deployed Resources:" -ForegroundColor Cyan
Write-Host "  Logic App: $logicAppName" -ForegroundColor White
Write-Host "  SQL Server: $sqlServerFqdn" -ForegroundColor White
Write-Host "  SQL Database: $sqlDatabaseName" -ForegroundColor White
Write-Host ""

# Deploy database schema
Write-Host "Deploying database schema..." -ForegroundColor Yellow
Write-Host "NOTE: You may need to add your IP to SQL firewall rules" -ForegroundColor Gray
Write-Host "      Run: az sql server firewall-rule create \" -ForegroundColor Gray
Write-Host "           --resource-group $ResourceGroupName \" -ForegroundColor Gray
Write-Host "           --server <sql-server-name> \" -ForegroundColor Gray
Write-Host "           --name AllowMyIP \" -ForegroundColor Gray
Write-Host "           --start-ip-address <your-ip> --end-ip-address <your-ip>" -ForegroundColor Gray
Write-Host ""
Write-Host "Press Enter after adding firewall rule to continue..." -ForegroundColor Yellow
Read-Host

# Deploy schema using sqlcmd or prompt user
$schemaPath = "..\..\..\database\schema\InboxOutbox.sql"
if (Test-Path $schemaPath) {
    Write-Host "Found schema file: $schemaPath" -ForegroundColor Green
    Write-Host "Deploying to: $sqlServerFqdn / $sqlDatabaseName" -ForegroundColor White
    
    # Check if sqlcmd is available
    $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($sqlcmd) {
        sqlcmd -S $sqlServerFqdn `
               -d $sqlDatabaseName `
               -U sqladmin `
               -P "$sqlAdminPasswordPlain" `
               -i $schemaPath
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Database schema deployed successfully!" -ForegroundColor Green
        } else {
            Write-Warning "Database schema deployment failed. Please deploy manually."
        }
    } else {
        Write-Warning "sqlcmd not found. Please deploy schema manually using:"
        Write-Host "  Azure Data Studio, SSMS, or sqlcmd -S $sqlServerFqdn -d $sqlDatabaseName -U sqladmin -i $schemaPath" -ForegroundColor Yellow
    }
} else {
    Write-Warning "Schema file not found at: $schemaPath"
    Write-Host "Please deploy manually." -ForegroundColor Yellow
}

# Update connections.json with actual values
Write-Host ""
Write-Host "Updating connections.json with deployed resource IDs..." -ForegroundColor Yellow

$connectionsFile = "connections.json"
$connectionsContent = Get-Content $connectionsFile -Raw | ConvertFrom-Json

# Get connection runtime URLs
$sqlConnectionDetails = az resource show --ids $sqlConnectionId --output json | ConvertFrom-Json
$serviceBusConnectionDetails = az resource show --ids $serviceBusConnectionId --output json | ConvertFrom-Json

# Update SQL connection
$connectionsContent.managedApiConnections.sql.api.id = "/subscriptions/$SubscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/sql"
$connectionsContent.managedApiConnections.sql.connection.id = $sqlConnectionId
$connectionsContent.managedApiConnections.sql.connectionRuntimeUrl = $sqlConnectionDetails.properties.connectionRuntimeUrl

# Update Service Bus connection
$connectionsContent.managedApiConnections.servicebus.api.id = "/subscriptions/$SubscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/servicebus"
$connectionsContent.managedApiConnections.servicebus.connection.id = $serviceBusConnectionId
$connectionsContent.managedApiConnections.servicebus.connectionRuntimeUrl = $serviceBusConnectionDetails.properties.connectionRuntimeUrl

# Save updated connections.json
$connectionsContent | ConvertTo-Json -Depth 10 | Set-Content $connectionsFile

Write-Host "connections.json updated successfully!" -ForegroundColor Green

# Authorize API connections
Write-Host ""
Write-Host "Authorizing API connections..." -ForegroundColor Yellow
Write-Host "NOTE: You may need to authorize connections manually in Azure Portal" -ForegroundColor Gray
Write-Host "      Navigate to: Resource Group > API Connections > Authorize" -ForegroundColor Gray
Write-Host ""

# Deploy workflows
Write-Host "Deploying workflows to Logic App..." -ForegroundColor Yellow

# Create zip file
$zipFile = "workflows.zip"
if (Test-Path $zipFile) {
    Remove-Item $zipFile -Force
}

# Zip all workflow files
$filesToZip = @(
    "connections.json",
    "parameters.json",
    "local.settings.json",
    "host.json",
    "package.json",
    "process-message",
    "outbox-processor",
    "message-status",
    "error-handler",
    "Artifacts"
)

Compress-Archive -Path $filesToZip -DestinationPath $zipFile -Force

Write-Host "Uploading workflows..." -ForegroundColor Yellow

az logicapp deployment source config-zip `
    --resource-group $ResourceGroupName `
    --name $logicAppName `
    --src $zipFile `
    --output none

if ($LASTEXITCODE -eq 0) {
    Write-Host "Workflows deployed successfully!" -ForegroundColor Green
} else {
    Write-Error "Workflow deployment failed!"
    exit 1
}

# Clean up zip file
Remove-Item $zipFile -Force

# Get Logic App URL
$logicAppUrl = $outputs.logicAppUrl.value

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Logic App URL: $logicAppUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Authorize API connections in Azure Portal" -ForegroundColor White
Write-Host "2. Test workflows using the endpoints:" -ForegroundColor White
Write-Host "   - POST $logicAppUrl/api/process-message/triggers/manual/invoke" -ForegroundColor Gray
Write-Host "   - GET  $logicAppUrl/api/message-status/triggers/manual/invoke?messageId=..." -ForegroundColor Gray
Write-Host "3. Monitor with Application Insights" -ForegroundColor White
Write-Host ""
Write-Host "To get workflow URLs with SAS tokens, run:" -ForegroundColor Yellow
Write-Host "  az logicapp show --resource-group $ResourceGroupName --name $logicAppName" -ForegroundColor Gray
Write-Host ""
