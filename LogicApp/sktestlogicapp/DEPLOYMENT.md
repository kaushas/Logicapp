# Azure Logic Apps Standard Deployment Guide

## Prerequisites

1. **Azure Subscription** with appropriate permissions
2. **Azure CLI** installed and authenticated
3. **VS Code** with Azure Logic Apps extension
4. **SQL Database** in Azure (or Azure SQL Managed Instance)
5. **Azure Service Bus** namespace

## Required Azure Resources

### 1. Core Resources
- **Resource Group**: `rg-logicapps-prod`
- **Logic App (Standard)**: Plan type - Workflow Standard (WS1, WS2, or WS3)
- **Storage Account**: For Logic Apps runtime state
- **Application Insights**: For monitoring and logging

### 2. Integration Resources
- **Azure SQL Database**: For Inbox/Outbox tables
- **Azure Service Bus**: Namespace with topics/queues

### 3. API Connections
- **SQL Server Connection**: Managed API connection for SQL actions
- **Service Bus Connection**: Managed API connection for Service Bus actions

## Deployment Steps

### Step 1: Create Azure Resources

```bash
# Variables
RESOURCE_GROUP="rg-logicapps-prod"
LOCATION="eastus"
LOGIC_APP_NAME="logic-app-processing"
STORAGE_ACCOUNT="stlogicappprod"
APP_INSIGHTS_NAME="ai-logicapps-prod"
SQL_SERVER_NAME="sql-logicapps-prod"
SQL_DATABASE_NAME="ProcessingDb"
SERVICEBUS_NAMESPACE="sb-logicapps-prod"

# Create Resource Group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Storage Account
az storage account create \
  --name $STORAGE_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --sku Standard_LRS

# Create Application Insights
az monitor app-insights component create \
  --app $APP_INSIGHTS_NAME \
  --location $LOCATION \
  --resource-group $RESOURCE_GROUP

# Create Logic App (Standard)
az logicapp create \
  --resource-group $RESOURCE_GROUP \
  --name $LOGIC_APP_NAME \
  --storage-account $STORAGE_ACCOUNT \
  --app-insights $APP_INSIGHTS_NAME

# Create SQL Server & Database
az sql server create \
  --name $SQL_SERVER_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --admin-user sqladmin \
  --admin-password 'YourSecurePassword123!'

az sql db create \
  --resource-group $RESOURCE_GROUP \
  --server $SQL_SERVER_NAME \
  --name $SQL_DATABASE_NAME \
  --service-objective S0

# Create Service Bus Namespace
az servicebus namespace create \
  --name $SERVICEBUS_NAMESPACE \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --sku Standard

# Create Service Bus Topics
az servicebus topic create \
  --resource-group $RESOURCE_GROUP \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --name canonical-events

az servicebus topic create \
  --resource-group $RESOURCE_GROUP \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --name dead-letter-events
```

### Step 2: Configure API Connections

Create API connections through Azure Portal or Azure CLI:

#### SQL Connection

```bash
# Get SQL connection string
SQL_CONN_STRING=$(az sql db show-connection-string \
  --client ado.net \
  --server $SQL_SERVER_NAME \
  --name $SQL_DATABASE_NAME)

# Create SQL API Connection (do this in Azure Portal)
# Navigate to: Azure Portal > API Connections > + Add
# Select "SQL Server"
# Name: "sql"
# Authentication Type: SQL Server Authentication
# Connection String: Use from above
```

#### Service Bus Connection

```bash
# Get Service Bus connection string
az servicebus namespace authorization-rule keys list \
  --resource-group $RESOURCE_GROUP \
  --namespace-name $SERVICEBUS_NAMESPACE \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv

# Create Service Bus API Connection (do this in Azure Portal)
# Navigate to: Azure Portal > API Connections > + Add
# Select "Azure Service Bus"
# Name: "servicebus"
# Authentication Type: Access Key
# Connection String: Use from above
```

### Step 3: Update connections.json

After creating API connections, update `connections.json` with actual Azure resource IDs:

```json
{
  "managedApiConnections": {
    "sql": {
      "api": {
        "id": "/subscriptions/{subscription-id}/providers/Microsoft.Web/locations/{location}/managedApis/sql"
      },
      "connection": {
        "id": "/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.Web/connections/sql"
      },
      "connectionRuntimeUrl": "https://logic-apis-{location}.azure-apim.net/apim/sql/{connection-id}",
      "authentication": {
        "type": "ManagedServiceIdentity"
      }
    },
    "servicebus": {
      "api": {
        "id": "/subscriptions/{subscription-id}/providers/Microsoft.Web/locations/{location}/managedApis/servicebus"
      },
      "connection": {
        "id": "/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.Web/connections/servicebus"
      },
      "connectionRuntimeUrl": "https://logic-apis-{location}.azure-apim.net/apim/servicebus/{connection-id}",
      "authentication": {
        "type": "ManagedServiceIdentity"
      }
    }
  }
}
```

### Step 4: Configure Application Settings

```bash
# Get Application Insights instrumentation key
AI_KEY=$(az monitor app-insights component show \
  --app $APP_INSIGHTS_NAME \
  --resource-group $RESOURCE_GROUP \
  --query instrumentationKey -o tsv)

# Configure Logic App settings
az logicapp config appsettings set \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    "APPINSIGHTS_INSTRUMENTATIONKEY=$AI_KEY" \
    "JWT_STUB_URL=https://your-jwt-service.azurewebsites.net/api/generate-token" \
    "MANHATTAN_PUBLISH_URL=https://pubsub.googleapis.com/v1/projects/batf-masc-prod-01-ops/topics/HST_XNT_Facility_GCPQ:publish"
```

