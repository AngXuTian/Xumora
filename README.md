# Xumora

Xumora is an AI-powered personal photography assistant. The backend is an ASP.NET Core Web API targeting .NET 10, with PostgreSQL for persistence.

## Containerized development

Requirements:

- .NET 10 SDK
- Docker Compose

1. Create a local Compose environment file and replace its example password:

   ```sh
   cp .env.example .env
   ```

2. Build and start the API and PostgreSQL:

   ```sh
   docker compose up --build -d
   ```

   Compose waits for PostgreSQL's health check before starting the API. The API listens on `http://localhost:${API_PORT:-8080}`; Swagger UI is at `/swagger`. The PostgreSQL host port can be changed with `POSTGRES_PORT`. The `.env` file is ignored by Git. Replace `JWT_SIGNING_KEY` with a randomly generated value before using authentication locally; for example, `openssl rand -base64 48`.

   To enable mobile Google sign-in, set `GOOGLE_WEB_CLIENT_ID` in `.env` to the Web OAuth client ID from your Google Cloud project.

3. Stop the containers when finished:

   ```sh
   docker compose down
   ```

   The named PostgreSQL volume is retained, so database data persists. If reusing a volume created with an earlier configuration, set `POSTGRES_PASSWORD` in `.env` to that database's existing password; changing the environment variable does not change credentials stored in an initialized database. `docker compose down -v` removes the database volume and its data.

The Compose `.env` file contains local development credentials and is not automatically read by a natively run API process.

## Authentication API

The versioned API uses ASP.NET Core Identity for account and password management, short-lived JWT access tokens, and rotating refresh tokens. Refresh-token values are returned only in authentication responses; the database stores their SHA-256 hashes. Reuse of a rotated refresh token revokes the active tokens in that token family.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/auth/register` | Create an account and return an access/refresh token pair. |
| `POST` | `/api/v1/auth/login` | Authenticate and return a token pair. |
| `POST` | `/api/v1/auth/refresh` | Rotate a refresh token and return a new token pair. |
| `POST` | `/api/v1/auth/revoke` | Revoke a refresh token; returns `204 No Content`. |
| `GET` | `/api/v1/auth/me` | Return the authenticated user's profile; requires a Bearer access token. |
| `POST` | `/api/v1/auth/google` | Validate a Google ID token and return a Xumora token pair. Requires `Google:WebClientId`. |

Development automatically applies Identity EF Core migrations at startup. For other environments, apply the migrations as part of deployment before starting the API:

```sh
dotnet ef database update \
  --project backend/src/Modules/Identity/Xumora.Identity.Infrastructure/Xumora.Identity.Infrastructure.csproj \
  --startup-project backend/src/Hosts/Xumora.API/Xumora.API.csproj \
  --context IdentityDbContext
```

Keep the JWT signing key private and at least 32 bytes long. Access tokens are valid for 15 minutes by default; refresh tokens are valid for 30 days, up to a 90-day family lifetime.

## Native backend development

Requirements:

- .NET 10 SDK
- Docker Compose (for PostgreSQL)

1. Start PostgreSQL:

   ```sh
   docker compose up -d postgres
   ```

2. Configure the API's database connection in .NET User Secrets using the same credentials from `.env`:

   ```sh
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
     "Host=localhost;Port=5432;Database=xumora_db;Username=xumora;Password=<your-local-password>" \
     --project backend/src/Hosts/Xumora.API/Xumora.API.csproj
   dotnet user-secrets set "Jwt:SigningKey" "<random-key-of-at-least-32-bytes>" \
     --project backend/src/Hosts/Xumora.API/Xumora.API.csproj
   dotnet user-secrets set "Google:WebClientId" "<web-client-id>.apps.googleusercontent.com" \
     --project backend/src/Hosts/Xumora.API/Xumora.API.csproj
   ```

3. Run the API:

   ```sh
   dotnet run --project backend/src/Hosts/Xumora.API/Xumora.API.csproj
   ```

   In Development, ASP.NET Core loads User Secrets using the API project's `UserSecretsId`. The API validates the database connection setting at startup.

## Configuration

The API reads:

- `ConnectionStrings:DefaultConnection`
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`, `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays`, and `Jwt:RefreshTokenAbsoluteDays`
- `Google:WebClientId` (optional; required to enable Google sign-in)
- `AI:Provider`, `AI:Model`, `AI:ApiKey`, and `AI:Endpoint`
- `Storage:Provider`, `Storage:BucketName`, `Storage:Endpoint`, `Storage:AccessKey`, and `Storage:SecretKey`

