# Personal Knowledge Hub — План реализации

**Цель:** облачный сервис (Azure, .NET 10 + React) для сохранения статей, ссылок, заметок, фрагментов кода и документов с полнотекстовым поиском, Telegram-ботом, напоминаниями и экспортом — по дизайну из [`docs/project-design.md`](project-design.md).

## Процесс выполнения

**Изучи контекст:**
- Прочитай `docs/project-design.md` и разделы «Контекст проекта» и «Подводные камни» ниже
- Найди первую незакрытую фичу в плане (без ✅ в заголовке)

**Выполни фичу:**
- Выполни все шаги фичи
- Тесты обязательны для каждой фичи
- Перед завершением: `dotnet build KnowledgeHub.slnx -warnaserror` и `dotnet test KnowledgeHub.slnx` — без ошибок; для фич с фронтом — ещё `npm --prefix src/KnowledgeHub.Web run build` и `npm --prefix src/KnowledgeHub.Web test`

**Подведи итог:**
- Напиши короткий итог: что сделал, что работает, есть ли проблемы
- Жди подтверждения пользователя
- После подтверждения: отметь шаги `- [x]` и поставь ✅ в заголовке фичи

## Принципы

- **DRY** — не дублируй код, выноси общее
- **YAGNI** — не добавляй то, что не описано в дизайне
- **Точные пути** — все пути в плане указаны от корня репозитория `C:\Work\Claude\Projects\PersonalKnowledgeHub`
- **Отклонения от плана** — если шаг невозможен как написан (изменилось API библиотеки и т.п.), сделай минимально отличающийся вариант и явно укажи это в итоге

---

## Контекст проекта

### Структура репозитория

```
KnowledgeHub.slnx                     — solution (формат .slnx, по умолчанию в .NET 10 SDK)
global.json                           — фиксирует .NET SDK 10.0.x
Directory.Build.props                 — общие настройки: net10.0, Nullable, TreatWarningsAsErrors
Directory.Packages.props              — Central Package Management: все версии NuGet здесь
.config/dotnet-tools.json             — локальный инструмент dotnet-ef
docker-compose.yml                    — PostgreSQL 18 + Azurite для локальной разработки
docker/postgres/init.sql              — создание роли приложения kh_app
src/KnowledgeHub.Api/                 — ASP.NET Core хост: API, раздача SPA, воркеры
  Program.cs
  Common/                             — общее для всех модулей (ICurrentUser, ошибки, Clock)
  Infrastructure/Persistence/         — AppDbContext, DataSource, interceptor, миграции
  Infrastructure/Web/                 — ProblemDetails, antiforgery-фильтр, rate limiting
  Modules/<Module>/                   — Identity, Items, Search, Jobs, Ingestion, Bot, Reminders, Export
  wwwroot/                            — сюда собирается SPA (в git не коммитится)
src/KnowledgeHub.Web/                 — React + TypeScript + Vite (SPA), e2e-тесты Playwright
tests/KnowledgeHub.UnitTests/         — xUnit, без БД и сети
tests/KnowledgeHub.IntegrationTests/  — xUnit + WebApplicationFactory + Testcontainers
infra/                                — Bicep
.github/workflows/                    — CI/CD
```

### Соглашения по модулям

- Модуль — папка `src/KnowledgeHub.Api/Modules/<Module>/` с namespace `KnowledgeHub.Api.Modules.<Module>`.
- Точка входа модуля — `<Module>Module.cs` со статическими методами `Add<Module>Module(this IServiceCollection, IConfiguration)` и `Map<Module>Endpoints(this IEndpointRouteBuilder)`; `Program.cs` только вызывает их.
- Сущности модуля и их `IEntityTypeConfiguration<T>` лежат в папке модуля; `AppDbContext` подключает конфигурации через `ApplyConfigurationsFromAssembly`.
- Модули обращаются друг к другу **только** через публичные интерфейсы (`I*Service`), объявленные в папке модуля-владельца. Запросы к чужим `DbSet` запрещены.
- Эндпоинты — Minimal API, группа `/api/<resource>`, возвращают `TypedResults`.
- Ошибки — `ProblemDetails`; бизнес-ошибки через общий тип `Result`/исключение из `Common/` (вводится в фиче 2).
- Идентификаторы — `Guid.CreateVersion7()`. Время — только через `TimeProvider` (в тестах — `FakeTimeProvider`).
- Имена таблиц и колонок — `snake_case` (пакет `EFCore.NamingConventions`, `UseSnakeCaseNamingConvention()`).
- Перечисления хранятся строками в нижнем регистре (`HasConversion<string>()` + конвертер), без PostgreSQL enum.

### Соглашения по тестам

- Unit: `tests/KnowledgeHub.UnitTests/<Module>/<Class>Tests.cs`.
- Интеграционные: `tests/KnowledgeHub.IntegrationTests/<Module>/<Feature>Tests.cs`, общий фикстур `Infrastructure/ApiFactory.cs` (один контейнер PostgreSQL на сборку, сброс данных между тестами через Respawn).
- Тестовая аутентификация: заголовок `X-Test-User: <guid>` (схема `Test`, регистрируется только в `ApiFactory`).
- Каждый новый пользовательский эндпоинт добавляется в тест изоляции `tests/KnowledgeHub.IntegrationTests/Identity/IsolationTests.cs` (пользователь B получает `404` на ресурсы A).
- Ассерты — `Shouldly`.
- Фильтр запуска: `dotnet test <проект> --filter "FullyQualifiedName~<Namespace или класс>"`. Ожидаемый вывод в конце: `Passed!  - Failed:     0`.

### Технологии и версии (проверено 2026-10-26)

| Пакет | Версия |
|---|---|
| .NET SDK / runtime | 10.0.4xx / 10.0.x (LTS) |
| Microsoft.AspNetCore.* (в т.ч. OpenApi), Microsoft.EntityFrameworkCore.*, Microsoft.Extensions.* (10.x) | 10.0.14 (Extensions.* — 10.11.0) |
| Swashbuckle.AspNetCore.SwaggerUI (только UI поверх встроенного OpenAPI) | 10.3.1 |
| Npgsql / Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| EFCore.NamingConventions | 10.0.1 |
| dotnet-ef (local tool) | 10.0.14 |
| Azure.Identity / Azure.Storage.Blobs / Azure.Communication.Email | 1.21.0 / 12.30.1 / 1.1.0 |
| Azure.Monitor.OpenTelemetry.AspNetCore | 1.6.0 |
| SmartReader | 0.11.1 |
| PdfPig (namespace `UglyToad.PdfPig`) | 0.1.16 |
| DocumentFormat.OpenXml | 3.5.1 |
| Telegram.Bot | 22.10.3.3 |
| YamlDotNet | 18.1.0 |
| xunit.v3 / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 4.0.2 / 4.0.1 / 18.11.0 |
| Shouldly / Testcontainers.PostgreSql / Testcontainers.Azurite / Respawn / WireMock.Net | 4.3.0 / 4.16.0 / 4.16.0 / 7.0.0 / 2.20.0 |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 |
| PostgreSQL (Azure и Docker) | 18 |
| react / react-dom / react-router | 19.3.x / 8.5.x |
| vite / @vitejs/plugin-react / typescript | 8.3.x / 6.1.x / **~6.0.x** (как в шаблоне create-vite; TS 7 не используем) |
| vitest / @testing-library/react / jsdom / msw | 5.0.x / 16.3.x / 30.x / 3.0.x |
| @tanstack/react-query / dompurify / react-markdown / highlight.js | 5.105.x / 3.4.x / 10.1.x / 11.12.x |
| @playwright/test | 1.64.x |

### Подводные камни (учесть при реализации)

