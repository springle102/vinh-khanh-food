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
  CustomerUser,
  FoodItem,
  MediaAsset,
  Poi,
  Promotion,
  Review,
  SystemSetting,
  TourRoute,
  Translation,
} from "./types";

const SESSION_KEY = "vinh-khanh-admin-web:session";

const normalizeTtsProvider = (_value: string | undefined): SystemSetting["ttsProvider"] => "google_translate";

const normalizeSystemSetting = (settings: SystemSetting): SystemSetting => ({
  ...settings,
  ttsProvider: normalizeTtsProvider(settings.ttsProvider),
});

const EMPTY_ADMIN_STATE: AdminDataState = {
  users: [],
  customerUsers: [],
  categories: [],
  pois: [],
  foodItems: [],
  translations: [],
  audioGuides: [],
  mediaAssets: [],
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
    ttsProvider: "google_translate",
    geofenceRadiusMeters: 0,
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
  pois: payload.pois ?? [],
  foodItems: payload.foodItems ?? [],
  translations: payload.translations ?? [],
  audioGuides: payload.audioGuides ?? [],
  mediaAssets: payload.mediaAssets ?? [],
  routes: payload.routes ?? [],
  promotions: payload.promotions ?? [],
  reviews: payload.reviews ?? [],
  viewLogs: payload.viewLogs ?? [],
  audioListenLogs: payload.audioListenLogs ?? [],
  auditLogs: payload.auditLogs ?? [],
  settings: normalizeSystemSetting(payload.settings ?? EMPTY_ADMIN_STATE.settings),
});

const readScopeParams = () => {
  if (typeof window === "undefined") {
    return null;
  }

  const rawValue = localStorage.getItem(SESSION_KEY);
  if (!rawValue) {
    return null;
  }

  try {
    const parsed = JSON.parse(rawValue) as Partial<{ userId: string; role: AdminUser["role"] }>;
    if (!parsed.userId || !parsed.role) {
      return null;
    }

    return {
      userId: parsed.userId,
      role: parsed.role,
    };
  } catch {
    return null;
  }
};

const applyOwnerScope = (nextState: AdminDataState) => {
  const scope = readScopeParams();
  if (!scope || scope.role !== "PLACE_OWNER") {
    return nextState;
  }

  const owner = nextState.users.find(
    (item) => item.id === scope.userId && item.role === "PLACE_OWNER",
  );
  if (!owner) {
    return nextState;
  }

  const ownerPoiIds = new Set(
    nextState.pois
      .filter(
        (poi) =>
          poi.ownerUserId === scope.userId ||
          (!!owner.managedPoiId && poi.id === owner.managedPoiId),
      )
      .map((poi) => poi.id),
  );

  if (owner.managedPoiId) {
    ownerPoiIds.add(owner.managedPoiId);
  }

  const pois = nextState.pois.filter((poi) => ownerPoiIds.has(poi.id));
  const foodItems = nextState.foodItems.filter((item) => ownerPoiIds.has(item.poiId));
  const promotions = nextState.promotions.filter((item) => ownerPoiIds.has(item.poiId));
  const reviews = nextState.reviews.filter((item) => ownerPoiIds.has(item.poiId));
  const viewLogs = nextState.viewLogs.filter((item) => ownerPoiIds.has(item.poiId));
  const audioListenLogs = nextState.audioListenLogs.filter((item) => ownerPoiIds.has(item.poiId));
  const categories = nextState.categories.filter((category) =>
    pois.some((poi) => poi.categoryId === category.id),
  );
  const foodItemIds = new Set(foodItems.map((item) => item.id));
  const routeIds = new Set(
    nextState.routes
      .filter((route) => route.stopPoiIds.some((poiId) => ownerPoiIds.has(poiId)))
      .map((route) => route.id),
  );
  const routes = nextState.routes.filter((route) => routeIds.has(route.id));
  const translations = nextState.translations.filter(
    (item) =>
      (item.entityType === "poi" && ownerPoiIds.has(item.entityId)) ||
      (item.entityType === "food_item" && foodItemIds.has(item.entityId)) ||
      (item.entityType === "route" && routeIds.has(item.entityId)),
  );
  const audioGuides = nextState.audioGuides.filter(
    (item) =>
      (item.entityType === "poi" && ownerPoiIds.has(item.entityId)) ||
      (item.entityType === "food_item" && foodItemIds.has(item.entityId)) ||
      (item.entityType === "route" && routeIds.has(item.entityId)),
  );
  const mediaAssets = nextState.mediaAssets.filter(
    (item) =>
      (item.entityType === "poi" && ownerPoiIds.has(item.entityId)) ||
      (item.entityType === "food_item" && foodItemIds.has(item.entityId)) ||
      (item.entityType === "route" && routeIds.has(item.entityId)),
  );
  const users = nextState.users.filter((item) => item.id === scope.userId);
  const customerUsers = nextState.customerUsers.filter((customer) =>
    customer.favoritePoiIds.some((poiId) => ownerPoiIds.has(poiId)),
  );

  return {
    ...nextState,
    users,
    customerUsers,
    categories,
    pois,
    foodItems,
    translations,
    audioGuides,
    mediaAssets,
    routes,
    promotions,
    reviews,
    viewLogs,
    audioListenLogs,
  };
};

