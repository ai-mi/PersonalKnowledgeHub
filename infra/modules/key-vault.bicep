param location string
param name string
param tenantId string
param webAppPrincipalId string

@secure()
param acsConnectionString string

var keyVaultSecretsUser = '4633458b-17de-406a-b4a8-4b14d3b3c1c6'

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: name
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Остальные секреты (Telegram, Google, Microsoft) заводятся вручную — см. infra/README.md.
resource acsSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'AcsConnectionString'
  properties: {
    value: acsConnectionString
  }
}

resource secretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, webAppPrincipalId, keyVaultSecretsUser)
  properties: {
    principalId: webAppPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
  }
}

output name string = vault.name