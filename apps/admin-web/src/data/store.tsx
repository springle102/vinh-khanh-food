import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from "react";
import { adminApi } from "../lib/api";
import type {
  AdminDataState,
  AdminUser,
  AudioGuide,
  FoodItem,
  MediaAsset,
  Place,
  Promotion,
  Review,
  SystemSetting,
  Translation,
} from "./types";

const EMPTY_ADMIN_STATE: AdminDataState = {
  users: [],
  customerUsers: [],
  categories: [],
  places: [],
  foodItems: [],
  translations: [],
  audioGuides: [],
  mediaAssets: [],
  qrCodes: [],
  routes: [],
  promotions: [],
  reviews: [],
  viewLogs: [],
  audioListenLogs: [],
  auditLogs: [],
  settings: {
    appName: "",
    supportEmail: "",
    defaultLanguage: "vi",
    fallbackLanguage: "en",
    freeLanguages: [],
    premiumLanguages: [],
    premiumUnlockPriceUsd: 0,
    mapProvider: "openstreetmap",
    storageProvider: "cloudinary",
    ttsProvider: "native",
    geofenceRadiusMeters: 0,
    qrAutoPlay: false,
    guestReviewEnabled: false,
    analyticsRetentionDays: 0,
  },
};

const toState = (payload: Partial<AdminDataState>): AdminDataState => ({
  ...EMPTY_ADMIN_STATE,
  ...payload,
  users: payload.users ?? [],
  customerUsers: payload.customerUsers ?? [],
  categories: payload.categories ?? [],
  places: payload.places ?? [],
  foodItems: payload.foodItems ?? [],
  translations: payload.translations ?? [],
  audioGuides: payload.audioGuides ?? [],
  mediaAssets: payload.mediaAssets ?? [],
  qrCodes: payload.qrCodes ?? [],
  routes: payload.routes ?? [],
  promotions: payload.promotions ?? [],
  reviews: payload.reviews ?? [],
  viewLogs: payload.viewLogs ?? [],
  audioListenLogs: payload.audioListenLogs ?? [],
  auditLogs: payload.auditLogs ?? [],
  settings: payload.settings ?? EMPTY_ADMIN_STATE.settings,
});

type PlaceDraft = Omit<Place, "id" | "createdAt" | "updatedAt" | "updatedBy"> & {
  id?: string;
  title: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
};

