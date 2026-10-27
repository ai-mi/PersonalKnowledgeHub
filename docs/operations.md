# Эксплуатация

Команды для PowerShell. Имена ресурсов берите из `az deployment group show -g $rg -n main --query properties.outputs` (см. [`infra/README.md`](../infra/README.md)).

```powershell
$rg = '<resource group>'
$pg = '<имя сервера PostgreSQL без .postgres.database.azure.com>'
```

## Алерты

Настроены в `infra/modules/alerts.bicep`, уведомления уходят на `ALERT_EMAIL`:

| Алерт | Условие | Что делать |
|---|---|---|
| `*-exceptions` | > 10 исключений за 15 минут | Application Insights → Failures: найти исключение, проверить последний деплой |
| `*-availability` | `/health` недоступен минимум из 2 из 3 точек | Проверить App Service и PostgreSQL (health check включает БД); при необходимости перезапустить Web App |
| `*-failed-jobs` | в логе есть `JobFailed` за 15 минут | Задача исчерпала 4 попытки. Запрос в Logs: `traces \| where message startswith "JobFailed" \| order by timestamp desc`. Для записи — кнопка «Повторить» или `POST /api/items/{id}/reprocess` |

Бюджет: $60/мес на resource group, письма на 80% и 100% (`infra/modules/budget.bicep`). Ожидаемые расходы ≈ $30/мес — сверяйте в Cost Management.

## Восстановление PostgreSQL на точку во времени

Бэкапы хранятся 7 дней. Восстановление создаёт **новый** сервер; боевой не затрагивается.

```powershell
az postgres flexible-server restore -g $rg `
  --name "$pg-restore" --source-server $pg `
  --restore-time '2026-10-27T09:00:00Z'      # UTC, не старше 7 дней
```

Проверка данных на временном сервере (вход под Entra-администратором):

```powershell
$env:PGPASSWORD = az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv
psql "host=$pg-restore.postgres.database.azure.com dbname=knowledgehub user=$env:ADMIN_PRINCIPAL_NAME sslmode=require" `
  -c "SELECT count(*) FROM items;"
```

Firewall восстановленного сервера пуст — добавьте свой IP: `az postgres flexible-server firewall-rule create -g $rg -n "$pg-restore" --rule-name me --start-ip-address <ip> --end-ip-address <ip>`.

Если нужно вернуть данные в боевой сервер — выгрузите нужные таблицы (`pg_dump --table ...`) и загрузите их в боевую базу, а не переключайте приложение на восстановленный сервер: у него другое имя и свой firewall. После проверки временный сервер удалите:

```powershell
az postgres flexible-server delete -g $rg --name "$pg-restore" --yes
```

## Восстановление блоба (soft delete)

Удалённые документы и архивы экспорта хранятся 7 дней.

```powershell
$account = '<storageAccount из outputs>'
# Список удалённых блобов
az storage blob list --account-name $account -c documents --include d --auth-mode login `
  --query "[?deleted].name" -o tsv
# Восстановление
az storage blob undelete --account-name $account -c documents -n '<имя блоба>' --auth-mode login
```

Вашему пользователю нужна роль `Storage Blob Data Contributor` на storage account (у managed identity Web App она уже есть).

## Ротация секретов

Секреты лежат в Key Vault; Web App читает их через Key Vault references и подхватывает **новую версию** после перезапуска (или автоматически в течение суток).

```powershell
$kv = '<keyVaultName из outputs>'
az keyvault secret set --vault-name $kv --name GoogleClientSecret --value '<новый секрет>'
az webapp restart -g $rg -n '<webAppName>'
```

| Секрет | Источник нового значения | Дополнительно |
|---|---|---|
| `GoogleClientSecret`, `MicrosoftClientSecret` | Консоль OAuth-приложения (Google Cloud / Entra) | Старый секрет отзывать после проверки входа |
| `TelegramBotToken` | `@BotFather` → `/revoke` | Webhook регистрируется при старте приложения |
| `TelegramWebhookSecret` | Любая строка из `A-Z a-z 0-9 _ -`, 1–256 символов | Нужна повторная регистрация webhook (ниже) |
| `AcsConnectionString` | Communication Services → Keys → Regenerate | — |

Доступ в PostgreSQL по Entra ID паролей не требует. API-токены пользователей ротируются самими пользователями (Настройки → API-токены).

## Повторная регистрация webhook Telegram

Приложение регистрирует webhook при старте (`Bot:WebhookUrl`, `Bot:WebhookSecret`). Если регистрация не прошла или секрет сменился — перезапустите Web App:

```powershell
az webapp restart -g $rg -n '<webAppName>'
```

Проверить состояние (токен берётся из Key Vault и не должен попадать в историю оболочки или логи):

```powershell
$token = az keyvault secret show --vault-name $kv --name TelegramBotToken --query value -o tsv
curl "https://api.telegram.org/bot$token/getWebhookInfo"   # url, pending_update_count, last_error_message
```

Ручная регистрация, если перезапуск не помог:

```powershell
$secret = az keyvault secret show --vault-name $kv --name TelegramWebhookSecret --query value -o tsv
curl -X POST "https://api.telegram.org/bot$token/setWebhook" `
  -d "url=https://<webAppHostName>/telegram/webhook" -d "secret_token=$secret"
```

## Ручные проверки перед открытием другим пользователям

- [ ] Восстановить БД из бэкапа во временный сервер (раздел выше), убедиться, что данные на месте, удалить сервер.
- [ ] Перезапустить App Service (`az webapp restart`) — пользователь остаётся в системе (ключи Data Protection в `/home/ASP.NET/DataProtection-Keys`).
- [ ] Сверить фактические расходы с оценкой ≈ $30/мес (Cost Management).
- [ ] `curl -I https://<webAppHostName>/` — есть `Content-Security-Policy` и `X-Content-Type-Options: nosniff`.
- [ ] Остановить Web App на 10 минут — приходит алерт доступности (затем запустить обратно).
