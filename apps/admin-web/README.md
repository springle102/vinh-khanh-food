# Vinh Khanh Admin Web

Admin dashboard for Vinh Khanh operations.

## Stack

- React 19 + TypeScript + Vite
- Tailwind CSS
- Recharts
- Leaflet / OpenStreetMap

## Local development

```bash
npm install
npm run dev
npm run build
```

Inside `apps/admin-web`, `npm run dev` now starts both the backend and the admin web together. Use `npm run dev:client` only when you explicitly want the standalone Vite client.

From the repository root, `npm install` now bootstraps `apps/admin-web` automatically. `npm run dev` starts both the backend and the admin web together, while `npm run dev:admin` keeps the frontend-only workflow for tooling and scripts.

When `VITE_API_BASE_URL` is relative (default `/api/v1`), the Vite dev server proxies `/api`, `/storage`, and `/swagger` to `VITE_DEV_API_TARGET`. If `VITE_DEV_API_TARGET` is empty, the frontend auto-detects the backend URL from `VK_BACKEND_URLS`, `ASPNETCORE_URLS`, or `apps/backend-api/Properties/launchSettings.json`.

If you deploy the frontend separately from the API, set:

```bash
VITE_API_BASE_URL=/api/v1
# Optional for local dev only. Leave empty to auto-detect backend launchSettings.
VITE_DEV_API_TARGET=
```

For production behind a reverse proxy, keep `VITE_API_BASE_URL=/api/v1` so the frontend calls the API on the same origin. Only use an absolute `VITE_API_BASE_URL` when the frontend must call a different API origin directly, and never point it to `127.0.0.1` in Azure or other deployed environments.

## Seed accounts in database

- `superadmin@vinhkhanh.vn` / `Admin@123`
- `bbq@vinhkhanh.vn` / `Admin@123`
- `oc@vinhkhanh.vn` / `Admin@123`

## Docker

```bash
docker build -t vinh-khanh-admin .
docker run -p 8080:80 vinh-khanh-admin
```
