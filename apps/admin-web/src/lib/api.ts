import type {
  AdminDataState,
  AdminUser,
  AudioGuide,
  EndUserProfile,
  FoodItem,
  GeocodingLocation,
  LanguageCode,
  MediaAsset,
  Poi,
  PoiDetail,
  Promotion,
  RegionVoice,
  ResolvedPoiNarration,
  Review,
  SystemSetting,
  TourRoute,
  Translation,
} from "../data/types";
import type { AuthPortal } from "../features/auth/auth-routing";

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

export type LoginAccountOption = {
  userId: string;
  name: string;
  email: string;
  password: string;
  role: AdminUser["role"];
  status: "active" | "locked";
  managedPoiId: string | null;
};

export type StoredFileResponse = {
  url: string;
  fileName: string;
  contentType: string;
  size: number;
};

export type TextTranslationResponse = {
  targetLanguageCode: string;
  sourceLanguageCode: string | null;
  texts: string[];
  provider: string;
};

export class ApiError extends Error {
  status: number;
  kind: "backend" | "invalid_response" | "network";
  requestUrl: string | null;

  constructor(
    message: string,
    status: number,
    kind: "backend" | "invalid_response" | "network" = "backend",
    requestUrl: string | null = null,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.kind = kind;
    this.requestUrl = requestUrl;
  }
}

const ABSOLUTE_URL_PATTERN = /^[a-z]+:\/\//i;
const INVALID_RESPONSE_MESSAGE = "Backend trả về phản hồi không hợp lệ.";
const NETWORK_ERROR_MESSAGE =
  "Không thể kết nối tới backend. Hãy kiểm tra API base URL, proxy /api, CORS, hoặc backend có đang chạy trên cổng 5080 hay không.";
const SESSION_KEY = "vinh-khanh-admin-web:session";
const API_BASE_URL_KEY = "vinh-khanh-admin-web:api-base-url";
const DEFAULT_LOCAL_API_PORT = "5080";
const DIRECT_LOCALHOST_API_BASE_URL = `http://localhost:${DEFAULT_LOCAL_API_PORT}/api/v1`;
const DIRECT_LOOPBACK_API_BASE_URL = `http://127.0.0.1:${DEFAULT_LOCAL_API_PORT}/api/v1`;

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

const normalizeDirectApiBaseUrl = (value: string | undefined) => {
  const normalized = normalizeConfiguredBaseUrl(value);
  if (!normalized) {
    return "";
  }

  return normalized.endsWith("/api/v1") ? normalized : `${normalized}/api/v1`;
};

const readStoredApiBaseUrl = () => {
  if (typeof window === "undefined") {
    return "";
  }

  return normalizeConfiguredBaseUrl(localStorage.getItem(API_BASE_URL_KEY) ?? undefined);
};

const API_BASE_URL = normalizeConfiguredBaseUrl(import.meta.env.VITE_API_BASE_URL);
const DIRECT_PROXY_API_BASE_URL = normalizeDirectApiBaseUrl(import.meta.env.VITE_API_PROXY_TARGET);
let preferredApiBaseUrl: string | null = readStoredApiBaseUrl() || API_BASE_URL || null;

const rememberApiBaseUrl = (baseUrl: string) => {
  preferredApiBaseUrl = baseUrl;

  if (typeof window === "undefined") {
    return;
  }

  if (baseUrl) {
    localStorage.setItem(API_BASE_URL_KEY, baseUrl);
    return;
  }

  localStorage.removeItem(API_BASE_URL_KEY);
};

const getPrimaryApiBaseUrl = () => preferredApiBaseUrl ?? API_BASE_URL;

const inferLocalApiBaseUrl = () => {
  if (typeof window === "undefined") {
    return "";
  }

  try {
    const url = new URL(window.location.origin);
    if (!url.hostname) {
      return "";
    }

    url.port = DEFAULT_LOCAL_API_PORT;
    return `${url.origin}/api/v1`;
  } catch {
    return "";
  }
};

const buildApiBaseCandidates = (method: string) => {
  const candidates: string[] = [];
  const isReadRequest = method === "GET" || method === "HEAD";

  const pushCandidate = (value: string | null | undefined) => {
    const candidate = value ?? "";
    if (!candidates.includes(candidate)) {
      candidates.push(candidate);
    }
  };

  pushCandidate(getPrimaryApiBaseUrl());

  if (isReadRequest) {
    pushCandidate("");
    pushCandidate(DIRECT_PROXY_API_BASE_URL);
    pushCandidate(inferLocalApiBaseUrl());
    pushCandidate(DIRECT_LOCALHOST_API_BASE_URL);
    pushCandidate(DIRECT_LOOPBACK_API_BASE_URL);
  }

  if (candidates.length === 0) {
    pushCandidate("");
  }

  return candidates;
};

