import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
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
  Poi,
  Promotion,
  SystemSetting,
  TourRoute,
  Translation,
} from "./types";

const DEFAULT_SUPPORTED_LANGUAGES: SystemSetting["supportedLanguages"] = ["vi", "en", "zh-CN", "ko", "ja"];

const normalizeTtsProvider = (_value: string | undefined): SystemSetting["ttsProvider"] => "elevenlabs";

const normalizeSystemSetting = (settings: SystemSetting): SystemSetting => ({
  ...settings,
  defaultLanguage: settings.defaultLanguage || "vi",
  fallbackLanguage: settings.fallbackLanguage || "en",
  supportedLanguages:
    settings.supportedLanguages && settings.supportedLanguages.length > 0
      ? [...settings.supportedLanguages]
      : [...DEFAULT_SUPPORTED_LANGUAGES],
  ttsProvider: normalizeTtsProvider(settings.ttsProvider),
});

const EMPTY_ADMIN_STATE: AdminDataState = {
  users: [],
  categories: [],
  pois: [],
  foodItems: [],
  translations: [],
  audioGuides: [],
  mediaAssets: [],
  routes: [],
  promotions: [],
  usageEvents: [],
  viewLogs: [],
  audioListenLogs: [],
  auditLogs: [],
  settings: {
    appName: "",
    supportEmail: "",
    defaultLanguage: "vi",
    fallbackLanguage: "en",
    supportedLanguages: [...DEFAULT_SUPPORTED_LANGUAGES],
    mapProvider: "openstreetmap",
    storageProvider: "cloudinary",
    ttsProvider: "elevenlabs",
    geofenceRadiusMeters: 0,
    analyticsRetentionDays: 0,
  },
};

const toState = (payload: Partial<AdminDataState>): AdminDataState => ({
  ...EMPTY_ADMIN_STATE,
  ...payload,
  users: payload.users ?? [],
  categories: payload.categories ?? [],
  pois: payload.pois ?? [],
  foodItems: payload.foodItems ?? [],
  translations: payload.translations ?? [],
  audioGuides: payload.audioGuides ?? [],
  mediaAssets: payload.mediaAssets ?? [],
  routes: payload.routes ?? [],
  promotions: payload.promotions ?? [],
  usageEvents: payload.usageEvents ?? [],
  viewLogs: [],
  audioListenLogs: [],
  auditLogs: payload.auditLogs ?? [],
  settings: normalizeSystemSetting(payload.settings ?? EMPTY_ADMIN_STATE.settings),
});

type PoiDraft = Omit<Poi, "id" | "createdAt" | "updatedAt" | "updatedBy" | "approvedAt" | "rejectionReason" | "rejectedAt" | "isActive" | "lockedBySuperAdmin"> & {
  id?: string;
  requestedId?: string;
  translationLanguageCode: Translation["languageCode"];
  title: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
};

type RouteDraft = Pick<TourRoute, "name" | "description" | "stopPoiIds" | "isFeatured" | "isActive"> & {
  id?: string;
};

type AudioGuideDraft = {
  id?: string;
  entityType: AudioGuide["entityType"];
  entityId: string;
  languageCode: AudioGuide["languageCode"];
  audioUrl: string;
  sourceType: AudioGuide["sourceType"];
  status: AudioGuide["status"];
} & Partial<Omit<AudioGuide, "id" | "entityType" | "entityId" | "languageCode" | "audioUrl" | "sourceType" | "status" | "updatedAt" | "updatedBy">>;

