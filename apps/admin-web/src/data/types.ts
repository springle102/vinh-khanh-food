export type LanguageCode = "vi" | "en" | "zh-CN" | "ko" | "ja";
export type RegionVoice = "north" | "central" | "south" | "standard";
export type Role = "SUPER_ADMIN" | "PLACE_OWNER";
export type UserStatus = "active" | "locked";
export type CustomerStatus = "active" | "inactive" | "banned";
export type EndUserStatusCode = "ACTIVE" | "INACTIVE" | "BANNED";
export type ContentStatus = "draft" | "pending" | "published" | "archived";
export type EntityType = "poi" | "food_item" | "route";
export type AudioSourceType = "uploaded" | "tts";
export type AudioStatus = "ready" | "processing" | "missing";
export type MediaType = "image" | "video";
export type PromotionStatus = "upcoming" | "active" | "expired";
export type ReviewStatus = "pending" | "approved" | "hidden";
export type DeviceType = "ios" | "android" | "web";
export type TtsProvider = "google_translate";

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
  password: string;
  status: CustomerStatus;
  isActive: boolean;
  isBanned: boolean;
  preferredLanguage: LanguageCode;
  isPremium: boolean;
  favoritePoiIds: string[];
  createdAt: string;
  lastActiveAt: string | null;
  username?: string | null;
  country?: string;
}

export interface EndUserProfile {
  id: string;
  name: string;
  email: string;
  phone: string;
  password: string;
  username: string | null;
  isActive: boolean;
  isBanned: boolean;
  defaultLanguage: LanguageCode;
  country: string;
  createdAt: string;
  lastActiveAt: string | null;
  status: CustomerStatus;
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

export type NarrationResolutionStatus =
  | "stored"
  | "auto_translated"
  | "fallback_source";

export interface ResolvedPoiNarration {
  poiId: string;
  requestedLanguageCode: LanguageCode;
  sourceLanguageCode: LanguageCode | null;
  effectiveLanguageCode: LanguageCode;
  selectedVoice: RegionVoice;
  displayTitle: string;
  displayText: string;
  ttsInputText: string;
  sourceText: string;
  translatedText: string | null;
  translationStatus: NarrationResolutionStatus;
  fallbackMessage: string | null;
  audioGuide: AudioGuide | null;
  uiPlaybackKey: string;
  audioCacheKey: string;
  ttsLocale: string;
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
  theme: string;
  description: string;
  durationMinutes: number;
  difficulty: string;
  coverImageUrl: string;
  isFeatured: boolean;
  stopPoiIds: string[];
  isActive: boolean;
  updatedBy: string;
  updatedAt: string;
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
  ttsProvider: TtsProvider;
  geofenceRadiusMeters: number;
  guestReviewEnabled: boolean;
  analyticsRetentionDays: number;
}

export interface DataSyncState {
  version: string;
  generatedAt: string;
  lastChangedAt: string;
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
  syncState?: DataSyncState | null;
}