const buildApiUrl = (path: string, baseUrl: string) => {
  if (!baseUrl || ABSOLUTE_URL_PATTERN.test(path)) {
    return path;
  }

  const basePath = resolveConfiguredBasePath(baseUrl);
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  const nextPath =
    basePath && (normalizedPath === basePath || normalizedPath.startsWith(`${basePath}/`))
      ? normalizedPath.slice(basePath.length)
      : normalizedPath;

  return `${baseUrl}${nextPath || "/"}`;
};

const readSession = () => {
  if (typeof window === "undefined") {
    return null;
  }

  const rawValue = localStorage.getItem(SESSION_KEY);
  if (!rawValue) {
    return null;
  }

  try {
    const parsed = JSON.parse(rawValue) as Partial<{ accessToken: string }>;
    if (!parsed.accessToken) {
      return null;
    }

    return {
      accessToken: parsed.accessToken,
    };
  } catch {
    return null;
  }
};

const buildHeaders = (headers?: HeadersInit) => {
  const nextHeaders = new Headers(headers);
  if (!nextHeaders.has("Accept")) {
    nextHeaders.set("Accept", "application/json");
  }

  const session = readSession();
  if (session?.accessToken && !nextHeaders.has("Authorization")) {
    nextHeaders.set("Authorization", `Bearer ${session.accessToken}`);
  }

  return nextHeaders;
};

export const resolveApiUrl = (path: string) => {
  return buildApiUrl(path, getPrimaryApiBaseUrl());
};

const isJsonResponse = (contentType: string) => {
  const normalized = contentType.toLowerCase();
  return normalized.includes("application/json") || normalized.includes("+json");
};

const buildInvalidResponseMessage = (response: Response, requestUrl: string, bodyPreview: string) => {
  const normalizedPreview = bodyPreview.trim().toLowerCase();
  const normalizedContentType = (response.headers.get("content-type") ?? "").toLowerCase();
  const looksLikeHtml =
    normalizedContentType.includes("text/html") ||
    normalizedPreview.startsWith("<!doctype html") ||
    normalizedPreview.startsWith("<html");

  if (looksLikeHtml) {
    return `Frontend đang nhận HTML thay vì JSON từ ${requestUrl}. Hãy kiểm tra proxy /api hoặc VITE_API_BASE_URL.`;
  }

  if (response.status === 404) {
    return `Không tìm thấy endpoint ${requestUrl}. Hãy kiểm tra API base URL hoặc proxy /api.`;
  }

  if ([502, 503, 504].includes(response.status)) {
    return `Không kết nối được tới backend qua ${requestUrl}. Hãy kiểm tra backend có đang chạy và proxy có trỏ đúng cổng 5080 không.`;
  }

  return INVALID_RESPONSE_MESSAGE;
};

const parseResponse = async <T>(response: Response, requestUrl: string) => {
  const contentType = response.headers.get("content-type") ?? "";
  if (!isJsonResponse(contentType)) {
    const bodyPreview = await response.text().catch(() => "");
    throw new ApiError(
      buildInvalidResponseMessage(response, requestUrl, bodyPreview),
      response.status,
      "invalid_response",
      requestUrl,
    );
  }

  let payload: ApiEnvelope<T>;
  const clonedResponse = response.clone();

  try {
    payload = (await response.json()) as ApiEnvelope<T>;
  } catch {
    const bodyPreview = await clonedResponse.text().catch(() => "");
    throw new ApiError(
      buildInvalidResponseMessage(response, requestUrl, bodyPreview),
      response.status,
      "invalid_response",
      requestUrl,
    );
  }

  if (!response.ok || !payload.success || payload.data === null) {
    throw new ApiError(
      payload.message ?? "Yêu cầu đến backend thất bại.",
      response.status,
      "backend",
      requestUrl,
    );
  }

  return payload.data;
};

const request = async <T>(path: string, init?: RequestInit) => {
  const method = init?.method?.toUpperCase() ?? "GET";
  const candidateBaseUrls = buildApiBaseCandidates(method);
  let lastError: unknown = null;

  for (let index = 0; index < candidateBaseUrls.length; index += 1) {
    const baseUrl = candidateBaseUrls[index] ?? "";
    const requestUrl = buildApiUrl(path, baseUrl);

    try {
      const response = await fetch(requestUrl, {
        ...init,
        cache: "no-store",
        headers: buildHeaders(init?.headers),
      });

      const data = await parseResponse<T>(response, requestUrl);
      rememberApiBaseUrl(baseUrl);
      return data;
    } catch (error) {
      if (error instanceof Error && error.name === "AbortError") {
        throw error;
      }

      const apiError =
        error instanceof ApiError
          ? error
          : new ApiError(NETWORK_ERROR_MESSAGE, 0, "network", requestUrl);
      const canTryAnotherCandidate =
        (method === "GET" || method === "HEAD") &&
        index < candidateBaseUrls.length - 1 &&
        (apiError.kind === "network" || apiError.kind === "invalid_response");

      if (canTryAnotherCandidate) {
        console.warn("Retrying API request with alternate base URL", {
          path,
          failedUrl: requestUrl,
          message: apiError.message,
        });
        lastError = apiError;
        continue;
      }

      throw apiError;
    }
  }

  throw (lastError instanceof ApiError
    ? lastError
    : new ApiError(
        NETWORK_ERROR_MESSAGE,
        0,
        "network",
        buildApiUrl(path, getPrimaryApiBaseUrl()),
      ));
};

