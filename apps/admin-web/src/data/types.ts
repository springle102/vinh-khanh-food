export type LanguageCode = "vi" | "en" | "zh-CN" | "ko" | "ja";
export type RegionVoice = "north" | "central" | "south" | "standard";
export type Role = "SUPER_ADMIN" | "PLACE_OWNER";
export type UserStatus = "active" | "locked";
export type CustomerStatus = "active" | "inactive" | "banned";
export type EndUserStatusCode = "ACTIVE" | "INACTIVE" | "BANNED";
export type ContentStatus = "draft" | "published" | "archived";
export type EntityType = "poi" | "food_item" | "route";
export type AudioSourceType = "uploaded" | "tts";
export type AudioStatus = "ready" | "processing" | "missing";
export type MediaType = "image" | "video";
export type PromotionStatus = "upcoming" | "active" | "expired";
export type ReviewStatus = "pending" | "approved" | "hidden";
export type DeviceType = "ios" | "android" | "web";

export interface GeocodingLocation {
  address: string;
  district: string;
  ward: string;
  lat: number;
  lng: number;
}

export interface AdminUser {
  id: string;
  name: string;
  email: string;
  phone: string;
  role: Role;
  password: string;
  status: UserStatus;
  createdAt: string;
  lastLoginAt: string | null;
  avatarColor: string;
  managedPoiId: string | null;
}

export interface CustomerUser {
  id: string;
  name: string;
  email: string;
  phone: string;
  status: CustomerStatus;
  isActive: boolean;
  isBanned: boolean;
  preferredLanguage: LanguageCode;
  isPremium: boolean;
  totalScans: number;
  favoritePoiIds: string[];
  createdAt: string;
  lastActiveAt: string | null;
  username?: string | null;
  deviceId?: string | null;
  country?: string;
  deviceType?: Extract<DeviceType, "ios" | "android">;
}

export interface EndUserProfile {
  id: string;
  username: string | null;
  deviceId: string | null;
  isActive: boolean;
  isBanned: boolean;
  defaultLanguage: LanguageCode;
  country: string;
  deviceType: Extract<DeviceType, "ios" | "android">;
  createdAt: string;
  lastActiveAt: string | null;
  status: CustomerStatus;
}

export interface EndUserPoiVisit {
  id: string;
  userId: string;
  poiId: string;
  poiSlug: string;
  poiAddress: string;
  visitedAt: string;
  translatedLanguage: LanguageCode;
}

export interface PoiCategory {
  id: string;
  name: string;
  slug: string;
  icon: string;
  color: string;
}

export interface Poi {
  id: string;
  slug: string;
  address: string;
  lat: number;
  lng: number;
  categoryId: string;
  status: ContentStatus;
  featured: boolean;
  defaultLanguageCode: LanguageCode;
  district: string;
  ward: string;
  priceRange: string;
  averageVisitDuration: number;
  popularityScore: number;
  tags: string[];
  ownerUserId: string | null;
  updatedBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface FoodItem {
  id: string;
  poiId: string;
  name: string;
  description: string;
  priceRange: string;
  imageUrl: string;
  spicyLevel: "mild" | "medium" | "hot";
}

export interface Translation {
  id: string;
  entityType: EntityType;
  entityId: string;
  languageCode: LanguageCode;
  title: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
  isPremium: boolean;
  updatedBy: string;
  updatedAt: string;
}

export interface AudioGuide {
  id: string;
  entityType: EntityType;
  entityId: string;
  languageCode: LanguageCode;
  audioUrl: string;
  voiceType: RegionVoice;
  sourceType: AudioSourceType;
  status: AudioStatus;
  updatedBy: string;
  updatedAt: string;
}

export interface PoiDetail {
  poi: Poi;
  translations: Translation[];
  audioGuides: AudioGuide[];
}

export interface MediaAsset {
  id: string;
  entityType: EntityType;
  entityId: string;
  type: MediaType;
  url: string;
  altText: string;
  createdAt: string;
}

export interface TourRoute {
  id: string;
  name: string;
  description: string;
  durationMinutes: number;
  difficulty: "easy" | "balanced" | "foodie";
  stopPoiIds: string[];
  isFeatured: boolean;
}

export interface Promotion {
  id: string;
  poiId: string;
  title: string;
  description: string;
  startAt: string;
  endAt: string;
  status: PromotionStatus;
}

export interface Review {
  id: string;
  poiId: string;
  userName: string;
  rating: number;
  comment: string;
  languageCode: LanguageCode;
  createdAt: string;
  status: ReviewStatus;
}

export interface ViewLog {
  id: string;
  poiId: string;
  languageCode: LanguageCode;
  deviceType: DeviceType;
  viewedAt: string;
}

export interface AudioListenLog {
  id: string;
  poiId: string;
  languageCode: LanguageCode;
  listenedAt: string;
  durationInSeconds: number;
}

export interface AuditLog {
  id: string;
  actorName: string;
  actorRole: Role;
  action: string;
  target: string;
  createdAt: string;
}

export interface SystemSetting {
  appName: string;
  supportEmail: string;
  defaultLanguage: LanguageCode;
  fallbackLanguage: LanguageCode;
  freeLanguages: LanguageCode[];
  premiumLanguages: LanguageCode[];
  premiumUnlockPriceUsd: number;
  mapProvider: "google" | "mapbox" | "openstreetmap";
  storageProvider: "cloudinary" | "s3";
  ttsProvider: "native" | "azure";
  geofenceRadiusMeters: number;
  guestReviewEnabled: boolean;
  analyticsRetentionDays: number;
}

export interface AdminDataState {
  users: AdminUser[];
  customerUsers: CustomerUser[];
  categories: PoiCategory[];
  pois: Poi[];
  foodItems: FoodItem[];
  translations: Translation[];
  audioGuides: AudioGuide[];
  mediaAssets: MediaAsset[];
  routes: TourRoute[];
  promotions: Promotion[];
  reviews: Review[];
  viewLogs: ViewLog[];
  audioListenLogs: AudioListenLog[];
  auditLogs: AuditLog[];
  settings: SystemSetting;
}
