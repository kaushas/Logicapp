// Azure Logic Apps Standard Infrastructure
// Deploy with: az deployment group create --resource-group rg-logicapps-prod --template-file infrastructure.bicep --parameters environment=prod

@description('Environment name (dev, test, prod)')
param environment string = 'prod'

@description('Azure region for resources')
param location string = resourceGroup().location

@description('SQL Server administrator login')
@secure()
param sqlAdminLogin string

@description('SQL Server administrator password')
@secure()
param sqlAdminPassword string

@description('Service Bus connection string (if using existing namespace)')
@secure()
param serviceBusConnectionString string = ''

@description('JWT authentication service URL')
param jwtStubUrl string = ''

@description('Manhattan publisher URL')
param manhattanPublishUrl string = ''

// Naming convention
var resourcePrefix = 'logicapp-processing'
var resourceSuffix = '${environment}-${uniqueString(resourceGroup().id)}'

// Resource names
var logicAppName = '${resourcePrefix}-${environment}'
var storageAccountName = replace('st${resourcePrefix}${environment}', '-', '')
var appInsightsName = 'ai-${resourcePrefix}-${environment}'
var sqlServerName = '${resourcePrefix}-sql-${resourceSuffix}'
var sqlDatabaseName = 'ProcessingDb'
var serviceBusNamespaceName = '${resourcePrefix}-sb-${environment}'
var appServicePlanName = 'asp-${resourcePrefix}-${environment}'

// Storage Account for Logic App runtime
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: take(storageAccountName, 24)
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    RetentionInDays: 90
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// App Service Plan (for Logic App Standard)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'WS1'
    tier: 'WorkflowStandard'
  }
  properties: {
    reserved: false
  }
}

// Logic App (Standard)
resource logicApp 'Microsoft.Web/sites@2023-01-01' = {
  name: logicAppName
  location: location
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(logicAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'node'
        }
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~18'
        }
        {
          name: 'AzureFunctionsJobHost__extensionBundle__id'
          value: 'Microsoft.Azure.Functions.ExtensionBundle.Workflows'
        }
        {
          name: 'AzureFunctionsJobHost__extensionBundle__version'
          value: '[1.*, 2.0.0)'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'SQL_CONNECTION_STRING'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;'
        }
        {
          name: 'SERVICEBUS_CONNECTION_STRING'
          value: empty(serviceBusConnectionString) ? listKeys(serviceBusNamespace::authRule.id, serviceBusNamespace.apiVersion).primaryConnectionString : serviceBusConnectionString
        }
        {
          name: 'JWT_STUB_URL'
          value: jwtStubUrl
        }
        {
          name: 'MANHATTAN_PUBLISH_URL'
          value: manhattanPublishUrl
        }
      ]
      use32BitWorkerProcess: false
      netFrameworkVersion: 'v6.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

// SQL Server
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// SQL Database
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'S0'
    tier: 'Standard'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 268435456000
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    readScale: 'Disabled'
  }
}

// SQL Firewall - Allow Azure Services
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Azure AD Admin for SQL (using Logic App identity)
resource sqlAdAdmin 'Microsoft.Sql/servers/administrators@2023-05-01-preview' = {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: logicAppName
    sid: logicApp.identity.principalId
    tenantId: subscription().tenantId
  }
}

// Service Bus Namespace
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespaceName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }

  // Authorization Rule
  resource authRule 'AuthorizationRules@2022-10-01-preview' = {
    name: 'RootManageSharedAccessKey'
    properties: {
      rights: ['Listen', 'Send', 'Manage']
    }
  }
}

// Service Bus Topics
resource canonicalEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'canonical-events'
  properties: {
    maxSizeInMegabytes: 1024
    defaultMessageTimeToLive: 'P14D'
    requiresDuplicateDetection: false
    enableBatchedOperations: true
  }
}

resource deadLetterEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'dead-letter-events'
  properties: {
    maxSizeInMegabytes: 1024
    defaultMessageTimeToLive: 'P14D'
    requiresDuplicateDetection: false
    enableBatchedOperations: true
  }
}

// API Connection - SQL
resource sqlConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'sql'
  location: location
  properties: {
    displayName: 'SQL Connection'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'sql')
    }
    parameterValueType: 'Alternative'
    alternativeParameterValues: {
      server: sqlServer.properties.fullyQualifiedDomainName
      database: sqlDatabaseName
    }
  }
}

// API Connection - Service Bus
resource serviceBusConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'servicebus'
  location: location
  properties: {
    displayName: 'Service Bus Connection'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'servicebus')
    }
    parameterValues: {
      connectionString: listKeys(serviceBusNamespace::authRule.id, serviceBusNamespace.apiVersion).primaryConnectionString
    }
  }
}

// Role Assignment - Service Bus Data Sender
resource serviceBusRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, logicApp.id, 'ServiceBusDataSender')
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39') // Azure Service Bus Data Sender
    principalId: logicApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Outputs
output logicAppName string = logicApp.name
output logicAppUrl string = 'https://${logicApp.properties.defaultHostName}'
output logicAppPrincipalId string = logicApp.identity.principalId
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output serviceBusNamespace string = serviceBusNamespace.name
output storageAccountName string = storageAccount.name
output appInsightsName string = appInsights.name
output sqlConnectionId string = sqlConnection.id
output serviceBusConnectionId string = serviceBusConnection.id