### Step 5: Deploy Database Schema

Run the database schema script against Azure SQL:

```bash
# Using sqlcmd
sqlcmd -S $SQL_SERVER_NAME.database.windows.net \
  -d $SQL_DATABASE_NAME \
  -U sqladmin \
  -P 'YourSecurePassword123!' \
  -i ../../../database/schema/InboxOutbox.sql

# Or use Azure Data Studio / SSMS
```

### Step 6: Deploy Workflows

Using VS Code:

1. Open Logic App workspace in VS Code
2. Right-click on `LogicApp/sktestlogicapp` folder
3. Select **Deploy to Logic App**
4. Choose your Azure subscription
5. Select the Logic App: `logic-app-processing`
6. Confirm deployment

Or using Azure CLI:

```bash
# Zip the workflow folder
cd LogicApp/sktestlogicapp
zip -r workflows.zip *

# Deploy
az logicapp deployment source config-zip \
  --resource-group $RESOURCE_GROUP \
  --name $LOGIC_APP_NAME \
  --src workflows.zip
```

### Step 7: Enable Managed Identity (Recommended)

```bash
# Enable system-assigned managed identity
az logicapp identity assign \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP

# Get the principal ID
PRINCIPAL_ID=$(az logicapp identity show \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query principalId -o tsv)

# Grant SQL permissions
az sql server ad-admin create \
  --resource-group $RESOURCE_GROUP \
  --server-name $SQL_SERVER_NAME \
  --display-name $LOGIC_APP_NAME \
  --object-id $PRINCIPAL_ID

# Grant Service Bus permissions
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Azure Service Bus Data Sender" \
  --scope "/subscriptions/{subscription-id}/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.ServiceBus/namespaces/$SERVICEBUS_NAMESPACE"
```

## Post-Deployment Verification

### 1. Check Workflows Status

Navigate to Azure Portal > Logic App > Workflows:
- ✅ process-message (HTTP trigger)
- ✅ outbox-processor (Recurrence trigger)
- ✅ message-status (HTTP trigger)
- ✅ error-handler (Recurrence trigger)

### 2. Test HTTP Endpoints

```bash
# Get Logic App URL
LOGIC_APP_URL=$(az logicapp show \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query defaultHostName -o tsv)

# Test process-message endpoint
curl -X POST "https://$LOGIC_APP_URL/api/process-message/triggers/manual/invoke?api-version=2020-05-01-preview&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=YOUR_SIG" \
  -H "Content-Type: application/json" \
  -H "x-source-topic: customer" \
  -d '{
    "customerId": "CUST001",
    "name": "John Doe",
    "email": "john@example.com"
  }'

# Check status
curl "https://$LOGIC_APP_URL/api/message-status/triggers/manual/invoke?api-version=2020-05-01-preview&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=YOUR_SIG&messageId=MESSAGE_ID"
```

### 3. Monitor with Application Insights

```bash
# View logs
az monitor app-insights query \
  --app $APP_INSIGHTS_NAME \
  --resource-group $RESOURCE_GROUP \
  --analytics-query "traces | where message contains 'Correlation' | take 10"
```

## Monitoring & Operations

### Application Insights Queries

```kusto
// Correlation ID tracking
traces
| where message contains "Correlation_ID"
| project timestamp, message, operation_Name
| order by timestamp desc

// Error tracking
traces
| where severityLevel >= 3
| project timestamp, message, operation_Name, severityLevel
| order by timestamp desc

// Performance metrics
requests
| summarize avg(duration), percentile(duration, 95) by name
```

### Scaling Configuration

```bash
# Scale out to 3 instances
az logicapp update \
  --name $LOGIC_APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --set properties.siteConfig.numberOfWorkers=3
```

## Troubleshooting

### Common Issues

1. **Connection Validation Failed**
   - Verify API connections exist and are authorized
   - Check Managed Identity permissions
   - Ensure connection IDs match in connections.json

2. **SQL Timeout Errors**
   - Increase retry policy intervals
   - Check SQL firewall rules allow Azure services
   - Verify connection string has correct credentials

3. **Service Bus Send Failures**
   - Verify topic exists: `canonical-events`, `dead-letter-events`
   - Check Service Bus namespace policy has Send permissions
   - Ensure connection string is valid

## Security Best Practices

1. **Use Managed Identity** instead of connection strings
2. **Enable HTTPS only** for Logic App
3. **Restrict network access** using VNet integration
4. **Store secrets** in Azure Key Vault
5. **Enable diagnostic logs** for audit trail
6. **Use separate environments** (dev/test/prod)

## Cost Optimization

- **Plan Selection**: Start with WS1, scale to WS2/WS3 as needed
- **Recurrence Triggers**: Set appropriate intervals (don't poll too frequently)
- **Connection Pooling**: Reuse API connections across workflows
- **Storage**: Use Standard_LRS for non-critical workloads

## Support & Resources

- [Azure Logic Apps Documentation](https://docs.microsoft.com/azure/logic-apps/)
- [Workflow Actions Reference](https://docs.microsoft.com/azure/logic-apps/logic-apps-workflow-actions-triggers)
- [Best Practices Guide](./BEST_PRACTICES_IMPLEMENTATION.md)