const jsonRequest = async <T>(
  path: string,
  method: string,
  body?: unknown,
  init?: Omit<RequestInit, "body" | "headers" | "method">,
) =>
  request<T>(path, {
    ...init,
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
  getLoginOptions: (portal?: AuthPortal) => {
    const query = portal ? `?portal=${encodeURIComponent(portal)}` : "";
    return request<LoginAccountOption[]>(`/api/v1/auth/login-options${query}`);
  },
  login: (email: string, password: string, portal?: AuthPortal) =>
    jsonRequest<AuthSessionResponse>("/api/v1/auth/login", "POST", { email, password, portal }),
  refresh: (refreshToken: string) =>
    jsonRequest<AuthSessionResponse>("/api/v1/auth/refresh", "POST", { refreshToken }),
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
    request<GeocodingLocation>(
      `/api/v1/geocoding/reverse?lat=${encodeURIComponent(lat)}&lng=${encodeURIComponent(lng)}`,
      { signal },
    ),
  forwardGeocode: (query: string, signal?: AbortSignal) =>
    request<GeocodingLocation>(`/api/v1/geocoding/search?q=${encodeURIComponent(query)}`, { signal }),
  getPoiById: (poiId: string, signal?: AbortSignal) =>
    request<Poi>(`/api/v1/pois/${poiId}`, { signal }),
  getPoiDetail: (poiId: string, signal?: AbortSignal) =>
    request<PoiDetail>(`/api/v1/pois/${poiId}/detail`, { signal }),
  getPoiNarration: (
    poiId: string,
    languageCode: LanguageCode,
    voiceType: RegionVoice,
    signal?: AbortSignal,
  ) => {
    const query = new URLSearchParams({
      languageCode,
      voiceType,
    });

    return request<ResolvedPoiNarration>(`/api/v1/pois/${poiId}/narration?${query.toString()}`, {
      signal,
    });
  },
  savePoi: (poi: {
    id?: string;
    requestedId?: string;
    slug: string;
    address: string;
    lat: number;
    lng: number;
    categoryId: string;
    status: Poi["status"];
    featured: boolean;
    district: string;
    ward: string;
    priceRange: string;
    averageVisitDuration: number;
    popularityScore: number;
    tags: string[];
    ownerUserId: string | null;
    updatedBy: string;
    actorRole: AdminUser["role"];
    actorUserId: string;
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
    jsonRequest<AdminUser>(
      account.id ? `/api/v1/admin-users/${account.id}` : "/api/v1/admin-users",
      account.id ? "PUT" : "POST",
      account,
    ),
  getEndUser: (userId: string) =>
    request<EndUserProfile>(`/api/v1/users/${userId}`),
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
  saveRoute: (route: {
    id?: string;
    name: string;
    theme: string;
    description: string;
    durationMinutes: number;
    difficulty: string;
    coverImageUrl: string;
    isFeatured: boolean;
    stopPoiIds: string[];
    isActive: boolean;
    actorName: string;
    actorRole: AdminUser["role"];
    actorUserId: string;
  }) =>
    jsonRequest<TourRoute>(
      route.id ? `/api/v1/tours/${route.id}` : "/api/v1/tours",
      route.id ? "PUT" : "POST",
      route,
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
  translateTexts: (
    payload: {
      targetLanguageCode: Translation["languageCode"];
      sourceLanguageCode?: Translation["languageCode"];
      texts: string[];
    },
    signal?: AbortSignal,
  ) =>
    jsonRequest<TextTranslationResponse>(
      "/api/v1/translations/translate",
      "POST",
      payload,
      { signal },
    ),
  saveReviewStatus: (
    reviewId: string,
    payload: {
      status: Review["status"];
      actorName: string;
      actorRole: AdminUser["role"];
    },
  ) => jsonRequest<Review>(`/api/v1/reviews/${reviewId}/status`, "PATCH", payload),
  saveSettings: (
    settings: SystemSetting & {
      actorName: string;
      actorRole: AdminUser["role"];
    },
  ) => jsonRequest<SystemSetting>("/api/v1/settings", "PUT", settings),
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
