# Чек-лист безопасности

Построчно по таблице «Безопасность» из [`project-design.md`](project-design.md) (раздел 7): где мера реализована и чем покрыта. Пути — от корня репозитория.

| Риск | Мера | Где реализовано | Чем покрыто |
|---|---|---|---|
| **SSRF** | Только `http`/`https`; IP проверяется в `ConnectCallback` после резолва (все адреса); редиректы вручную, каждый заново; `UseProxy = false` | `src/KnowledgeHub.Api/Modules/Ingestion/SsrfGuard.cs`, `.../Ingestion/SafeHttpFetcher.cs` | `tests/KnowledgeHub.UnitTests/Ingestion/SsrfGuardTests.cs` (приватные, loopback, link-local, IPv6, десятичная запись IP, схемы и порты), `tests/KnowledgeHub.IntegrationTests/Ingestion/SafeHttpFetcherTests.cs`, `.../Ingestion/LinkIngestionTests.cs` |
| **XSS** из извлечённого контента | Хранится текст, не HTML; Markdown рендерится в одном месте (`react-markdown` без `rehype-raw` + DOMPurify), код — как текст; CSP `default-src 'self'` | `src/KnowledgeHub.Web/src/shared/Markdown.tsx`, `.../shared/CodeBlock.tsx`, `src/KnowledgeHub.Api/Infrastructure/Web/SecurityHeadersExtensions.cs` | `src/KnowledgeHub.Web/src/shared/Markdown.test.tsx` (`<script>` и `onerror` не попадают в DOM), `tests/KnowledgeHub.IntegrationTests/Infrastructure/SecurityHeadersTests.cs` |
| **Вредоносные файлы** | Приватный Blob-контейнер; скачивание только через API с `Content-Disposition: attachment` и `nosniff`; тип по сигнатуре; лимит 20 МБ | `src/KnowledgeHub.Api/Modules/Ingestion/FileSignature.cs`, `.../Ingestion/DocumentIntake.cs`, `.../Items/ItemEndpoints.cs` (`/file`), `infra/modules/storage.bicep` (`allowBlobPublicAccess: false`) | `tests/KnowledgeHub.UnitTests/Ingestion/FileSignatureTests.cs`, `.../Ingestion/DocumentIntakeTests.cs`, `tests/KnowledgeHub.IntegrationTests/Ingestion/DocumentUploadTests.cs` (заголовки скачивания, `413` на файл > 20 МБ) |
| **Утечка данных между пользователями** | Глобальный фильтр EF по `user_id` + Row-Level Security (`ENABLE` + `FORCE`); роль приложения без `SUPERUSER`/`BYPASSRLS`/`azure_pg_admin` проверяется при старте | `src/KnowledgeHub.Api/Common/IUserOwned.cs`, `.../Infrastructure/Persistence/AppDbContext.cs`, `.../Persistence/RlsMigrationExtensions.cs`, `.../Persistence/UserContextConnectionInterceptor.cs`, `.../Persistence/DatabaseRoleGuard.cs` | `tests/KnowledgeHub.IntegrationTests/Identity/IsolationTests.cs` (пользователь B → `404` на каждый ресурс A, включая экспорт), `.../Identity/RlsTests.cs` (сырой SQL в обход EF), `.../Infrastructure/DatabaseRoleGuardTests.cs` |
| **Сессии и CSRF** | Cookie `HttpOnly`, `Secure`, `SameSite=Lax`; antiforgery (`X-XSRF-TOKEN`) для изменяющих запросов с cookie; API-токены только `Authorization: Bearer`, SHA-256-хэш в БД, показ один раз | `src/KnowledgeHub.Api/Modules/Identity/IdentityModule.cs`, `.../Infrastructure/Web/AntiforgeryFilter.cs`, `.../Identity/ApiTokenService.cs`, `.../Identity/ApiTokenAuthenticationHandler.cs` | `tests/KnowledgeHub.IntegrationTests/Identity/AntiforgeryTests.cs`, `.../Identity/ApiTokenTests.cs`, `.../Identity/AuthTests.cs` (в т.ч. `returnUrl=https://evil.com`), `tests/KnowledgeHub.UnitTests/Identity/ApiTokenServiceTests.cs`, `.../Identity/ReturnUrlTests.cs` |
| **Секреты** | Key Vault через Managed Identity (RBAC, роль `Key Vault Secrets User`); вход в PostgreSQL по Entra ID без паролей; TLS 1.2+, `httpsOnly` | `infra/modules/key-vault.bicep`, `infra/modules/app-service.bicep`, `src/KnowledgeHub.Api/Infrastructure/Persistence/DataSourceFactory.cs` | `tests/KnowledgeHub.UnitTests/Persistence/DataSourceFactoryTests.cs`; `az bicep build` в CI; секретов в репозитории нет (пустые значения в `appsettings.json`) |
| **Сетевой доступ к БД** | Firewall PostgreSQL — только исходящие IP App Service | `infra/modules/postgres-firewall.bicep` | ⚠ Только ручная проверка после развёртывания: подключение с чужого IP отклоняется |
| **Злоупотребления** | Rate limiting: `default` 120/мин на пользователя (иначе IP), `auth` 10/мин на IP, `webhook` 60/мин на IP, `upload` 10/мин на пользователя; `429` + `Retry-After` | `src/KnowledgeHub.Api/Infrastructure/Web/RateLimitingExtensions.cs`, `Program.cs` (`UseRateLimiter` после `UseAuthentication`), `RequireRateLimiting(...)` в группах `/api`, `/api/auth`, `/api/items/upload`, `/telegram/webhook` | `tests/KnowledgeHub.IntegrationTests/Infrastructure/RateLimitingTests.cs` (11-й вход → `429`, пользователи не делят лимит, политики независимы) |
| **Потеря данных** | Автобэкапы PostgreSQL (7 дней), soft delete в Blob Storage (7 дней) | `infra/modules/postgres.bicep`, `infra/modules/storage.bicep` | ⚠ Ручная проверка восстановления — см. [`operations.md`](operations.md) |

