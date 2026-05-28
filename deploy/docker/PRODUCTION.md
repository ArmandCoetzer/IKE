# Production Docker Deployment

This project supports Docker deployment for:
- Angular frontend (`ike-frontend`)
- .NET API (`ike-api`)
- SQL Server (`ike-sqlserver`)

## 1) Create production env file

Copy:

`.env.prod.example` -> `.env.prod`

Set values:
- `SA_PASSWORD` (SQL Server SA password)
- `APP_DB_USER` and `APP_DB_PASSWORD` (application DB login used by the API; avoids running the API as `sa`)
- `SQL_VOLUME_NAME` (use different values for local/prod to prevent cross-environment password drift)
- `CLOUDFLARE_TUNNEL_TOKEN` (required only for the laptop/Cloudflare Tunnel deployment below)
- `JWT_KEY_BASE64` (required by the API in production; base64 that decodes to >= 32 bytes)
- `EMAIL_PROVIDER` (`Smtp` by default, or `MicrosoftGraph`)
- `SMTP_PASSWORD` (optional but recommended when `EMAIL_PROVIDER=Smtp`)
- `MICROSOFT_GRAPH_TENANT_ID`, `MICROSOFT_GRAPH_CLIENT_ID`, `MICROSOFT_GRAPH_CLIENT_SECRET`, `MICROSOFT_GRAPH_FROM_EMAIL` (required when `EMAIL_PROVIDER=MicrosoftGraph`)
- `DEFAULT_ADMIN_EMAIL` and `DEFAULT_ADMIN_PASSWORD` (required for first production bootstrap; change the password immediately after first login)

Generate a JWT key example:

```powershell
[Convert]::ToBase64String((1..64 | ForEach-Object { Get-Random -Maximum 256 }))
```

## 2) Start containers

### Development/local

From repo root:

```bash
docker compose --env-file .env -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

### Production

From repo root:

```bash
docker compose --env-file .env.prod -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

### Production on Docker Desktop with Cloudflare Tunnel

This is the recommended setup when hosting from a laptop because it avoids router port-forwarding and does not require the laptop to expose public ports.

1. In Cloudflare Zero Trust, create a tunnel for this machine.
2. Add public hostnames to the tunnel:
   - `ike.accent-dev.co.za` -> `http://frontend:80`
   - `ikeapi.accent-dev.co.za` -> `http://api:5020`
3. Copy the tunnel token into `.env.prod` as `CLOUDFLARE_TUNNEL_TOKEN`.
4. Start the stack with the Cloudflare override:

```bash
docker compose --env-file .env.prod -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.cloudflare.yml up -d --build
```

If you are using normal Cloudflare DNS/proxy with your own router port forwarding and host nginx, you do not need `CLOUDFLARE_TUNNEL_TOKEN` and should not include `docker-compose.cloudflare.yml` in the command.

For Microsoft 365 email, set `EMAIL_PROVIDER=MicrosoftGraph` and fill the `MICROSOFT_GRAPH_*` values in `.env.prod`.

If generated PDFs in production show logos/shapes but no text, rebuild the API image without Docker cache after pulling this change so the Linux font packages are installed:

```bash
docker compose --env-file .env.prod -f docker-compose.yml -f docker-compose.prod.yml build --no-cache api
docker compose --env-file .env.prod -f docker-compose.yml -f docker-compose.prod.yml up -d api
```

## Password mismatch recovery (existing SQL volume)

If SQL starts but logs show repeated `18456` / `State 8` for `sa`, your persisted volume was initialized with a different `SA_PASSWORD`.

For local dev, fastest safe reset:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v
docker compose --env-file .env -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

For production, **do not destroy volume**. Instead use the original `SA_PASSWORD` for startup, then rotate credentials in a planned maintenance window.

## 3) Set up host reverse proxy

Only use this section if you are not using Cloudflare Tunnel. Use `deploy/nginx/ike.example.conf` as a template on your host nginx. It is configured for `ike.accent-dev.co.za` and `ikeapi.accent-dev.co.za`; update certificate paths if your certbot/live directory names differ, and reload nginx.

It routes:
- `https://ike.accent-dev.co.za` -> `127.0.0.1:4300` (Angular)
- `https://ikeapi.accent-dev.co.za` -> `127.0.0.1:5020` (.NET API)

## 4) DNS

For Cloudflare Tunnel, the public hostnames are created on the tunnel and Cloudflare manages the DNS/CNAME target. For a normal reverse proxy, create A/AAAA records for your chosen app and API hostnames pointing at the server IP.

## 5) Quick checks

- Frontend: your public app URL
- API: your public API URL (Swagger if enabled by environment)
- Containers: `docker compose ps`
- Logs: `docker compose logs -f api frontend sqlserver`
- Cloudflare Tunnel logs: `docker compose logs -f cloudflared`

## Email Providers

The API supports two outbound email providers. Select one per deployment with `EmailSettings:Provider` or `EMAIL_PROVIDER`.

### SMTP

Use `EMAIL_PROVIDER=Smtp` or leave the provider unset. This keeps the existing `SmtpSettings` flow using `System.Net.Mail.SmtpClient` for providers such as GoDaddy, xneelo, or any SMTP server that supports username/password authentication.

Required configuration:
- `SmtpSettings:Server`
- `SmtpSettings:Port`
- `SmtpSettings:Username`
- `SmtpSettings:Password` / `SMTP_PASSWORD`
- `SmtpSettings:FromEmail`
- `SmtpSettings:FromName`

### Microsoft Graph

Use `EMAIL_PROVIDER=MicrosoftGraph` for Microsoft 365 tenants where SMTP AUTH is blocked by Security Defaults. This uses OAuth2 client credentials and `POST https://graph.microsoft.com/v1.0/users/{FromEmail}/sendMail`.

Azure App Registration requirements:
- Create an app registration in the client's Microsoft Entra tenant.
- Add a client secret.
- Add Microsoft Graph API permission: `Application permissions` -> `Mail.Send`.
- Grant admin consent for the tenant.
- Configure `MICROSOFT_GRAPH_TENANT_ID`, `MICROSOFT_GRAPH_CLIENT_ID`, `MICROSOFT_GRAPH_CLIENT_SECRET`, and `MICROSOFT_GRAPH_FROM_EMAIL` (for example `admin@ikegroup.co.za`).

## Notes

- In the production override, API and frontend bind to localhost only for safer exposure (`127.0.0.1`), with nginx as the public entrypoint.
- With `docker-compose.cloudflare.yml`, the `cloudflared` container reaches `frontend:80` and `api:5020` over the Docker network, so public host ports are not required.
- SQL Server port is not published in production.
- Set `Jwt__Issuer`, `Jwt__Audience`, `App__BaseUrl`, and `Cors__Origins__*` in `docker-compose.prod.yml` (or via env) to match your deployed URLs. They must align with how the browser obtains and validates JWTs.
- The Flutter release app defaults to `https://ikeapi.accent-dev.co.za/api`; override with `--dart-define=API_URL=...` only if deploying to a different API host.
