# Huong dan phat hanh APK Android qua Azure App Service

Host production hien tai:

- `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net`

## Muc tieu

- Trang download public: `/app`
- Link QR/APK chinh: `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net/downloads/vinh-khanh-food-guide/tour.apk`
- Endpoint trung gian co tracking, giu tuong thich: `/api/downloads/apk` va `/api/public/download/app`
- Static APK fallback cu van duoc alias: `/downloads/vinh-khanh-food-guide.apk` va `/downloads/vinh-khanh-food-tour.apk`
- Mobile app production goi dung backend Azure production moi.

## Cach hoat dong

1. QR tro truc tiep den `/downloads/vinh-khanh-food-guide/tour.apk`.
2. Backend middleware bat request APK truoc `UseStaticFiles`, ghi `qr_scan` vao `dbo.AppUsageEvents`, roi stream file APK.
3. Moi request `GET` den link APK tao mot event `qr_scan` moi; `HEAD` chi dung de verify va khong tang dem.
4. Endpoint `/api/downloads/apk` va `/api/public/download/app` van ghi `qr_scan` roi tra file APK, dung khi can link API trung gian.
5. Dashboard admin tinh tong QR scan tu database Azure SQL qua `AppUsageEvents`, khong luu tam trong memory.
6. Luong POI view va audio listen van dung event type rieng va khong bi thay doi.

## File chinh

- `apps/backend-api/Program.cs`
- `apps/backend-api/Controllers/PublicDownloadController.cs`
- `apps/backend-api/Infrastructure/MobileDistributionOptions.cs`
- `apps/backend-api/appsettings.json`
- `apps/mobile-app/Resources/Raw/appsettings.json`
- `scripts/package-azure-backend.ps1`
- `apk-host/index.html`

## Dat file APK truoc khi deploy

Dat file APK vao:

- Source path chinh: `apps/backend-api/wwwroot/downloads/vinh-khanh-food-guide/tour.apk`

Script package cung copy alias de giu link cu:

- `apps/backend-api/wwwroot/downloads/vinh-khanh-food-guide.apk`
- `apps/backend-api/wwwroot/downloads/vinh-khanh-food-tour.apk`

Publish path chinh:

- `.artifacts/azure-backend/publish/wwwroot/downloads/vinh-khanh-food-guide/tour.apk`

## URL de demo

- Download page:
  `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net/app`
- QR/direct APK link:
  `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net/downloads/vinh-khanh-food-guide/tour.apk`
- Tracked APK API endpoint:
  `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net/api/downloads/apk`
- Legacy tracked APK API endpoint:
  `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net/api/public/download/app`
- QR diagnostics:
  `https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net/api/public/diagnostics/qr-scan-count`

## Kiem tra sau deploy

1. Mo QR/direct APK link va dam bao browser tai file APK.
2. Goi QR diagnostics truoc/sau khi mo link APK, `qrScanCount` va `dashboardQrTotal` phai tang.
3. Mo dashboard admin va bam lam moi, tong QR scan phai khop database.
4. Goi API POI view/audio listen smoke test de dam bao thong ke POI va audio khong bi anh huong.
5. Neu App Service dung app settings override file json, set:
   - `MobileDistribution__PublicBaseUrl`
   - `MobileDistribution__MobileApiBaseUrl`
   - `MobileDistribution__PublicDownloadApkPath=/downloads/vinh-khanh-food-guide/tour.apk`

## Ghi chu

- Khong can migration moi; bang `dbo.AppUsageEvents` hien tai da luu duoc `qr_scan`.
- Khong xoa du lieu QR/POI/audio hien co.
- Cac URL localhost trong README/script dev duoc giu lai neu chi phuc vu chay local.