type PoiDraft = Omit<Poi, "id" | "createdAt" | "updatedAt" | "updatedBy"> & {
  id?: string;
  requestedId?: string;
  translationLanguageCode: Translation["languageCode"];
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
  savePoi: (draft: PoiDraft, actor: AdminUser) => Promise<Poi>;
  saveUser: (
    user: Omit<AdminUser, "id" | "createdAt" | "lastLoginAt"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  savePromotion: (
    promotion: Omit<Promotion, "id"> & { id?: string },
    actor: AdminUser,
  ) => Promise<void>;
  saveRoute: (
    route: Omit<TourRoute, "id" | "updatedBy" | "updatedAt"> & { id?: string },
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
  saveCustomerUserStatus: (
    userId: string,
    isBanned: boolean,
    actor: AdminUser,
  ) => Promise<void>;
  saveSettings: (settings: SystemSetting, actor: AdminUser) => Promise<void>;
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
      const nextState = applyOwnerScope(toState(await adminApi.getBootstrap()));
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
        featured: draft.featured,
        district: draft.district,
        ward: draft.ward,
        priceRange: draft.priceRange,
        averageVisitDuration: draft.averageVisitDuration,
        popularityScore: draft.popularityScore,
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
          isPremium: !state.settings.freeLanguages.includes(draft.translationLanguageCode),
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
    [refreshData, state, state.settings.freeLanguages, state.translations],
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
        managedPoiId: user.role === "PLACE_OWNER" ? user.managedPoiId : null,
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
    },
    [refreshData],
  );

  const saveRoute = useCallback(
    async (
      route: Omit<TourRoute, "id" | "updatedBy" | "updatedAt"> & { id?: string },
      actor: AdminUser,
    ) => {
      const routePayload = {
        id: route.id,
        name: route.name,
        theme: route.theme,
        description: route.description,
        durationMinutes: route.durationMinutes,
        difficulty: route.difficulty,
        coverImageUrl: route.coverImageUrl,
        isFeatured: route.isFeatured,
        stopPoiIds: route.stopPoiIds,
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

  const saveCustomerUserStatus = useCallback(
    async (userId: string, isBanned: boolean, actor: AdminUser) => {
      await adminApi.saveEndUserStatus(userId, {
        isBanned,
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
      savePoi,
      saveUser,
      savePromotion,
      saveRoute,
      saveAudioGuide,
      saveTranslation,
      saveReviewStatus,
      saveCustomerUserStatus,
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
      saveCustomerUserStatus,
      saveReviewStatus,
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
