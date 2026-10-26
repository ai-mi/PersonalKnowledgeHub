targetScope = 'resourceGroup'

@description('Префикс имён ресурсов.')
param namePrefix string = 'kh'
param location string = resourceGroup().location

@description('Entra-администратор PostgreSQL (владелец подписки или группа). Не identity Web App.')
param adminPrincipalId string
param adminPrincipalName string
@allowed(['User', 'Group', 'ServicePrincipal'])
param adminPrincipalType string = 'User'

var suffix = uniqueString(resourceGroup().id)
var webAppName = '${namePrefix}-${suffix}'
var keyVaultName = '${namePrefix}-kv-${suffix}'
var databaseName = 'knowledgehub'
var tenantId = subscription().tenantId

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    name: '${namePrefix}-${suffix}'
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    location: location
    name: '${namePrefix}-pg-${suffix}'
    databaseName: databaseName
    tenantId: tenantId
    adminPrincipalId: adminPrincipalId
    adminPrincipalName: adminPrincipalName
    adminPrincipalType: adminPrincipalType
  }
}

module appService 'modules/app-service.bicep' = {
  name: 'app-service'
  params: {
    location: location
    name: webAppName
    planName: '${namePrefix}-plan-${suffix}'
    appInsightsConnectionString: monitoring.outputs.connectionString
    postgresFqdn: postgres.outputs.fqdn
    databaseName: databaseName
    keyVaultName: keyVaultName
  }
}

module postgresFirewall 'modules/postgres-firewall.bicep' = {
  name: 'postgres-firewall'
  params: {
    serverName: postgres.outputs.name
    // union ??????? ?????????: ? possibleOutboundIpAddresses ???? IP ????? ???????????.
    ipAddresses: union(split(appService.outputs.possibleOutboundIpAddresses, ','), [])
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: 'st${suffix}'
    webAppPrincipalId: appService.outputs.principalId
  }
}

module communication 'modules/communication.bicep' = {
  name: 'communication'
  params: {
    name: '${namePrefix}-${suffix}'
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    location: location
    name: keyVaultName
    tenantId: tenantId
    webAppPrincipalId: appService.outputs.principalId
    acsConnectionString: communication.outputs.connectionString
  }
}

output webAppName string = appService.outputs.name
output webAppHostName string = appService.outputs.defaultHostName
output postgresServer string = postgres.outputs.fqdn
output keyVaultName string = keyVault.outputs.name
output storageAccount string = storage.outputs.name
output emailSenderDomain string = communication.outputs.senderDomain