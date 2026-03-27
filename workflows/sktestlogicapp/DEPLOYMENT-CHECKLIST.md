# Azure Deployment Checklist

## Pre-Deployment

### 1. Azure Prerequisites
- [ ] Azure subscription with Owner or Contributor + User Access Administrator roles
- [ ] Azure CLI installed and updated (`az --version`)
- [ ] Logged into Azure CLI (`az login`)
- [ ] Subscription set (`az account set --subscription <id>`)

### 2. Required Information
- [ ] Subscription ID: `_______________________________________`
- [ ] Resource Group Name: `_______________________________________`
- [ ] Location (e.g., eastus): `_______________________________________`
- [ ] Environment (dev/test/prod): `_______________________________________`
- [ ] SQL Admin Password (secure): `_______________________________________`

### 3. External Dependencies
- [ ] JWT authentication service URL (if applicable)
- [ ] Manhattan publisher URL (if applicable)
- [ ] Service Bus namespace (if using existing)

### 4. Local Files Ready
- [ ] `infrastructure.bicep` - IaC template
- [ ] `infrastructure.parameters.json` - Parameters file
- [ ] `deploy-to-azure.ps1` - Deployment script
- [ ] `connections.json` - Updated with Azure placeholders
- [ ] `parameters.json` - Workflow parameters
- [ ] All 4 workflow folders (process-message, outbox-processor, message-status, error-handler)
- [ ] `Artifacts/Maps/*.liquid` - Transformation templates
- [ ] Database schema SQL file

## Deployment Steps

### Option A: Automated Deployment (Recommended)

```powershell
# 1. Navigate to workflow directory
cd LogicApp/sktestlogicapp

# 2. Run deployment script
.\deploy-to-azure.ps1 `
  -SubscriptionId "your-subscription-id" `
  -ResourceGroupName "rg-logicapps-prod" `
  -Location "eastus" `
  -Environment "prod" `
  -SqlAdminPassword (ConvertTo-SecureString "YourPassword123!" -AsPlainText -Force) `
  -JwtStubUrl "https://your-jwt-service.azurewebsites.net/api/generate-token" `
  -ManhattanPublishUrl "https://your-publisher-url"
```

- [ ] Script executed successfully
- [ ] All resources created
- [ ] Database schema deployed
- [ ] Workflows deployed

### Option B: Manual Deployment

#### Step 1: Deploy Infrastructure
```bash
az deployment group create \
  --resource-group rg-logicapps-prod \
  --template-file infrastructure.bicep \
  --parameters @infrastructure.parameters.json
```

- [ ] Logic App created
- [ ] SQL Server and Database created
- [ ] Service Bus namespace created
- [ ] API connections created
- [ ] Storage Account created
- [ ] Application Insights created

#### Step 2: Configure SQL Firewall
```bash
# Add your IP
az sql server firewall-rule create \
  --resource-group rg-logicapps-prod \
  --server <sql-server-name> \
  --name AllowMyIP \
  --start-ip-address <your-ip> \
  --end-ip-address <your-ip>
```

- [ ] Firewall rule created
- [ ] Can connect to SQL Server

#### Step 3: Deploy Database Schema
```bash
sqlcmd -S <server>.database.windows.net \
  -d ProcessingDb \
  -U sqladmin \
  -i ../../../database/schema/InboxOutbox.sql
```

- [ ] Inbox table created
- [ ] Outbox table created
- [ ] Stored procedures created

#### Step 4: Update connections.json

Get deployed resource IDs:
```bash
# SQL Connection ID
az resource show --ids <sql-connection-id>

# Service Bus Connection ID
az resource show --ids <servicebus-connection-id>
```

Update `connections.json`:
- [ ] SQL connection ID updated
- [ ] SQL connectionRuntimeUrl updated
- [ ] Service Bus connection ID updated
- [ ] Service Bus connectionRuntimeUrl updated

#### Step 5: Deploy Workflows

Using VS Code:
- [ ] Open Logic App workspace
- [ ] Right-click on workflow folder
- [ ] Select "Deploy to Logic App"
- [ ] Choose subscription and Logic App
- [ ] Confirm deployment

Or using ZIP:
```bash
zip -r workflows.zip *
az logicapp deployment source config-zip \
  --resource-group rg-logicapps-prod \
  --name <logic-app-name> \
  --src workflows.zip
```

- [ ] Workflows deployed
- [ ] No deployment errors

## Post-Deployment

### 1. Authorize API Connections

Azure Portal method:
- [ ] Navigate to Resource Group > API Connections
- [ ] Click on "sql" connection
- [ ] Click "Edit API connection"
- [ ] Click "Authorize" and sign in
- [ ] Save
- [ ] Repeat for "servicebus" connection

### 2. Verify Workflows

- [ ] Navigate to Logic App > Workflows in Azure Portal
- [ ] Verify all 4 workflows are listed:
  - [ ] process-message
  - [ ] outbox-processor
  - [ ] message-status
  - [ ] error-handler
- [ ] Check workflow status is "Enabled"
- [ ] No validation errors shown

### 3. Configure Application Settings

```bash
az logicapp config appsettings set \
  --name <logic-app-name> \
  --resource-group rg-logicapps-prod \
  --settings \
    JWT_STUB_URL="https://..." \
    MANHATTAN_PUBLISH_URL="https://..."
