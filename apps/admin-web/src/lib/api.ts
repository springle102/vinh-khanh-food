import type {
  AdminDataState,
  AdminUser,
  AudioGuide,
  CustomerUser,
  EndUserPoiVisit,
  EndUserProfile,
  FoodItem,
  GeocodingLocation,
  MediaAsset,
  Poi,
  PoiDetail,
  Promotion,
  Review,
  SystemSetting,
  Translation,
} from "../data/types";

type ApiEnvelope<T> = {
  success: boolean;
  data: T | null;
  message?: string | null;
};

export type AuthSessionResponse = {
  userId: string;
  name: string;
  email: string;
  role: AdminUser["role"];
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
};

export type StoredFileResponse = {
  url: string;
  fileName: string;
  contentType: string;
  size: number;
};

export class ApiError extends Error
{
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

const ABSOLUTE_URL_PATTERN = /^[a-z]+:\/\//i;
const INVALID_RESPONSE_MESSAGE = "Backend tra ve phan hoi khong hop le.";

const normalizeConfiguredBaseUrl = (value: string | undefined) => {
  const trimmed = value?.trim().replace(/\/+$/, "") ?? "";
  if (!trimmed) {
    return "";
  }

  return ABSOLUTE_URL_PATTERN.test(trimmed) || trimmed.startsWith("/")
    ? trimmed
    : `/${trimmed}`;
};

const resolveConfiguredBasePath = (baseUrl: string) => {
  if (!baseUrl) {
    return "";
  }

  try {
    return new URL(baseUrl, "http://localhost").pathname.replace(/\/+$/, "");
  } catch {
    return "";
  }
};

const API_BASE_URL = normalizeConfiguredBaseUrl(import.meta.env.VITE_API_BASE_URL);
const API_BASE_PATH = resolveConfiguredBasePath(API_BASE_URL);

const buildHeaders = (headers?: HeadersInit) => {
  const nextHeaders = new Headers(headers);
  if (!nextHeaders.has("Accept")) {
    nextHeaders.set("Accept", "application/json");
  }

  return nextHeaders;
};

const resolveRequestUrl = (path: string) => {
  if (!API_BASE_URL || ABSOLUTE_URL_PATTERN.test(path)) {
    return path;
  }

  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  const nextPath = API_BASE_PATH &&
    (normalizedPath === API_BASE_PATH || normalizedPath.startsWith(`${API_BASE_PATH}/`))
    ? normalizedPath.slice(API_BASE_PATH.length)
    : normalizedPath;

  return `${API_BASE_URL}${nextPath || "/"}`;
};

const parseResponse = async <T>(response: Response) => {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) {
    if (!response.ok) {
      throw new ApiError(INVALID_RESPONSE_MESSAGE, response.status);
    }

    return null as T;
  }

  let payload: ApiEnvelope<T>;

  try {
    payload = (await response.json()) as ApiEnvelope<T>;
  } catch {
    throw new ApiError("Backend tráº£ vá» pháº£n há»“i khÃ´ng há»£p lá»‡.", response.status);
  }
  if (!response.ok || !payload.success || payload.data === null) {
    throw new ApiError(payload.message ?? "Yêu cầu đến backend thất bại.", response.status);
  }

  return payload.data;
};

const request = async <T>(path: string, init?: RequestInit) => {
  const response = await fetch(resolveRequestUrl(path), {
    ...init,
    headers: buildHeaders(init?.headers),
  });

  return parseResponse<T>(response);
};

const jsonRequest = async <T>(path: string, method: string, body?: unknown) =>
  request<T>(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
    headers: {
      "Content-Type": "application/json",
    },
  });

export const getErrorMessage = (error: unknown) =>
  error instanceof Error ? error.message : "Yêu cầu đến backend thất bại.";