## Webhook Telegram

| Мера | Где | Чем покрыто |
|---|---|---|
| Секретный заголовок `X-Telegram-Bot-Api-Secret-Token`, сравнение за постоянное время; пустой секрет → webhook выключен | `src/KnowledgeHub.Api/Modules/Bot/WebhookEndpoint.cs` | `tests/KnowledgeHub.IntegrationTests/Bot/WebhookTests.cs` |
| Дедупликация по `update_id`; тело ≤ 1 МБ | `WebhookEndpoint.cs` (`dedupKey`, `RequestSizeLimitAttribute`) | `.../Bot/WebhookTests.cs` |
| Сообщения от непривязанного `chat_id` не сохраняются | `src/KnowledgeHub.Api/Modules/Bot/ProcessUpdateJob.cs` | `.../Bot/LinkingTests.cs` |

## Заголовки ответа

Добавляются middleware `UseSecurityHeaders` ко всем ответам (включая статику, `404`, `429`):

| Заголовок | Значение |
|---|---|
| `Content-Security-Policy` | `default-src 'self'; img-src 'self' data:; base-uri 'self'; frame-ancestors 'none'` (в `Development` не выставляется — Swagger UI использует inline-скрипты) |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |

SPA не содержит inline-скриптов и стилей (в `src/KnowledgeHub.Web` нет `style=`/`<style>`), поэтому строгий CSP не ломает интерфейс. HSTS на App Service обеспечивает `httpsOnly`; при появлении собственного домена добавить `UseHsts()`.

## Остаточные риски и ручные проверки

- ⚠ После деплоя: `curl -I https://<app>.azurewebsites.net/` — есть `Content-Security-Policy` и `X-Content-Type-Options: nosniff`.
- ⚠ После деплоя: рестарт App Service не разлогинивает пользователей (ключи Data Protection в `/home/ASP.NET/DataProtection-Keys`).
- Rate limiting хранится в памяти процесса: при нескольких инстансах лимиты считаются на каждый отдельно (сейчас инстанс один — B1).
- Private Endpoint для PostgreSQL не используется (осознанное решение, см. дизайн, раздел 7).