See [API Standards](docs/API_STANDARDS.md) for request validation, response, error, pagination, route, and OpenAPI conventions.

The `AI` and `Storage` sections are bound through the Options pattern and validated at startup. Their `Provider` settings are required. The checked-in settings select `AI:Provider` as `None` and `Storage:Provider` as `Local`; no AI or remote storage credentials are needed for those selections.

Use User Secrets for local sensitive values, for example:

```sh
dotnet user-secrets set "AI:ApiKey" "<your-local-key>" \
  --project backend/src/Hosts/Xumora.API/Xumora.API.csproj
```

For deployed environments, supply configuration using environment variables or the deployment platform's secret manager. Environment variable names use double underscores in place of configuration colons, such as `ConnectionStrings__DefaultConnection` and `AI__ApiKey`. Do not put real credentials in checked-in settings or `.env.example`.

The JWT signing key is required and must be at least 32 bytes. Keep it private and rotate it using a deliberate token invalidation/rotation plan; do not use the example `.env` value in a real environment.

## Mobile Google sign-in

The mobile app uses Expo SDK 58 and Google's native sign-in flow. Google OAuth requires a Google Cloud project with Web, Android, and iOS OAuth clients. Set the Web client ID in both the API configuration (`Google:WebClientId` or root `.env`'s `GOOGLE_WEB_CLIENT_ID`) and `mobile/.env`; set the iOS client ID and reversed iOS URL scheme in `mobile/.env` as well. The Android OAuth client must use the app's package name and signing certificate SHA-1.

```sh
cp mobile/.env.example mobile/.env
```

Set `EXPO_PUBLIC_API_URL` to the API's reachable URL (`http://localhost:8080` for an iOS simulator, `http://10.0.2.2:8080` for the default Android emulator, or your computer's LAN address for a physical device). Then generate and run a native development build:

```sh
cd mobile
npx expo prebuild
npx expo run:ios
# or: npx expo run:android
npx expo start --dev-client
```

Google sign-in uses a native module and does not work in Expo Go. The app stores only Xumora access/refresh tokens using SecureStore; Google ID tokens are sent to the API for audience, issuer, signature, expiry, and verified-email validation, and are not persisted. An existing email/password account is not silently linked to a Google identity; sign in with that account's existing method.

Check the mobile app with:

```sh
cd mobile
npm ci
npm run lint
npx tsc --noEmit
```

## Logging and tracing

The API writes structured Serilog events to the console. Request logs include the method, response status, elapsed time, and correlation ID; request and response bodies, query strings, and headers are not logged. The API accepts a valid `X-Correlation-ID` header (up to 64 ASCII letters, digits, dots, underscores, or hyphens), generates one when absent or invalid, and returns it in the response header. The same value is attached to request and exception logs.

OpenTelemetry collects ASP.NET Core and outgoing HTTP client traces. To export them using OTLP, set `OTEL_EXPORTER_OTLP_ENDPOINT` to an absolute collector endpoint, for example `http://localhost:4317`. No exporter is enabled when the variable is unset. Do not configure telemetry to export request bodies, credentials, or private image data.

## Build

```sh
dotnet build backend/Xumora.slnx
```

## Test

The test suite includes unit tests and API integration tests. Integration tests use Testcontainers to launch a disposable PostgreSQL 17 container, so Docker must be running:

```sh
dotnet test backend/Xumora.slnx
```

## Continuous integration

The GitHub Actions workflow in `.github/workflows/dotnet.yml` restores the solution, builds Release with warnings treated as errors, runs the PostgreSQL-backed tests, and checks mobile dependencies, lint, and types on pushes, pull requests, and manual dispatch. The backend integration tests require the Docker engine available on the GitHub-hosted Ubuntu runner.