type AdminDataContextValue = {
  state: AdminDataState;
  isBootstrapping: boolean;
  isRefreshing: boolean;
  bootstrapError: string | null;
  refreshData: () => Promise<AdminDataState>;
  savePoi: (draft: PoiDraft, actor: AdminUser) => Promise<Poi>;
  saveUser: (
    user: Omit<
      AdminUser,
      | "id"
      | "createdAt"
      | "lastLoginAt"
      | "approvalStatus"
      | "rejectionReason"
      | "registrationSubmittedAt"
      | "registrationReviewedAt"
      > & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveUserStatus: (
    userId: string,
    status: AdminUser["status"],
    actor: AdminUser,
  ) => Promise<void>;
  savePromotion: (
    promotion: Omit<Promotion, "id"> & { id?: string },
    actor: AdminUser,
  ) => Promise<Promotion>;
  saveRoute: (
    route: RouteDraft,
    actor: AdminUser,
  ) => Promise<void>;
  deleteRoute: (routeId: string) => Promise<void>;
  saveAudioGuide: (
    audioGuide: AudioGuideDraft,
    actor: AdminUser,
  ) => Promise<void>;
  saveTranslation: (
    translation: Omit<Translation, "id" | "updatedAt" | "updatedBy"> & { id?: string },
    actor: AdminUser,
  ) => Promise<Translation>;
  saveSettings: (settings: SystemSetting, actor: AdminUser) => Promise<void>;
  saveMediaAsset: (
    asset: Omit<MediaAsset, "id" | "createdAt"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveFoodItem: (
    foodItem: Omit<FoodItem, "id"> & { id?: string },
    actor: AdminUser,
  ) => Promise<FoodItem>;
};

const AdminDataContext = createContext<AdminDataContextValue | null>(null);

export const AdminDataProvider = ({ children }: PropsWithChildren) => {
  const [state, setState] = useState<AdminDataState>(EMPTY_ADMIN_STATE);
  const [hasBootstrapped, setHasBootstrapped] = useState(false);
  const [isBootstrapping, setBootstrapping] = useState(true);
  const [isRefreshing, setRefreshing] = useState(false);
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);
  const bootstrapRequestIdRef = useRef(0);

  const loadBootstrap = useCallback(async (mode: "initial" | "refresh") => {
    const requestId = bootstrapRequestIdRef.current + 1;
    bootstrapRequestIdRef.current = requestId;

    if (mode === "initial") {
      setBootstrapping(true);
      setBootstrapError(null);
    } else {
      setRefreshing(true);
    }

    try {
      const nextState = toState(await adminApi.getBootstrap());
      if (requestId === bootstrapRequestIdRef.current) {
        setState(nextState);
        setHasBootstrapped(true);
      }

      return nextState;
    } catch (error) {
      if (mode === "initial" && requestId === bootstrapRequestIdRef.current) {
        setBootstrapError(error instanceof Error ? error.message : "Không thể tải dữ liệu khởi tạo.");
      }

      throw error;
    } finally {
      if (requestId === bootstrapRequestIdRef.current) {
        if (mode === "initial") {
          setBootstrapping(false);
        } else {
          setRefreshing(false);
        }
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

  const savePoi = useCallback(
    async (draft: PoiDraft, actor: AdminUser) => {
      console.debug("[admin-poi] submit-form-draft", {
        poiId: draft.id ?? null,
        requestedPoiId: draft.requestedId ?? null,
        slug: draft.slug,
        translationLanguageCode: draft.translationLanguageCode,
        title: draft.title,
        shortTextLength: draft.shortText.length,
        fullTextLength: draft.fullText.length,
        address: draft.address,
        tags: draft.tags,
      });

      const poiPayload = {
        id: draft.id,
        requestedId: draft.requestedId,
        slug: draft.slug,
        address: draft.address,
        lat: draft.lat,
        lng: draft.lng,
        categoryId: draft.categoryId,
        status: draft.status,
        district: draft.district,
        ward: draft.ward,
        priceRange: draft.priceRange,
        triggerRadius: draft.triggerRadius,
        priority: draft.priority,
        tags: draft.tags,
        ownerUserId: draft.ownerUserId,
        updatedBy: actor.name,
        actorRole: actor.role,
        actorUserId: actor.id,
      };
      console.debug("[admin-poi] poi-api-payload", poiPayload);

      const savedPoi = await adminApi.savePoi({
        ...poiPayload,
      });
      let nextState = state;

      try {
        const translationPayload: Parameters<typeof adminApi.saveTranslation>[0] = {
          entityType: "poi",
          entityId: savedPoi.id,
          id: state.translations.find(
            (item) =>
              item.entityType === "poi" &&
              item.entityId === savedPoi.id &&
              item.languageCode === draft.translationLanguageCode,
          )?.id,
          languageCode: draft.translationLanguageCode,
          title: draft.title,
          shortText: draft.shortText,
          fullText: draft.fullText,
          seoTitle: draft.seoTitle,
          seoDescription: draft.seoDescription,
          updatedBy: actor.name,
        };
        console.debug("[admin-poi] translation-api-payload", translationPayload);
        await adminApi.saveTranslation(translationPayload);
      } finally {
        nextState = await refreshData();
      }

      console.debug("[admin-poi] saved-poi-result", {
        poi: nextState.pois.find((item) => item.id === savedPoi.id) ?? savedPoi,
        translation: nextState.translations.find(
          (item) =>
            item.entityType === "poi" &&
            item.entityId === savedPoi.id &&
            item.languageCode === draft.translationLanguageCode,
        ) ?? null,
      });

      return nextState.pois.find((item) => item.id === savedPoi.id) ?? savedPoi;
    },
    [refreshData, state, state.translations],
  );

  const saveUser = useCallback(
    async (
      user: Omit<
        AdminUser,
        | "id"
        | "createdAt"
        | "lastLoginAt"
        | "approvalStatus"
        | "rejectionReason"
        | "registrationSubmittedAt"
        | "registrationReviewedAt"
      > & { id?: string },
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
        managedPoiId: user.role === "PLACE_OWNER" ? user.managedPoiId : null,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
    },
    [refreshData],
  );

  const saveUserStatus = useCallback(
    async (
      userId: string,
      status: AdminUser["status"],
      _actor: AdminUser,
    ) => {
      await adminApi.saveUserStatus(userId, status);
      await refreshData();
    },
    [refreshData],
  );

  const savePromotion = useCallback(
    async (promotion: Omit<Promotion, "id"> & { id?: string }, actor: AdminUser) => {
      const saved = await adminApi.savePromotion({
        id: promotion.id,
        poiId: promotion.poiId,
        title: promotion.title,
        description: promotion.description,
        startAt: promotion.startAt,
        endAt: promotion.endAt,
        status: promotion.status,
        actorName: actor.name,
        actorRole: actor.role,
      });

      await refreshData();
      return saved;
    },
    [refreshData],
  );

  const saveRoute = useCallback(
    async (
      route: RouteDraft,
      actor: AdminUser,
    ) => {
      const routePayload = {
        id: route.id,
        name: route.name,
        description: route.description,
        stopPoiIds: route.stopPoiIds,
        isFeatured: route.isFeatured,
        isActive: route.isActive,
        actorName: actor.name,
        actorRole: actor.role,
        actorUserId: actor.id,
      };
      console.debug("[admin-route] route-api-payload", routePayload);

      await adminApi.saveRoute(routePayload);

      const nextState = await refreshData();
      console.debug(
        "[admin-route] saved-route-result",
        nextState.routes.find((item) => item.id === route.id) ?? null,
      );
    },
    [refreshData],
  );

  const deleteRoute = useCallback(async (routeId: string) => {
    await adminApi.deleteRoute(routeId);
    await refreshData();
  }, [refreshData]);

  const saveAudioGuide = useCallback(
    async (
      audioGuide: AudioGuideDraft,
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
      const saved = await adminApi.saveTranslation({
        ...translation,
        updatedBy: actor.name,
      });

      await refreshData();
      return saved;
    },
    [refreshData],
  );

  const saveSettings = useCallback(
    async (settingsDraft: SystemSetting, actor: AdminUser) => {
      await adminApi.saveSettings({
        ...normalizeSystemSetting(settingsDraft),
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
      console.debug("[admin-media] media-asset-api-payload", asset);
      await adminApi.saveMediaAsset(asset);
      const nextState = await refreshData();
      console.debug(
        "[admin-media] saved-media-asset-result",
        nextState.mediaAssets.find((item) => item.id === asset.id) ?? null,
      );
    },
    [refreshData],
  );

  const saveFoodItem = useCallback(
    async (foodItem: Omit<FoodItem, "id"> & { id?: string }, _actor: AdminUser) => {
      const saved = await adminApi.saveFoodItem(foodItem);
      await refreshData();
      return saved;
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
      savePoi,
      saveUser,
      saveUserStatus,
      savePromotion,
      saveRoute,
      deleteRoute,
      saveAudioGuide,
      saveTranslation,
      saveSettings,
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
      savePoi,
      savePromotion,
      saveRoute,
      deleteRoute,
      saveSettings,
      saveTranslation,
      saveUser,
      saveUserStatus,
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
