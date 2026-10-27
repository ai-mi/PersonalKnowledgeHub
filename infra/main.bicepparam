using 'main.bicep'

param namePrefix = 'kh'
param location = 'swedencentral'

// Задаются владельцем подписки перед развёртыванием (см. infra/README.md):
//   $env:ADMIN_PRINCIPAL_ID = (az ad signed-in-user show --query id -o tsv)
//   $env:ADMIN_PRINCIPAL_NAME = (az ad signed-in-user show --query userPrincipalName -o tsv)
param adminPrincipalId = readEnvironmentVariable('ADMIN_PRINCIPAL_ID', '')
param adminPrincipalName = readEnvironmentVariable('ADMIN_PRINCIPAL_NAME', '')
param adminPrincipalType = 'User'

// Telegram-бот (создаётся в @BotFather). Хост Web App известен после первого развёртывания (output webAppHostName),
// поэтому webhook включается повторным развёртыванием.
param botUsername = readEnvironmentVariable('BOT_USERNAME', '')
param botWebhookUrl = readEnvironmentVariable('BOT_WEBHOOK_URL', '')
// Алерты Azure Monitor и уведомления о бюджете ($60/мес, пороги 80% и 100%).
param alertEmail = readEnvironmentVariable('ALERT_EMAIL', '')
