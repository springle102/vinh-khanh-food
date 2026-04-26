import type {
  AdminDataState,
  AdminUser,
  AudioGuide,
  FoodItem,
  GeocodingLocation,
  DashboardSummary,
  LanguageCode,
  MediaAsset,
  Poi,
  PoiChangeRequest,
  PoiDetail,
  PlaceOwnerRegistrationRecord,
  Promotion,
  ResolvedPoiNarration,
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

export type PoiAudioGenerationRequest = {
  languageCode: LanguageCode;
  voiceId?: string | null;
  modelId?: string | null;
  outputFormat?: string | null;
  forceRegenerate?: boolean;
};

export type PoiAudioBulkGenerationRequest = {
  forceRegenerate?: boolean;
  includeMissing?: boolean;
  includeFailed?: boolean;
  includeOutdated?: boolean;
};

export type PoiAudioGenerationResult = {
  poiId: string;
  requestedLanguageCode: LanguageCode;
  effectiveLanguageCode: LanguageCode;
  success: boolean;
  skipped: boolean;
  regenerated: boolean;
  message: string;
  transcriptText: string;
  textHash: string;
  audioGuide: AudioGuide | null;
  providerStatusCode?: number | null;
  providerErrorCode?: string | null;
  providerErrorMessage?: string | null;
  providerResponseBody?: string | null;
  attemptedVoiceId?: string | null;
  attemptedModelId?: string | null;
  outputFormat?: string | null;
};

type PoiSavePayload = {
  id?: string;
  requestedId?: string;
  slug: string;
  address: string;
  lat: number;
  lng: number;
  categoryId: string;
  status: Poi["status"];
  district: string;
  ward: string;
  priceRange: string;
  triggerRadius: number;
  priority: number;
  placeTier: Poi["placeTier"];
  tags: string[];
  ownerUserId: string | null;
  updatedBy: string;
  actorRole: AdminUser["role"];
  actorUserId: string;
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
  "Không thể kết nối tới API. Nếu đang chạy local, hãy kiểm tra backend, CORS và dev proxy /api.";
const API_DIAGNOSTIC_LABEL = "[admin-api]";
const SESSION_KEY = "vinh-khanh-admin-web:session";
const LEGACY_API_BASE_URL_KEY = "vinh-khanh-admin-web:api-base-url";
const DEFAULT_API_BASE_PATH = "/api/v1";
export const ADMIN_SESSION_INVALIDATED_EVENT = "vinh-khanh-admin-web:session-invalidated";

type StoredSession = {
  userId: string;
  role: AdminUser["role"];
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
};

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

const clearLegacyApiBaseUrlPreference = () => {
  if (typeof window === "undefined") {
    return;
  }

  localStorage.removeItem(LEGACY_API_BASE_URL_KEY);
};

clearLegacyApiBaseUrlPreference();

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

const buildNetworkErrorMessage = (requestUrl: string) =>
  `Không thể kết nối tới backend qua ${requestUrl}. ` +
  "Nếu đang chạy local, hãy kiểm tra backend có đang listen đúng port, CORS và dev proxy /api.";

const PRIMARY_API_BASE_URL = normalizeConfiguredBaseUrl(
  import.meta.env.VITE_API_BASE_URL || DEFAULT_API_BASE_PATH,
) || DEFAULT_API_BASE_PATH;

const logApiConnectionIssue = (
  message: string,
  details: Record<string, unknown>,
) => {
  if (typeof console === "undefined") {
    return;
  }

  console.error(API_DIAGNOSTIC_LABEL, message, {
    configuredApiBaseUrl: PRIMARY_API_BASE_URL,
    ...details,
  });
};

const dispatchSessionInvalidated = () => {
  if (typeof window === "undefined") {
    return;
  }

  window.dispatchEvent(new Event(ADMIN_SESSION_INVALIDATED_EVENT));
};

const readStoredSession = (): StoredSession | null => {
  if (typeof window === "undefined") {
    return null;
  }

  const rawValue = localStorage.getItem(SESSION_KEY);
  if (!rawValue) {
    return null;
  }

  try {
    const parsed = JSON.parse(rawValue) as Partial<StoredSession>;
    if (!parsed.userId || !parsed.role || !parsed.refreshToken) {
      return null;
    }

    return {
      userId: parsed.userId,
      role: parsed.role,
      accessToken: parsed.accessToken ?? "",
      refreshToken: parsed.refreshToken,
      expiresAt: parsed.expiresAt ?? "",
    };
  } catch {
    return null;
  }
};

const readSession = () => {
  const session = readStoredSession();
  if (!session?.accessToken) {
    return null;
  }

  return {
    accessToken: session.accessToken,
  };
};

const writeSession = (session: AuthSessionResponse) => {
  if (typeof window === "undefined") {
    return;
  }

  localStorage.setItem(
    SESSION_KEY,
    JSON.stringify({
      userId: session.userId,
      role: session.role,
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
      expiresAt: session.expiresAt,
    } satisfies StoredSession),
  );
};

const clearSession = () => {
  if (typeof window === "undefined") {
    return;
  }

  localStorage.removeItem(SESSION_KEY);
};

const buildHeaders = (
  headers?: HeadersInit,
  options?: {
    accessToken?: string | null;
    forceAccessToken?: boolean;
  },
) => {
  const nextHeaders = new Headers(headers);
  if (!nextHeaders.has("Accept")) {
    nextHeaders.set("Accept", "application/json");
  }

  const accessToken = options?.accessToken ?? readSession()?.accessToken ?? "";
  if (accessToken && (options?.forceAccessToken || !nextHeaders.has("Authorization"))) {
    nextHeaders.set("Authorization", `Bearer ${accessToken}`);
  }

  return nextHeaders;
};

export const resolveApiUrl = (path: string) => {
  return buildApiUrl(path, PRIMARY_API_BASE_URL);
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
  const looksLikeProxyError =
    normalizedPreview.includes("proxy error") ||
    normalizedPreview.includes("error occurred while trying to proxy");

  if (looksLikeHtml) {
    return `Frontend đang nhận HTML thay vì JSON từ ${requestUrl}. Hãy kiểm tra VITE_API_BASE_URL hoặc Vite proxy /api.`;
  }

  if (looksLikeProxyError) {
    return `Không thể kết nối tới backend dev qua ${requestUrl}. Hãy bật backend hoặc dùng npm run dev để chạy cả backend và admin-web.`;
  }

  if (response.status === 404) {
    return `Không tìm thấy endpoint ${requestUrl}. Hãy kiểm tra VITE_API_BASE_URL hoặc route backend.`;
  }

  if ([502, 503, 504].includes(response.status)) {
    return `Không kết nối được tới backend qua ${requestUrl}. Nếu đang chạy local, hãy kiểm tra backend và dev proxy /api.`;
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

const isAuthenticationRequest = (path: string) => {
  const normalizedPath = path.trim().toLowerCase();
  return normalizedPath.includes("/api/v1/auth/login") ||
    normalizedPath.includes("/api/v1/auth/refresh") ||
    normalizedPath.includes("/api/v1/auth/logout");
};

let refreshAccessTokenPromise: Promise<string | null> | null = null;

const refreshAccessToken = async (baseUrl: string) => {
  if (refreshAccessTokenPromise) {
    return refreshAccessTokenPromise;
  }

  refreshAccessTokenPromise = (async () => {
    const session = readStoredSession();
    if (!session?.refreshToken) {
      clearSession();
      dispatchSessionInvalidated();
      return null;
    }

    const requestUrl = buildApiUrl("/api/v1/auth/refresh", baseUrl);

    try {
      const response = await fetch(requestUrl, {
        method: "POST",
        cache: "no-store",
        headers: buildHeaders(
          {
            "Content-Type": "application/json",
          },
          {
            accessToken: "",
            forceAccessToken: true,
          },
        ),
        body: JSON.stringify({
          refreshToken: session.refreshToken,
        }),
      });
      const refreshedSession = await parseResponse<AuthSessionResponse>(response, requestUrl);
      writeSession(refreshedSession);
      return refreshedSession.accessToken;
    } catch {
      clearSession();
      dispatchSessionInvalidated();
      return null;
    }
  })();

  try {
    return await refreshAccessTokenPromise;
  } finally {
    refreshAccessTokenPromise = null;
  }
};

const requestWithBase = async <T>(path: string, baseUrl: string, init?: RequestInit) => {
  const requestUrl = buildApiUrl(path, baseUrl);

  try {
    const sendRequest = (accessToken?: string | null) =>
      fetch(requestUrl, {
        ...init,
        cache: "no-store",
        headers: buildHeaders(init?.headers, {
          accessToken,
          forceAccessToken: true,
        }),
      });

    let response = await sendRequest();
    if (response.status === 401 && !isAuthenticationRequest(path)) {
      const refreshedAccessToken = await refreshAccessToken(baseUrl);
      if (refreshedAccessToken) {
        response = await sendRequest(refreshedAccessToken);
      }
    }

    return await parseResponse<T>(response, requestUrl);
  } catch (error) {
    if (error instanceof Error && error.name === "AbortError") {
      throw error;
    }

    const apiError =
      error instanceof ApiError
        ? error
        : new ApiError(buildNetworkErrorMessage(requestUrl), 0, "network", requestUrl);

    if (apiError.kind !== "backend") {
      logApiConnectionIssue("API request failed", {
        method: init?.method ?? "GET",
        requestUrl,
        status: apiError.status,
        kind: apiError.kind,
        message: apiError.message,
      });
    }

    throw apiError;
  }
};

const request = async <T>(path: string, init?: RequestInit) =>
  requestWithBase<T>(path, PRIMARY_API_BASE_URL, init);

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
  getDashboardSummary: () => request<DashboardSummary>("/api/v1/dashboard/summary"),
  getLoginOptions: (portal?: AuthPortal, signal?: AbortSignal) => {
    const query = portal ? `?portal=${encodeURIComponent(portal)}` : "";
    return request<LoginAccountOption[]>(`/api/v1/auth/login-options${query}`, { signal });
  },
  login: (email: string, password: string, portal?: AuthPortal) =>
    jsonRequest<AuthSessionResponse>("/api/v1/auth/login", "POST", { email, password, portal }),
  refresh: (refreshToken: string) =>
    jsonRequest<AuthSessionResponse>("/api/v1/auth/refresh", "POST", { refreshToken }),
  logout: (refreshToken: string) =>
    jsonRequest<string>("/api/v1/auth/logout", "POST", { refreshToken }),
  createPlaceOwnerRegistration: (registration: {
    name: string;
    email: string;
    password: string;
    confirmPassword: string;
    phone: string;
  }) =>
    jsonRequest<PlaceOwnerRegistrationRecord>("/api/v1/place-owner-registrations", "POST", registration),
  accessPlaceOwnerRegistration: (credentials: { email: string; password: string }) =>
    jsonRequest<PlaceOwnerRegistrationRecord>(
      "/api/v1/place-owner-registrations/access",
      "POST",
      credentials,
    ),
  resubmitPlaceOwnerRegistration: (
    id: string,
    registration: {
      name: string;
      email: string;
      password: string;
      confirmPassword: string;
      phone: string;
      currentPassword: string;
    },
  ) =>
    jsonRequest<PlaceOwnerRegistrationRecord>(
      `/api/v1/place-owner-registrations/${id}/self`,
      "PUT",
      registration,
    ),
  approvePlaceOwnerRegistration: (id: string) =>
    jsonRequest<PlaceOwnerRegistrationRecord>(`/api/v1/place-owner-registrations/${id}/approve`, "POST"),
  rejectPlaceOwnerRegistration: (id: string, reason: string) =>
    jsonRequest<PlaceOwnerRegistrationRecord>(
      `/api/v1/place-owner-registrations/${id}/reject`,
      "POST",
      { reason },
    ),
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
  approvePoi: (poiId: string) =>
    jsonRequest<Poi>(`/api/v1/pois/${poiId}/approve`, "POST"),
  rejectPoi: (poiId: string, reason: string) =>
    jsonRequest<Poi>(`/api/v1/pois/${poiId}/reject`, "POST", { reason }),
  togglePoiActive: (poiId: string, isActive: boolean) =>
    jsonRequest<Poi>(`/api/v1/pois/${poiId}/toggle-active`, "PATCH", { isActive }),
  getPoiNarration: (
    poiId: string,
    languageCode: LanguageCode,
    signal?: AbortSignal,
  ) => {
    const query = new URLSearchParams({
      languageCode,
    });

    return request<ResolvedPoiNarration>(`/api/v1/pois/${poiId}/narration?${query.toString()}`, {
      signal,
    });
  },
  getPoiAudioStatus: (poiId: string, signal?: AbortSignal) =>
    request<AudioGuide[]>(`/api/v1/audio-guides/poi/${poiId}/status`, { signal }),
  generatePoiAudio: (
    poiId: string,
    payload: PoiAudioGenerationRequest,
  ) => jsonRequest<PoiAudioGenerationResult>(`/api/v1/audio-guides/poi/${poiId}/generate`, "POST", payload),
  regeneratePoiAudio: (
    poiId: string,
    payload: PoiAudioGenerationRequest,
  ) => jsonRequest<PoiAudioGenerationResult>(`/api/v1/audio-guides/poi/${poiId}/regenerate`, "POST", payload),
  generatePoiAllLanguagesAudio: (
    poiId: string,
    payload: PoiAudioBulkGenerationRequest = {},
  ) => jsonRequest<PoiAudioGenerationResult[]>(`/api/v1/audio-guides/poi/${poiId}/generate-all`, "POST", payload),
  generateBulkPoiAudio: (payload: PoiAudioBulkGenerationRequest = {}) =>
    jsonRequest<PoiAudioGenerationResult[]>(`/api/v1/audio-guides/bulk/generate`, "POST", payload),
  savePoi: (poi: PoiSavePayload) =>
    jsonRequest<Poi>(poi.id ? `/api/v1/pois/${poi.id}` : "/api/v1/pois", poi.id ? "PUT" : "POST", poi),
  submitPoiChangeRequest: (poiId: string, payload: {
    poi: PoiSavePayload;
    languageCode: LanguageCode;
    title: string;
    fullText: string;
    seoTitle?: string | null;
    seoDescription?: string | null;
  }) =>
    jsonRequest<PoiChangeRequest>(`/api/v1/poi-change-requests/poi/${poiId}`, "POST", payload),
  getPoiChangeRequests: () =>
    request<PoiChangeRequest[]>("/api/v1/poi-change-requests"),
  approvePoiChangeRequest: (requestId: string) =>
    jsonRequest<Poi>(`/api/v1/poi-change-requests/${requestId}/approve`, "POST", {}),
  rejectPoiChangeRequest: (requestId: string, reason: string) =>
    jsonRequest<PoiChangeRequest>(`/api/v1/poi-change-requests/${requestId}/reject`, "POST", { reason }),
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
  saveUserStatus: (userId: string, status: AdminUser["status"]) =>
    jsonRequest<AdminUser>(`/api/v1/admin-users/${userId}/status`, "PATCH", { status }),
  savePromotion: (promotion: {
    id?: string;
    poiId: string;
    title: string;
    description: string;
    startAt: string;
    endAt: string;
    status: Promotion["status"];
    visibleFrom?: string | null;
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<Promotion>(
      promotion.id ? `/api/v1/promotions/${promotion.id}` : "/api/v1/promotions",
      promotion.id ? "PUT" : "POST",
      promotion,
    ),
  deletePromotion: (promotionId: string) =>
    request<string>(`/api/v1/promotions/${promotionId}`, {
      method: "DELETE",
    }),
  saveRoute: (route: {
    id?: string;
    name: string;
    description: string;
    stopPoiIds: string[];
    isFeatured?: boolean;
    isActive?: boolean;
    actorName: string;
    actorRole: AdminUser["role"];
    actorUserId: string;
  }) =>
    jsonRequest<TourRoute>(
      route.id ? `/api/v1/tours/${route.id}` : "/api/v1/tours",
      route.id ? "PUT" : "POST",
      route,
    ),
  getRouteById: (routeId: string) =>
    request<TourRoute>(`/api/v1/tours/${routeId}`),
  deleteRoute: (routeId: string) =>
    request<string>(`/api/v1/tours/${routeId}`, {
      method: "DELETE",
    }),
  saveAudioGuide: (audioGuide: {
    id?: string;
    entityType: AudioGuide["entityType"];
    entityId: string;
    languageCode: AudioGuide["languageCode"];
    audioUrl: string;
    sourceType: AudioGuide["sourceType"];
    status: AudioGuide["status"];
    updatedBy: string;
    transcriptText?: string | null;
    audioFilePath?: string | null;
    audioFileName?: string | null;
    provider?: string | null;
    voiceId?: string | null;
    modelId?: string | null;
    outputFormat?: string | null;
    durationInSeconds?: number | null;
    fileSizeBytes?: number | null;
    textHash?: string | null;
    contentVersion?: string | null;
    generatedAt?: string | null;
    generationStatus?: AudioGuide["generationStatus"] | null;
    errorMessage?: string | null;
    isOutdated?: boolean | null;
    voiceType?: string | null;
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
  }) =>
    jsonRequest<FoodItem>(
      item.id ? `/api/v1/food-items/${item.id}` : "/api/v1/food-items",
      item.id ? "PUT" : "POST",
      item,
    ),
};
