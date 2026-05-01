# Huong dan phat hanh APK/media qua Azure Blob Storage

Host production hien tai:

- `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net`

## Muc tieu

- App Service chi xu ly API, analytics va redirect.
- APK/audio/image/media nam tren Azure Blob Storage.
- `/download`, `/app`, `/api/downloads/apk`, `/api/public/download/app` van ghi `qr_scan`, sau do redirect sang Blob URL.
- Du lieu local cu trong `wwwroot/storage` duoc backfill len Blob va cap nhat DB dan dan.

## Cau hinh Azure App Service

Set cac App Settings sau tren Azure Portal:

- `BlobStorage__ConnectionString`
- `BlobStorage__ContainerName=public-assets`
- `BlobStorage__PublicBaseUrl=https://<storage-account>.blob.core.windows.net/public-assets`
- `BlobStorage__ApkFolder=downloads`
- `BlobStorage__AudioFolder=audio`
- `BlobStorage__MediaFolder=media`

Khong hard-code connection string vao source. Neu dung custom domain/CDN cho Blob, tro `BlobStorage__PublicBaseUrl` ve public base URL do.
Container can cho phep public blob read hoac duoc front bang CDN public, vi mobile/browser se tai file truc tiep tu URL nay.

## Package deploy

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-azure-backend.ps1
```

Script tao 2 file:

- Backend zip cho App Service: `.artifacts/azure-backend/vinh-khanh-backend-*.zip`
- Goi asset de upload/backfill Blob: `.artifacts/azure-backend/vinh-khanh-blob-assets.zip`

Backend zip khong con dong goi `wwwroot/downloads` va `wwwroot/storage`.

## Upload/backfill asset

1. Upload APK trong blob asset bundle vao:
   - `downloads/tour.apk`
2. Chay endpoint noi bo bang token Super Admin:
   - `POST /api/v1/admin/blob-backfill/run`
   - body dry-run: `{ "dryRun": true }`
   - body chay that: `{ "dryRun": false }`
3. Endpoint se quet `wwwroot/downloads`, `wwwroot/storage`, upload file chua co len Blob va cap nhat DB cho `AudioGuides`, `MediaAssets`, `FoodItems`, `Routes`.

## URL demo

- Download page: `/app`
- QR/download tracked: `/download`
- Legacy tracked endpoints: `/api/downloads/apk`, `/api/public/download/app`
- QR diagnostics: `/api/public/diagnostics/qr-scan-count`

## Kiem tra sau deploy

1. Mo `/download`: backend ghi `qr_scan` va redirect 302 sang Blob URL.
2. Goi nhieu request tai APK: traffic file di qua Blob, App Service khong stream APK.
3. Generate audio trong admin: file duoc upload len Blob, DB luu Blob URL/path, mobile phat truc tiep URL do.
4. API bootstrap/POI detail khong tra `localhost`, `ngrok`, hoac URL `/storage/...` moi; neu gap local fallback, log co marker `[BlobMigration]`.
5. Dashboard van ghi nhan `qr_scan`, `poi_view`, `audio_play`.

## Ghi chu

- Fallback local `/storage/...` chi de giu tuong thich trong giai doan backfill.
- Sau khi backfill xong va DB khong con local path, co the xoa file nang khoi App Service.
