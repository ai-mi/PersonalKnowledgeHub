param name string
@description('Имя Application Insights из monitoring.bicep.')
param appInsightsName string
@description('Хост Web App для проверки доступности: <имя>.azurewebsites.net.')
param webAppHostName string
@description('Куда слать оповещения.')
param alertEmail string
param location string

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${name}-alerts'
  location: 'global'
  properties: {
    groupShortName: 'khalerts'
    enabled: true
    emailReceivers: [
      {
        name: 'owner'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// Исключения: больше 10 за 15 минут.
resource exceptionsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${name}-exceptions'
  location: 'global'
  properties: {
    description: 'Более 10 исключений за 15 минут.'
    severity: 2
    enabled: true
    scopes: [appInsights.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'exceptions'
          metricNamespace: 'microsoft.insights/components'
          metricName: 'exceptions/count'
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Count'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

// Доступность: стандартный тест /health из нескольких точек, алерт при недоступности минимум из двух.
// Классические URL ping-тесты (kind 'ping' с XML WebTest) Microsoft вывела из эксплуатации 30.09.2026.
var availabilityTestName = '${name}-health'

resource healthTest 'Microsoft.Insights/webtests@2022-06-15' = {
  name: availabilityTestName
  location: location
  kind: 'standard'
  tags: {
    // Связь теста с ресурсом Application Insights.
    'hidden-link:${appInsights.id}': 'Resource'
  }
  properties: {
    SyntheticMonitorId: availabilityTestName
    Name: 'KnowledgeHub /health'
    Enabled: true
    Frequency: 300
    Timeout: 30
    Kind: 'standard'
    RetryEnabled: true
    Locations: [
      { Id: 'us-va-ash-azr' }
      { Id: 'emea-nl-ams-azr' }
      { Id: 'emea-gb-db3-azr' }
    ]
    Request: {
      RequestUrl: 'https://${webAppHostName}/health'
      HttpVerb: 'GET'
      ParseDependentRequests: false
      FollowRedirects: true
    }
    ValidationRules: {
      ExpectedHttpStatusCode: 200
    }
  }
}

resource availabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${name}-availability'
  location: 'global'
  properties: {
    description: '/health недоступен минимум из двух точек проверки.'
    severity: 1
    enabled: true
    scopes: [healthTest.id, appInsights.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: healthTest.id
      componentId: appInsights.id
      failedLocationCount: 2
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

// Упавшие задачи: JobWorker пишет сообщение «JobFailed: …» (LoggerMessage, EventName = JobFailed), когда задача исчерпала попытки.
// Запись идёт с исключением, а такие записи экспортёр Azure Monitor кладёт в таблицу exceptions, а не traces;
// ищем в обеих, по тексту сообщения, шаблону и имени события. После первого реального сбоя проверить запрос в Logs.
resource failedJobsAlert 'Microsoft.Insights/scheduledQueryRules@2022-06-15' = {
  name: '${name}-failed-jobs'
  location: location
  properties: {
    displayName: 'Фоновые задачи завершились ошибкой (JobFailed)'
    description: 'Хотя бы одна задача исчерпала все попытки за последние 15 минут.'
    severity: 2
    enabled: true
    scopes: [appInsights.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'union traces, exceptions | where message startswith "JobFailed" or tostring(customDimensions["{OriginalFormat}"]) startswith "JobFailed" or tostring(customDimensions.EventName) == "JobFailed"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}
