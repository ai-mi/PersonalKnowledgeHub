param location string
param name string
param databaseName string
param tenantId string

@description('Entra-администратор сервера: владелец подписки или группа. НЕ managed identity Web App (она не должна входить в azure_pg_admin и обходить RLS).')
param adminPrincipalId string
param adminPrincipalName string
@allowed(['User', 'Group', 'ServicePrincipal'])
param adminPrincipalType string

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  name: name
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '18'
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: tenantId
    }
  }
}

// Операции над одним сервером выполняются строго по очереди (иначе ServerIsBusy).
resource admin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2025-08-01' = {
  parent: server
  name: adminPrincipalId
  properties: {
    principalType: adminPrincipalType
    principalName: adminPrincipalName
    tenantId: tenantId
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2025-08-01' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
  dependsOn: [admin]
}

output name string = server.name
output fqdn string = server.properties.fullyQualifiedDomainName