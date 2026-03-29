# Vinh Khanh Admin Web Workspace

This workspace is focused on the admin web flow.

Mobile app sources, mobile build scripts, and guide/mobile-specific backend code have been removed. The remaining backend source is kept only for the admin web experience.

## Remaining apps

- `apps/admin-web`: React + TypeScript + Vite admin frontend
- `apps/backend-api`: admin-facing API for bootstrap, auth, CRUD, uploads, and SQL Server access

## Root scripts

```bash
npm run install:admin
npm run dev
npm run dev:backend
npm run build
npm run build:backend
npm run preview
npm run lint
```

Frontend root scripts target `apps/admin-web`, and backend scripts call the .NET API project in `apps/backend-api`.

## Runtime configuration

- Local dev proxy uses `VITE_API_PROXY_TARGET` and defaults to `http://localhost:5080`
- Standalone frontend builds can point to an API with `VITE_API_BASE_URL`

Example:

```bash
VITE_API_BASE_URL=http://localhost:5080/api/v1
```

## Suggested entry points

- `apps/admin-web/src/app/App.tsx`
- `apps/admin-web/src/data/store.tsx`
- `apps/admin-web/src/lib/api.ts`
- `apps/backend-api/Program.cs`
- `apps/backend-api/Infrastructure/AdminDataRepository.cs`