1. **RLS и пул соединений.** Контекст пользователя ставится `set_config('app.user_id', …, false)` при **каждом** открытии соединения (EF `DbConnectionInterceptor`), обязательно вместе с `app.is_system` — так значение от прошлого запроса не может «просочиться». Таблицы — `ENABLE` + `FORCE ROW LEVEL SECURITY` (иначе владелец таблиц обходит политики). Роль приложения не должна быть superuser и не иметь `BYPASSRLS` — это проверяется при старте (`DatabaseRoleGuard`).
2. **Testcontainers и RLS.** Пользователь `postgres` в контейнере — superuser и игнорирует RLS. Приложение в тестах подключается ролью `kh_app` (NOSUPERUSER, NOBYPASSRLS), а Respawn сбрасывает данные соединением `postgres`.
3. **Entra ID для PostgreSQL.** Пароль = токен Entra, область `https://ossrdbms-aad.database.windows.net/.default`, токен живёт ~60–90 мин → `NpgsqlDataSourceBuilder.UsePeriodicPasswordProvider(...)` с обновлением раз в 50 мин. Имя пользователя БД = имя managed identity (имя Web App). **Managed identity приложения не делать Entra-администратором сервера:** администратор входит в `azure_pg_admin` и может обходить RLS. Администратор — владелец подписки (или группа), а роль приложения создаётся им вручную как обычная: `SELECT * FROM pgaadauth_create_principal('<имя Web App>', false, false);` + владелец базы `knowledgehub`. `DatabaseRoleGuard` дополнительно проверяет членство в `azure_pg_admin`.
4. **Миграции.** Firewall PostgreSQL пускает только App Service, поэтому GitHub runner до БД не достанет. Миграции применяются **при старте приложения** (`Database:MigrateOnStartup=true`), это отклонение от «migrations bundle в CI» из дизайна.
5. **Полнотекстовый поиск RU+EN.** Конфигурация `russian` в PostgreSQL уже обрабатывает латиницу английским стеммером (`asciiword → english_stem`), поэтому используется **одна** конфигурация `russian` (покрывает оба языка из дизайна — проверяется тестом). `tsvector` ограничен 1 МБ — тело обрезается до 500 000 символов. `ts_headline` дорогой — считать только для записей текущей страницы.
6. **Cookie-аутентификация в .NET 10** для API-эндпоинтов (JSON / `TypedResults`) уже возвращает `401/403` вместо редиректа — свои обработчики `OnRedirectToLogin` не нужны.
7. **Antiforgery в Minimal API** автоматически проверяется только для form-эндпоинтов. Для JSON-запросов с cookie нужен свой endpoint-фильтр (`IAntiforgery.ValidateRequestAsync`), запросы с `Bearer`-токеном — без проверки. Эндпоинты с `IFormFile`/`[FromForm]` в .NET 8+ автоматически получают antiforgery-метаданные и падают с `InvalidOperationException` без `UseAntiforgery()` (а с ним отклоняют загрузку по API-токену) — помечать их `.DisableAntiforgery()`, проверку делает наш фильтр.
8. **SSRF.** Проверять IP нужно внутри `SocketsHttpHandler.ConnectCallback` (резолв → проверка **всех** адресов → подключение к проверенному IP), а не до запроса — иначе DNS rebinding. `AllowAutoRedirect = false` (редиректы вручную, каждый — снова через проверку), `UseProxy = false`.
9. **Telegram.Bot v22:** методы без суффикса `Async` (`SendMessage`, `SetWebhook`, `GetFile`, `DownloadFile`); JSON Bot API — snake_case: десериализовать `Update` через `JsonBotAPI.Options`, **не** меняя глобальные JSON-настройки API. `getFile` работает только для файлов ≤ 20 МБ. В payload задачи класть **исходный JSON** `Update` (строкой), а не объект — иначе он пересериализуется с настройками по умолчанию и потеряет поля. Webhook-секрет — заголовок `X-Telegram-Bot-Api-Secret-Token` (1–256 символов `A-Z a-z 0-9 _ -`).
10. **PdfPig синхронный** и не прерывается `CancellationToken` — таймаут через `Task.Run(...).WaitAsync(60s)` плюс ограничение числа страниц (2000).
11. **App Service за прокси:** включить `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, иначе неверные IP в rate limiting и схема `http` в редиректах OAuth.
12. **Bicep:** цикл по `possibleOutboundIpAddresses` Web App нельзя делать в том же шаблоне (значение известно только во время развёртывания) — правила firewall создаются в отдельном модуле, которому IP передаются параметром.
13. **Data Protection:** ключи cookie на App Service Linux хранятся в `/home/ASP.NET/DataProtection-Keys` (общий постоянный диск) — проверить после деплоя, что перезапуск не разлогинивает.

---

### Фича 1: Каркас решения ✅

**Цель:** решение собирается, `GET /health` отвечает `Healthy`, unit- и интеграционный проекты запускаются.

**Файлы:**
- Создать: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `KnowledgeHub.slnx`, `.config/dotnet-tools.json`
- Создать: `src/KnowledgeHub.Api/KnowledgeHub.Api.csproj`, `src/KnowledgeHub.Api/Program.cs`, `src/KnowledgeHub.Api/appsettings.json`, `src/KnowledgeHub.Api/appsettings.Development.json`
- Создать: `tests/KnowledgeHub.UnitTests/KnowledgeHub.UnitTests.csproj`, `tests/KnowledgeHub.IntegrationTests/KnowledgeHub.IntegrationTests.csproj`
- Тесты: `tests/KnowledgeHub.UnitTests/SmokeTests.cs`, `tests/KnowledgeHub.IntegrationTests/HealthTests.cs`

**Шаги:**
- [x] `git init`; `.gitignore` из `dotnet new gitignore` + `node_modules/`, `src/KnowledgeHub.Api/wwwroot/`, `playwright-report/`, `test-results/`
- [x] `global.json`: `"sdk": { "version": "10.0.400", "rollForward": "latestFeature" }`
- [x] `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `ManagePackageVersionsCentrally=true`
- [x] `Directory.Packages.props`: все версии из таблицы «Технологии и версии» (добавлять пакеты по мере появления — здесь только для Api/тестов этой фичи)
- [x] `dotnet new web -n KnowledgeHub.Api -o src/KnowledgeHub.Api`, удалить версии из `PackageReference` (CPM)
- [x] `Program.cs`: `AddHealthChecks()`, `MapHealthChecks("/health")`, `public partial class Program;` в конце (для `WebApplicationFactory`)
- [x] OpenAPI (из дизайна): `AddOpenApi()` + `MapOpenApi()` (документ `/openapi/v1.json`); в `Development` — Swagger UI `app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "KnowledgeHub"))` (пакет `Swashbuckle.AspNetCore.SwaggerUI`, генератор Swashbuckle не нужен)
- [x] `Properties/launchSettings.json`: профиль `http` с `applicationUrl = http://localhost:5000` — на этот порт рассчитаны прокси Vite и команды проверки
- [x] Тестовые проекты: `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Shouldly`; интеграционный — плюс `Microsoft.AspNetCore.Mvc.Testing` и ссылка на Api
- [x] `dotnet new sln -n KnowledgeHub` (создаст `.slnx`), добавить три проекта
- [x] `dotnet new tool-manifest`, `dotnet tool install dotnet-ef --version 10.0.14`
- [x] `HealthTests`: `GET /health` → `200` и тело `Healthy` (пока без БД); `GET /openapi/v1.json` → `200`

**Проверка:**
- [x] `dotnet build KnowledgeHub.slnx -warnaserror` — `Build succeeded`, 0 предупреждений
- [x] `dotnet test KnowledgeHub.slnx` — `Passed!  - Failed:     0` для обоих проектов

---

### Фича 2: База данных, общая инфраструктура и тестовый фикстур ✅

**Цель:** приложение подключается к PostgreSQL (локально — паролем, в Azure — Entra ID), применяет миграции при старте, отказывается стартовать под ролью с обходом RLS; интеграционные тесты поднимают PostgreSQL в Docker.

**Файлы:**
- Создать: `docker-compose.yml`, `docker/postgres/init.sql`
- Создать: `src/KnowledgeHub.Api/Infrastructure/Persistence/AppDbContext.cs`, `.../Persistence/DataSourceFactory.cs`, `.../Persistence/DatabaseOptions.cs`, `.../Persistence/DatabaseRoleGuard.cs`, `.../Persistence/PersistenceExtensions.cs`
- Создать: `src/KnowledgeHub.Api/Common/Errors.cs` (общие бизнес-ошибки → ProblemDetails), `src/KnowledgeHub.Api/Infrastructure/Web/ProblemDetailsExtensions.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, `src/KnowledgeHub.Api/appsettings*.json`, `Directory.Packages.props`
- Создать: `tests/KnowledgeHub.IntegrationTests/Infrastructure/ApiFactory.cs`, `.../Infrastructure/PostgresContainer.cs`, `.../Infrastructure/IntegrationTest.cs` (базовый класс: `HttpClient`, `ResetDatabaseAsync`)
- Тесты: `tests/KnowledgeHub.IntegrationTests/Infrastructure/DatabaseTests.cs`, изменить `tests/KnowledgeHub.IntegrationTests/HealthTests.cs`

**Шаги:**
- [x] `docker-compose.yml`: `postgres:18` (порт 5432, `POSTGRES_PASSWORD=postgres`, монтирует `docker/postgres/init.sql`), `mcr.microsoft.com/azure-storage/azurite` (порт 10000)
- [x] `init.sql`: `CREATE ROLE kh_app LOGIN PASSWORD 'kh_app' NOSUPERUSER NOBYPASSRLS; CREATE DATABASE knowledgehub OWNER kh_app;`
- [x] `DatabaseOptions`: `ConnectionString`, `UseEntraAuth`, `MigrateOnStartup`
- [x] `DataSourceFactory`: `NpgsqlDataSourceBuilder`; при `UseEntraAuth` — `UsePeriodicPasswordProvider` с `DefaultAzureCredential` (scope из «Подводных камней» п.3, обновление 50 мин, повтор 10 с)
- [x] `AppDbContext`: `UseNpgsql(dataSource)`, `UseSnakeCaseNamingConvention()`, `ApplyConfigurationsFromAssembly`
- [x] `DatabaseRoleGuard` (hosted service; **он же** затем запускает миграции — так порядок «проверка → миграции» гарантирован): `SELECT rolsuper OR rolbypassrls OR pg_has_role(current_user, 'azure_pg_admin', 'MEMBER') ...` (проверку `azure_pg_admin` делать только если роль существует — локально её нет) → `true` ⇒ исключение и остановка приложения
- [x] Миграции при старте (в `DatabaseRoleGuard`), если `MigrateOnStartup=true` (`Database.MigrateAsync()`); первая пустая миграция `Initial`: `dotnet ef migrations add Initial -p src/KnowledgeHub.Api -o Infrastructure/Persistence/Migrations`
- [x] Health check БД: `AddDbContextCheck<AppDbContext>()` (пакет `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`)
- [x] `ProblemDetails`: `AddProblemDetails()`, `UseExceptionHandler()`, `UseStatusCodePages()`; маппинг `Errors.NotFound/Validation/Conflict` → 404/400/409
- [x] `PostgresContainer`: Testcontainers `postgres:18`, после старта выполняет тот же `init.sql` (DRY — читать файл из `docker/postgres/init.sql`); один контейнер на сборку (`[assembly: AssemblyFixture]`)
- [x] `ApiFactory`: строка подключения ролью `kh_app`, `MigrateOnStartup=true`, `Environment=Testing`
- [x] `ResetDatabaseAsync`: Respawn соединением `postgres`, `TablesToIgnore = ["__EFMigrationsHistory"]`
- [x] `DatabaseTests`: миграции применены; guard падает, если подключиться ролью `postgres` (superuser)

**Проверка:**
- [x] `docker compose up -d` и `dotnet run --project src/KnowledgeHub.Api` → `curl http://localhost:5000/health` — `Healthy`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests` — `Passed!  - Failed:     0`

---

### Фича 3: Каркас SPA ✅

**Цель:** React-приложение собирается в `wwwroot` и раздаётся бэкендом, неизвестные пути отдают `index.html`, в dev-режиме Vite проксирует `/api` на бэкенд, Vitest работает.

**Файлы:**
- Создать: `src/KnowledgeHub.Web/` из шаблона `create-vite` (`react-ts`)
- Создать: `src/KnowledgeHub.Web/src/api/client.ts`, `src/KnowledgeHub.Web/src/app/App.tsx`, `src/KnowledgeHub.Web/src/app/router.tsx`, `src/KnowledgeHub.Web/src/app/Layout.tsx`, `src/KnowledgeHub.Web/vitest.setup.ts`
- Изменить: `src/KnowledgeHub.Web/vite.config.ts`, `src/KnowledgeHub.Web/package.json`, `src/KnowledgeHub.Api/Program.cs`
- Тесты: `src/KnowledgeHub.Web/src/api/client.test.ts`, `src/KnowledgeHub.Web/src/app/App.test.tsx`, `tests/KnowledgeHub.IntegrationTests/SpaFallbackTests.cs`

**Шаги:**
- [x] `npm create vite@latest src/KnowledgeHub.Web -- --template react-ts`; оставить `typescript ~6.0` из шаблона
- [x] Зависимости: `react-router`, `@tanstack/react-query`; dev: `vitest`, `jsdom`, `@testing-library/react`, `@testing-library/jest-dom`, `@testing-library/user-event`, `msw`
- [x] `vite.config.ts`: `build.outDir = '../KnowledgeHub.Api/wwwroot'`, `emptyOutDir: true`, `server.proxy['/api'] = 'http://localhost:5000'` (и `/signin-*` для OAuth), блок `test` (jsdom, setup-файл)
- [x] `package.json` scripts: `test: "vitest run"`, `test:watch: "vitest"`
- [x] `client.ts`: единая обёртка `fetch` — `credentials: 'include'`, JSON, разбор `ProblemDetails` в `ApiError`, событие «401 → на страницу входа»; (заголовок XSRF добавляется в фиче 5)
- [x] `App.tsx`: `QueryClientProvider` + роутер; `Layout` с шапкой и навигацией (пока пустые страницы «Записи», «Поиск», «Настройки»)
- [x] Бэкенд: `UseDefaultFiles()`, `UseStaticFiles()`, `MapFallbackToFile("index.html")` — но **не** для путей `/api/*` (они должны отдавать 404 ProblemDetails)
- [x] `client.test.ts`: успешный JSON, `ProblemDetails` → `ApiError`, `401` → событие (через `msw`)
- [x] `SpaFallbackTests`: `GET /items/123` → `200 text/html` (в тесте положить фиктивный `wwwroot/index.html`); `GET /api/unknown` → `404 application/problem+json`

