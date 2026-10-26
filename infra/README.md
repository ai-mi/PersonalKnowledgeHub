# Инфраструктура Azure

Bicep описывает все ресурсы из дизайна (`docs/project-design.md`, раздел 3). Всё лежит в одной resource group.

| Модуль | Ресурсы |
|---|---|
| `modules/monitoring.bicep` | Log Analytics + Application Insights (workspace-based) |
| `modules/app-service.bicep` | План Linux **B1**, Web App (.NET 10, system-assigned identity, HTTPS only, Always On, TLS 1.2) |
| `modules/postgres.bicep` | PostgreSQL Flexible Server **B1ms**, версия 18, 32 ГБ, бэкапы 7 дней, только Entra-аутентификация |
| `modules/postgres-firewall.bicep` | Правила firewall на исходящие IP Web App |
| `modules/storage.bicep` | StorageV2, приватные контейнеры `documents` и `exports`, soft delete 7 дней, роль Blob Data Contributor для Web App |
| `modules/key-vault.bicep` | Key Vault (RBAC), роль Secrets User для Web App, секрет `AcsConnectionString` |
| `modules/communication.bicep` | Communication Services + Email с Azure Managed Domain |

## Первое развёртывание

Шаги, помеченные ⚠, выполняет **владелец подписки** — их нельзя автоматизировать из CI.

Требуются: Azure CLI (`az login`), права владельца подписки.

### 1. ⚠ Resource group и инфраструктура

```powershell
$rg = 'rg-knowledgehub'
az group create -n $rg -l eastus

# Entra-администратор PostgreSQL — вы сами (или группа), НЕ managed identity Web App.
$env:ADMIN_PRINCIPAL_ID   = (az ad signed-in-user show --query id -o tsv)
$env:ADMIN_PRINCIPAL_NAME = (az ad signed-in-user show --query userPrincipalName -o tsv)

# Проверка плана без применения
az deployment group what-if -g $rg --template-file infra/main.bicep --parameters infra/main.bicepparam

az deployment group create -g $rg --template-file infra/main.bicep --parameters infra/main.bicepparam
```

Имена ресурсов (`webAppName`, `keyVaultName`, `postgresServer`) выводятся в `outputs` развёртывания:

```powershell
az deployment group show -g $rg -n main --query properties.outputs
```

Перед деплоем сверьте строку рантайма: `az webapp list-runtimes --os linux` должен содержать `DOTNETCORE|10.0`.

### 2. ⚠ Роль приложения в PostgreSQL

Managed identity Web App **не** делается Entra-администратором сервера: администратор входит в `azure_pg_admin` и может обходить RLS (приложение при старте это проверяет и откажется работать — `DatabaseRoleGuard`). Роль создаётся администратором вручную как обычная:

```powershell
$app = '<webAppName из outputs>'
$pg  = '<postgresServer из outputs>'
$env:PGPASSWORD = (az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)

psql "host=$pg dbname=postgres user=$env:ADMIN_PRINCIPAL_NAME sslmode=require" `
  -c "SELECT * FROM pgaadauth_create_principal('$app', false, false);" `
  -c "ALTER DATABASE knowledgehub OWNER TO \"$app\";"
```

Имя роли должно совпадать с именем Web App — оно используется как `Username` в строке подключения.

### 3. ⚠ Секреты Key Vault

`AcsConnectionString` создаётся автоматически. Остальные заводятся вручную (для выполнения нужна роль *Key Vault Secrets Officer* на хранилище):

```powershell
$kv = '<keyVaultName из outputs>'
az role assignment create --assignee $env:ADMIN_PRINCIPAL_ID --role "Key Vault Secrets Officer" `
  --scope (az keyvault show -n $kv --query id -o tsv)

az keyvault secret set --vault-name $kv -n TelegramBotToken      --value '<токен BotFather>'
az keyvault secret set --vault-name $kv -n TelegramWebhookSecret --value '<1–256 символов A-Z a-z 0-9 _ ->'
az keyvault secret set --vault-name $kv -n GoogleClientSecret    --value '<секрет>'
az keyvault secret set --vault-name $kv -n MicrosoftClientSecret --value '<секрет>'
```

Имя бота и адрес webhook — не секреты, они задаются параметрами развёртывания: после первого развёртывания возьмите `webAppHostName` из outputs и повторите `az deployment group create` с переменными `BOT_USERNAME` (имя бота без `@`) и `BOT_WEBHOOK_URL` (`https://<webAppHostName>/telegram/webhook`). Приложение само зарегистрирует webhook в Telegram при старте.

Пока секрета нет, соответствующая настройка Web App показывает ошибку ссылки на Key Vault — приложение при этом работает, но функция недоступна.

### 4. ⚠ Регистрация OAuth-приложений

| Провайдер | Redirect URI |
|---|---|
| Microsoft (Entra app registration, «личные и рабочие аккаунты») | `https://<webAppHostName>/signin-microsoft` |
| Google (OAuth client, Web application) | `https://<webAppHostName>/signin-google` |

Client ID задаются настройками приложения `Authentication__Microsoft__ClientId` / `Authentication__Google__ClientId`, секреты — в Key Vault (шаг 3).

### 5. ⚠ GitHub Actions (OIDC без паролей)

```powershell
$appId = az ad app create --display-name knowledgehub-github --query appId -o tsv
az ad sp create --id $appId
az ad app federated-credential create --id $appId --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
az role assignment create --assignee $appId --role Contributor `
  --scope (az group show -n $rg --query id -o tsv)
```

В GitHub (Settings → Secrets and variables → Actions):

- secrets: `AZURE_CLIENT_ID` (= `$appId`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`;
- variable: `AZURE_WEBAPP_NAME` (= `webAppName` из outputs);
- environment `production` (в subject federated credential указан именно он).

### 6. Проверка

После первого пуша в `main` (CI → Deploy):

```powershell
curl https://<webAppHostName>/health   # Healthy
```

Затем убедитесь, что перезапуск App Service не разлогинивает пользователей (ключи Data Protection лежат в `/home/ASP.NET/DataProtection-Keys`).

## Локальная проверка шаблонов

```powershell
az bicep build --file infra/main.bicep --outfile $env:TEMP\main.json
az bicep build-params --file infra/main.bicepparam --outfile $env:TEMP\main.parameters.json
```