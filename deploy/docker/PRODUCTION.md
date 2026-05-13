# Production Docker Deployment

This project now supports Docker deployment for:
- Angular frontend (`tradion-frontend`)
- .NET API (`tradion-api`)
- SQL Server (`tradion-sqlserver`)

## 1) Create production env file

Copy:

`.env.prod.example` -> `.env.prod`

Set values:
- `SA_PASSWORD` (SQL Server SA password)
- `APP_DB_USER` and `APP_DB_PASSWORD` (application DB login used by API; avoids running API as `sa`)
- `SQL_VOLUME_NAME` (use different values for local/prod to prevent cross-environment password drift)
- `JWT_KEY_BASE64` (required by API in production; base64 that decodes to >= 32 bytes)
- `SMTP_PASSWORD` (optional but recommended)

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

## Password mismatch recovery (existing SQL volume)

If SQL starts but logs show repeated `18456` / `State 8` for `sa`, your persisted volume was initialized with a different `SA_PASSWORD`.

For local dev, fastest safe reset:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v
docker compose --env-file .env -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

For production, **do not destroy volume**. Instead use the original `SA_PASSWORD` for startup, then rotate credentials in a planned maintenance window.

## 3) Set up host reverse proxy

Use `deploy/nginx/dvcp.accent-dev.co.za.conf` on your host nginx.

It routes:
- `https://dvcp.accent-dev.co.za` -> `127.0.0.1:4300`
- `https://dvcpapi.accent-dev.co.za` -> `127.0.0.1:5020`

Update certificate paths and reload nginx.

## 4) DNS

Create A/AAAA records:
- `dvcp.accent-dev.co.za` -> server IP
- `dvcpapi.accent-dev.co.za` -> server IP

## 5) Quick checks

- Frontend: https://dvcp.accent-dev.co.za
- API health/basic response: https://dvcpapi.accent-dev.co.za/swagger (if enabled by env) or `/api/...`
- Containers: `docker compose ps`
- Logs: `docker compose logs -f api frontend sqlserver`

## Notes

- In production override, API and frontend bind to localhost only for safer exposure (`127.0.0.1`), with nginx as the public entrypoint.
- SQL Server port is not published in production.