type AdminDataContextValue = {
  state: AdminDataState;
  isBootstrapping: boolean;
  isRefreshing: boolean;
  bootstrapError: string | null;
  refreshData: () => Promise<AdminDataState>;
  savePlace: (draft: PlaceDraft, actor: AdminUser) => Promise<Place>;
  saveUser: (
    user: Omit<AdminUser, "id" | "createdAt" | "lastLoginAt"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  savePromotion: (
    promotion: Omit<Promotion, "id"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveAudioGuide: (
    audioGuide: Omit<AudioGuide, "id" | "updatedAt" | "updatedBy"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveTranslation: (
    translation: Omit<Translation, "id" | "updatedAt" | "updatedBy"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveReviewStatus: (
    reviewId: string,
    status: Review["status"],
    actor: AdminUser,
  ) => Promise<void>;
  saveSettings: (settings: SystemSetting, actor: AdminUser) => Promise<void>;
  saveRouteQrState: (qrId: string, isActive: boolean, actor: AdminUser) => Promise<void>;
  saveQrCodeImage: (qrId: string, qrImageUrl: string, actor: AdminUser) => Promise<void>;
  saveMediaAsset: (
    asset: Omit<MediaAsset, "id" | "createdAt"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveFoodItem: (
    foodItem: Omit<FoodItem, "id"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
};

const AdminDataContext = createContext<AdminDataContextValue | null>(null);

export const AdminDataProvider = ({ children }: PropsWithChildren) => {
  const [state, setState] = useState<AdminDataState>(EMPTY_ADMIN_STATE);
  const [hasBootstrapped, setHasBootstrapped] = useState(false);
  const [isBootstrapping, setBootstrapping] = useState(true);
  const [isRefreshing, setRefreshing] = useState(false);
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);

  const loadBootstrap = useCallback(async (mode: "initial" | "refresh") => {
    if (mode === "initial") {
      setBootstrapping(true);
      setBootstrapError(null);
    } else {
      setRefreshing(true);
    }

    try {
      const nextState = toState(await adminApi.getBootstrap());
      setState(nextState);
      setHasBootstrapped(true);
      return nextState;
    } catch (error) {
      if (mode === "initial") {
        setBootstrapError(error instanceof Error ? error.message : "Khong the tai bootstrap.");
      }

      throw error;
    } finally {
      if (mode === "initial") {
        setBootstrapping(false);
      } else {
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    void loadBootstrap("initial").catch(() => undefined);
  }, [loadBootstrap]);

  const refreshData = useCallback(
    () => loadBootstrap(hasBootstrapped ? "refresh" : "initial"),
    [hasBootstrapped, loadBootstrap],
  );

  const savePlace = useCallback(
    async (draft: PlaceDraft, actor: AdminUser) => {
      const savedPlace = await adminApi.savePlace({
        id: draft.id,
        slug: draft.slug,
        address: draft.address,
        lat: draft.lat,
        lng: draft.lng,
        categoryId: draft.categoryId,
        status: draft.status,
        featured: draft.featured,
        defaultLanguageCode: draft.defaultLanguageCode,
        district: draft.district,
        ward: draft.ward,
        priceRange: draft.priceRange,
        averageVisitDuration: draft.averageVisitDuration,
        popularityScore: draft.popularityScore,
        tags: draft.tags,
        ownerUserId: draft.ownerUserId,
        updatedBy: actor.name,
      });

      try {
        await adminApi.saveTranslation({
          entityType: "place",
          entityId: savedPlace.id,
          id: state.translations.find(
            (item) =>
              item.entityType === "place" &&
              item.entityId === savedPlace.id &&
              item.languageCode === draft.defaultLanguageCode,
          )?.id,
          languageCode: draft.defaultLanguageCode,
          title: draft.title,
          shortText: draft.shortText,
          fullText: draft.fullText,
          seoTitle: draft.seoTitle,
          seoDescription: draft.seoDescription,
          isPremium: !state.settings.freeLanguages.includes(draft.defaultLanguageCode),
          updatedBy: actor.name,
        });
      } finally {
        await refreshData();
      }

      return savedPlace;
    },
    [refreshData, state.settings.freeLanguages, state.translations],
  );

  const saveUser = useCallback(
    async (
      user: Omit<AdminUser, "id" | "createdAt" | "lastLoginAt"> & { id?: string },
      actor: AdminUser,
    ) => {
      await adminApi.saveUser({
        id: user.id,
        name: user.name,
        email: user.email,
        phone: user.phone,
        role: user.role,
        status: user.status,
        avatarColor: user.avatarColor,
        password: user.password || null,
        managedPlaceId: user.role === "PLACE_OWNER" ? user.managedPlaceId : null,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const savePromotion = useCallback(
    async (promotion: Omit<Promotion, "id"> & { id?: string }, actor: AdminUser) => {
      await adminApi.savePromotion({
        id: promotion.id,
        placeId: promotion.placeId,
        title: promotion.title,
        description: promotion.description,
        startAt: promotion.startAt,
        endAt: promotion.endAt,
        status: promotion.status,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveAudioGuide = useCallback(
    async (
      audioGuide: Omit<AudioGuide, "id" | "updatedAt" | "updatedBy"> & { id?: string },
      actor: AdminUser,
    ) => {
      await adminApi.saveAudioGuide({
        ...audioGuide,
        updatedBy: actor.name,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveTranslation = useCallback(
    async (
      translation: Omit<Translation, "id" | "updatedAt" | "updatedBy"> & { id?: string },
      actor: AdminUser,
    ) => {
      await adminApi.saveTranslation({
        ...translation,
        updatedBy: actor.name,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveReviewStatus = useCallback(
    async (reviewId: string, status: Review["status"], actor: AdminUser) => {
      await adminApi.saveReviewStatus(reviewId, {
        status,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveSettings = useCallback(
    async (settingsDraft: SystemSetting, actor: AdminUser) => {
      await adminApi.saveSettings({
        ...settingsDraft,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveRouteQrState = useCallback(
    async (qrId: string, isActive: boolean, actor: AdminUser) => {
      await adminApi.saveQrCodeState(qrId, {
        isActive,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveQrCodeImage = useCallback(
    async (qrId: string, qrImageUrl: string, actor: AdminUser) => {
      await adminApi.saveQrCodeImage(qrId, {
        qrImageUrl,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveMediaAsset = useCallback(
    async (
      asset: Omit<MediaAsset, "id" | "createdAt"> & { id?: string },
      _actor: AdminUser,
    ) => {
      await adminApi.saveMediaAsset(asset);
      await refreshData();
    },
    [refreshData],
  );

  const saveFoodItem = useCallback(
    async (foodItem: Omit<FoodItem, "id"> & { id?: string }, _actor: AdminUser) => {
      await adminApi.saveFoodItem(foodItem);
      await refreshData();
    },
    [refreshData],
  );

  const value = useMemo<AdminDataContextValue>(
    () => ({
      state,
      isBootstrapping,
      isRefreshing,
      bootstrapError,
      refreshData,
      savePlace,
      saveUser,
      savePromotion,
      saveAudioGuide,
      saveTranslation,
      saveReviewStatus,
      saveSettings,
      saveQrCodeImage,
      saveRouteQrState,
      saveMediaAsset,
      saveFoodItem,
    }),
    [
      bootstrapError,
      isBootstrapping,
      isRefreshing,
      refreshData,
      saveAudioGuide,
      saveFoodItem,
      saveMediaAsset,
      savePlace,
      savePromotion,
      saveQrCodeImage,
      saveReviewStatus,
      saveRouteQrState,
      saveSettings,
      saveTranslation,
      saveUser,
      state,
    ],
  );

  return <AdminDataContext.Provider value={value}>{children}</AdminDataContext.Provider>;
};

export const useAdminData = () => {
  const context = useContext(AdminDataContext);
  if (!context) {
    throw new Error("useAdminData must be used within AdminDataProvider");
  }

  return context;
};
