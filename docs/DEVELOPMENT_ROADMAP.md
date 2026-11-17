# Development Roadmap Progress

Last audited: 2026-11-17

This progress record tracks implementation against the Xumora development roadmap. A status of **In Progress** means the capability exists in part but its roadmap completion criteria are not yet met.

## Phase 0 — Backend Foundation

**Status: In Progress**

| Task | Status | Current evidence / remaining work |
|---|---|---|
| Solution and project structure | Completed | .NET 10 solution, BuildingBlocks, API host, and Photography module projects build successfully. |
| PostgreSQL and EF Core | In Progress | Npgsql and PostgreSQL are configured. The Identity context has an initial migration and Testcontainers-backed migration/auth tests; migrations for remaining application data contexts are not established. |
| Health endpoints | Completed | Liveness and database readiness endpoints return typed responses; integration tests verify liveness and readiness against PostgreSQL. Readiness failures log the exception type. |
| Global exception handling | Completed | The API registers a global exception handler that logs unhandled exceptions and returns ProblemDetails. |
| API versioning and OpenAPI | Completed | URL-segment versioning and per-version OpenAPI documents are configured and runtime-verified, including Swagger UI. |
| Step 8.6 — Configuration management | Completed | AI and Storage options use startup data-annotation validation; local Compose credentials are configurable and ignored by Git; setup, User Secrets, and environment-variable configuration are documented. |
| Step 8.7 — Logging and observability | Completed | Serilog structured console logging, body-free request logs, validated correlation IDs, exception logging, OpenTelemetry ASP.NET Core/HTTP tracing, and optional OTLP export are configured. Runtime export to a collector remains to be verified. |
| Step 8.8 — API standards | Completed | Documented route, validation, ProblemDetails, typed response, pagination, and OpenAPI conventions; added a paginated response contract and typed health responses. Feature endpoints will need to follow these standards as they are built. |
| Step 8.9 — Testing foundation | Completed | Added xUnit contract-validation tests, WebApplicationFactory API integration tests, and a shared PostgreSQL Testcontainers fixture. The suite now also applies the Identity migration and covers authentication flows. |
| Step 8.10 — Docker and development environment | Completed | Compose builds a non-root API image and starts it only after PostgreSQL is healthy; credentials and host ports are configurable, the database volume persists, and both containerized and native workflows are documented. |
| Step 8.11 — CI/CD foundation | In Progress | GitHub Actions runs the warning-free .NET Release build/tests plus mobile install, lint, type-check, and dependency audit. Local checks pass; an actual GitHub Actions run is still needed. |

### Phase 0 completion criteria

- [x] Backend builds successfully (`dotnet build backend/Xumora.slnx --no-restore`: 0 warnings, 0 errors after API-versioned OpenAPI integration).
- [x] Database connection and liveness/readiness endpoints are verified in PostgreSQL-backed integration tests.
- [x] API documentation is verified at runtime; duplicate registration has been removed and documents are mapped per API version.
- [x] Configuration is separated from checked-in secrets; local setup is documented.
- [x] Automated tests run successfully (14 passed, including PostgreSQL-backed Identity flows).
- [x] Docker environment is reproduced from the documented setup; isolated Compose startup and API/database readiness were verified.
- [ ] CI pipeline passes in GitHub Actions.

## Later phases

| Phase | Status | Notes |
|---|---|---|
| Phase 1 — Authentication and application foundation | In Progress | ASP.NET Core Identity, Google ID-token exchange, PostgreSQL persistence, JWT access tokens, rotating/revocable refresh tokens, and versioned authentication/profile endpoints are implemented. An Expo SDK 58 mobile sign-in client stores Xumora tokens securely; real Google OAuth credentials/device testing and broader application foundation work remain. |
| Phase 2 — Photography session and image management | Not Started | No session workflow, image management, or mobile capture/upload flow exists. |
| Phase 3 — Computer vision and AI analysis | Not Started | No AIAnalysis or ComputerVision module or analysis workflow exists. |
| Phase 4 — Photography intelligence engine | Not Started | No Composition module or decision engine exists. |
| Phase 5 — Photography guidance system | Not Started | No Guidance module or user-facing guidance exists. |
| Phase 6 — Live camera assistance | Not Started | No mobile camera application or live guidance exists. |
| Phase 7 — History and personalization | Not Started | No history or user-preference workflows exist. |
| Phase 8 — Advanced AI photography features | Not Started | Depends on earlier photography and AI capabilities. |
| Phase 9 — Usage management and monetization | Not Started | No usage, quota, or subscription features exist. |
| Phase 10 — Production readiness and launch | Not Started | Production deployment, monitoring, security, and end-to-end testing are not established. |

## Audit notes

- The API registers JWT Bearer authentication and protects the profile endpoint. The Expo mobile client supports Google sign-in and persists Xumora sessions securely; provider setup and device flow still require project credentials.
- The Photography Application and Infrastructure dependency-injection methods are placeholders and currently register no services.
- The API still contains the template WeatherForecast endpoint.
- The latest build succeeds with 0 warnings and 0 errors after the versioned OpenAPI integration was added.
- The API liveness/readiness and database connection are verified by Testcontainers-backed tests. The earlier standalone liveness smoke test did not require a database.
- `docker compose --env-file .env.example config --quiet` succeeds. An isolated Compose project built and ran the API/PostgreSQL stack; both liveness and readiness returned healthy, and the API process ran as a non-root user. The pre-existing `xumora-postgres` container remained running and untouched.
- Logging and tracing configuration was added after the initial audit. Runtime smoke tests confirmed that a valid correlation ID is echoed and appears in request logs, and invalid IDs are replaced. OTLP export is opt-in through `OTEL_EXPORTER_OTLP_ENDPOINT`; telemetry export to a live collector has not been verified.
- Step 8.8 standardized ProblemDetails content type for unhandled errors and rate limits, documented API conventions, and added version-aware OpenAPI registration. Runtime verification confirmed the `/openapi/v1.json` document and Swagger UI; a rate-limit smoke test confirmed `application/problem+json` and correlation metadata.
- Step 8.9 added unit and API integration tests. Testcontainers launches a disposable PostgreSQL 17 instance. All 14 tests pass. Coverage includes readiness, correlation IDs, versioned OpenAPI, contract validation, account registration/login, protected profile access, refresh rotation/replay, hashed token persistence, Google sign-in configuration/invalid-token responses, and revocation.
- Step 8.10 added a multi-stage API Dockerfile and a Compose API service that waits for PostgreSQL health. The documented isolated-stack smoke test passed; its temporary containers, network, and test volume were removed afterward.
- Step 8.11 added `.github/workflows/dotnet.yml`. It runs restore, a Release build with warnings as errors, the Testcontainers suite, and mobile dependency/lint/type checks. No remote GitHub Actions run has yet occurred.
- Phase 1 authentication uses a separate `IdentityDbContext` and initial EF migration. Development applies it automatically; other environments must apply migrations before starting the API. The refresh-token table persists hashes rather than plaintext tokens.
- Mobile Google sign-in uses Expo SDK 58 with `react-native-nitro-google-signin`, a native development build, and SecureStore. The API validates Google ID tokens against the configured Web client ID and only accepts verified-email accounts. A database unique index enforces normalized email uniqueness; an existing account is not silently linked to Google.
- Google sign-in configuration is optional by default. The API returns `503` until `Google:WebClientId` is configured. Tests cover disabled and invalid-token paths; a successful live Google OAuth flow requires project credentials and device testing.
- The Git repository had no commits at the time of the audit; the application files were untracked.
