param name string
@description('Месячный бюджет resource group, USD.')
param amount int = 60
@description('Кому слать уведомления о расходах.')
param contactEmail string
@description('Первое число месяца начала действия бюджета (формат yyyy-MM-01).')
param startDate string = '${utcNow('yyyy-MM')}-01'

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: name
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
    }
  }
}
