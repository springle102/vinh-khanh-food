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

From the repository root, `npm install` now bootstraps `apps/admin-web` automatically, and the root scripts (`npm run dev`, `npm run build`, `npm run lint`) will install admin dependencies on demand if they are missing.

The Vite dev server proxies `/api` and `/storage` to `http://localhost:5080` by default.

If you deploy the frontend separately from the API, set:

```bash
VITE_API_BASE_URL=http://localhost:5080/api/v1
```

## Seed accounts in database

- `superadmin@vinhkhanh.vn` / `Admin@123`
- `bbq@vinhkhanh.vn` / `Admin@123`
- `oc@vinhkhanh.vn` / `Admin@123`

## Docker

```bash
docker build -t vinh-khanh-admin .
docker run -p 8080:80 vinh-khanh-admin
```