```

- [ ] JWT_STUB_URL configured
- [ ] MANHATTAN_PUBLISH_URL configured
- [ ] SQL_CONNECTION_STRING present (auto-configured)
- [ ] SERVICEBUS_CONNECTION_STRING present (auto-configured)
- [ ] APPINSIGHTS_INSTRUMENTATIONKEY present (auto-configured)

### 4. Test Workflows

Get workflow callback URLs:
```bash
az logicapp show \
  --name <logic-app-name> \
  --resource-group rg-logicapps-prod \
  --query defaultHostName -o tsv
```

Test process-message:
```bash
curl -X POST "https://<logic-app-url>/api/process-message/triggers/manual/invoke?..." \
  -H "Content-Type: application/json" \
  -H "x-source-topic: customer" \
  -d '{"customerId":"CUST001","name":"Test","email":"test@example.com"}'
```

- [ ] HTTP 202 Accepted received
- [ ] Response contains `messageId`
- [ ] Response contains `statusUrl`

Test message-status:
```bash
curl "https://<logic-app-url>/api/message-status/triggers/manual/invoke?...&messageId=<message-id>"
```

- [ ] HTTP 200 OK received
- [ ] Status returned (Received/Processing/Sent)

### 5. Verify Database

Connect to SQL and query:
```sql
-- Check Inbox
SELECT TOP 10 * FROM [dbo].[Inbox] ORDER BY ReceivedAt DESC;

-- Check Outbox
SELECT TOP 10 * FROM [dbo].[Outbox] ORDER BY CreatedAt DESC;
```

- [ ] Test message appears in Inbox
- [ ] Test message appears in Outbox
- [ ] Outbox message marked as Sent after processing

### 6. Verify Service Bus

```bash
# List topics
az servicebus topic list \
  --resource-group rg-logicapps-prod \
  --namespace-name <namespace-name>
```

- [ ] canonical-events topic exists
- [ ] dead-letter-events topic exists
- [ ] Messages are being published

### 7. Monitor with Application Insights

```bash
az monitor app-insights query \
  --app ai-logicapps-prod \
  --resource-group rg-logicapps-prod \
  --analytics-query "traces | where timestamp > ago(1h) | take 20"
```

- [ ] Logs are being captured
- [ ] Correlation IDs present
- [ ] No unexpected errors

## Validation

### Functional Tests

- [ ] **Customer Event**: POST with x-source-topic: customer
- [ ] **Vendor Event**: POST with x-source-topic: vendor
- [ ] **Customer Address Event**: POST with x-source-topic: customerAddress
- [ ] **Vendor Address Event**: POST with x-source-topic: vendorAddress
- [ ] **Status Query**: GET with messageId parameter
- [ ] **Idempotency**: Duplicate message rejected (same MessageId)
- [ ] **Retry Logic**: Failed message retried with exponential backoff
- [ ] **Dead Letter**: Message with 5+ failures moved to dead-letter queue

### Performance Tests

- [ ] Response time < 2 seconds for process-message
- [ ] Response time < 500ms for message-status
- [ ] Outbox processor runs every 1 minute
- [ ] Error handler runs every 5 minutes

### Security Checks

- [ ] HTTPS only enabled on Logic App
- [ ] SQL authentication uses Managed Identity
- [ ] Service Bus authentication uses Managed Identity
- [ ] Firewall rules restrict SQL access
- [ ] API connections authorized
- [ ] No secrets in application settings (use Key Vault if needed)

## Troubleshooting

### Workflow Validation Errors

Problem: Workflows fail to load with "API connection reference invalid"

Solution:
- [ ] Verify API connections exist in resource group
- [ ] Check connections are authorized
- [ ] Validate connection IDs in connections.json match deployed resources
- [ ] Ensure connectionRuntimeUrl is set correctly

### SQL Connection Failures

Problem: Actions fail with "Cannot connect to SQL Server"

Solution:
- [ ] Check firewall rules allow Azure services
- [ ] Verify Managed Identity has permissions on database
- [ ] Test connection string manually
- [ ] Check SQL Server is online

### Service Bus Send Errors

Problem: Messages fail to publish to Service Bus

Solution:
- [ ] Verify topics exist (canonical-events, dead-letter-events)
- [ ] Check Managed Identity has "Azure Service Bus Data Sender" role
- [ ] Validate namespace and topic names
- [ ] Test connection string

### Missing Logs in Application Insights

Problem: No traces or logs appearing

Solution:
- [ ] Verify APPINSIGHTS_INSTRUMENTATIONKEY is set
- [ ] Check Application Insights resource is created
- [ ] Wait 5-10 minutes for data ingestion
- [ ] Check LogicApp > Diagnostic Settings

## Rollback Plan

If deployment fails or issues are critical:

1. **Disable Workflows**:
```bash
az logicapp stop --name <logic-app-name> --resource-group rg-logicapps-prod
```

2. **Restore Previous Version** (if redeployment):
   - Use previous deployment package
   - Redeploy workflows

3. **Delete Resources** (if new deployment):
```bash
az group delete --name rg-logicapps-prod --yes --no-wait
```

## Sign-Off

Deployment completed by: `_______________________________________`

Date: `_______________________________________`

Validated by: `_______________________________________`

Date: `_______________________________________`

Production approved by: `_______________________________________`

Date: `_______________________________________`
