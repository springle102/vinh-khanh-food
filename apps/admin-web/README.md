# Vinh Khanh Admin Web

Admin dashboard cho hệ thống thuyết minh tự động đa ngôn ngữ Phố Ẩm Thực Vĩnh Khánh.

## Stack

- React 19 + TypeScript + Vite
- Tailwind CSS
- Recharts cho dashboard analytics
- Mock data layer bằng localStorage để chạy độc lập trước khi nối ASP.NET Core API

## Chạy local

```bash
npm install
npm run dev
```

## Google Maps

Form quản lý địa điểm dùng OpenStreetMap thông qua Leaflet và không cần API key.

## Tài khoản demo

- `superadmin@vinhkhanh.vn` / `Admin@123`
- `content@vinhkhanh.vn` / `Admin@123`

## Triển khai Docker

```bash
docker build -t vinh-khanh-admin .
docker run -p 8080:80 vinh-khanh-admin
```
