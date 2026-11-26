// Outfit Finder: Azure resources (Phase 10).
// az deployment group create -g <rg> -f infra/main.bicep -p infra/main.parameters.json -p mysqlAdminPassword=<secret>
targetScope = 'resourceGroup'

@description('Azure region (decision 7). Use an EU region if serving EU users.')
param location string = resourceGroup().location
@description('Short lowercase prefix for resource names.')
param prefix string = 'outfitfinder'
param appServiceSku string = 'P0v3'
param mysqlAdminLogin string = 'outfitadmin'
@secure()
param mysqlAdminPassword string
@description('Monthly Azure budget in the resource group currency; alerts at 80% and 100%.')
param monthlyBudget int = 300
param alertEmail string = ''
param adminUsername string = ''
param extensionId string = ''
@description('Contact addresses printed in the Privacy Policy and Terms. The website refuses to start without them.')
param privacyEmail string
param abuseEmail string

var tags = { app: 'outfit-finder' }
var identityUrl = 'https://${prefix}-identity.azurewebsites.net'
var apiUrl = 'https://${prefix}-api.azurewebsites.net'
var webUrl = 'https://${prefix}-web.azurewebsites.net'

// ---------- Monitoring ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-insights'
  location: location
  kind: 'web'
  tags: tags
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

// ---------- Data ----------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  #disable-next-line BCP334
  name: take(replace('${prefix}st${uniqueString(resourceGroup().id)}', '-', ''), 24)
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
  resource blob 'blobServices' = {
    name: 'default'
    resource outfits 'containers' = { name: 'outfits', properties: { publicAccess: 'None' } }
  }
}

resource mysql 'Microsoft.DBforMySQL/flexibleServers@2023-12-30' = {
  name: '${prefix}-mysql'
  location: location
  tags: tags
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '8.0.21'
    administratorLogin: mysqlAdminLogin
    administratorLoginPassword: mysqlAdminPassword
    storage: { storageSizeGB: 32, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
  }
  resource db 'databases' = { name: 'outfitfinder', properties: { charset: 'utf8mb4', collation: 'utf8mb4_0900_ai_ci' } }
  resource allowAzure 'firewallRules' = { name: 'AllowAzureServices', properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' } }
}

// ---------- AI safety ----------
resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${prefix}-safety'
  location: location
  tags: tags
  kind: 'ContentSafety'
  sku: { name: 'S0' }
  properties: { customSubDomainName: '${prefix}-safety', publicNetworkAccess: 'Enabled' }
}

// ---------- Secrets ----------
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${prefix}-kv-${uniqueString(resourceGroup().id)}', 24)
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    enablePurgeProtection: true
  }
  resource dbSecret 'secrets' = {
    name: 'ConnectionStrings--OutfitDb'
    properties: { value: 'server=${mysql.properties.fullyQualifiedDomainName};port=3306;database=outfitfinder;user=${mysqlAdminLogin};password=${mysqlAdminPassword};SslMode=Required' }
  }
  resource blobSecret 'secrets' = {
    name: 'Storage--BlobConnection'
    properties: { value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}' }
  }
  resource safetySecret 'secrets' = {
    name: 'Safety--ContentSafetyKey'
    properties: { value: contentSafety.listKeys().key1 }
  }
  // Set by hand (az keyvault secret set): AI--ApiKey, Api--UrlSigningKey, Identity--ClientSecret / Clients--Web--Secret,
  // Turnstile--SecretKey, Smtp--Password, Duende--LicenseKey, Safety--SafeBrowsingApiKey.
}

// ---------- Apps ----------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan'
  location: location
  tags: tags
  sku: { name: appServiceSku }
  kind: 'linux'
  properties: { reserved: true }
}

var kv = '@Microsoft.KeyVault(VaultName=${vault.name};SecretName='
var common = [
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Database__Provider', value: 'MySql' }
  { name: 'ConnectionStrings__OutfitDb', value: '${kv}ConnectionStrings--OutfitDb)' }
]

