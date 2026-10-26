param location string
param name string
param planName string
param appInsightsConnectionString string
param postgresFqdn string
param databaseName string
param keyVaultName string
// Имя известно заранее (задаётся в main.bicep), поэтому зависимости app-service -> storage (и цикла с ролью) нет.
param storageAccountName string

// Ссылка на секрет Key Vault без версии — App Service подхватывает последнюю версию.
func kvReference(vaultName string, secretName string) string =>
  '@Microsoft.KeyVault(SecretUri=https://${vaultName}${environment().suffixes.keyvaultDns}/secrets/${secretName})'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        // Пользователь БД = имя managed identity (имя Web App), пароля нет — токен Entra.
        { name: 'Database__ConnectionString', value: 'Host=${postgresFqdn};Database=${databaseName};Username=${name};SSL Mode=Require' }
        { name: 'Database__UseEntraAuth', value: 'true' }
        { name: 'Database__MigrateOnStartup', value: 'true' }
        { name: 'Storage__AccountUri', value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}' }
        { name: 'Telegram__BotToken', value: kvReference(keyVaultName, 'TelegramBotToken') }
        { name: 'Telegram__WebhookSecret', value: kvReference(keyVaultName, 'TelegramWebhookSecret') }
        { name: 'Authentication__Google__ClientSecret', value: kvReference(keyVaultName, 'GoogleClientSecret') }
        { name: 'Authentication__Microsoft__ClientSecret', value: kvReference(keyVaultName, 'MicrosoftClientSecret') }
        { name: 'Email__ConnectionString', value: kvReference(keyVaultName, 'AcsConnectionString') }
      ]
    }
  }
}

output name string = webApp.name
output principalId string = webApp.identity.principalId
output defaultHostName string = webApp.properties.defaultHostName
output possibleOutboundIpAddresses string = webApp.properties.possibleOutboundIpAddresses