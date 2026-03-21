export type LanguageCode = "vi" | "en" | "zh-CN" | "ko" | "ja";
export type RegionVoice = "north" | "central" | "south" | "standard";
export type Role = "SUPER_ADMIN" | "PLACE_OWNER";
export type UserStatus = "active" | "locked";
export type CustomerStatus = "active" | "blocked";
export type ContentStatus = "draft" | "published" | "archived";
export type EntityType = "place" | "food_item" | "route";
export type AudioSourceType = "uploaded" | "tts";
export type AudioStatus = "ready" | "processing" | "missing";
export type MediaType = "image" | "video";
export type PromotionStatus = "upcoming" | "active" | "expired";
export type ReviewStatus = "pending" | "approved" | "hidden";
export type DeviceType = "ios" | "android" | "web";

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
  managedPlaceId: string | null;
}

export interface CustomerUser {
  id: string;
  name: string;
  email: string;
  phone: string;
  status: CustomerStatus;
  preferredLanguage: LanguageCode;
  isPremium: boolean;
  totalScans: number;
  favoritePlaceIds: string[];
  createdAt: string;
  lastActiveAt: string | null;
}

export interface PlaceCategory {
  id: string;
  name: string;
  slug: string;
  icon: string;
  color: string;
}

export interface Place {
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
  placeId: string;
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

export interface MediaAsset {
  id: string;
  entityType: EntityType;
  entityId: string;
  type: MediaType;
  url: string;
  altText: string;
  createdAt: string;
}

export interface QRCodeRecord {
  id: string;
  entityType: EntityType;
  entityId: string;
  qrValue: string;
  qrImageUrl: string;
  isActive: boolean;
  lastScanAt: string | null;
}

export interface TourRoute {
  id: string;
  name: string;
  description: string;
  durationMinutes: number;
  difficulty: "easy" | "balanced" | "foodie";
  stopPlaceIds: string[];
  isFeatured: boolean;
}

export interface Promotion {
  id: string;
  placeId: string;
  title: string;
  description: string;
  startAt: string;
  endAt: string;
  status: PromotionStatus;
}

export interface Review {
  id: string;
  placeId: string;
  userName: string;
  rating: number;
  comment: string;
  languageCode: LanguageCode;
  createdAt: string;
  status: ReviewStatus;
}

export interface ViewLog {
  id: string;
  placeId: string;
  languageCode: LanguageCode;
  deviceType: DeviceType;
  viewedAt: string;
}

export interface AudioListenLog {
  id: string;
  placeId: string;
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
  qrAutoPlay: boolean;
  guestReviewEnabled: boolean;
  analyticsRetentionDays: number;
}

export interface AdminDataState {
  users: AdminUser[];
  customerUsers: CustomerUser[];
  categories: PlaceCategory[];
  places: Place[];
  foodItems: FoodItem[];
  translations: Translation[];
  audioGuides: AudioGuide[];
  mediaAssets: MediaAsset[];
  qrCodes: QRCodeRecord[];
  routes: TourRoute[];
  promotions: Promotion[];
  reviews: Review[];
  viewLogs: ViewLog[];
  audioListenLogs: AudioListenLog[];
  auditLogs: AuditLog[];
  settings: SystemSetting;
}