export const adminApi = {
  getBootstrap: () => request<AdminDataState>("/api/v1/bootstrap"),
  login: (email: string, password: string) =>
    jsonRequest<AuthSessionResponse>("/api/v1/auth/login", "POST", { email, password }),
  logout: (refreshToken: string) =>
    jsonRequest<string>("/api/v1/auth/logout", "POST", { refreshToken }),
  uploadFile: async (file: File, folder: string) => {
    const formData = new FormData();
    formData.append("file", file);
    formData.append("folder", folder);

    return request<StoredFileResponse>("/api/v1/storage/upload", {
      method: "POST",
      body: formData,
    });
  },
  reverseGeocode: (lat: number, lng: number, signal?: AbortSignal) =>
    request<GeocodingLocation>(`/api/v1/geocoding/reverse?lat=${encodeURIComponent(lat)}&lng=${encodeURIComponent(lng)}`, { signal }),
  forwardGeocode: (query: string, signal?: AbortSignal) =>
    request<GeocodingLocation>(`/api/v1/geocoding/search?q=${encodeURIComponent(query)}`, { signal }),
  getPoiById: (poiId: string, signal?: AbortSignal) =>
    request<Poi>(`/api/v1/pois/${poiId}`, { signal }),
  getPoiDetail: (poiId: string, signal?: AbortSignal) =>
    request<PoiDetail>(`/api/v1/pois/${poiId}/detail`, { signal }),
  savePoi: (poi: {
    id?: string;
    slug: string;
    address: string;
    lat: number;
    lng: number;
    categoryId: string;
    status: Poi["status"];
    featured: boolean;
    defaultLanguageCode: Poi["defaultLanguageCode"];
    district: string;
    ward: string;
    priceRange: string;
    averageVisitDuration: number;
    popularityScore: number;
    tags: string[];
    ownerUserId: string | null;
    updatedBy: string;
  }) =>
    jsonRequest<Poi>(poi.id ? `/api/v1/pois/${poi.id}` : "/api/v1/pois", poi.id ? "PUT" : "POST", poi),
  saveUser: (account: {
    id?: string;
    name: string;
    email: string;
    phone: string;
    role: AdminUser["role"];
    status: AdminUser["status"];
    avatarColor: string;
    password: string | null;
    managedPoiId: string | null;
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<AdminUser>(account.id ? `/api/v1/admin-users/${account.id}` : "/api/v1/admin-users", account.id ? "PUT" : "POST", account),
  getEndUser: (userId: string) => request<EndUserProfile>(`/api/v1/users/${userId}`),
  getEndUserHistory: (userId: string) => request<EndUserPoiVisit[]>(`/api/v1/users/${userId}/history`),
  saveEndUserStatus: (userId: string, payload: {
    isBanned: boolean;
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<EndUserProfile>(`/api/v1/users/${userId}/status`, "PATCH", payload),
  savePromotion: (promotion: {
    id?: string;
    poiId: string;
    title: string;
    description: string;
    startAt: string;
    endAt: string;
    status: Promotion["status"];
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<Promotion>(
      promotion.id ? `/api/v1/promotions/${promotion.id}` : "/api/v1/promotions",
      promotion.id ? "PUT" : "POST",
      promotion,
    ),
  saveAudioGuide: (audioGuide: {
    id?: string;
    entityType: AudioGuide["entityType"];
    entityId: string;
    languageCode: AudioGuide["languageCode"];
    audioUrl: string;
    voiceType: AudioGuide["voiceType"];
    sourceType: AudioGuide["sourceType"];
    status: AudioGuide["status"];
    updatedBy: string;
  }) =>
    jsonRequest<AudioGuide>(
      audioGuide.id ? `/api/v1/audio-guides/${audioGuide.id}` : "/api/v1/audio-guides",
      audioGuide.id ? "PUT" : "POST",
      audioGuide,
    ),
  saveTranslation: (translation: {
    id?: string;
    entityType: Translation["entityType"];
    entityId: string;
    languageCode: Translation["languageCode"];
    title: string;
    shortText: string;
    fullText: string;
    seoTitle: string;
    seoDescription: string;
    isPremium: boolean;
    updatedBy: string;
  }) =>
    jsonRequest<Translation>(
      translation.id ? `/api/v1/translations/${translation.id}` : "/api/v1/translations",
      translation.id ? "PUT" : "POST",
      translation,
    ),
  saveReviewStatus: (reviewId: string, payload: {
    status: Review["status"];
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<Review>(`/api/v1/reviews/${reviewId}/status`, "PATCH", payload),
  saveSettings: (settings: SystemSetting & {
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<SystemSetting>("/api/v1/settings", "PUT", settings),
  saveMediaAsset: (asset: {
    id?: string;
    entityType: MediaAsset["entityType"];
    entityId: string;
    type: MediaAsset["type"];
    url: string;
    altText: string;
  }) =>
    jsonRequest<MediaAsset>(
      asset.id ? `/api/v1/media-assets/${asset.id}` : "/api/v1/media-assets",
      asset.id ? "PUT" : "POST",
      asset,
    ),
  saveFoodItem: (item: {
    id?: string;
    poiId: string;
    name: string;
    description: string;
    priceRange: string;
    imageUrl: string;
    spicyLevel: FoodItem["spicyLevel"];
  }) =>
    jsonRequest<FoodItem>(
      item.id ? `/api/v1/food-items/${item.id}` : "/api/v1/food-items",
      item.id ? "PUT" : "POST",
      item,
    ),
};