**Проверка:**
- [x] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят
- [x] `npm --prefix src/KnowledgeHub.Web run build` — файлы появились в `src/KnowledgeHub.Api/wwwroot/`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~SpaFallback"` — `Passed!  - Failed:     0`

---

### Фича 4: Инфраструктура Azure и CI/CD ✅

**Цель:** Bicep описывает все ресурсы из дизайна; PR прогоняет сборку и тесты; push в `main` собирает SPA + API и деплоит в App Service.

**Файлы:**
- Создать: `infra/main.bicep`, `infra/main.bicepparam`, `infra/modules/app-service.bicep`, `infra/modules/postgres.bicep`, `infra/modules/postgres-firewall.bicep`, `infra/modules/storage.bicep`, `infra/modules/key-vault.bicep`, `infra/modules/communication.bicep`, `infra/modules/monitoring.bicep`, `infra/README.md`
- Создать: `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`
- Изменить: `src/KnowledgeHub.Api/Program.cs` (Application Insights), `Directory.Packages.props`

**Шаги:**
- [x] `monitoring.bicep`: Log Analytics + Application Insights (workspace-based)
- [x] `app-service.bicep`: Plan Linux **B1**, Web App с system-assigned identity, `linuxFxVersion` для .NET 10 (сверить строку: `az webapp list-runtimes --os linux`, ожидается `DOTNETCORE|10.0`), `httpsOnly`, `alwaysOn`, `minTlsVersion 1.2`, app settings: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `Database__UseEntraAuth=true`, `Database__MigrateOnStartup=true`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, секреты — Key Vault references `@Microsoft.KeyVault(SecretUri=...)`
- [x] `postgres.bicep`: Flexible Server **B1ms**, версия 18, 32 ГБ, бэкапы 7 дней, `authConfig`: Entra включён, пароль выключен; Entra-администратор — пользователь/группа из параметров (`adminPrincipalId`, `adminPrincipalName`, `adminPrincipalType`), **не** identity Web App (п.3 «Подводных камней»); база `knowledgehub`
- [x] `postgres-firewall.bicep`: принимает массив IP и создаёт правило на каждый (см. «Подводные камни» п.12); в `main.bicep` передать `split(webApp.properties.possibleOutboundIpAddresses, ',')`
- [x] `storage.bicep`: StorageV2, приватные контейнеры `documents` и `exports`, soft delete 7 дней, роль `Storage Blob Data Contributor` для identity Web App
- [x] `key-vault.bicep`: RBAC-режим, роль `Key Vault Secrets User` для identity Web App; секреты (`TelegramBotToken`, `TelegramWebhookSecret`, `GoogleClientSecret`, `MicrosoftClientSecret`, `AcsConnectionString`) заводятся вручную (описать в `infra/README.md`)
- [x] `communication.bicep`: Communication Services + Email Service с **Azure Managed Domain** (без своего DNS); строка подключения → Key Vault
- [x] `Program.cs`: `builder.Services.AddOpenTelemetry().UseAzureMonitor()` только если задан `APPLICATIONINSIGHTS_CONNECTION_STRING`
- [x] `ci.yml` (pull_request + push): setup .NET 10 и Node 24 → `npm ci` + `npm test` + `npm run build` → `dotnet build -warnaserror` → `dotnet test` (Docker на `ubuntu-latest` есть) → `az bicep build --file infra/main.bicep`
- [x] `deploy.yml` (push в `main`, после CI): `azure/login` через OIDC (секреты `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), `npm run build`, `dotnet publish src/KnowledgeHub.Api -c Release -o out`, `azure/webapps-deploy`, затем проверка `curl --fail https://<app>/health`
- [x] `infra/README.md`: команды первого развёртывания и ручные шаги ⚠ (выполняет владелец подписки): создать resource group, `az deployment group create`, federated credential для GitHub, регистрация приложений Microsoft/Google (redirect URI `https://<app>/signin-microsoft`, `/signin-google`), заполнение секретов, создание роли приложения в PostgreSQL под Entra-администратором (`pgaadauth_create_principal` + `ALTER DATABASE knowledgehub OWNER TO "<app>"`)

**Проверка:**
- [x] `az bicep build --file infra/main.bicep` — без ошибок и предупреждений
- [ ] `az deployment group what-if -g <rg> --template-file infra/main.bicep --parameters infra/main.bicepparam` — план без ошибок (⚠ требует подписки) — **ждёт подписки Azure** (не выполнено)
- [ ] После первого деплоя: `curl https://<app>.azurewebsites.net/health` — `Healthy` — **ждёт подписки Azure** (не выполнено)

---

### Фича 5: Вход через Microsoft и Google ✅

**Цель:** пользователь входит через Microsoft или Google, получает cookie-сессию, `GET /api/me` возвращает профиль; изменяющие запросы с cookie защищены antiforgery.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Common/ICurrentUser.cs`, `src/KnowledgeHub.Api/Common/CurrentUser.cs`
- Создать: `src/KnowledgeHub.Api/Modules/Identity/IdentityModule.cs`, `.../Identity/User.cs`, `.../Identity/UserLogin.cs`, `.../Identity/UserSettings.cs`, `.../Identity/IdentityConfigurations.cs`, `.../Identity/UserProvisioningService.cs`, `.../Identity/AuthEndpoints.cs`, `.../Identity/MeEndpoints.cs`, `.../Identity/AuthSchemes.cs`
- Создать: `src/KnowledgeHub.Api/Infrastructure/Web/AntiforgeryFilter.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, `src/KnowledgeHub.Api/appsettings.json`, `Directory.Packages.props`
- Создать: `tests/KnowledgeHub.IntegrationTests/Infrastructure/TestAuthHandler.cs`, изменить `.../Infrastructure/ApiFactory.cs`
- Тесты: `tests/KnowledgeHub.IntegrationTests/Identity/AuthTests.cs`, `tests/KnowledgeHub.IntegrationTests/Identity/AntiforgeryTests.cs`, `tests/KnowledgeHub.UnitTests/Identity/UserProvisioningServiceTests.cs`

**Шаги:**
- [x] Сущности `users`, `user_logins` (PK `provider + subject`), `user_settings` (`timezone` по умолчанию `UTC`, `digest_frequency=off`, `digest_day=monday`, `digest_channels`, `email`); миграция `Identity`
- [x] Схемы: `Cookies` (основная, `HttpOnly`, `Secure`, `SameSite=Lax`, 30 дней скользящая), `External` (временная cookie для OAuth), `MicrosoftAccount` (пакет `Microsoft.AspNetCore.Authentication.MicrosoftAccount`, endpoint `common` — личные и рабочие аккаунты), `Google`; policy scheme `Smart` по умолчанию: заголовок `Authorization: Bearer` → `ApiToken` (фича 7), иначе `Cookies`
- [x] `GET /api/auth/login/{provider}?returnUrl=` — `Challenge` (только локальный `returnUrl`); `GET /api/auth/callback` — читает `External`, `UserProvisioningService.FindOrCreateAsync(provider, subject, email, name)`, вход в `Cookies` с claim `kh_user_id`, выход из `External`, редирект; `POST /api/auth/logout`
- [x] `ICurrentUser` (scoped): `UserId` из claim `kh_user_id`, `IsSystem`; `RequireUserId()` бросает при отсутствии
- [x] `GET /api/me` → `{ id, email, displayName, settings }`; `PUT /api/me/settings` (валидация IANA-таймзоны через `TimeZoneInfo.FindSystemTimeZoneById`)
- [x] Antiforgery: `HeaderName = "X-XSRF-TOKEN"`; middleware на `GET` выставляет читаемую cookie `XSRF-TOKEN`; `AntiforgeryFilter` на группу `/api` — для небезопасных методов при аутентификации схемой `Cookies` вызывает `ValidateRequestAsync`, ошибка → `400`
- [x] Dev-вход для локальной разработки и e2e: `POST /api/auth/dev-login { email }` регистрируется **только** при `Environment=Development` и `Auth:EnableDevLogin=true`
- [x] `TestAuthHandler` (схема `Test`, заголовок `X-Test-User`) и хелпер `CreateClientFor(userId)` в `ApiFactory`, создающий пользователя в БД
- [x] Тесты: `/api/me` без входа → `401`; с тестовым пользователем → профиль; повторный вход тем же `provider+subject` не создаёт второго пользователя; `returnUrl=https://evil.com` игнорируется; dev-login недоступен в `Production`; `POST` с cookie без XSRF → `400`, с XSRF → успех

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Identity"` — `Passed!  - Failed:     0`
- [x] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Identity"` — `Passed!  - Failed:     0`

---

### Фича 6: Изоляция данных — фильтр EF и Row-Level Security ✅

**Цель:** данные пользователя недоступны другим ни через EF, ни через сырой SQL; фоновые задачи могут работать в системном контексте; есть переиспользуемый тест изоляции.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Common/IUserOwned.cs`, `src/KnowledgeHub.Api/Infrastructure/Persistence/UserContextConnectionInterceptor.cs`, `.../Persistence/RlsMigrationExtensions.cs`, `src/KnowledgeHub.Api/Common/SystemContext.cs`
- Изменить: `src/KnowledgeHub.Api/Infrastructure/Persistence/AppDbContext.cs`, `.../Persistence/PersistenceExtensions.cs`
- Тесты: `tests/KnowledgeHub.IntegrationTests/Identity/RlsTests.cs`, `tests/KnowledgeHub.IntegrationTests/Identity/IsolationTests.cs`

**Шаги:**
- [x] `IUserOwned { Guid UserId }`; в `AppDbContext.OnModelCreating` — глобальный фильтр `e.UserId == currentUser.UserId` для всех `IUserOwned` (через цикл по типам, без копипасты); при сохранении новых `IUserOwned` `UserId` проставляется автоматически
- [x] `UserContextConnectionInterceptor.ConnectionOpened(Async)`: `SELECT set_config('app.user_id', @uid, false), set_config('app.is_system', @sys, false)` — оба значения на **каждое** открытие (п.1 «Подводных камней»)
- [x] `SystemContext.Enter()` (`IDisposable`, scoped): переводит `ICurrentUser` в системный режим (EF-фильтр отключается через флаг в выражении фильтра) или в режим конкретного пользователя `RunAs(userId)` — используется воркерами
- [x] `RlsMigrationExtensions.EnableUserRls(this MigrationBuilder, string table)`: `ENABLE` + `FORCE ROW LEVEL SECURITY` + политика `USING/WITH CHECK (user_id = nullif(current_setting('app.user_id', true), '')::uuid OR current_setting('app.is_system', true) = 'on')`; применить к `user_settings` (миграция `Rls`), далее — в каждой миграции с новой пользовательской таблицей
- [x] `RlsTests`: под пользователем A сырой SQL `SELECT count(*) FROM user_settings` видит только свою строку; без контекста — 0 строк; `INSERT` с чужим `user_id` → ошибка политики; в системном контексте видны все
- [x] `IsolationTests`: data-driven тест (`[Theory]` + `MemberData`) — список «как создать ресурс у A» + «запрос B» → ожидается `404`; пока одна запись (`/api/me/settings` не применима — добавить заглушку-пример и комментарий, как расширять)

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Rls|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 7: Персональные API-токены ✅

**Цель:** пользователь создаёт, просматривает и отзывает API-токены; запросы с `Authorization: Bearer <token>` аутентифицированы без antiforgery.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Identity/ApiToken.cs`, `.../Identity/ApiTokenService.cs`, `.../Identity/ApiTokenAuthenticationHandler.cs`, `.../Identity/ApiTokenEndpoints.cs`
- Изменить: `src/KnowledgeHub.Api/Modules/Identity/IdentityModule.cs`, `.../Identity/IdentityConfigurations.cs`
- Тесты: `tests/KnowledgeHub.UnitTests/Identity/ApiTokenServiceTests.cs`, `tests/KnowledgeHub.IntegrationTests/Identity/ApiTokenTests.cs`

**Шаги:**
- [x] Таблица `api_tokens` (`token_hash` unique, `expires_at` nullable); миграция с `EnableUserRls`
- [x] Формат токена: `kh_` + 32 случайных байта (`RandomNumberGenerator`, Base64Url); хранится SHA-256 hex
- [x] `POST /api/me/tokens { name, expiresInDays? }` → `{ id, name, token }` (токен — только в этом ответе); `GET /api/me/tokens` — без токенов; `DELETE /api/me/tokens/{id}` — проставляет `revoked_at`
- [x] `ApiTokenAuthenticationHandler`: поиск по хэшу в **системном** контексте (пользователь ещё неизвестен), проверка `revoked_at`/`expires_at`, обновление `last_used_at` не чаще раза в минуту; claim `kh_user_id`
- [x] Antiforgery-фильтр пропускает схему `ApiToken` (уже реализовано условием «только Cookies» — покрыть тестом)
- [x] Тесты: создание → запрос `/api/me` с токеном → `200`; отозванный/просроченный/неизвестный → `401`; `POST` с токеном без XSRF → успех; чужой токен нельзя отозвать (`404`); добавить эндпоинт в `IsolationTests`

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~ApiToken"` — `Passed!  - Failed:     0`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~ApiToken|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 8: SPA — вход, профиль, токены ✅

**Цель:** в браузере работает вход через Microsoft/Google, выход, страница настроек с профилем, часовым поясом и управлением API-токенами.

**Файлы:**
- Создать: `src/KnowledgeHub.Web/src/features/auth/LoginPage.tsx`, `.../features/auth/useMe.ts`, `.../features/auth/RequireAuth.tsx`
- Создать: `src/KnowledgeHub.Web/src/features/settings/SettingsPage.tsx`, `.../features/settings/ApiTokensSection.tsx`, `.../features/settings/ProfileSection.tsx`
- Изменить: `src/KnowledgeHub.Web/src/api/client.ts` (заголовок `X-XSRF-TOKEN` из cookie `XSRF-TOKEN` для небезопасных методов), `src/KnowledgeHub.Web/src/app/router.tsx`, `src/KnowledgeHub.Web/src/app/Layout.tsx`
- Тесты: `src/KnowledgeHub.Web/src/api/client.test.ts`, `src/KnowledgeHub.Web/src/features/auth/RequireAuth.test.tsx`, `src/KnowledgeHub.Web/src/features/settings/ApiTokensSection.test.tsx`

**Шаги:**
- [x] `LoginPage`: кнопки «Войти через Microsoft/Google» → `window.location = /api/auth/login/{provider}?returnUrl=...`; в dev — форма dev-login (если `import.meta.env.DEV`)
- [x] `useMe` (React Query) + `RequireAuth`: `401` → редирект на `/login`
- [x] `ProfileSection`: e-mail, имя, выбор часового пояса (`Intl.supportedValuesOf('timeZone')`), сохранение через `PUT /api/me/settings`
- [x] `ApiTokensSection`: список, создание (показ токена один раз с кнопкой «Скопировать» и предупреждением), отзыв с подтверждением
- [x] Выход: `POST /api/auth/logout` → сброс кэша React Query → `/login`
- [x] Тесты (msw): XSRF-заголовок добавляется к `POST` и не добавляется к `GET`; `RequireAuth` редиректит при `401`; новый токен показывается один раз и пропадает после закрытия

**Проверка:**
- [x] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят
- [x] Вручную: `docker compose up -d`, `dotnet run --project src/KnowledgeHub.Api`, `npm --prefix src/KnowledgeHub.Web run dev` → dev-login → страница настроек открывается, токен создаётся

---

### Фича 9: Записи — CRUD ✅

**Цель:** через API можно создавать, читать, редактировать, удалять записи типов `note`, `snippet`, `link` (без автоизвлечения), отмечать прочитанными; дубликаты ссылок не создаются.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Items/ItemsModule.cs`, `.../Items/Item.cs`, `.../Items/ItemType.cs`, `.../Items/ItemStatus.cs`, `.../Items/ItemConfiguration.cs`, `.../Items/IItemService.cs`, `.../Items/ItemService.cs`, `.../Items/ItemDtos.cs`, `.../Items/ItemEndpoints.cs`, `.../Items/UrlNormalizer.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`
- Тесты: `tests/KnowledgeHub.UnitTests/Items/UrlNormalizerTests.cs`, `tests/KnowledgeHub.IntegrationTests/Items/ItemEndpointsTests.cs`, изменить `tests/KnowledgeHub.IntegrationTests/Identity/IsolationTests.cs`

**Шаги:**
- [x] Сущность `items` по дизайну (`search_vector` добавляется в фиче 12), включая `read_at`; индексы: unique `(user_id, url)` где `url IS NOT NULL`, unique `(user_id, content_hash)` где `content_hash IS NOT NULL`, `(user_id, created_at DESC)`; миграция `Items` с `EnableUserRls`
- [x] `UrlNormalizer`: только `http/https`, нижний регистр хоста, удаление фрагмента `#...` и `utm_*`-параметров, без завершающего `/` у пути — используется для проверки дубликатов
- [x] `IItemService` (публичный интерфейс модуля, используется ботом и ingestion): `CreateAsync(CreateItemCommand)`, `GetAsync`, `UpdateAsync`, `DeleteAsync`, `MarkReadAsync(id, bool)`, `SetProcessingResultAsync(...)` (для фичи 15)
- [x] Создание: `note` и `snippet` → `status=ready`; `link` → `status=ready` (до фичи 15), проверка дубликата: при существующей записи вернуть её с `200` (а не `201`)
- [x] Эндпоинты: `POST /api/items`, `GET /api/items?type=&unread=&page=&pageSize=` (по умолчанию 20, максимум 100), `GET /api/items/{id}`, `PUT /api/items/{id}`, `PATCH /api/items/{id}` (`{ read: true|false }`), `DELETE /api/items/{id}`
- [x] Валидация: `title` ≤ 500, `user_note` ≤ 20 000, `body` для note/snippet ≤ 200 000, `code_language` ≤ 50, `url` обязателен для `link` → `400 ValidationProblem`
- [x] Тесты: каждый тип создаётся и читается; повторный `POST` той же ссылки (с `utm_source` и `#фрагментом`) → та же запись; `PATCH read` ставит/сбрасывает `read_at`; удаление; пагинация; ошибки валидации; все эндпоинты добавлены в `IsolationTests`

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Items"` — `Passed!  - Failed:     0`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Items|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 10: Теги и коллекции ✅

**Цель:** записи можно помечать тегами и добавлять в несколько плоских коллекций; есть списки тегов/коллекций и фильтрация записей по ним.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Items/Tag.cs`, `.../Items/ItemTag.cs`, `.../Items/Collection.cs`, `.../Items/CollectionItem.cs`, `.../Items/TaggingConfiguration.cs`, `.../Items/TagService.cs`, `.../Items/TagEndpoints.cs`, `.../Items/CollectionEndpoints.cs`
- Изменить: `src/KnowledgeHub.Api/Modules/Items/ItemService.cs`, `.../Items/ItemDtos.cs`, `.../Items/ItemEndpoints.cs`, `.../Items/ItemsModule.cs`
- Тесты: `tests/KnowledgeHub.UnitTests/Items/TagNameTests.cs`, `tests/KnowledgeHub.IntegrationTests/Items/TagsTests.cs`, `tests/KnowledgeHub.IntegrationTests/Items/CollectionsTests.cs`, изменить `.../Identity/IsolationTests.cs`

**Шаги:**
- [x] Таблицы `tags` (unique `(user_id, lower(name))`), `item_tags`, `collections`, `collection_items`; миграция с `EnableUserRls` для `tags` и `collections` (связующие таблицы защищаются через внешние ключи на защищённые таблицы + проверку в сервисе)
- [x] Нормализация имени тега: trim, без `#` в начале, ≤ 50 символов, без пробелов внутри (пробелы → `-`) — один метод `TagName.Normalize`, используется API и ботом
- [x] `TagService.ResolveAsync(names)` — находит или создаёт теги пользователя (DRY для API и бота)
- [x] `CreateItemCommand`/`UpdateItem` принимают `tags: string[]` и `collectionIds: Guid[]`; DTO записи возвращает теги и коллекции
- [x] `GET /api/tags` (с количеством записей), `PUT /api/tags/{id}` (переименование), `DELETE /api/tags/{id}`
- [x] `GET/POST /api/collections`, `PUT/DELETE /api/collections/{id}`, `POST /api/collections/{id}/items/{itemId}`, `DELETE /api/collections/{id}/items/{itemId}`
- [x] Фильтры `GET /api/items?tags=a,b&collection={id}` (теги — логическое И)
- [x] Тесты: `Azure` и `azure` — один тег; запись в двух коллекциях; удаление коллекции не удаляет записи; нельзя добавить чужую запись в свою коллекцию (`404`); фильтры; эндпоинты в `IsolationTests`

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Items"` — `Passed!  - Failed:     0`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Items|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 11: SPA — записи, теги, коллекции ✅

**Цель:** в вебе можно просматривать список записей, создавать и редактировать заметки, фрагменты кода и ссылки, назначать теги и коллекции, отмечать прочитанное.

**Файлы:**
- Создать: `src/KnowledgeHub.Web/src/features/items/ItemsPage.tsx`, `.../items/ItemCard.tsx`, `.../items/ItemPage.tsx`, `.../items/ItemForm.tsx`, `.../items/itemsApi.ts`, `.../items/TagInput.tsx`
- Создать: `src/KnowledgeHub.Web/src/features/collections/CollectionsPage.tsx`, `.../collections/collectionsApi.ts`
- Создать: `src/KnowledgeHub.Web/src/shared/Markdown.tsx`, `src/KnowledgeHub.Web/src/shared/CodeBlock.tsx`
- Изменить: `src/KnowledgeHub.Web/src/app/router.tsx`, `src/KnowledgeHub.Web/package.json`
- Тесты: `src/KnowledgeHub.Web/src/shared/Markdown.test.tsx`, `src/KnowledgeHub.Web/src/features/items/ItemForm.test.tsx`, `src/KnowledgeHub.Web/src/features/items/ItemsPage.test.tsx`

**Шаги:**
- [x] Зависимости: `react-markdown`, `dompurify`, `highlight.js`
- [x] `Markdown.tsx`: единственное место рендера Markdown — `react-markdown` без `rehype-raw` + DOMPurify для любого HTML; ссылки `target="_blank" rel="noopener noreferrer"`
- [x] `CodeBlock.tsx`: подсветка `highlight.js` по `code_language`, код всегда как текст (никакого `dangerouslySetInnerHTML` без DOMPurify)
- [x] `ItemsPage`: список с фильтрами (тип, тег, коллекция, «непрочитанное»), пагинация; `ItemCard` — тип, заголовок, теги, дата, отметка «прочитано»
- [x] `ItemForm`: выбор типа, поля по типу, `TagInput` (автодополнение из `/api/tags`), выбор коллекций; ошибки валидации из `ProblemDetails` у полей
- [x] `ItemPage`: просмотр, редактирование, удаление, кнопка «Прочитано»; для ссылок — ссылка на источник
- [x] `CollectionsPage`: CRUD коллекций и переход к записям коллекции
- [x] Тесты: `<script>` и `onerror=` в Markdown не попадают в DOM; форма показывает ошибку поля из `ProblemDetails`; фильтр «непрочитанное» передаёт `unread=true`

**Проверка:**
- [x] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят
- [x] `npm --prefix src/KnowledgeHub.Web run build` — без ошибок TypeScript

---

### Фича 12: Полнотекстовый поиск — бэкенд ✅

**Цель:** `GET /api/search` находит записи по словам из заголовка, тегов, заметки и текста с учётом морфологии RU/EN, ранжирует, фильтрует и подсвечивает фрагменты.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Search/SearchModule.cs`, `.../Search/ISearchIndexer.cs`, `.../Search/SearchIndexer.cs`, `.../Search/ISearchService.cs` (публичный интерфейс — его использует бот), `.../Search/SearchService.cs`, `.../Search/SearchDtos.cs`, `.../Search/SearchEndpoints.cs`
- Изменить: `src/KnowledgeHub.Api/Modules/Items/Item.cs` (`SearchVector: NpgsqlTsVector`), `.../Items/ItemConfiguration.cs` (GIN-индекс), `.../Items/ItemService.cs` и `.../Items/TagService.cs` (вызов `ISearchIndexer` после изменений), `src/KnowledgeHub.Api/Program.cs`
- Тесты: `tests/KnowledgeHub.IntegrationTests/Search/SearchTests.cs`, изменить `.../Identity/IsolationTests.cs`

**Шаги:**
- [x] Колонка `search_vector tsvector` + `CREATE INDEX ... USING GIN (search_vector)`; миграция `Search`
- [x] `SearchIndexer.ReindexAsync(itemIds)` — **один** SQL `UPDATE items SET search_vector = setweight(to_tsvector('russian', title),'A') || setweight(to_tsvector('russian', user_note || ' ' || tags || ' ' || url),'B') || setweight(to_tsvector('russian', left(body, 500000)),'C')` (теги через подзапрос `string_agg`, `coalesce` для null); вызывается после создания/изменения записи, изменения тегов записи, переименования/удаления тега
- [x] В той же миграции — пересчёт для существующих записей
- [x] `SearchService`: шаг 1 — `websearch_to_tsquery('russian', q)` + фильтры (`type`, `tags`, `collection`, `from`, `to`, `unread`) + `ts_rank_cd` → страница id; шаг 2 — `ts_headline` только для id страницы (`MaxFragments=2, MaxWords=30, StartSel=<mark>, StopSel=</mark>`); пустой `q` → `400`
- [x] Подсветка возвращается как текст с маркерами `<mark>`; остальной HTML в фрагменте экранируется на бэкенде (фронт разрешает только `<mark>`)
- [x] `GET /api/search?q=&type=&tags=&collection=&from=&to=&unread=&page=&pageSize=` → `{ total, items: [{ item, rank, highlights }] }`
- [x] Тесты: «очереди» находит «очередь сообщений»; «messages» находит «message»; смешанный запрос «очереди service bus»; совпадение в заголовке выше, чем только в тексте; поиск по тегу и по заметке; кавычки `"service bus"` — фраза; `-azure` исключает; переименование тега обновляет выдачу; HTML из текста экранирован в `highlights`; запись пользователя A не находится пользователем B

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Search|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 13: SPA — поиск (MVP) ✅

**Цель:** в вебе есть страница поиска со строкой запроса, фильтрами и подсветкой найденных фрагментов; сценарий «сохранил ссылку вручную → нашёл по заметке» работает end-to-end.

**Файлы:**
- Создать: `src/KnowledgeHub.Web/src/features/search/SearchPage.tsx`, `.../search/searchApi.ts`, `.../search/SearchResult.tsx`, `.../search/Highlight.tsx`
- Изменить: `src/KnowledgeHub.Web/src/app/Layout.tsx` (строка поиска в шапке), `src/KnowledgeHub.Web/src/app/router.tsx`
- Тесты: `src/KnowledgeHub.Web/src/features/search/Highlight.test.tsx`, `src/KnowledgeHub.Web/src/features/search/SearchPage.test.tsx`

**Шаги:**
- [x] Параметры поиска хранятся в URL (`/search?q=...&type=...`) — ссылкой можно поделиться, «назад» работает
- [x] Ввод с debounce 300 мс; фильтры переиспользуют компоненты фильтров из `ItemsPage` (вынести в `src/KnowledgeHub.Web/src/features/items/ItemFilters.tsx`, DRY)
- [x] `Highlight.tsx`: разбивает строку по `<mark>`/`</mark>` и рендерит `<mark>` React-элементами, остальное — текст (без `innerHTML`)
- [x] Пустое состояние, «ничего не найдено», ошибки `ApiError`
- [x] Тесты: `Highlight` не исполняет `<img onerror>`; изменение фильтра меняет URL и запрос

**Проверка:**
- [x] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят
- [x] Вручную: создать ссылку с заметкой «очереди сообщений в Azure» → поиск «очередь» находит её с подсветкой

---

### Фича 14: Очередь фоновых задач ✅

**Цель:** модули ставят задачи в очередь в PostgreSQL; воркер выполняет их с повторами, восстановлением после падения, дедупликацией и в контексте нужного пользователя; есть планировщик периодических действий.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Jobs/JobsModule.cs`, `.../Jobs/Job.cs`, `.../Jobs/JobConfiguration.cs`, `.../Jobs/IJobQueue.cs`, `.../Jobs/JobQueue.cs`, `.../Jobs/IJobHandler.cs`, `.../Jobs/IJobFailureHandler.cs`, `.../Jobs/JobWorker.cs`, `.../Jobs/JobOptions.cs`, `.../Jobs/IRecurringTask.cs`, `.../Jobs/RecurringScheduler.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, `src/KnowledgeHub.Api/appsettings.json`
- Тесты: `tests/KnowledgeHub.UnitTests/Jobs/RetryPolicyTests.cs`, `tests/KnowledgeHub.IntegrationTests/Jobs/JobQueueTests.cs`, `tests/KnowledgeHub.IntegrationTests/Jobs/JobWorkerTests.cs`

**Шаги:**
- [x] Таблица `jobs` по дизайну + `user_id` (nullable — для системных задач) + `dedup_key` (unique, nullable) + `created_at`; индекс `(status, run_after)`; RLS **не** включается (системная таблица, доступ только из модуля Jobs)
- [x] `IJobQueue.EnqueueAsync<TPayload>(type, payload, userId?, runAfter?, dedupKey?)` — `INSERT ... ON CONFLICT (dedup_key) DO NOTHING`; работает в текущей транзакции `AppDbContext` (задача и запись сохраняются атомарно)
- [x] Захват: `UPDATE jobs SET status='processing', locked_until=@now+5min, attempts=attempts+1 WHERE id = (SELECT id FROM jobs WHERE (status='pending' AND run_after<=@now) OR (status='processing' AND locked_until<@now) ORDER BY run_after FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING *`; `@now` из `TimeProvider`
- [x] `IJobHandler<TPayload>` с константой `Type`; регистрация `services.AddJobHandler<THandler, TPayload>()`; payload — `jsonb` через `System.Text.Json`
- [x] `JobWorker` (`BackgroundService`): опрос каждые 2 с (`JobOptions.PollInterval`), новый DI-scope на задачу, `SystemContext.RunAs(job.UserId)` или системный контекст; успех → `completed`; исключение → `pending` с `run_after = now + [1, 5, 30] мин` по `attempts`; после 4-й попытки → `failed` + `last_error` + лог `JobFailed` (на него завязан алерт в фиче 24); вызов `IJobFailureHandler.OnFailedAsync(job, error)` по типу задачи (для статуса записи — фича 15)
- [x] `IRecurringTask { Name; Interval; ExecuteAsync }` + `RecurringScheduler` (`PeriodicTimer`, системный контекст, исключение одной задачи не останавливает остальные)
- [x] Очистка: recurring-задача удаляет `completed` старше 7 дней
- [x] В `ApiFactory` воркер отключён (`Jobs:Enabled=false`); тесты вызывают `JobWorker.ProcessNextAsync()` вручную
- [x] Тесты: два параллельных захвата не получают одну задачу; задача с `run_after` в будущем не берётся; истёкший `locked_until` подхватывается; паузы 1/5/30 мин (`FakeTimeProvider`); `failed` после 4 попыток; повторный `dedup_key` не создаёт вторую задачу; обработчик видит данные только своего пользователя (RLS)

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Jobs"` — `Passed!  - Failed:     0`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Jobs"` — `Passed!  - Failed:     0`

---

### Фича 15: Безопасная загрузка страниц и извлечение статей ✅

**Цель:** после сохранения ссылки воркер безопасно скачивает страницу, извлекает заголовок и текст через SmartReader, обновляет поиск и статус; неудача не теряет запись; есть повторная обработка.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Ingestion/IngestionModule.cs`, `.../Ingestion/SsrfGuard.cs`, `.../Ingestion/SafeHttpFetcher.cs`, `.../Ingestion/FetchResult.cs`, `.../Ingestion/ArticleExtractor.cs`, `.../Ingestion/IngestItemJob.cs`, `.../Ingestion/IngestFailureHandler.cs` (`IJobFailureHandler` для `ingest`), `.../Ingestion/IngestionOptions.cs`, `.../Ingestion/IIngestionCompletedHandler.cs`
- Изменить: `src/KnowledgeHub.Api/Modules/Items/ItemService.cs` (ссылка → `pending` + задача `ingest` в той же транзакции), `.../Items/ItemEndpoints.cs` (`POST /api/items/{id}/reprocess`), `src/KnowledgeHub.Api/Program.cs`
- Тесты: `tests/KnowledgeHub.UnitTests/Ingestion/SsrfGuardTests.cs`, `tests/KnowledgeHub.UnitTests/Ingestion/ArticleExtractorTests.cs`, `tests/KnowledgeHub.UnitTests/Ingestion/Fixtures/*.html`, `tests/KnowledgeHub.IntegrationTests/Ingestion/LinkIngestionTests.cs`

**Шаги:**
- [x] `SsrfGuard.IsAllowed(IPAddress)`: запрет loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, fe80::/10), CGNAT 100.64/10, 0.0.0.0/8, multicast, broadcast, unique local fc00::/7, IPv4-mapped IPv6 (проверять `MapToIPv4()`), `::`
- [x] `SafeHttpFetcher`: `SocketsHttpHandler { ConnectCallback = резолв + проверка всех адресов + подключение к проверенному IP, AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = All, PooledConnectionLifetime = 5 мин }`; только `http/https` и порты 80/443; ручные редиректы ≤ 5 (каждый URL заново валидируется); таймаут 15 с; чтение тела с лимитом 5 МБ (превышение → ошибка); принимаются только `text/html`/`application/xhtml+xml`; кодировка из заголовка/`<meta charset>`
- [x] Для тестов `SafeHttpFetcher` принимает опцию `AllowLoopbackForTests` (только из `ApiFactory`), чтобы ходить в WireMock на `localhost` — по умолчанию `false`
- [x] `ArticleExtractor`: `new Reader(url, html).GetArticle()` (SmartReader **не** скачивает сам — передаём HTML); `IsReadable` → `title`, `body = TextContent`; иначе — только `title` из страницы; ни заголовка, ни текста → ошибка «Не удалось извлечь содержимое»
- [x] `IngestItemJob` (`type = "ingest"`): `processing` → fetch → extract → `IItemService.SetProcessingResultAsync` (заголовок пользователя не перезаписывается, если задан) → `ISearchIndexer.ReindexAsync` → `ready`; уведомление `IIngestionCompletedHandler` (реализует бот в фиче 19; по умолчанию — пустая реализация)
- [x] Окончательная неудача задачи → запись `failed` + понятный `error` (без стека); запись остаётся находимой по URL/заметке
- [x] `POST /api/items/{id}/reprocess` → `pending` + новая задача; для `note`/`snippet` → `400`
- [x] Тесты unit: таблица адресов (`127.0.0.1`, `10.1.2.3`, `169.254.169.254`, `::1`, `::ffff:127.0.0.1`, `fd00::1`, `100.64.0.1`, `8.8.8.8` ✔); `http://2130706433/` и `http://0x7f.0.0.1/` отклоняются после резолва; извлечение из 3 фикстур (статья, страница без статьи, кириллица в `windows-1251`)
- [x] Тесты интеграционные (WireMock): статья → `ready` + находится поиском по тексту; редирект на `http://169.254.169.254/` → `failed`; ответ 6 МБ → `failed`; `application/pdf` → `failed`; 503 → повтор → затем успех; `reprocess`

**Проверка:**
- [x] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Ingestion"` — `Passed!  - Failed:     0`
- [x] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Ingestion"` — `Passed!  - Failed:     0`

---

### Фича 16: Загрузка и обработка документов

**Цель:** пользователь загружает PDF, DOCX, TXT или MD до 20 МБ; файл хранится в приватном Blob, текст извлекается в фоне и находится поиском; файл можно скачать только владельцу.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Infrastructure/Storage/IBlobStore.cs`, `.../Storage/AzureBlobStore.cs`, `.../Storage/StorageOptions.cs`
- Создать: `src/KnowledgeHub.Api/Modules/Ingestion/FileSignature.cs`, `.../Ingestion/DocumentTextExtractor.cs`, `.../Ingestion/IDocumentIntake.cs`, `.../Ingestion/DocumentIntake.cs` (общий приём файла — для API и бота)
- Изменить: `src/KnowledgeHub.Api/Modules/Ingestion/IngestItemJob.cs` (ветка `document`), `src/KnowledgeHub.Api/Modules/Items/ItemEndpoints.cs` (`POST /api/items/upload`, `GET /api/items/{id}/file`), `.../Items/ItemService.cs`, `src/KnowledgeHub.Api/Program.cs`, `Directory.Packages.props`
- Изменить: `tests/KnowledgeHub.IntegrationTests/Infrastructure/ApiFactory.cs` (Azurite через Testcontainers)
- Тесты: `tests/KnowledgeHub.UnitTests/Ingestion/FileSignatureTests.cs`, `tests/KnowledgeHub.UnitTests/Ingestion/DocumentTextExtractorTests.cs`, `tests/KnowledgeHub.UnitTests/Ingestion/Fixtures/sample.pdf|sample.docx|sample.txt`, `tests/KnowledgeHub.IntegrationTests/Ingestion/DocumentUploadTests.cs`

**Шаги:**
- [ ] `IBlobStore`: `UploadAsync(container, path, stream, contentType)`, `OpenReadAsync`, `DeleteAsync`, `OpenWriteAsync` (для экспорта); `AzureBlobStore` — `BlobServiceClient` через `DefaultAzureCredential` (Azure) или строку подключения (локально, Azurite); контейнеры создаются при старте, если их нет
- [ ] `FileSignature.Detect(header, fileName)`: PDF — `%PDF-`; DOCX — `PK\x03\x04` + успешное открытие `WordprocessingDocument`; TXT/MD — расширение `.txt/.md`, валидный UTF-8, без `\0`; остальное → `400`
- [ ] `IDocumentIntake.AcceptAsync(stream, fileName, metadata)` — вся логика приёма ниже (сигнатура, хэш, Blob, запись, задача); эндпоинт и бот (фича 18) только вызывают его
- [ ] `POST /api/items/upload` (multipart: `file`, `title?`, `userNote?`, `tags?`, `collectionIds?`): `.WithMetadata(new RequestSizeLimitAttribute(21 МБ))` + `FormOptions.MultipartBodyLengthLimit`, `.DisableAntiforgery()` (п.7 «Подводных камней»; наш фильтр продолжает проверять cookie-запросы); потоковый SHA-256 → `content_hash` (дубликат → существующая запись); путь блоба `{userId}/{itemId}/{безопасное имя}`; запись `document` + задача `ingest` в одной транзакции (при ошибке БД — удалить блоб)
- [ ] `DocumentTextExtractor`: PDF — `PdfDocument.Open` + текст страниц (≤ 2000 страниц), DOCX — текст абзацев `Body.Descendants<Paragraph>()`, TXT/MD — как есть; таймаут 60 с через `Task.Run(...).WaitAsync` (п.10 «Подводных камней»); заголовок по умолчанию — имя файла без расширения
- [ ] `GET /api/items/{id}/file` — поток из Blob, `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, исходный `Content-Type` только из белого списка, иначе `application/octet-stream`
- [ ] Удаление записи-документа удаляет блоб
- [ ] Тесты unit: сигнатуры (EXE, переименованный в `.pdf` → отклонён); извлечение из трёх фикстур
- [ ] Тесты интеграционные: загрузка PDF → после `ProcessNextAsync` `ready` и поиск по тексту из PDF; загрузка с API-токеном без XSRF → успех, с cookie без XSRF → `400`; 21 МБ → `413`; повторная загрузка того же файла → та же запись; скачивание с `attachment`; пользователь B не может скачать (`404`, добавить в `IsolationTests`)

**Проверка:**
- [ ] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Ingestion"` — `Passed!  - Failed:     0`
- [ ] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Ingestion|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 17: SPA — статус обработки, загрузка файлов, повтор

**Цель:** в вебе можно загрузить документ, видеть статус обработки записи с автообновлением, ошибку и кнопку «Повторить».

**Файлы:**
- Создать: `src/KnowledgeHub.Web/src/features/items/UploadForm.tsx`, `.../items/ItemStatusBadge.tsx`
- Изменить: `src/KnowledgeHub.Web/src/features/items/itemsApi.ts`, `.../items/ItemPage.tsx`, `.../items/ItemCard.tsx`, `.../items/ItemsPage.tsx`, `src/KnowledgeHub.Web/src/api/client.ts` (поддержка `FormData`)
- Тесты: `src/KnowledgeHub.Web/src/features/items/ItemStatusBadge.test.tsx`, `src/KnowledgeHub.Web/src/features/items/UploadForm.test.tsx`

**Шаги:**
- [ ] `UploadForm`: выбор файла и drag&drop, проверка размера (≤ 20 МБ) и расширения на клиенте, теги и коллекции (переиспользовать `TagInput`), прогресс отправки
- [ ] Опрос записи каждые 2 с, пока `pending`/`processing` (`refetchInterval` React Query), затем остановка
- [ ] `ItemStatusBadge`: «В обработке» / «Готово» / «Ошибка» с текстом ошибки; кнопка «Повторить» → `POST /api/items/{id}/reprocess`
- [ ] `ItemPage` для документа: кнопка «Скачать» (`/api/items/{id}/file`), извлечённый текст
- [ ] Тесты: опрос прекращается на `ready`; файл 25 МБ отклоняется без запроса; «Повторить» вызывает API

**Проверка:**
- [ ] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят

---

### Фича 18: Telegram-бот — привязка и сохранение

**Цель:** пользователь привязывает Telegram по одноразовой ссылке; ссылки, текст и файлы, присланные боту, сохраняются как записи с тегами из хэштегов.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Bot/BotModule.cs`, `.../Bot/BotOptions.cs`, `.../Bot/TelegramLink.cs`, `.../Bot/TelegramLinkCode.cs`, `.../Bot/BotConfiguration.cs`, `.../Bot/WebhookEndpoint.cs`, `.../Bot/ProcessUpdateJob.cs`, `.../Bot/MessageParser.cs`, `.../Bot/TelegramLinkService.cs`, `.../Bot/LinkEndpoints.cs`, `.../Bot/WebhookRegistration.cs`, `.../Bot/ITelegramClient.cs`, `.../Bot/TelegramClient.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, `Directory.Packages.props`, `src/KnowledgeHub.Api/appsettings.json`
- Тесты: `tests/KnowledgeHub.UnitTests/Bot/MessageParserTests.cs`, `tests/KnowledgeHub.IntegrationTests/Bot/WebhookTests.cs`, `tests/KnowledgeHub.IntegrationTests/Bot/LinkingTests.cs`, `tests/KnowledgeHub.IntegrationTests/Infrastructure/FakeTelegramClient.cs`

**Шаги:**
- [ ] Таблицы `telegram_links` (`chat_id` unique) и `telegram_link_codes` (`code_hash`, `user_id`, `expires_at`); миграция с `EnableUserRls` для обеих
- [ ] `ITelegramClient` — тонкая обёртка над `TelegramBotClient` (v22: `SendMessage`, `GetFile`, `DownloadFile`, `AnswerCallbackQuery`, `SetWebhook`) — подменяется `FakeTelegramClient` в тестах
- [ ] `POST /api/me/telegram/link` → `{ url: "https://t.me/<bot>?start=<code>", expiresAt }` (код 16 байт Base64Url, хранится хэш, 10 мин); `DELETE /api/me/telegram/link`; `GET /api/me/telegram` → статус привязки
- [ ] `POST /telegram/webhook`: сравнение `X-Telegram-Bot-Api-Secret-Token` через `CryptographicOperations.FixedTimeEquals` (неверный → `401`); тело → `JsonSerializer.Deserialize<Update>(..., JsonBotAPI.Options)`; задача `telegram.update` с `dedupKey = "tg:{update_id}"`; ответ `200` сразу; эндпоинт без cookie-аутентификации и antiforgery
- [ ] `WebhookRegistration` (hosted service, только если задан `Bot:WebhookUrl`): `SetWebhook(url, secretToken, allowedUpdates: [Message, CallbackQuery])`
- [ ] `ProcessUpdateJob`: `/start <code>` → проверка кода (системный контекст) → `telegram_links` → ответ «Аккаунт привязан»; непривязанный `chat_id` → инструкция; привязанный → `SystemContext.RunAs(userId)` и `MessageParser`
- [ ] `MessageParser`: первая ссылка из `entities` (`url`/`text_link`) → `link`, остальной текст/подпись → `user_note`; текст без ссылок → `note`; `document` → скачать через `GetFile`/`DownloadFile` (≤ 20 МБ, иначе ответ с объяснением) → `IDocumentIntake.AcceptAsync` из фичи 16 (DRY); хэштеги → теги (`TagName.Normalize`)
- [ ] Создание записей — только через `IItemService` (DRY с API); ответ «Сохранено»
- [ ] Тесты unit: ссылка + подпись + `#azure #dotnet`; `text_link`; только текст; несколько ссылок (первая — запись, остальные — в заметке)
- [ ] Тесты интеграционные: неверный секрет → `401`; повтор того же `update_id` → одна запись; привязка по коду; просроченный код; сообщение непривязанного чата не создаёт запись; присланный PDF становится документом

**Проверка:**
- [ ] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Bot"` — `Passed!  - Failed:     0`
- [ ] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Bot"` — `Passed!  - Failed:     0`

---

### Фича 19: Telegram-бот — команды и уведомления

**Цель:** бот сообщает «Готово: \<заголовок\>» после обработки, выполняет `/search` и `/recent`, обрабатывает кнопку «Прочитано» и отключает привязку, если бот заблокирован.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Bot/BotCommands.cs`, `.../Bot/BotNotifier.cs`, `.../Bot/IBotNotifier.cs`, `.../Bot/IngestionCompletedNotifier.cs`, `.../Bot/BotMessageFormatter.cs`
- Изменить: `src/KnowledgeHub.Api/Modules/Bot/ProcessUpdateJob.cs`, `.../Bot/MessageParser.cs` (передача `chat_id` в задачу `ingest`), `src/KnowledgeHub.Api/Modules/Ingestion/IngestItemJob.cs` (payload `notifyChatId?`)
- Тесты: `tests/KnowledgeHub.UnitTests/Bot/BotMessageFormatterTests.cs`, `tests/KnowledgeHub.IntegrationTests/Bot/CommandsTests.cs`, `tests/KnowledgeHub.IntegrationTests/Bot/NotificationsTests.cs`

**Шаги:**
- [ ] `IBotNotifier.SendAsync(userId, text, buttons?)` — общий метод отправки (используется уведомлениями, напоминаниями и дайджестом — DRY); ошибка `403 Forbidden` от Telegram → удаление `telegram_links` пользователя и лог
- [ ] `IngestionCompletedNotifier : IIngestionCompletedHandler` — если в payload есть `notifyChatId`: «Готово: \<заголовок\>» или «Не удалось обработать: \<ошибка\>»
- [ ] `/search <запрос>` — через `ISearchService` (DRY), топ-5: заголовок, ссылка на запись в вебе (`{Bot:WebBaseUrl}/items/{id}`); `/recent` — 5 последних записей
- [ ] `BotMessageFormatter`: HTML-разметка Telegram с экранированием `<`, `>`, `&` во всём пользовательском тексте
- [ ] Inline-кнопка «Прочитано» (`callback_data = "read:{itemId}"`) → `IItemService.MarkReadAsync` в контексте пользователя чата → `AnswerCallbackQuery`; чужой `itemId` → «Запись не найдена»
- [ ] Тесты: после `ingest` бот отправил «Готово»; `/search` возвращает ≤ 5 результатов только своего пользователя; `callback read` ставит `read_at`; `403` при отправке удаляет привязку; экранирование `<script>` в заголовке

**Проверка:**
- [ ] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Bot"` — `Passed!  - Failed:     0`
- [ ] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Bot"` — `Passed!  - Failed:     0`

---

### Фича 20: Напоминания по дате

**Цель:** пользователь ставит напоминание на запись; в назначенное время оно приходит в Telegram и/или на e-mail ровно один раз.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Reminders/RemindersModule.cs`, `.../Reminders/Reminder.cs`, `.../Reminders/ReminderConfiguration.cs`, `.../Reminders/ReminderEndpoints.cs`, `.../Reminders/DueRemindersTask.cs`, `.../Reminders/SendReminderJob.cs`, `.../Reminders/NotificationChannel.cs`, `.../Reminders/INotificationDispatcher.cs`, `.../Reminders/NotificationDispatcher.cs`
- Создать: `src/KnowledgeHub.Api/Infrastructure/Email/IEmailSender.cs`, `.../Email/AcsEmailSender.cs`, `.../Email/EmailOptions.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, `Directory.Packages.props`, `src/KnowledgeHub.Api/appsettings.json`
- Создать: `tests/KnowledgeHub.IntegrationTests/Infrastructure/FakeEmailSender.cs`
- Тесты: `tests/KnowledgeHub.IntegrationTests/Reminders/ReminderEndpointsTests.cs`, `tests/KnowledgeHub.IntegrationTests/Reminders/ReminderDeliveryTests.cs`, изменить `.../Identity/IsolationTests.cs`

**Шаги:**
- [ ] Таблица `reminders` (`channel`: `telegram|email|both`), индекс `(remind_at) WHERE sent_at IS NULL`; миграция с `EnableUserRls`
- [ ] `POST /api/reminders { itemId, remindAt, channel }` (дата в будущем, запись своя), `GET /api/reminders?upcoming=true`, `GET /api/items/{id}/reminders`, `DELETE /api/reminders/{id}`
- [ ] `IEmailSender.SendAsync(to, subject, html, text)`; `AcsEmailSender` — `EmailClient` (строка подключения из Key Vault reference), `WaitUntil.Started`, отправитель из `Email:From` (адрес Azure Managed Domain)
- [ ] `INotificationDispatcher.SendAsync(userId, channels, message)` — выбирает `IBotNotifier` и/или `IEmailSender` (e-mail из `user_settings.email`, иначе из профиля); канал без настройки пропускается с логом — **общий** для напоминаний и дайджеста (DRY)
- [ ] `DueRemindersTask : IRecurringTask` (каждую минуту, системный контекст): напоминания `remind_at <= now AND sent_at IS NULL` → задача `reminder.send` с `dedupKey = "reminder:{id}"`
- [ ] `SendReminderJob`: в контексте пользователя → текст «Напоминание: \<заголовок\>» + ссылка + кнопка «Прочитано» (Telegram) → `sent_at = now`
- [ ] Тесты: создание/удаление/валидация (дата в прошлом → `400`); с `FakeTimeProvider` — до срока не отправлено, после — отправлено один раз в нужные каналы; повторный запуск задачи не дублирует; e-mail без адреса — Telegram всё равно отправлен; эндпоинты в `IsolationTests`

**Проверка:**
- [ ] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Reminders|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`

---

### Фича 21: Дайджест

**Цель:** по настройкам пользователя (ежедневно или раз в неделю в выбранный день) в 9:00 по его часовому поясу приходит подборка: непрочитанное, «сохранено год назад», ближайшие напоминания.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Reminders/DigestTask.cs`, `.../Reminders/SendDigestJob.cs`, `.../Reminders/DigestBuilder.cs`, `.../Reminders/DigestSchedule.cs`, `.../Reminders/DigestFormatter.cs`
- Изменить: `src/KnowledgeHub.Api/Modules/Reminders/RemindersModule.cs`, `src/KnowledgeHub.Api/Modules/Items/IItemService.cs` (запросы для дайджеста)
- Тесты: `tests/KnowledgeHub.UnitTests/Reminders/DigestScheduleTests.cs`, `tests/KnowledgeHub.UnitTests/Reminders/DigestFormatterTests.cs`, `tests/KnowledgeHub.IntegrationTests/Reminders/DigestTests.cs`

**Шаги:**
- [ ] `DigestSchedule.IsDue(settings, utcNow)` — чистая функция: локальное время (`TimeZoneInfo.ConvertTimeFromUtc`) ≥ 09:00, частота `daily` или `weekly` + совпадение `digest_day`; возвращает локальную дату для ключа
- [ ] `DigestTask : IRecurringTask` (каждый час, системный контекст): для пользователей с `digest_frequency != off`, у которых `IsDue` → задача `digest.send` с `dedupKey = "digest:{userId}:{localDate}"` (пропущенный час после рестарта не теряет дайджест и не дублирует его)
- [ ] `DigestBuilder` (в контексте пользователя, через `IItemService`): до 10 непрочитанных старше 3 дней (самые старые первыми); записи, сохранённые ровно год назад ±3 дня; напоминания на ближайшие 7 дней; всё пусто → дайджест не отправляется
- [ ] `DigestFormatter`: HTML-письмо + текстовая версия, сообщение Telegram (экранирование — через `BotMessageFormatter`, DRY); отправка через `INotificationDispatcher` по `digest_channels`
- [ ] Тесты unit: `Europe/Moscow` 08:59 → нет, 09:00 → да; weekly в другой день → нет; переход на летнее время (`America/New_York`) корректен; экранирование
- [ ] Тесты интеграционные: дайджест содержит непрочитанное и «год назад», не содержит прочитанное и чужие записи; второй запуск в тот же день не отправляет повторно; пустой дайджест не отправляется

**Проверка:**
- [ ] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Reminders"` — `Passed!  - Failed:     0`
- [ ] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Reminders"` — `Passed!  - Failed:     0`

---

### Фича 22: SPA — напоминания, дайджест, привязка Telegram

**Цель:** в вебе можно ставить и удалять напоминания на записи, видеть ближайшие, настраивать дайджест и каналы, привязывать и отвязывать Telegram.

**Файлы:**
- Создать: `src/KnowledgeHub.Web/src/features/reminders/ReminderPicker.tsx`, `.../reminders/UpcomingReminders.tsx`, `.../reminders/remindersApi.ts`
- Создать: `src/KnowledgeHub.Web/src/features/settings/DigestSection.tsx`, `.../settings/TelegramSection.tsx`
- Изменить: `src/KnowledgeHub.Web/src/features/items/ItemPage.tsx`, `src/KnowledgeHub.Web/src/features/settings/SettingsPage.tsx`, `src/KnowledgeHub.Web/src/app/Layout.tsx`
- Тесты: `src/KnowledgeHub.Web/src/features/reminders/ReminderPicker.test.tsx`, `src/KnowledgeHub.Web/src/features/settings/TelegramSection.test.tsx`

**Шаги:**
- [ ] `ReminderPicker` на странице записи: дата/время в часовом поясе пользователя (конвертация в UTC перед отправкой), быстрые варианты («завтра», «через неделю», «через месяц»), канал; список напоминаний записи с удалением
- [ ] `UpcomingReminders`: блок на главной/в меню
- [ ] `DigestSection`: частота (`off/daily/weekly`), день недели, каналы, e-mail
- [ ] `TelegramSection`: статус; «Привязать» → открыть `t.me`-ссылку и опрашивать статус до привязки (≤ 10 мин); «Отвязать»
- [ ] Тесты: время из пояса `Asia/Yekaterinburg` уходит на сервер в верном UTC; дата в прошлом недоступна; опрос статуса Telegram прекращается после привязки

**Проверка:**
- [ ] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят

---

### Фича 23: Экспорт данных

**Цель:** пользователь запускает экспорт и скачивает ZIP с Markdown-файлами (YAML-метаданные), исходными документами и `export.json`; архивы удаляются через 7 дней.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Modules/Export/ExportModule.cs`, `.../Export/ExportRecord.cs`, `.../Export/ExportConfiguration.cs`, `.../Export/ExportEndpoints.cs`, `.../Export/BuildExportJob.cs`, `.../Export/MarkdownWriter.cs`, `.../Export/ExportJsonModel.cs`, `.../Export/ExpiredExportsCleanupTask.cs`
- Создать: `src/KnowledgeHub.Web/src/features/export/ExportSection.tsx`, `src/KnowledgeHub.Web/src/features/export/exportApi.ts`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, `Directory.Packages.props`, `src/KnowledgeHub.Web/src/features/settings/SettingsPage.tsx`
- Тесты: `tests/KnowledgeHub.UnitTests/Export/MarkdownWriterTests.cs`, `tests/KnowledgeHub.IntegrationTests/Export/ExportTests.cs`, `src/KnowledgeHub.Web/src/features/export/ExportSection.test.tsx`, изменить `.../Identity/IsolationTests.cs`

**Шаги:**
- [ ] Таблица `exports`; миграция с `EnableUserRls`
- [ ] `POST /api/exports` → `202 { id }` + задача `export.build` (не больше одного активного экспорта на пользователя → `409`); `GET /api/exports` (список со статусами); `GET /api/exports/{id}/download` — поток из Blob, `attachment; filename="knowledgehub-export-YYYY-MM-DD.zip"`
- [ ] `MarkdownWriter`: имя `items/{slug}-{первые 8 символов id}.md` (slug — транслит/латиница, ≤ 60 символов); front matter через YamlDotNet (`id, type, title, url, tags, collections, created, updated, read, code_language, file`); тело: заметка пользователя, затем текст; фрагменты кода — в блоке ```` ```lang ````
- [ ] `BuildExportJob` (в контексте пользователя): потоковая запись ZIP прямо в Blob (`IBlobStore.OpenWriteAsync` + `ZipArchive`), записи читаются порциями по 100 (`AsAsyncEnumerable`), документы — `files/{itemId}/{имя}`, в конце `export.json` (записи, теги, коллекции, напоминания, настройки); `ready` + `expires_at = now + 7 дней`
- [ ] `ExpiredExportsCleanupTask` (раз в сутки, системный контекст): удаляет блоб и запись
- [ ] `ExportSection` в настройках: кнопка «Экспортировать», список с опросом статуса, «Скачать»
- [ ] Тесты unit: YAML корректно экранирует `:` и кавычки в заголовке; slug для кириллицы; блок кода
- [ ] Тесты интеграционные: архив содержит все записи пользователя, файлы документов и валидный `export.json`; не содержит чужих записей; второй экспорт параллельно → `409`; просроченный удаляется; скачивание чужого → `404` (`IsolationTests`)
- [ ] Тест фронта: кнопка неактивна, пока экспорт в процессе

**Проверка:**
- [ ] `dotnet test tests/KnowledgeHub.UnitTests --filter "FullyQualifiedName~Export"` — `Passed!  - Failed:     0`
- [ ] `dotnet test tests/KnowledgeHub.IntegrationTests --filter "FullyQualifiedName~Export|FullyQualifiedName~Isolation"` — `Passed!  - Failed:     0`
- [ ] `npm --prefix src/KnowledgeHub.Web test` — все тесты проходят

---

### Фича 24: Надёжность и релиз

**Цель:** включены лимиты запросов и алерты, smoke-тесты Playwright проходят в CI, проверены безопасность, бэкапы и расходы — систему можно открывать другим пользователям.

**Файлы:**
- Создать: `src/KnowledgeHub.Api/Infrastructure/Web/RateLimitingExtensions.cs`
- Изменить: `src/KnowledgeHub.Api/Program.cs`, эндпоинты модулей (`RequireRateLimiting(...)`)
- Создать: `infra/modules/alerts.bicep`, `infra/modules/budget.bicep`; изменить `infra/main.bicep`
- Создать: `src/KnowledgeHub.Web/playwright.config.ts`, `src/KnowledgeHub.Web/e2e/smoke.spec.ts`
- Изменить: `.github/workflows/ci.yml` (job `e2e`), `src/KnowledgeHub.Web/package.json` (`test:e2e`)
- Создать: `docs/security-checklist.md`, `docs/operations.md`
- Тесты: `tests/KnowledgeHub.IntegrationTests/Infrastructure/RateLimitingTests.cs`, `src/KnowledgeHub.Web/e2e/smoke.spec.ts`

**Шаги:**
- [ ] Rate limiting (`AddRateLimiter`): партиция по `kh_user_id`, иначе по IP (после forwarded headers); политики: `default` 120/мин, `auth` 10/мин на IP (`/api/auth/*`), `webhook` 60/мин, `upload` 10/мин; ответ `429` + `Retry-After`; `UseRateLimiter()` — **после** `UseForwardedHeaders`/`UseAuthentication()`, иначе claim `kh_user_id` и реальный IP ещё недоступны; в `ApiFactory` лимиты можно переопределить конфигурацией
- [ ] `alerts.bicep`: исключения > 10 за 15 мин; `/health` недоступен (availability test); рост `failed`-задач — custom metric/лог-запрос по сообщению `JobFailed` (воркер логирует его в фиче 14 — проверить)
- [ ] `budget.bicep`: бюджет resource group $60/мес, уведомления на 80% и 100% (e-mail из параметра)
- [ ] Playwright: `webServer` запускает API (`ASPNETCORE_ENVIRONMENT=Development`, `Auth__EnableDevLogin=true`) против `docker compose`; сценарий: dev-login → создать ссылку `https://example.com` с заметкой «проверка поиска» → дождаться «Готово» → найти поиском «проверка» → отметить прочитанной
- [ ] CI job `e2e`: `docker compose up -d`, `npx playwright install --with-deps chromium`, `npm run test:e2e`; артефакт `playwright-report` при падении
- [ ] `docs/security-checklist.md`: пройти таблицу безопасности из дизайна, по каждой строке — где реализовано и каким тестом покрыто; заголовки ответа: `Content-Security-Policy` (`default-src 'self'`), `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options: DENY` — добавить middleware, если не хватает
- [ ] `docs/operations.md`: восстановление PostgreSQL на точку во времени (команда `az postgres flexible-server restore`), восстановление блоба из soft delete, ротация секретов, ручная повторная регистрация webhook
- [ ] ⚠ Вручную в Azure: восстановить БД из бэкапа во временный сервер и проверить данные; убедиться, что рестарт App Service не разлогинивает (п.13 «Подводных камней»); сверить фактические расходы с оценкой ~$30/мес
- [ ] Тесты: 11-й запрос входа за минуту → `429`; запросы двух пользователей не делят лимит

**Проверка:**
- [ ] `dotnet test KnowledgeHub.slnx` — `Passed!  - Failed:     0`
- [ ] `npm --prefix src/KnowledgeHub.Web run test:e2e` — `1 passed`
- [ ] `az bicep build --file infra/main.bicep` — без ошибок
- [ ] `curl -I https://<app>.azurewebsites.net/` — есть заголовки `Content-Security-Policy` и `X-Content-Type-Options: nosniff`

---

## Источники

- Версии пакетов: [NuGet API](https://api.nuget.org/v3-flatcontainer/), [npm registry](https://registry.npmjs.org/), [.NET releases index](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json), шаблон `create-vite` `template-react-ts`
- [Entra ID + Npgsql (PeriodicPasswordProvider)](https://devblogs.microsoft.com/dotnet/using-postgre-sql-with-dotnet-and-entra-id/), [Managed identity для PostgreSQL Flexible Server](https://learn.microsoft.com/da-dk/azure/postgresql/security/security-connect-with-managed-identity), [Marten — Azure managed identity](https://martendb.io/configuration/azure-managed-identity)
- [Управление доступом в Azure PostgreSQL Flexible Server (`azure_pg_admin`)](https://learn.microsoft.com/en-us/azure/postgresql/security/security-access-control), [Q&A: Entra-роли и BYPASSRLS](https://learn.microsoft.com/en-us/answers/questions/2046046/ad-integration-on-postgresql-flexible-server-is-cr)
- [Поддерживаемые версии PostgreSQL в Azure](https://docs.azure.cn/en-us/postgresql/flexible-server/concepts-supported-versions)
- [RLS в PostgreSQL](https://oneuptime.com/blog/post/2026-01-25-use-row-level-security-postgresql/markdown)
- [.NET 10: cookie-аутентификация и API-эндпоинты](https://learn.microsoft.com/th-th/dotnet/core/compatibility/aspnet-core/10/cookie-authentication-api-endpoints)
- [SocketsHttpHandler.ConnectCallback](https://learn.microsoft.com/nl-nl/dotnet/api/system.net.http.socketshttphandler.connectcallback), [защита от DNS rebinding (ADR)](https://docs.typo3.org/p/netresearch/nr-vault/0.16/en-us/_sources/Developer/Adr/ADR-026-DnsRebindingDefence.rst.txt)
- [Telegram Bot API](https://core.telegram.org/bots/api.md), [Bot FAQ (лимиты файлов)](https://core.telegram.org/bots/faq), [Telegram.Bot.AspNetCore](https://www.nuget.org/packages/Telegram.Bot.AspNetCore)
- [SmartReader](https://www.nuget.org/packages/SmartReader)
- [Azure Communication Services Email SDK](https://learn.microsoft.com/cs-cz/dotnet/api/overview/azure/Communication.Email-readme)
- [TypeScript 7.0](https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/)
- [Полнотекстовый поиск RU/EN в PostgreSQL](https://code.djangoproject.com/ticket/28528)
