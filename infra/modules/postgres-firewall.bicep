param serverName string

@description('Исходящие IP Web App. Значение известно только во время развёртывания, поэтому правила создаются в отдельном модуле.')
param ipAddresses array

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' existing = {
  name: serverName
}

// Правила создаются по очереди: параллельные операции над сервером приводят к ServerIsBusy.
@batchSize(1)
resource rules 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2025-08-01' = [
  for (ip, i) in ipAddresses: {
    parent: server
    name: 'app-${i}'
    properties: {
      startIpAddress: ip
      endIpAddress: ip
    }
  }
]