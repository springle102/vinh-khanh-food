import clsx from "clsx";
import type { ApprovalStatus, AuditActorRole, ContentStatus, LanguageCode, PoiPlaceTier, UserStatus } from "../data/types";

export const cn = (...values: Array<string | false | null | undefined>) => clsx(values);

export const languageLabels: Record<LanguageCode, string> = {
  vi: "Tiếng Việt",
  en: "Tiếng Anh",
  "zh-CN": "Tiếng Trung",
  ko: "Tiếng Hàn",
  ja: "Tiếng Nhật",
};

export const roleLabels: Record<AuditActorRole, string> = {
  SUPER_ADMIN: "Super Admin",
  PLACE_OWNER: "Chủ quán",
  SYSTEM: "Hệ thống",
};

export const userStatusLabels: Record<UserStatus, string> = {
  active: "Đang hoạt động",
  locked: "Đã khóa",
};

export const approvalStatusLabels: Record<ApprovalStatus, string> = {
  pending: "Chờ duyệt",
  approved: "Đã duyệt",
  rejected: "Từ chối",
};

export const contentStatusLabels: Record<ContentStatus, string> = {
  draft: "Bản nháp",
  pending: "Chờ duyệt",
  published: "Đã duyệt",
  rejected: "Từ chối",
  archived: "Lưu trữ",
  deleted: "Đã xóa",
};

export const poiActivityLabels = {
  active: "Đang hoạt động",
  inactive: "Ngừng hoạt động",
} as const;

export const poiPlaceTierLabels: Record<PoiPlaceTier, string> = {
  0: "Basic",
  1: "Premium",
};

export const normalizePoiPlaceTier = (value: number | null | undefined): PoiPlaceTier =>
  value === 1 ? 1 : 0;

export const formatDateTime = (value: string | null) => {
  if (!value) {
    return "Chưa có";
  }

  return new Intl.DateTimeFormat("vi-VN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
};

export const formatNumber = (value: number) => new Intl.NumberFormat("vi-VN").format(value);

export const formatPercent = (value: number) =>
  new Intl.NumberFormat("vi-VN", {
    maximumFractionDigits: 0,
    style: "percent",
  }).format(value);

export const getInitials = (name: string) =>
  name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");

export const slugify = (value: string) =>
  value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[đĐ]/g, "d")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/(^-|-$)+/g, "");

export const normalizeSearchText = (value: string) =>
  value
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[đĐ]/g, "d")
    .toLowerCase()
    .trim();

export const buildGradient = (color: string) =>
  `linear-gradient(135deg, ${color}, rgba(255,255,255,0.55))`;
