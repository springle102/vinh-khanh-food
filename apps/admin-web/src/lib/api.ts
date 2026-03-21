import type {
  AdminDataState,
  AdminUser,
  AudioGuide,
  FoodItem,
  MediaAsset,
  Place,
  Promotion,
  QRCodeRecord,
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

const buildHeaders = (headers?: HeadersInit) => {
  const nextHeaders = new Headers(headers);
  if (!nextHeaders.has("Accept")) {
    nextHeaders.set("Accept", "application/json");
  }

  return nextHeaders;
};

const parseResponse = async <T>(response: Response) => {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) {
    if (!response.ok) {
      throw new ApiError("Backend tra ve phan hoi khong hop le.", response.status);
    }

    return null as T;
  }

  const payload = (await response.json()) as ApiEnvelope<T>;
  if (!response.ok || !payload.success || payload.data === null) {
    throw new ApiError(payload.message ?? "Yeu cau den backend that bai.", response.status);
  }

  return payload.data;
};

const request = async <T>(path: string, init?: RequestInit) => {
  const response = await fetch(path, {
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
  error instanceof Error ? error.message : "Yeu cau den backend that bai.";

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
  savePlace: (place: {
    id?: string;
    slug: string;
    address: string;
    lat: number;
    lng: number;
    categoryId: string;
    status: Place["status"];
    featured: boolean;
    defaultLanguageCode: Place["defaultLanguageCode"];
    district: string;
    ward: string;
    priceRange: string;
    averageVisitDuration: number;
    popularityScore: number;
    tags: string[];
    ownerUserId: string | null;
    updatedBy: string;
  }) =>
    jsonRequest<Place>(place.id ? `/api/v1/places/${place.id}` : "/api/v1/places", place.id ? "PUT" : "POST", place),
  saveUser: (account: {
    id?: string;
    name: string;
    email: string;
    phone: string;
    role: AdminUser["role"];
    status: AdminUser["status"];
    avatarColor: string;
    password: string | null;
    managedPlaceId: string | null;
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<AdminUser>(account.id ? `/api/v1/users/${account.id}` : "/api/v1/users", account.id ? "PUT" : "POST", account),
  savePromotion: (promotion: {
    id?: string;
    placeId: string;
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
  saveQrCodeState: (qrId: string, payload: {
    isActive: boolean;
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<QRCodeRecord>(`/api/v1/qr-codes/${qrId}/state`, "PATCH", payload),
  saveQrCodeImage: (qrId: string, payload: {
    qrImageUrl: string;
    actorName: string;
    actorRole: AdminUser["role"];
  }) =>
    jsonRequest<QRCodeRecord>(`/api/v1/qr-codes/${qrId}/image`, "PATCH", payload),
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
    placeId: string;
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