var appNames = [ 'identity', 'api', 'web' ]
var appSettings = {
  identity: concat(common, [
      { name: 'Clients__Web__Url', value: webUrl }
      { name: 'Clients__Web__Secret', value: '${kv}Clients--Web--Secret)' }
      { name: 'Clients__Extension__Id', value: extensionId }
      { name: 'Admin__Username', value: adminUsername }
      { name: 'Turnstile__SecretKey', value: '${kv}Turnstile--SecretKey)' }
      { name: 'Smtp__Password', value: '${kv}Smtp--Password)' }
      { name: 'Duende__LicenseKey', value: '${kv}Duende--LicenseKey)' }
      { name: 'Database__MigrateOnStartup', value: 'true' }
    ])
  api: concat(common, [
      { name: 'Identity__Authority', value: identityUrl }
      { name: 'Api__PublicBaseUrl', value: apiUrl }
      { name: 'Api__UrlSigningKey', value: '${kv}Api--UrlSigningKey)' }
      { name: 'Storage__Provider', value: 'Azure' }
      { name: 'Storage__BlobConnection', value: '${kv}Storage--BlobConnection)' }
      { name: 'Safety__Mode', value: 'AzureContentSafety' }
      { name: 'Safety__ContentSafetyEndpoint', value: contentSafety.properties.endpoint }
      { name: 'Safety__ContentSafetyKey', value: '${kv}Safety--ContentSafetyKey)' }
      { name: 'Safety__SafeBrowsingApiKey', value: '${kv}Safety--SafeBrowsingApiKey)' }
      { name: 'AI__ApiKey', value: '${kv}AI--ApiKey)' }
      { name: 'Turnstile__SecretKey', value: '${kv}Turnstile--SecretKey)' }
      { name: 'Extension__Id', value: extensionId }
      { name: 'Database__MigrateOnStartup', value: 'true' }
    ])
  web: concat(common, [
      { name: 'Identity__Authority', value: identityUrl }
      { name: 'Identity__ClientSecret', value: '${kv}Clients--Web--Secret)' }
      { name: 'Api__BaseUrl', value: apiUrl }
      { name: 'Extension__Id', value: extensionId }
      { name: 'Legal__PrivacyEmail', value: privacyEmail }
      { name: 'Legal__AbuseEmail', value: abuseEmail }
    ])
}

resource sites 'Microsoft.Web/sites@2023-12-01' = [for a in appNames: {
  name: '${prefix}-${a}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      webSocketsEnabled: a == 'web' // Blazor Server circuits
      appSettings: appSettings[a]
    }
  }
}]

// Apps read Key Vault secrets with their managed identities (Key Vault Secrets User).
resource kvAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (a, i) in appNames: {
  name: guid(vault.id, a, 'kv-secrets-user')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: sites[i].identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

// ---------- Cost alerts ----------
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (!empty(alertEmail)) {
  name: '${prefix}-alerts'
  location: 'global'
  properties: {
    groupShortName: 'outfitalert'
    enabled: true
    emailReceivers: [ { name: 'owner', emailAddress: alertEmail, useCommonAlertSchema: true } ]
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(alertEmail)) {
  name: '${prefix}-budget'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: '2026-12-01' }
    notifications: {
      actual80: { enabled: true, operator: 'GreaterThan', threshold: 80, contactEmails: [ alertEmail ] }
      actual100: { enabled: true, operator: 'GreaterThan', threshold: 100, contactEmails: [ alertEmail ] }
    }
  }
}

// The Api logs "ALERT: daily AI budget reached" at Critical level; this alert fires on it.
resource budgetLogAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = if (!empty(alertEmail)) {
  name: '${prefix}-ai-budget-reached'
  location: location
  properties: {
    severity: 1
    enabled: true
    scopes: [ insights.id ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [ {
        query: 'traces | where severityLevel == 4 and message has "daily AI budget reached"'
        timeAggregation: 'Count'
        operator: 'GreaterThan'
        threshold: 0
      } ]
    }
    actions: { actionGroups: [ actionGroup.id ] }
  }
}

output webUrl string = webUrl
output apiUrl string = apiUrl
output identityUrl string = identityUrl
output keyVault string = vault.name
