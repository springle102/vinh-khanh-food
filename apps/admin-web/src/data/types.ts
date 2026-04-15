export type LanguageCode = "vi" | "en" | "zh-CN" | "ko" | "ja";
export type Role = "SUPER_ADMIN" | "PLACE_OWNER";
export type AuditActorRole = Role | "SYSTEM";
export type UserStatus = "active" | "locked";
export type ApprovalStatus = "pending" | "approved" | "rejected";
export type ContentStatus = "draft" | "pending" | "published" | "rejected" | "archived" | "deleted";
export type EntityType = "poi" | "food_item" | "route" | "promotion";
export type AudioSourceType = "uploaded" | "tts";
export type AudioStatus = "ready" | "processing" | "missing";
export type MediaType = "image" | "video";
export type PromotionStatus = "upcoming" | "active" | "expired" | "hidden" | "deleted";
export type DeviceType = "ios" | "android" | "web";
export type TtsProvider = "elevenlabs";
export type UsageEventType = "poi_view" | "audio_play" | "qr_scan";

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
  approvalStatus: ApprovalStatus;
  rejectionReason: string | null;
  registrationSubmittedAt: string | null;
  registrationReviewedAt: string | null;
}

export interface PlaceOwnerRegistrationRecord {
  id: string;
  name: string;
  email: string;
  phone: string;
  status: UserStatus;
  approvalStatus: ApprovalStatus;
  rejectionReason: string | null;
  createdAt: string;
  registrationSubmittedAt: string | null;
  registrationReviewedAt: string | null;
}

export interface CustomerUser {
  id: string;
  name: string;
  email: string;
  phone: string;
  password: string;
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
  defaultLanguage: LanguageCode;
  country: string;
  createdAt: string;
  lastActiveAt: string | null;
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
  isActive: boolean;
  lockedBySuperAdmin: boolean;
  district: string;
  ward: string;
  priceRange: string;
  tags: string[];
  ownerUserId: string | null;
  updatedBy: string;
  createdAt: string;
  updatedAt: string;
  approvedAt: string | null;
  rejectionReason: string | null;
  rejectedAt: string | null;
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
  /** @deprecated Premium gating is no longer used by the public app. */
  isPremium?: boolean;
  updatedBy: string;
  updatedAt: string;
}

export interface AudioGuide {
  id: string;
  entityType: EntityType;
  entityId: string;
  languageCode: LanguageCode;
  audioUrl: string;
  sourceType: AudioSourceType;
  status: AudioStatus;
  updatedBy: string;
  updatedAt: string;
}

export interface PoiDetail {
  poi: Poi;
  translations: Translation[];
  audioGuides: AudioGuide[];
  foodItems: FoodItem[];
  foodItemTranslations: Translation[];
  promotions: Promotion[];
  promotionTranslations: Translation[];
  mediaAssets: MediaAsset[];
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
  isSystemRoute: boolean;
  ownerUserId: string | null;
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

export interface ViewLog {
  id: string;
  poiId: string;
  languageCode: LanguageCode;
  deviceType: DeviceType;
  viewedAt: string;
}

/** @deprecated Replaced by AppUsageEvent with eventType="audio_play". */
export interface AudioListenLog {
  id: string;
  poiId: string;
  languageCode: LanguageCode;
  listenedAt: string;
  durationInSeconds: number;
}

export interface AppUsageEvent {
  id: string;
  eventType: UsageEventType;
  poiId: string | null;
  languageCode: LanguageCode;
  platform: DeviceType;
  sessionId: string;
  source: string;
  metadata: string;
  durationInSeconds: number | null;
  occurredAt: string;
}

export interface AuditLog {
  id: string;
  actorId: string;
  actorName: string;
  actorRole: AuditActorRole;
  actorType: "ADMIN";
  action: string;
  module: string;
  targetId: string;
  targetSummary: string;
  beforeSummary: string | null;
  afterSummary: string | null;
  sourceApp: string;
  createdAt: string;
}

export interface SystemSetting {
  appName: string;
  supportEmail: string;
  defaultLanguage: LanguageCode;
  fallbackLanguage: LanguageCode;
  supportedLanguages: LanguageCode[];
  /** @deprecated Replaced by supportedLanguages. */
  freeLanguages?: LanguageCode[];
  /** @deprecated Replaced by supportedLanguages. */
  premiumLanguages?: LanguageCode[];
  /** @deprecated Premium unlock is no longer used by the public app. */
  premiumUnlockPriceUsd?: number;
  mapProvider: "google" | "mapbox" | "openstreetmap";
  storageProvider: "cloudinary" | "s3";
  ttsProvider: TtsProvider;
  geofenceRadiusMeters: number;
  analyticsRetentionDays: number;
}

export interface DataSyncState {
  version: string;
  generatedAt: string;
  lastChangedAt: string;
}

export interface AdminDataState {
  users: AdminUser[];
  /** @deprecated Customer accounts are no longer part of the active admin flow. */
  customerUsers: CustomerUser[];
  categories: PoiCategory[];
  pois: Poi[];
  foodItems: FoodItem[];
  translations: Translation[];
  audioGuides: AudioGuide[];
  mediaAssets: MediaAsset[];
  routes: TourRoute[];
  promotions: Promotion[];
  usageEvents: AppUsageEvent[];
  /** @deprecated Replaced by usageEvents. */
  viewLogs: ViewLog[];
  /** @deprecated Replaced by usageEvents. */
  audioListenLogs: AudioListenLog[];
  auditLogs: AuditLog[];
  settings: SystemSetting;
  syncState?: DataSyncState | null;
}
