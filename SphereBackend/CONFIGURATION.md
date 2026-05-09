# Configuration

## Secret policy
- Do not store secrets in `appsettings*.json`.
- Local development: use `.env` (gitignored).
- Production: use platform environment variables or secret manager.
- Keep `appsettings.json` only for non-secret defaults and structure.
- `.env.example` and `appsettings.Example.json` are local-friendly templates.

## Required secret variables
- `ConnectionStrings__DefaultConnection`
- `Hashids__Salt`
- `Jwt__Key`
- `SessionHint__Key`
- `Resend__ApiKey`
- `Resend__FromEmail` (required outside Development; optional in local development because the app falls back to `onboarding@resend.dev`)

## Required non-secret variables (if not kept in `appsettings.json`)
- `Jwt__Issuer`
- `Jwt__Audience`
- `SessionHint__Issuer`
- `SessionHint__Audience`
- `Frontend__BaseUrl`
- `Cookies__UseCrossSiteAuth` (`false` for local development and same-origin proxy deployment; `true` only when the browser talks directly to a backend on another site)
- `AllowedOrigins__0` (and more indexes for multiple origins)

## Runtime environment variables
- `PORT` (optional; when set, the app binds to `http://0.0.0.0:<PORT>`)
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (optional; enable this when the app runs behind a reverse proxy that sets `X-Forwarded-For` / `X-Forwarded-Proto`)

## Optional application variables
- `SessionHint__TtlSeconds` for the short-lived session hint cookie lifetime
- `SessionHint__Issuer` and `SessionHint__Audience` for stricter session hint validation
- `Sse__AllowQueryStringToken=true` for local SSE debugging only
- `Sse__EnableDistributedRelay=true` to relay SSE events between backend instances through PostgreSQL `LISTEN/NOTIFY`
- `Hashids__MinHashLength` to override the default public ID length

## Local `.env` usage
- Copy `.env.example` to `.env`.
- Fill real values in `.env`.
- Run `dotnet run`.
- `.env` is ignored by git.
- Default local values assume frontend on `https://localhost:3000`.
- In local development, `Resend__FromEmail` can be omitted if you are fine with the development fallback sender.

## Recommended production overrides for `Vercel + Render` with frontend proxy
- Override `Frontend__BaseUrl` with your Vercel URL.
- Override `AllowedOrigins__0` with the same Vercel URL.
- Keep `Cookies__UseCrossSiteAuth=false`.
- Keep backend cookies host-only and do not set a cookie `Domain`.
- Keep cookie `Path=/` so middleware and proxied auth requests can see them.
- Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` when the platform terminates TLS or forwards requests through a proxy.

## Optional direct cross-site browser-to-backend mode
- Use this only if the browser calls the backend origin directly from another site.
- Set `Cookies__UseCrossSiteAuth=true`.
- Ensure the frontend sends credentialed requests to the backend origin.
- Expect stricter cookie and CSRF considerations than in the proxy setup.

## PowerShell example (local current session)
```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=kanban_db;Username=app_user;Password=..."
$env:Hashids__Salt = "..."
$env:Jwt__Key = "..."
$env:SessionHint__Key = "..."
$env:Resend__ApiKey = "..."
$env:Frontend__BaseUrl = "https://localhost:3000"
$env:AllowedOrigins__0 = "https://localhost:3000"
$env:Cookies__UseCrossSiteAuth = "false"
$env:SessionHint__TtlSeconds = "20"
$env:Sse__AllowQueryStringToken = "false"
$env:Sse__EnableDistributedRelay = "false"
```

## PowerShell example (production current session)
```powershell
$env:ConnectionStrings__DefaultConnection = "Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>"
$env:Hashids__Salt = "..."
$env:Jwt__Key = "..."
$env:SessionHint__Key = "..."
$env:Resend__ApiKey = "..."
$env:Resend__FromEmail = "noreply@example.com"
$env:Frontend__BaseUrl = "https://your-frontend.vercel.app"
$env:AllowedOrigins__0 = "https://your-frontend.vercel.app"
$env:Cookies__UseCrossSiteAuth = "false"
$env:ASPNETCORE_FORWARDEDHEADERS_ENABLED = "true"
```

## PowerShell example (direct cross-site browser to backend)
```powershell
$env:ConnectionStrings__DefaultConnection = "Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>"
$env:Hashids__Salt = "..."
$env:Jwt__Key = "..."
$env:SessionHint__Key = "..."
$env:Resend__ApiKey = "..."
$env:Resend__FromEmail = "noreply@example.com"
$env:Frontend__BaseUrl = "https://your-frontend.vercel.app"
$env:AllowedOrigins__0 = "https://your-frontend.vercel.app"
$env:Cookies__UseCrossSiteAuth = "true"
$env:ASPNETCORE_FORWARDEDHEADERS_ENABLED = "true"
```

## PowerShell example (persist for user)
```powershell
setx ConnectionStrings__DefaultConnection "Host=localhost;Port=5432;Database=kanban_db;Username=app_user;Password=..."
setx Hashids__Salt "..."
setx Jwt__Key "..."
setx SessionHint__Key "..."
setx Resend__ApiKey "..."
setx Frontend__BaseUrl "https://localhost:3000"
setx AllowedOrigins__0 "https://localhost:3000"
setx Cookies__UseCrossSiteAuth "false"
```

Open a new terminal after `setx`.
