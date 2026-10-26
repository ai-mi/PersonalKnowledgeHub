param name string

@description('Регион хранения данных ACS (не Azure-регион ресурса).')
param dataLocation string = 'United States'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: '${name}-email'
  location: 'global'
  properties: {
    dataLocation: dataLocation
  }
}

// Azure Managed Domain: отправка с домена *.azurecomm.net без собственного DNS.
resource domain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: '${name}-acs'
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: [domain.id]
  }
}

output senderDomain string = domain.properties.mailFromSenderDomain
#disable-next-line outputs-should-not-contain-secrets // Значение сразу передаётся в Key Vault и нигде не сохраняется.
output connectionString string = communicationService.listKeys().primaryConnectionString