using 'main.bicep'

param namePrefix = 'kh'
param location = 'eastus'

// Задаются владельцем подписки перед развёртыванием (см. infra/README.md):
//   $env:ADMIN_PRINCIPAL_ID = (az ad signed-in-user show --query id -o tsv)
//   $env:ADMIN_PRINCIPAL_NAME = (az ad signed-in-user show --query userPrincipalName -o tsv)
param adminPrincipalId = readEnvironmentVariable('ADMIN_PRINCIPAL_ID', '')
param adminPrincipalName = readEnvironmentVariable('ADMIN_PRINCIPAL_NAME', '')
param adminPrincipalType = 'User'