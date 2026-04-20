import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent, type FormEvent, type KeyboardEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { ImageSourceField } from "../../components/ui/ImageSourceField";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useSearchParams } from "react-router-dom";
import { useAdminData } from "../../data/store";
import type { AdminDataState, AudioGuide, FoodItem, LanguageCode, MediaAsset, Poi, PoiDetail, Translation } from "../../data/types";
import { adminApi, getErrorMessage, type PoiAudioGenerationResult } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { getCategoryName, getEntityTranslationFromList, getOwnerName, searchPois } from "../../lib/selectors";
import { contentStatusLabels, formatDateTime, formatNumber, languageLabels, poiActivityLabels, slugify } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";
import { OpenStreetMapPicker, type PoiMapItem } from "./OpenStreetMapPicker";
import { usePoiNarrationPlayback } from "./usePoiNarrationPlayback";
import {
  languageLocales,
  supportedNarrationLanguages,
  type ResolvedPoiNarration,
} from "../../lib/narration";

const DEFAULT_LAT = 10.7578;
const DEFAULT_LNG = 106.7033;

const describeAudioGenerationResult = (result: PoiAudioGenerationResult) => {
  if (result.success) {
    return result.message;
  }

  const details = [
    result.providerStatusCode ? `HTTP ${result.providerStatusCode}` : null,
    result.providerErrorCode,
    result.providerErrorMessage,
    result.attemptedVoiceId ? `voice=${result.attemptedVoiceId}` : null,
    result.attemptedModelId ? `model=${result.attemptedModelId}` : null,
  ].filter(Boolean);

  return details.length > 0
    ? `${result.message} (${details.join(" / ")})`
    : result.message;
};

type PoiFormState = {
  id?: string;
  poiId: string;
  title: string;
  slug: string;
  address: string;
  lat: string;
  lng: string;
  categoryId: string;
  status: Poi["status"];
  contentLanguageCode: LanguageCode;
  district: string;
  ward: string;
  priceRange: string;
  triggerRadius: string;
  priority: string;
  tags: string;
  ownerUserId: string;
  fullText: string;
  audioUrl: string;
  audioSourceType: AudioGuide["sourceType"];
  audioStatus: AudioGuide["status"];
};

type PoiRepresentativeImageFormState = {
  id?: string;
  url: string;
};

type PoiFoodItemFormState = {
  clientId: string;
  id?: string;
  poiId: string;
  name: string;
  description: string;
  priceRange: string;
  imageUrl: string;
  spicyLevel: FoodItem["spicyLevel"];
};

const createDefaultForm = (status: Poi["status"] = "draft"): PoiFormState => ({
  poiId: "",
  title: "",
  slug: "",
  address: "",
  lat: "",
  lng: "",
  categoryId: "",
  status,
  contentLanguageCode: "vi",
  district: "",
  ward: "",
  priceRange: "",
  triggerRadius: "20",
  priority: "0",
  tags: "",
  ownerUserId: "",
  fullText: "",
  audioUrl: "",
  audioSourceType: "generated",
  audioStatus: "missing",
});

const createDefaultPoiImageForm = (): PoiRepresentativeImageFormState => ({
  url: "",
});

const createPoiFoodItemForm = (
  clientId: string,
  poiId = "",
): PoiFoodItemFormState => ({
  clientId,
  poiId,
  name: "",
  description: "",
  priceRange: "",
  imageUrl: "",
  spicyLevel: "mild",
});

const isPoiEntityType = (entityType: string) => entityType === "poi" || entityType === "place";

const findPoiRepresentativeImage = (mediaAssets: MediaAsset[], poiId?: string) => {
  if (!poiId) {
    return null;
  }

  return mediaAssets.reduce<MediaAsset | null>((latestAsset, item) => {
    if (!isPoiEntityType(item.entityType) || item.entityId !== poiId || item.type !== "image") {
      return latestAsset;
    }

    if (!latestAsset) {
      return item;
    }

    return Date.parse(item.createdAt) > Date.parse(latestAsset.createdAt) ? item : latestAsset;
  }, null);
};

const buildPoiFoodItemForms = (
  foodItems: FoodItem[],
  poiId?: string,
  translations: Translation[] = [],
  languageCode: LanguageCode = "vi",
  settings?: Pick<AdminDataState, "settings">,
): PoiFoodItemFormState[] =>
  foodItems
    .filter((item) => item.poiId === poiId)
    .map((item) => {
      const translation = settings
        ? getEntityTranslationFromList(
            translations.filter(
              (translationItem) =>
                translationItem.entityType === "food_item" &&
                translationItem.entityId === item.id,
            ),
            settings,
            languageCode,
          )
        : null;

      return {
        clientId: item.id,
        id: item.id,
        poiId: item.poiId,
        name: translation?.title ?? item.name,
        description: translation?.fullText || translation?.shortText || item.description,
        priceRange: item.priceRange,
        imageUrl: item.imageUrl,
        spicyLevel: item.spicyLevel,
      };
    });

const hasFoodItemContent = (foodItem: PoiFoodItemFormState) =>
  Boolean(
    foodItem.name.trim() ||
      foodItem.description.trim() ||
      foodItem.priceRange.trim() ||
      foodItem.imageUrl.trim(),
  );

const parseCoordinate = (value: string, fallback: number) => {
  const normalizedValue = value.trim();
  // Create mode starts with blank coordinate inputs, so we must not coerce "" to 0.
  if (!normalizedValue) {
    return fallback;
  }

  const parsed = Number(normalizedValue);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const parseRequiredCoordinate = (value: string) => {
  const normalizedValue = value.trim();
  if (!normalizedValue) {
    return null;
  }

  const parsed = Number(normalizedValue);
  return Number.isFinite(parsed) ? parsed : null;
};

const resolveTriggerRadiusPreview = (value: string) => {
  const normalizedValue = value.trim().replace(",", ".");
  const parsed = Number(normalizedValue);
  return Number.isFinite(parsed) && parsed >= 20 ? parsed : 20;
};

const findPoiAudioGuide = (
  audioGuides: AudioGuide[],
  poiId?: string,
  languageCode?: AudioGuide["languageCode"],
) => {
  const matches = audioGuides.filter(
    (item) =>
      isPoiEntityType(item.entityType) &&
      item.entityId === poiId &&
      item.languageCode === languageCode,
  );

  return (
    matches.find(
      (item) =>
        item.status === "ready" &&
        item.generationStatus === "success" &&
        !item.isOutdated,
    ) ??
    matches[0] ??
    null
  );
};

const findPoiTranslationForLanguage = (
  translations: Translation[],
  poi: Poi,
  languageCode: LanguageCode,
) =>
  translations.find(
    (item) =>
      isPoiEntityType(item.entityType) &&
      item.entityId === poi.id &&
      item.languageCode === languageCode,
  ) ?? null;

const findPoiTranslationWithFallback = (
  translations: Translation[],
  poi: Poi,
  preferredLanguage: LanguageCode | undefined,
  settings: Pick<AdminDataState["settings"], "defaultLanguage" | "fallbackLanguage">,
) => {
  return getEntityTranslationFromList(
    translations.filter(
      (item) => isPoiEntityType(item.entityType) && item.entityId === poi.id,
    ),
    { settings },
    preferredLanguage,
  );
};

const buildPoiTranslationFields = (
  poi: Poi | null,
  translations: Translation[],
  languageCode: LanguageCode,
) => {
  if (!poi) {
    return {
      title: "",
      fullText: "",
    };
  }

  const exactTranslation = findPoiTranslationForLanguage(translations, poi, languageCode);
  return {
    title: exactTranslation?.title ?? "",
    fullText: exactTranslation?.fullText ?? exactTranslation?.shortText ?? "",
  };
};

const resolvePoiTranslationLanguage = (
  translations: Translation[],
  poi: Poi,
  preferredLanguage: LanguageCode | undefined,
  settings: Pick<AdminDataState["settings"], "defaultLanguage" | "fallbackLanguage">,
) =>
  findPoiTranslationWithFallback(translations, poi, preferredLanguage, settings)?.languageCode ??
  preferredLanguage ??
  settings.defaultLanguage;

const isApprovedLifecyclePoi = (poi: Pick<Poi, "approvedAt"> | null | undefined) =>
  Boolean(poi?.approvedAt);

const getPoiStatusBadge = (poi: Poi) => {
  if (poi.status === "pending" || poi.status === "rejected") {
    return {
      status: poi.status,
      label: contentStatusLabels[poi.status],
    };
  }

  if (isApprovedLifecyclePoi(poi)) {
    return {
      status: poi.isActive ? "active" : "inactive",
      label: poi.isActive ? poiActivityLabels.active : poiActivityLabels.inactive,
    };
  }

  return {
    status: poi.status,
    label: contentStatusLabels[poi.status],
  };
};

const buildPoiEditorContentSnapshot = (
  form: PoiFormState,
  poiImageForm: PoiRepresentativeImageFormState,
  poiFoodItemForms: PoiFoodItemFormState[],
) =>
  JSON.stringify({
    poiId: form.poiId.trim(),
    title: form.title.trim(),
    slug: form.slug.trim(),
    address: form.address.trim(),
    lat: form.lat.trim(),
    lng: form.lng.trim(),
    categoryId: form.categoryId.trim(),
    contentLanguageCode: form.contentLanguageCode,
    district: form.district.trim(),
    ward: form.ward.trim(),
    priceRange: form.priceRange.trim(),
    triggerRadius: form.triggerRadius.trim(),
    priority: form.priority.trim(),
    ownerUserId: form.ownerUserId.trim(),
    tags: form.tags
      .split(",")
      .map((item) => item.trim())
      .filter(Boolean),
    fullText: form.fullText.trim(),
    audioUrl: form.audioUrl.trim(),
    audioSourceType: form.audioSourceType,
    audioStatus: form.audioStatus,
    representativeImageUrl: poiImageForm.url.trim(),
    foodItems: poiFoodItemForms
      .map((item) => ({
        id: item.id ?? "",
        clientId: item.clientId,
        poiId: item.poiId.trim(),
        name: item.name.trim(),
        description: item.description.trim(),
        priceRange: item.priceRange.trim(),
        imageUrl: item.imageUrl.trim(),
        spicyLevel: item.spicyLevel,
      }))
      .filter((item) =>
        item.id ||
        item.name ||
        item.description ||
        item.priceRange ||
        item.imageUrl,
      ),
  });

const getSubmissionStatus = (
  role: "SUPER_ADMIN" | "PLACE_OWNER" | undefined,
  status: Poi["status"],
  approvedAt?: string | null,
  isActive?: boolean,
) => {
  if (role !== "PLACE_OWNER") {
    return status;
  }

  if (approvedAt && status !== "pending" && status !== "rejected") {
    return isActive ? "published" : "draft";
  }

  return status === "rejected" ? "rejected" : "pending";
};

const canToggleApprovedPoi = (
  poi: Pick<Poi, "approvedAt" | "lockedBySuperAdmin"> | null | undefined,
  role: "SUPER_ADMIN" | "PLACE_OWNER" | undefined,
) => {
  if (!poi || !isApprovedLifecyclePoi(poi)) {
    return false;
  }

  if (role === "SUPER_ADMIN") {
    return true;
  }

  return role === "PLACE_OWNER" && !poi.lockedBySuperAdmin;
};

export const PoisPage = () => {
  const { state, isBootstrapping, refreshData, saveAudioGuide, saveFoodItem, saveMediaAsset, savePoi, saveTranslation } = useAdminData();
  const { user } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const syncVersion = state.syncState?.version ?? "bootstrap";
  const isOwner = user?.role === "PLACE_OWNER";
  const isSuperAdmin = user?.role === "SUPER_ADMIN";
  const {
    playbackState,
    stopCurrentAudio,
    primePlayback,
    resolvePoiNarration,
    playPoiNarration,
    playResolvedNarration,
    buildPlaybackKey,
  } = usePoiNarrationPlayback(state);
  const [keyword, setKeyword] = useState("");
  const [statusFilter, setStatusFilter] = useState<Poi["status"] | "all">("all");
  const [selectedPoiId, setSelectedPoiId] = useState<string | null>(null);
  const [selectedPoiDetail, setSelectedPoiDetail] = useState<PoiDetail | null>(null);
  const [selectedNarrationLanguage, setSelectedNarrationLanguage] = useState<LanguageCode>("vi");
  const [isModalOpen, setModalOpen] = useState(false);
  const [isRejectModalOpen, setRejectModalOpen] = useState(false);
  const [isSaving, setSaving] = useState(false);
  const [isUploadingAudio, setUploadingAudio] = useState(false);
  const [isGeneratingAudio, setGeneratingAudio] = useState(false);
  const [formError, setFormError] = useState("");
  const [pageAlert, setPageAlert] = useState("");
  const [audioActionMessage, setAudioActionMessage] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const [rejectError, setRejectError] = useState("");
  const [addressSearchVersion, setAddressSearchVersion] = useState(0);
  const [hasNarrationInteraction, setHasNarrationInteraction] = useState(false);
  const [isFetchingPoiDetail, setFetchingPoiDetail] = useState(false);
  const [visiblePoiIds, setVisiblePoiIds] = useState<string[]>([]);
  const [hasSlugBeenManuallyEdited, setHasSlugBeenManuallyEdited] = useState(false);
  const [selectedNarration, setSelectedNarration] = useState<ResolvedPoiNarration | null>(null);
  const [isResolvingNarration, setResolvingNarration] = useState(false);
  const [poiIdBeingRejected, setPoiIdBeingRejected] = useState<string | null>(null);
  const [activeToggleTarget, setActiveToggleTarget] = useState<{
    poiId: string;
    nextIsActive: boolean;
  } | null>(null);
  const [form, setForm] = useState<PoiFormState>(() =>
    createDefaultForm(getSubmissionStatus(user?.role, "draft")),
  );
  const [poiFormLoadState, setPoiFormLoadState] = useState<{
    mode: "create" | "edit" | "view";
    dataLoaded: boolean;
  }>({
    mode: "create",
    dataLoaded: true,
  });
  const [poiImageForm, setPoiImageForm] = useState<PoiRepresentativeImageFormState>(
    createDefaultPoiImageForm,
  );
  const [poiFoodItemForms, setPoiFoodItemForms] = useState<PoiFoodItemFormState[]>(
    [],
  );
  const [activeImageUploads, setActiveImageUploads] = useState(0);
  const [playbackIntent, setPlaybackIntent] = useState<{
    token: number;
    poiId: string | null;
    language: LanguageCode;
  }>({
    token: 0,
    poiId: null,
    language: "vi",
  });
  const poiDetailCacheRef = useRef(new Map<string, PoiDetail>());
  const poiDetailAbortRef = useRef<AbortController | null>(null);
  const narrationAbortRef = useRef<AbortController | null>(null);
  const poiFoodItemDraftSequenceRef = useRef(0);
  const poiFormLoadRequestRef = useRef(0);
  const poiEditorInitialContentSnapshotRef = useRef<string | null>(null);
  const lastHandledPlaybackIntentTokenRef = useRef(0);
  const selectedNarrationRequestRef = useRef(0);
  const lastNarrationSelectionRef = useRef<{
    poiId: string | null;
    language: LanguageCode;
  }>({
    poiId: null,
    language: "vi",
  });

  const filteredPois = useMemo(() => {
    const searched = searchPois(state.pois, state, keyword);
    return statusFilter === "all" ? searched : searched.filter((item) => item.status === statusFilter);
  }, [keyword, state, statusFilter]);
  const pendingPois = useMemo(
    () => state.pois.filter((item) => item.status === "pending"),
    [state.pois],
  );
  const approvedLifecyclePois = useMemo(
    () => state.pois.filter((item) => isApprovedLifecyclePoi(item)),
    [state.pois],
  );
  const rejectedPois = useMemo(
    () => state.pois.filter((item) => item.status === "rejected"),
    [state.pois],
  );

  useEffect(() => {
    if (!filteredPois.length) {
      setSelectedPoiId(null);
      return;
    }

    if (!selectedPoiId || !filteredPois.some((item) => item.id === selectedPoiId)) {
      setSelectedPoiId(filteredPois[0].id);
    }
  }, [filteredPois, selectedPoiId]);

  const selectedPoi = selectedPoiDetail?.poi ?? state.pois.find((item) => item.id === selectedPoiId) ?? null;
  const poiBeingRejected = useMemo(
    () =>
      state.pois.find((item) => item.id === poiIdBeingRejected) ??
      (selectedPoi?.id === poiIdBeingRejected ? selectedPoi : null),
    [poiIdBeingRejected, selectedPoi, state.pois],
  );
  const poiBeingToggled = useMemo(
    () =>
      activeToggleTarget
        ? state.pois.find((item) => item.id === activeToggleTarget.poiId) ??
          (selectedPoi?.id === activeToggleTarget.poiId ? selectedPoi : null)
        : null,
    [activeToggleTarget, selectedPoi, state.pois],
  );
  const selectedPoiTranslations = useMemo(
    () =>
      selectedPoi && selectedPoiDetail?.poi.id === selectedPoi.id
        ? selectedPoiDetail.translations
        : state.translations.filter(
            (item) => isPoiEntityType(item.entityType) && item.entityId === selectedPoi?.id,
          ),
    [selectedPoi, selectedPoiDetail, state.translations],
  );
  const selectedNarrationTranslation = useMemo(
    () =>
      selectedPoi
        ? findPoiTranslationWithFallback(
            selectedPoiTranslations,
            selectedPoi,
            selectedNarrationLanguage,
            state.settings,
          )
        : null,
    [selectedNarrationLanguage, selectedPoi, selectedPoiTranslations, state.settings],
  );
  const selectedExactTitle =
    selectedNarrationTranslation?.languageCode === selectedNarrationLanguage
      ? selectedNarrationTranslation.title
      : "";
  const selectedNarrationDisplayTitle = selectedNarration?.displayTitle?.trim() || "";
  const selectedDisplayTitle =
    selectedNarrationDisplayTitle ||
    selectedExactTitle ||
    selectedPoi?.slug ||
    "";
  const selectedDisplayText =
    selectedNarration?.displayText ||
    selectedNarrationTranslation?.fullText ||
    selectedNarrationTranslation?.shortText ||
    "";
  const availableNarrationLanguages = useMemo(
    () => (selectedPoi ? supportedNarrationLanguages : []),
    [selectedPoi],
  );
  const selectedPlaybackKey = selectedPoi
    ? buildPlaybackKey(selectedPoi.id, selectedNarrationLanguage)
    : null;
  const selectedAudio = selectedNarration?.audioGuide ?? null;
  const currentFormPoiId = form.id || form.poiId.trim();
  const currentFormAudioGuide = useMemo(() => {
    if (!currentFormPoiId) {
      return null;
    }

    const scopedAudioGuides =
      selectedPoiDetail?.poi.id === currentFormPoiId
        ? selectedPoiDetail.audioGuides
        : state.audioGuides.filter(
            (item) => isPoiEntityType(item.entityType) && item.entityId === currentFormPoiId,
          );

    return findPoiAudioGuide(scopedAudioGuides, currentFormPoiId, form.contentLanguageCode);
  }, [currentFormPoiId, form.contentLanguageCode, selectedPoiDetail, state.audioGuides]);
  const currentFormAudioStatusLabel = currentFormAudioGuide
    ? currentFormAudioGuide.isOutdated || currentFormAudioGuide.generationStatus === "outdated"
      ? "Outdated"
      : currentFormAudioGuide.generationStatus === "failed"
        ? "Lỗi"
        : currentFormAudioGuide.generationStatus === "pending" || currentFormAudioGuide.status === "processing"
          ? "Đang chờ generate"
          : currentFormAudioGuide.generationStatus === "success" && currentFormAudioGuide.status === "ready"
            ? "Thành công"
            : "Chưa có"
    : "Chưa có";
  const isSelectedPoiNarrationPlaying =
    selectedPlaybackKey === playbackState.playbackKey &&
    playbackState.status === "playing";
  const isSelectedPoiNarrationPaused =
    selectedPlaybackKey === playbackState.playbackKey &&
    playbackState.status === "paused";
  const getDisplayedPoiTranslation = useCallback(
    (poi: Poi, preferredLanguage?: LanguageCode) => {
      const translations =
        selectedPoiDetail?.poi.id === poi.id
          ? selectedPoiDetail.translations
          : state.translations.filter(
              (item) => isPoiEntityType(item.entityType) && item.entityId === poi.id,
            );

      return findPoiTranslationWithFallback(
        translations,
        poi,
        preferredLanguage,
        state.settings,
      );
    },
    [selectedPoiDetail, state.settings, state.translations],
  );

  const mapPois = useMemo<PoiMapItem[]>(
    () =>
      filteredPois.map((poi) => ({
        id: poi.id,
        title: getDisplayedPoiTranslation(poi, selectedNarrationLanguage)?.title ?? poi.slug,
        address: poi.address,
        category: getCategoryName(state, poi.categoryId),
        status: poi.status,
        lat: poi.lat,
        lng: poi.lng,
        triggerRadius: poi.triggerRadius,
      })),
    [filteredPois, getDisplayedPoiTranslation, selectedNarrationLanguage, state],
  );

  const handleVisiblePoiIdsChange = useCallback((nextVisiblePoiIds: string[]) => {
    setVisiblePoiIds((current) =>
      current.length === nextVisiblePoiIds.length &&
      current.every((poiId, index) => poiId === nextVisiblePoiIds[index])
        ? current
        : nextVisiblePoiIds,
    );
  }, []);

  const createPoiFoodItemDraft = useCallback((poiId = "") => {
    poiFoodItemDraftSequenceRef.current += 1;
    return createPoiFoodItemForm(`food-draft-${poiFoodItemDraftSequenceRef.current}`, poiId);
  }, []);

  const uploadImageAsset = useCallback(async (file: File, folder: string) => {
    setActiveImageUploads((current) => current + 1);

    try {
      const uploaded = await adminApi.uploadFile(file, folder);
      return uploaded.url;
    } finally {
      setActiveImageUploads((current) => Math.max(0, current - 1));
    }
  }, []);

  const fetchPOIDetail = useCallback(
    async (poiId: string, options?: { useCache?: boolean; updateSelected?: boolean }) => {
      const cached = poiDetailCacheRef.current.get(poiId);
      if (options?.useCache && cached) {
        if (options.updateSelected) {
          setSelectedPoiDetail(cached);
        }

        return cached;
      }

      if (options?.updateSelected) {
        poiDetailAbortRef.current?.abort();
      }

      const controller = new AbortController();
      if (options?.updateSelected) {
        poiDetailAbortRef.current = controller;
        setFetchingPoiDetail(true);
      }

      try {
        const detail = await adminApi.getPoiDetail(poiId, controller.signal);
        poiDetailCacheRef.current.set(poiId, detail);

        if (options?.updateSelected && poiDetailAbortRef.current === controller) {
          setSelectedPoiDetail(detail);
        }

        return detail;
      } finally {
        if (options?.updateSelected && poiDetailAbortRef.current === controller) {
          poiDetailAbortRef.current = null;
          setFetchingPoiDetail(false);
        }
      }
    },
    [],
  );

  const syncPoiAfterReviewAction = useCallback(
    async (poiId: string) => {
      const nextState = await refreshData();
      poiDetailCacheRef.current.delete(poiId);
      setSelectedPoiDetail((current) => (current?.poi.id === poiId ? null : current));

      if (!nextState.pois.some((item) => item.id === poiId)) {
        setSelectedPoiId(null);
        return;
      }

      setSelectedPoiId(poiId);
      void fetchPOIDetail(poiId, {
        useCache: false,
        updateSelected: true,
      }).catch(() => undefined);
    },
    [fetchPOIDetail, refreshData],
  );

  const prefetchPoiNarration = useCallback(
    async (poiId: string) => {
      if (!state.pois.some((item) => item.id === poiId)) {
        return;
      }

      await fetchPOIDetail(poiId, { useCache: true, updateSelected: false }).catch(() => null);
    },
    [fetchPOIDetail, state.pois],
  );

  const handlePoiSelect = useCallback(
    (poiId: string) => {
      primePlayback();

      const poi = state.pois.find((item) => item.id === poiId);
      if (!poi) {
        return;
      }

      const cachedDetail = poiDetailCacheRef.current.get(poiId);
      const nextLanguage = selectedNarrationLanguage;

      // Marker click only updates the selected POI and queues one playback intent.
      // The actual narration is started inside useEffect below so the flow stays:
      // map click -> selectedPOI -> resolve narration -> play audio/TTS.
      setSelectedPoiId(poiId);
      setSelectedPoiDetail(cachedDetail ?? null);
      setHasNarrationInteraction(true);
      setSelectedNarrationLanguage(nextLanguage);
      setPlaybackIntent((current) => ({
        token: current.token + 1,
        poiId,
        language: nextLanguage,
      }));
    },
    [primePlayback, selectedNarrationLanguage, state.pois],
  );

  const openCreateModal = () => {
    poiFormLoadRequestRef.current += 1;
    stopCurrentAudio();
    setHasSlugBeenManuallyEdited(false);
    setFormError("");
    setAudioActionMessage("");
    setPageAlert("");
    setAddressSearchVersion(0);
    setPoiFormLoadState({ mode: "create", dataLoaded: true });
    setPoiImageForm(createDefaultPoiImageForm());
    setPoiFoodItemForms([]);
    poiEditorInitialContentSnapshotRef.current = null;
    setForm({
      ...createDefaultForm(getSubmissionStatus(user?.role, "draft")),
      contentLanguageCode: selectedNarrationLanguage,
      ownerUserId: "",
    });
    setModalOpen(true);
  };

  const closePoiModal = () => {
    poiFormLoadRequestRef.current += 1;
    poiEditorInitialContentSnapshotRef.current = null;
    setAudioActionMessage("");
    setModalOpen(false);
  };

  const openRejectModal = (poi: Poi) => {
    setPoiIdBeingRejected(poi.id);
    setRejectReason("");
    setRejectError("");
    setRejectModalOpen(true);
  };

  const closeRejectModal = () => {
    setRejectModalOpen(false);
    setPoiIdBeingRejected(null);
    setRejectReason("");
    setRejectError("");
  };

  const openActiveToggleModal = (poi: Poi, nextIsActive: boolean) => {
    if (!canToggleApprovedPoi(poi, user?.role)) {
      if (isOwner && poi.lockedBySuperAdmin) {
        setPageAlert("POI này đang bị Super Admin ngừng hoạt động nên bạn không thể tự bật lại hoặc chỉnh sửa.");
      }

      return;
    }

    setPageAlert("");
    setActiveToggleTarget({
      poiId: poi.id,
      nextIsActive,
    });
  };

  const closeActiveToggleModal = () => {
    setActiveToggleTarget(null);
  };

  const openEditModal = async (poi: Poi) => {
    if (isOwner && poi.lockedBySuperAdmin) {
      setPageAlert("POI này đang bị Super Admin ngừng hoạt động nên bạn không thể tự bật lại hoặc chỉnh sửa.");
      return;
    }

    const modalMode = isSuperAdmin ? "view" : "edit";
    stopCurrentAudio();
    const requestId = poiFormLoadRequestRef.current + 1;
    poiFormLoadRequestRef.current = requestId;

    setHasSlugBeenManuallyEdited(false);
    setFormError("");
    setPageAlert("");
    setAddressSearchVersion(0);
    setPoiFormLoadState({ mode: modalMode, dataLoaded: false });
    setPoiImageForm(createDefaultPoiImageForm());
    setPoiFoodItemForms([]);
    poiEditorInitialContentSnapshotRef.current = null;
    setForm({
      ...createDefaultForm(
        getSubmissionStatus(user?.role, poi.status, poi.approvedAt, poi.isActive),
      ),
      id: poi.id,
      poiId: poi.id,
      contentLanguageCode: selectedNarrationLanguage,
    });
    setModalOpen(true);

    try {
      const detail = await fetchPOIDetail(poi.id, {
        useCache: false,
        updateSelected: false,
      });

      if (poiFormLoadRequestRef.current !== requestId) {
        return;
      }

      const loadedPoi = detail.poi;
      const editableLanguageCode = resolvePoiTranslationLanguage(
        detail.translations,
        loadedPoi,
        selectedNarrationLanguage,
        state.settings,
      );
      const translationFields = buildPoiTranslationFields(
        loadedPoi,
        detail.translations,
        editableLanguageCode,
      );
      const audioGuide = findPoiAudioGuide(detail.audioGuides, loadedPoi.id, editableLanguageCode);
      const representativeImage = findPoiRepresentativeImage(state.mediaAssets, loadedPoi.id);
      const loadedPoiImageForm = {
        id: representativeImage?.id,
        url: representativeImage?.url ?? "",
      };
      const loadedFoodItemForms = buildPoiFoodItemForms(
        detail.foodItems?.length ? detail.foodItems : state.foodItems,
        loadedPoi.id,
        detail.foodItemTranslations ?? state.translations,
        editableLanguageCode,
        state,
      );
      const loadedForm = {
        id: loadedPoi.id,
        poiId: loadedPoi.id,
        title: translationFields.title,
        slug: loadedPoi.slug,
        address: loadedPoi.address,
        lat: loadedPoi.lat.toString(),
        lng: loadedPoi.lng.toString(),
        categoryId: loadedPoi.categoryId,
        status: getSubmissionStatus(
          user?.role,
          loadedPoi.status,
          loadedPoi.approvedAt,
          loadedPoi.isActive,
        ),
        contentLanguageCode: editableLanguageCode,
        district: loadedPoi.district,
        ward: loadedPoi.ward,
        priceRange: loadedPoi.priceRange,
        triggerRadius: String(loadedPoi.triggerRadius ?? 20),
        priority: String(loadedPoi.priority ?? 0),
        tags: loadedPoi.tags.join(", "),
        ownerUserId: isOwner ? user?.id ?? "" : loadedPoi.ownerUserId ?? "",
        fullText: translationFields.fullText,
        audioUrl: audioGuide?.audioUrl ?? "",
        audioSourceType: audioGuide?.sourceType ?? "generated",
        audioStatus: audioGuide?.status ?? "missing",
      } satisfies PoiFormState;

      setSelectedPoiId(loadedPoi.id);
      setSelectedPoiDetail(detail);
      setPoiImageForm(loadedPoiImageForm);
      setPoiFoodItemForms(loadedFoodItemForms);
      setForm(loadedForm);
      poiEditorInitialContentSnapshotRef.current = buildPoiEditorContentSnapshot(
        loadedForm,
        loadedPoiImageForm,
        loadedFoodItemForms,
      );
      setPoiFormLoadState({ mode: modalMode, dataLoaded: true });
    } catch (error) {
      if (poiFormLoadRequestRef.current !== requestId) {
        return;
      }

      setFormError(getErrorMessage(error));
    }
  };

  useEffect(() => {
    const permissionError = searchParams.get("permissionError");
    if (permissionError !== "poi-edit-forbidden" || !isSuperAdmin) {
      return;
    }

    setPageAlert("Bạn không có quyền chỉnh sửa nội dung POI.");

    const nextSearchParams = new URLSearchParams(searchParams);
    nextSearchParams.delete("permissionError");
    setSearchParams(nextSearchParams, { replace: true });
  }, [isSuperAdmin, searchParams, setSearchParams]);

  useEffect(() => {
    const editPoiId = searchParams.get("editPoiId");
    const viewPoiId = searchParams.get("viewPoiId");

    if (!editPoiId && !viewPoiId) {
      return;
    }

    if (isBootstrapping) {
      return;
    }

    const requestedPoiId = isSuperAdmin ? viewPoiId : editPoiId;
    if (!requestedPoiId) {
      return;
    }

    const nextSearchParams = new URLSearchParams(searchParams);
    nextSearchParams.delete("editPoiId");
    nextSearchParams.delete("viewPoiId");
    setSearchParams(nextSearchParams, { replace: true });

    const poi = state.pois.find((item) => item.id === requestedPoiId);
    if (!poi) {
      setPageAlert(isSuperAdmin ? "Không tìm thấy POI để xem chi tiết." : "Không tìm thấy POI để chỉnh sửa.");
      return;
    }

    void openEditModal(poi);
  }, [isBootstrapping, isSuperAdmin, searchParams, setSearchParams, state.pois]);

  const handleContentLanguageChange = useCallback(
    (languageCode: LanguageCode) => {
      const currentPoiId = form.id || form.poiId.trim();
      if (currentPoiId) {
        setPoiFoodItemForms(
          buildPoiFoodItemForms(
            selectedPoiDetail?.poi.id === currentPoiId && selectedPoiDetail.foodItems.length
              ? selectedPoiDetail.foodItems
              : state.foodItems,
            currentPoiId,
            selectedPoiDetail?.poi.id === currentPoiId
              ? selectedPoiDetail.foodItemTranslations
              : state.translations,
            languageCode,
            state,
          ),
        );
      }

      setForm((current) => {
        const currentPoi =
          current.id ? state.pois.find((item) => item.id === current.id) ?? null : null;
        const translations =
          current.id && selectedPoiDetail?.poi.id === current.id
            ? selectedPoiDetail.translations
            : state.translations.filter(
                (item) => isPoiEntityType(item.entityType) && item.entityId === current.id,
              );
        const translationFields = buildPoiTranslationFields(
          currentPoi,
          translations,
          languageCode,
        );
        const audioGuides =
          current.id && selectedPoiDetail?.poi.id === current.id
            ? selectedPoiDetail.audioGuides
            : state.audioGuides.filter(
                (item) => isPoiEntityType(item.entityType) && item.entityId === current.id,
              );
        const audioGuide = currentPoi
          ? findPoiAudioGuide(audioGuides, currentPoi.id, languageCode)
          : null;

        return {
          ...current,
          contentLanguageCode: languageCode,
          title: translationFields.title,
          fullText: translationFields.fullText,
          audioUrl: audioGuide?.audioUrl ?? "",
          audioSourceType: audioGuide?.sourceType ?? "generated",
          audioStatus: audioGuide?.status ?? "missing",
        };
      });
    },
    [form.id, form.poiId, selectedPoiDetail, state],
  );

  const triggerAddressSearch = useCallback(() => {
    setAddressSearchVersion((current) => current + 1);
  }, []);

  const handleAddressKeyDown = useCallback(
    (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key !== "Enter") {
        return;
      }

      event.preventDefault();
      triggerAddressSearch();
    },
    [triggerAddressSearch],
  );

  useEffect(() => {
    if (!selectedPoiId) {
      poiDetailAbortRef.current?.abort();
      narrationAbortRef.current?.abort();
      setSelectedPoiDetail(null);
      setSelectedNarration(null);
      setFetchingPoiDetail(false);
      setResolvingNarration(false);
      stopCurrentAudio();
    }
  }, [selectedPoiId, stopCurrentAudio]);

  useEffect(() => {
    poiDetailCacheRef.current.clear();
    setSelectedNarration(null);

    if (!selectedPoiId) {
      setSelectedPoiDetail(null);
      return;
    }

    setSelectedPoiDetail(null);
    void fetchPOIDetail(selectedPoiId, {
      useCache: false,
      updateSelected: true,
    }).catch(() => undefined);
  }, [fetchPOIDetail, selectedPoiId, syncVersion]);

  useEffect(() => {
    if (!selectedPoiId) {
      return;
    }

    void fetchPOIDetail(selectedPoiId, {
      useCache: false,
      updateSelected: true,
    }).catch(() => undefined);
  }, [fetchPOIDetail, selectedPoiId]);

  useEffect(() => {
    narrationAbortRef.current?.abort();

    if (!selectedPoi) {
      setSelectedNarration(null);
      setResolvingNarration(false);
      return;
    }

    const controller = new AbortController();
    narrationAbortRef.current = controller;
    const requestId = selectedNarrationRequestRef.current + 1;
    selectedNarrationRequestRef.current = requestId;
    setResolvingNarration(true);

    void resolvePoiNarration(
      selectedPoi,
      selectedNarrationLanguage,
      controller.signal,
    )
      .then((resolved) => {
        if (selectedNarrationRequestRef.current !== requestId) {
          return;
        }

        setSelectedNarration(resolved);
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === "AbortError") {
          return;
        }

        if (selectedNarrationRequestRef.current === requestId) {
          setSelectedNarration(null);
        }
      })
      .finally(() => {
        if (narrationAbortRef.current === controller) {
          narrationAbortRef.current = null;
        }

        if (selectedNarrationRequestRef.current === requestId) {
          setResolvingNarration(false);
        }
      });

    return () => {
      controller.abort();
    };
  }, [
    resolvePoiNarration,
    selectedNarrationLanguage,
    selectedPoi,
    syncVersion,
  ]);

  useEffect(() => {
    const previousSelection = lastNarrationSelectionRef.current;
    lastNarrationSelectionRef.current = {
      poiId: selectedPoiId,
      language: selectedNarrationLanguage,
    };

    if (!hasNarrationInteraction || !selectedPoiId) {
      return;
    }

    const shouldReplayCurrentPoi =
      previousSelection.poiId === selectedPoiId &&
      previousSelection.language !== selectedNarrationLanguage;

    if (!shouldReplayCurrentPoi) {
      return;
    }

    setPlaybackIntent((current) => ({
      token: current.token + 1,
      poiId: selectedPoiId,
      language: selectedNarrationLanguage,
    }));
  }, [hasNarrationInteraction, selectedNarrationLanguage, selectedPoiId]);

  useEffect(() => {
    if (!hasNarrationInteraction || !playbackIntent.poiId || playbackIntent.token === 0) {
      return;
    }

    if (lastHandledPlaybackIntentTokenRef.current === playbackIntent.token) {
      return;
    }

    const fallbackPoi = state.pois.find((item) => item.id === playbackIntent.poiId);
    if (!fallbackPoi) {
      return;
    }

    // Each intent token should trigger playback once, even if callback identities change later.
    lastHandledPlaybackIntentTokenRef.current = playbackIntent.token;

    void playPoiNarration({
      poi: fallbackPoi,
      language: playbackIntent.language,
    });
  }, [hasNarrationInteraction, playbackIntent, playPoiNarration, state.pois]);

  useEffect(() => {
    const poiIdsToPrefetch = visiblePoiIds.length
      ? visiblePoiIds
      : mapPois.slice(0, 8).map((poi) => poi.id);

    poiIdsToPrefetch.forEach((poiId) => {
      void prefetchPoiNarration(poiId);
    });
  }, [mapPois, prefetchPoiNarration, visiblePoiIds]);

  const handleReplaySelectedNarration = useCallback(() => {
    if (!selectedPoi) {
      return;
    }

    primePlayback();
    setHasNarrationInteraction(true);

    if (selectedNarration) {
      void playResolvedNarration(selectedNarration);
      return;
    }

    void playPoiNarration({
      poi: selectedPoi,
      language: selectedNarrationLanguage,
    });
  }, [
    playPoiNarration,
    playResolvedNarration,
    primePlayback,
    selectedNarration,
    selectedNarrationLanguage,
    selectedPoi,
  ]);

  const handleAudioFileChange = async (event: ChangeEvent<HTMLInputElement>) => {
    const nextFile = event.target.files?.[0];
    event.target.value = "";

    if (!nextFile) {
      return;
    }

    setUploadingAudio(true);
    setFormError("");

    try {
      const uploaded = await adminApi.uploadFile(nextFile, "audio/guides");
      setForm((current) => ({
        ...current,
        audioUrl: uploaded.url,
        audioSourceType: "uploaded",
        audioStatus: current.audioStatus === "missing" ? "ready" : current.audioStatus,
      }));
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setUploadingAudio(false);
    }
  };

  const refreshPoiAudioStatus = useCallback(
    async (poiId: string) => {
      const latestDetail = await adminApi.getPoiDetail(poiId);
      poiDetailCacheRef.current.set(poiId, latestDetail);

      if (selectedPoiDetail?.poi.id === poiId || selectedPoiId === poiId) {
        setSelectedPoiDetail(latestDetail);
      }

      setForm((current) => {
        const activePoiId = current.id || current.poiId.trim();
        if (activePoiId !== poiId) {
          return current;
        }

        const refreshedAudioGuide = findPoiAudioGuide(
          latestDetail.audioGuides,
          poiId,
          current.contentLanguageCode,
        );

        return {
          ...current,
          audioUrl: refreshedAudioGuide?.audioUrl ?? current.audioUrl,
          audioSourceType: refreshedAudioGuide?.sourceType ?? current.audioSourceType,
          audioStatus: refreshedAudioGuide?.status ?? "missing",
        };
      });
    },
    [selectedPoiDetail, selectedPoiId],
  );

  const runPoiAudioAction = useCallback(
    async (mode: "generate" | "regenerate" | "generateAll") => {
      if (!currentFormPoiId) {
        setFormError("Hãy lưu POI trước khi generate audio.");
        return;
      }

      setGeneratingAudio(true);
      setAudioActionMessage("");
      setFormError("");

      try {
        if (mode === "generate") {
          const result = await adminApi.generatePoiAudio(currentFormPoiId, {
            languageCode: form.contentLanguageCode,
          });
          setAudioActionMessage(describeAudioGenerationResult(result));
        } else if (mode === "regenerate") {
          const result = await adminApi.regeneratePoiAudio(currentFormPoiId, {
            languageCode: form.contentLanguageCode,
            forceRegenerate: true,
          });
          setAudioActionMessage(describeAudioGenerationResult(result));
        } else {
          const results = await adminApi.generatePoiAllLanguagesAudio(currentFormPoiId, {
            forceRegenerate: false,
          });
          const succeeded = results.filter((item) => item.success).length;
          setAudioActionMessage(`Đã xử lý generate audio cho ${results.length} ngôn ngữ, thành công ${succeeded}.`);
        }

        await refreshData();
        await refreshPoiAudioStatus(currentFormPoiId);
      } catch (error) {
        setFormError(getErrorMessage(error));
      } finally {
        setGeneratingAudio(false);
      }
    },
    [currentFormPoiId, form.contentLanguageCode, refreshData, refreshPoiAudioStatus],
  );

  const updatePoiFoodItemForm = useCallback((
    clientId: string,
    updates: Partial<Omit<PoiFoodItemFormState, "clientId">>,
  ) => {
    setPoiFoodItemForms((current) =>
      current.map((item) => (item.clientId === clientId ? { ...item, ...updates } : item)),
    );
  }, []);

  const addPoiFoodItemForm = useCallback(() => {
    setPoiFoodItemForms((current) => [
      ...current,
      createPoiFoodItemDraft(form.id ?? form.poiId.trim()),
    ]);
  }, [createPoiFoodItemDraft, form.id, form.poiId]);

  const removePoiFoodItemForm = useCallback((clientId: string) => {
    setPoiFoodItemForms((current) =>
      current.filter((item) => item.clientId !== clientId),
    );
  }, []);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      const normalizedCategoryId = form.categoryId.trim();
      if (!normalizedCategoryId) {
        setFormError("Hãy chọn phân loại POI trước khi lưu.");
        setSaving(false);
        return;
      }

      const lat = parseRequiredCoordinate(form.lat);
      const lng = parseRequiredCoordinate(form.lng);
      const triggerRadius = Number.parseInt(form.triggerRadius.trim(), 10);
      const priority = Number.parseInt(form.priority.trim(), 10);
      if (lat === null || lng === null) {
        setFormError("Hãy nhập tọa độ Lat/Lng hợp lệ trước khi lưu POI.");
        setSaving(false);
        return;
      }
      if (!Number.isFinite(triggerRadius) || triggerRadius < 20) {
        setFormError("Bán kính kích hoạt POI phải từ 20m trở lên.");
        setSaving(false);
        return;
      }
      if (!Number.isFinite(priority) || priority < 0) {
        setFormError("Độ ưu tiên POI phải là số nguyên từ 0 trở lên.");
        setSaving(false);
        return;
      }

      const normalizedFoodItemDrafts = poiFoodItemForms
        .map((item) => ({
          ...item,
          name: item.name.trim(),
          description: item.description.trim(),
          priceRange: item.priceRange.trim(),
          imageUrl: item.imageUrl.trim(),
        }))
        .filter((item) => item.id || hasFoodItemContent(item));
      const invalidFoodItem = normalizedFoodItemDrafts.find((item) => !item.name);
      if (invalidFoodItem) {
        setFormError("Hãy nhập tên món ăn cho tất cả món bạn muốn lưu trong POI.");
        setSaving(false);
        return;
      }

      console.debug("[admin-poi] edit-form-before-submit", {
        poiId: form.id ?? null,
        requestedPoiId: form.poiId.trim() || null,
        title: form.title,
        contentLanguageCode: form.contentLanguageCode,
        address: form.address,
        tags: form.tags,
        fullTextLength: form.fullText.length,
      });
      const nextSlug = form.slug || slugify(form.title);
      const existingPoiImage = findPoiRepresentativeImage(state.mediaAssets, form.id);
      const existingDetail =
        selectedPoiDetail?.poi.id === form.id
          ? selectedPoiDetail
          : form.id
            ? poiDetailCacheRef.current.get(form.id)
            : null;
      const originalPoi =
        existingDetail?.poi ??
        (form.id ? state.pois.find((item) => item.id === form.id) ?? null : null);
      if (isPoiModalViewOnly) {
        setFormError("Super Admin chỉ có thể xem chi tiết POI trong form này.");
        return;
      }

      const isOwnerEditingApprovedLifecyclePoi = Boolean(
        isOwner && originalPoi && isApprovedLifecyclePoi(originalPoi),
      );
      const currentContentSnapshot = buildPoiEditorContentSnapshot(
        {
          ...form,
          slug: nextSlug,
        },
        poiImageForm,
        normalizedFoodItemDrafts,
      );
      const hasPoiContentChanged = isOwnerEditingApprovedLifecyclePoi
        ? poiEditorInitialContentSnapshotRef.current !== currentContentSnapshot
        : true;

      if (isOwnerEditingApprovedLifecyclePoi && !hasPoiContentChanged) {
        const originalVisibleStatus = getSubmissionStatus(
          user.role,
          originalPoi!.status,
          originalPoi!.approvedAt,
          originalPoi!.isActive,
        );

        if (form.status === originalVisibleStatus) {
          setFormError("Bạn chưa thay đổi nội dung hoặc trạng thái hoạt động của POI.");
          setSaving(false);
          return;
        }

        const savedPoi = await adminApi.togglePoiActive(
          originalPoi!.id,
          form.status === "published",
        );
        closePoiModal();
        await syncPoiAfterReviewAction(savedPoi.id);
        return;
      }

      const savedPoi = await savePoi(
        {
          id: form.id,
          requestedId: form.poiId.trim() || undefined,
          slug: nextSlug,
          address: form.address,
          lat,
          lng,
          categoryId: normalizedCategoryId,
          status: isOwner ? "pending" : getSubmissionStatus(user.role, form.status),
          district: form.district,
          ward: form.ward,
          priceRange: form.priceRange,
          triggerRadius,
          priority,
          tags: form.tags.split(",").map((item) => item.trim()).filter(Boolean),
          ownerUserId: (isOwner ? user.id : form.ownerUserId) || null,
          translationLanguageCode: form.contentLanguageCode,
          title: form.title,
          fullText: form.fullText,
          seoTitle: form.title,
          seoDescription: form.fullText || form.title,
        },
        user,
      );

      const nextPoiImageUrl = poiImageForm.url.trim();
      const nextPoiImageAltText =
        existingPoiImage?.altText?.trim() || `Ảnh đại diện ${form.title || nextSlug}`;
      const shouldSavePoiImage =
        !!nextPoiImageUrl &&
        (
          !existingPoiImage ||
          existingPoiImage.url !== nextPoiImageUrl ||
          existingPoiImage.altText !== nextPoiImageAltText ||
          existingPoiImage.entityId !== savedPoi.id
        );

      if (shouldSavePoiImage) {
        await saveMediaAsset(
          {
            id: poiImageForm.id ?? existingPoiImage?.id,
            entityType: "poi",
            entityId: savedPoi.id,
            type: "image",
            url: nextPoiImageUrl,
            altText: nextPoiImageAltText,
          },
          user,
        );
      }

      const originalPoiId = form.id ?? savedPoi.id;
      const existingFoodItemsById = new Map(
        state.foodItems
          .filter((item) => item.poiId === originalPoiId)
          .map((item) => [item.id, item]),
      );
      const foodItemTranslations =
        existingDetail?.foodItemTranslations?.length
          ? existingDetail.foodItemTranslations
          : state.translations.filter((item) => item.entityType === "food_item");
      const shouldWriteFoodItemBaseText = form.contentLanguageCode === state.settings.defaultLanguage;

      for (const foodItemDraft of normalizedFoodItemDrafts) {
        const existingFoodItem = foodItemDraft.id
          ? existingFoodItemsById.get(foodItemDraft.id)
          : null;
        const baseFoodItemName =
          shouldWriteFoodItemBaseText || !existingFoodItem
            ? foodItemDraft.name
            : existingFoodItem.name;
        const baseFoodItemDescription =
          shouldWriteFoodItemBaseText || !existingFoodItem
            ? foodItemDraft.description
            : existingFoodItem.description;
        const hasFoodItemChanged =
          !existingFoodItem ||
          existingFoodItem.poiId !== savedPoi.id ||
          existingFoodItem.name !== baseFoodItemName ||
          existingFoodItem.description !== baseFoodItemDescription ||
          existingFoodItem.priceRange !== foodItemDraft.priceRange ||
          existingFoodItem.imageUrl !== foodItemDraft.imageUrl ||
          existingFoodItem.spicyLevel !== foodItemDraft.spicyLevel;

        const savedFoodItem = hasFoodItemChanged
          ? await saveFoodItem(
              {
                id: foodItemDraft.id,
                poiId: savedPoi.id,
                name: baseFoodItemName,
                description: baseFoodItemDescription,
                priceRange: foodItemDraft.priceRange,
                imageUrl: foodItemDraft.imageUrl,
                spicyLevel: foodItemDraft.spicyLevel,
              },
              user,
            )
          : existingFoodItem;

        if (!savedFoodItem) {
          continue;
        }

        const existingFoodItemTranslation = foodItemTranslations.find(
          (item) =>
            item.entityType === "food_item" &&
            item.entityId === savedFoodItem.id &&
            item.languageCode === form.contentLanguageCode,
        );
        await saveTranslation(
          {
            id: existingFoodItemTranslation?.id,
            entityType: "food_item",
            entityId: savedFoodItem.id,
            languageCode: form.contentLanguageCode,
            title: foodItemDraft.name,
            shortText: foodItemDraft.description,
            fullText: foodItemDraft.description,
            seoTitle: foodItemDraft.name,
            seoDescription: foodItemDraft.description || foodItemDraft.name,
          },
          user,
        );
      }

      let optimisticAudioGuide: AudioGuide | null = null;
      if (form.audioSourceType === "uploaded" && form.audioUrl) {
        await saveAudioGuide(
          {
            id: findPoiAudioGuide(state.audioGuides, form.id, form.contentLanguageCode)?.id,
            entityType: "poi",
            entityId: savedPoi.id,
            languageCode: form.contentLanguageCode,
            audioUrl: form.audioUrl,
            sourceType: "uploaded",
            status: "ready",
            transcriptText: form.fullText,
            audioFilePath: "",
            audioFileName: form.audioUrl.split("/").pop()?.split("?")[0] ?? "",
            provider: "uploaded",
            voiceId: "",
            modelId: "",
            outputFormat: "mp3_44100_128",
            durationInSeconds: null,
            fileSizeBytes: null,
            textHash: "",
            contentVersion: "",
            generatedAt: new Date().toISOString(),
            generationStatus: "success",
            errorMessage: null,
            isOutdated: false,
            voiceType: "standard",
          },
          user,
        );

        const existingAudioGuide =
          findPoiAudioGuide(
            existingDetail?.audioGuides ?? [],
            savedPoi.id,
            form.contentLanguageCode,
          ) ??
          findPoiAudioGuide(
            state.audioGuides,
            savedPoi.id,
            form.contentLanguageCode,
          );

        optimisticAudioGuide = {
          id: existingAudioGuide?.id ?? `audio-${savedPoi.id}-${form.contentLanguageCode}`,
          entityType: "poi",
          entityId: savedPoi.id,
          languageCode: form.contentLanguageCode,
          transcriptText: form.fullText,
          audioUrl: form.audioUrl,
          audioFilePath: "",
          audioFileName: form.audioUrl.split("/").pop()?.split("?")[0] ?? "",
          voiceType: "standard",
          sourceType: "uploaded",
          provider: "uploaded",
          voiceId: "",
          modelId: "",
          outputFormat: "mp3_44100_128",
          durationInSeconds: null,
          fileSizeBytes: null,
          textHash: "",
          contentVersion: "",
          generatedAt: new Date().toISOString(),
          generationStatus: "success",
          errorMessage: null,
          isOutdated: false,
          status: "ready",
          updatedBy: user.name,
          updatedAt: savedPoi.updatedAt,
        };
      }

      const existingTranslation =
        findPoiTranslationForLanguage(
          existingDetail?.translations ?? [],
          savedPoi,
          form.contentLanguageCode,
        ) ??
        findPoiTranslationForLanguage(
          state.translations.filter(
            (item) => isPoiEntityType(item.entityType) && item.entityId === savedPoi.id,
          ),
          savedPoi,
          form.contentLanguageCode,
        );
      const optimisticTranslation: Translation = {
        id: existingTranslation?.id ?? `trans-${savedPoi.id}-${form.contentLanguageCode}`,
        entityType: "poi",
        entityId: savedPoi.id,
        languageCode: form.contentLanguageCode,
        title: form.title,
        shortText: "",
        fullText: form.fullText,
        seoTitle: form.title,
        seoDescription: form.fullText || form.title,
        updatedBy: user.name,
        updatedAt: savedPoi.updatedAt,
      };
      const nextDetail: PoiDetail = {
        poi: savedPoi,
        translations: [
          optimisticTranslation,
          ...(existingDetail?.translations ?? []).filter(
            (item) => item.id !== optimisticTranslation.id,
          ),
        ],
        audioGuides: optimisticAudioGuide
          ? [
              optimisticAudioGuide,
              ...(existingDetail?.audioGuides ?? []).filter(
                (item) => item.id !== optimisticAudioGuide?.id,
              ),
            ]
          : existingDetail?.audioGuides ?? [],
        foodItems: existingDetail?.foodItems ?? [],
        foodItemTranslations: existingDetail?.foodItemTranslations ?? [],
        promotions: existingDetail?.promotions ?? [],
        promotionTranslations: existingDetail?.promotionTranslations ?? [],
        mediaAssets: existingDetail?.mediaAssets ?? [],
      };

      if (form.id && form.id !== savedPoi.id) {
        poiDetailCacheRef.current.delete(form.id);
      }

      poiDetailCacheRef.current.set(savedPoi.id, nextDetail);
      setSelectedPoiDetail(nextDetail);
      setSelectedNarration(null);
      setSelectedPoiId(savedPoi.id);
      setSelectedNarrationLanguage(form.contentLanguageCode);
      void fetchPOIDetail(savedPoi.id, {
        useCache: false,
        updateSelected: true,
      }).catch(() => undefined);
      closePoiModal();
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const editingPoi = useMemo(
    () =>
      form.id
        ? selectedPoiDetail?.poi.id === form.id
          ? selectedPoiDetail.poi
          : state.pois.find((item) => item.id === form.id) ?? null
        : null,
    [form.id, selectedPoiDetail, state.pois],
  );
  const isPoiModalViewOnly = poiFormLoadState.mode === "view";
  const editingApprovedLifecyclePoi = Boolean(editingPoi && isApprovedLifecyclePoi(editingPoi));
  const ownerCanToggleEditingPoi = canToggleApprovedPoi(editingPoi, user?.role);
  const poiStatusFieldDisabled = isPoiModalViewOnly
    ? true
    : isOwner
      ? !editingApprovedLifecyclePoi || !ownerCanToggleEditingPoi
      : false;
  const poiModalTitle = isOwner
    ? poiFormLoadState.mode === "edit"
      ? editingApprovedLifecyclePoi
        ? "Quản lý POI đã duyệt"
        : "Cập nhật POI để gửi duyệt"
      : "Tạo POI để gửi duyệt"
    : isPoiModalViewOnly
      ? "Chi tiết POI"
      : poiFormLoadState.mode === "edit"
        ? "Cập nhật POI"
        : "Tạo POI";
  const poiModalDescription = isOwner
    ? editingApprovedLifecyclePoi
      ? "Bạn có thể bật hoặc tắt hoạt động của POI đã duyệt. Nếu sửa nội dung và lưu, POI sẽ quay lại chờ duyệt."
      : "Điền thông tin POI và gửi cho super admin duyệt trước khi xuất bản."
    : isPoiModalViewOnly
      ? "Super Admin chỉ được xem chi tiết POI trong form này. Dùng các nút ngoài form để duyệt, từ chối hoặc đổi trạng thái hoạt động."
      : "Điền thông tin POI, chỉnh vị trí trên bản đồ và lưu nội dung thuyết minh theo ngôn ngữ đang sửa.";
  const poiModalEditable = !isPoiModalViewOnly;

  const handleApprovePoi = useCallback(
    async (poi: Poi) => {
      if (!user || !isSuperAdmin) {
        return;
      }

      setSaving(true);
      setFormError("");

      try {
        const savedPoi = await adminApi.approvePoi(poi.id);
        await syncPoiAfterReviewAction(savedPoi.id);
      } catch (error) {
        setFormError(getErrorMessage(error));
      } finally {
        setSaving(false);
      }
    },
    [isSuperAdmin, syncPoiAfterReviewAction, user],
  );

  const handleRejectPoi = useCallback(async () => {
    if (!user || !isSuperAdmin || !poiBeingRejected) {
      return;
    }

    const normalizedReason = rejectReason.trim();
    if (!normalizedReason) {
      setRejectError("Hãy nhập lý do từ chối trước khi xác nhận.");
      return;
    }

    setRejectError("");
    setSaving(true);

    try {
      const savedPoi = await adminApi.rejectPoi(poiBeingRejected.id, normalizedReason);
      closeRejectModal();
      await syncPoiAfterReviewAction(savedPoi.id);
    } catch (error) {
      setRejectError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  }, [isSuperAdmin, poiBeingRejected, rejectReason, syncPoiAfterReviewAction, user]);

  const handleTogglePoiActive = useCallback(async () => {
    if (!user || !activeToggleTarget || !poiBeingToggled || !canToggleApprovedPoi(poiBeingToggled, user.role)) {
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      const savedPoi = await adminApi.togglePoiActive(
        poiBeingToggled.id,
        activeToggleTarget.nextIsActive,
      );
      closeActiveToggleModal();
      await syncPoiAfterReviewAction(savedPoi.id);
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  }, [activeToggleTarget, poiBeingToggled, syncPoiAfterReviewAction, user]);

  const columns: DataColumn<Poi>[] = [
    {
      key: "poi",
      header: "POI",
      widthClassName: "min-w-[260px]",
      render: (poi) => {
        const translation = getDisplayedPoiTranslation(poi, selectedNarrationLanguage);
        const statusBadge = getPoiStatusBadge(poi);
        return (
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-semibold text-ink-900">{translation?.title ?? poi.slug}</p>
              <StatusBadge status={statusBadge.status} label={statusBadge.label} />
            </div>
            <p className="mt-2 text-sm text-ink-500">{translation?.shortText || poi.address}</p>
            {poi.rejectionReason ? (
              <p className="mt-2 text-xs text-rose-600">Lý do từ chối: {poi.rejectionReason}</p>
            ) : null}
            {poi.lockedBySuperAdmin ? (
              <p className="mt-2 text-xs text-amber-700">
                Super Admin đã ngừng hoạt động POI này. Chủ quán không thể tự bật lại hoặc chỉnh sửa.
              </p>
            ) : null}
          </div>
        );
      },
    },
    {
      key: "map",
      header: "Bản đồ",
      widthClassName: "min-w-[180px]",
      render: (poi) => (
        <div>
          <p className="font-medium text-ink-800">{getCategoryName(state, poi.categoryId)}</p>
          <p className="mt-1 text-xs text-ink-500">{poi.lat.toFixed(5)}, {poi.lng.toFixed(5)}</p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[420px]",
      render: (poi) => {
        const canTogglePoi = canToggleApprovedPoi(poi, user?.role);
        const canEditPoi = isOwner && !poi.lockedBySuperAdmin;

        return (
          <div className="flex items-center gap-2 whitespace-nowrap">
            <Button
              variant="ghost"
              className="shrink-0 whitespace-nowrap px-3 py-2 text-xs"
              onClick={() => {
                handlePoiSelect(poi.id);
                if (isSuperAdmin) {
                  void openEditModal(poi);
                }
              }}
            >
              Xem
            </Button>
            {isSuperAdmin && poi.status === "pending" ? (
              <Button
                className="shrink-0 whitespace-nowrap rounded-xl px-3.5 py-2 text-xs"
                onClick={() => {
                  void handleApprovePoi(poi);
                }}
                disabled={isSaving}
              >
                Duyệt
              </Button>
            ) : null}
            {isSuperAdmin && poi.status === "pending" ? (
              <Button
                variant="danger"
                className="shrink-0 whitespace-nowrap rounded-xl px-3.5 py-2 text-xs"
                onClick={() => openRejectModal(poi)}
                disabled={isSaving}
              >
                Từ chối
              </Button>
            ) : null}
            {canTogglePoi ? (
              <Button
                variant={poi.isActive ? "secondary" : "danger"}
                className="shrink-0 whitespace-nowrap rounded-xl px-3.5 py-2 text-xs"
                onClick={() => openActiveToggleModal(poi, !poi.isActive)}
                disabled={isSaving}
              >
                {poi.isActive ? "Ngừng hoạt động" : "Bật hoạt động"}
              </Button>
            ) : null}
            {canEditPoi ? (
              <Button
                variant="secondary"
                className="shrink-0 whitespace-nowrap px-3 py-2 text-xs"
                onClick={() => {
                  void openEditModal(poi);
                }}
              >
                Sửa
              </Button>
            ) : null}
          </div>
        );
      },
    },
  ];
  const isPoiFormLoading = isModalOpen && !poiFormLoadState.dataLoaded;
  const selectedPoiStatusBadge = selectedPoi ? getPoiStatusBadge(selectedPoi) : null;

  return (
    <div className="space-y-6">
      {pageAlert ? (
        <div className="rounded-2xl border border-rose-100 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {pageAlert}
        </div>
      ) : null}

      <Card>
        <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
          <div>
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">POI</p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý POI và bản đồ tương tác</h1>
            <p className="mt-3 max-w-3xl text-sm leading-6 text-ink-600">
              Khi chạm bất kỳ điểm nào gần marker trên bản đồ, hệ thống sẽ tự chọn POI gần nhất, hiện thông tin chi tiết và tự động phát thuyết minh của POI đó.
            </p>
          </div>
          {isOwner ? (
            <Button onClick={openCreateModal} disabled={isBootstrapping}>
              {isBootstrapping ? "Đang tải dữ liệu..." : "Tạo POI"}
            </Button>
          ) : (
            <Button
              disabled={!pendingPois.length}
              onClick={() => {
                setStatusFilter("pending");
                if (pendingPois[0]) {
                  handlePoiSelect(pendingPois[0].id);
                }
              }}
            >
              {pendingPois.length ? `Duyệt ${pendingPois.length} POI` : "Không có POI chờ duyệt"}
            </Button>
          )}
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-4">
        {[
          ["Tổng POI", formatNumber(state.pois.length)],
          ["Chờ duyệt", formatNumber(pendingPois.length)],
          ["Đã duyệt", formatNumber(approvedLifecyclePois.length)],
          ["Bị từ chối", formatNumber(rejectedPois.length)],
        ].map(([label, value]) => (
          <Card key={label}>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-ink-500">{label}</p>
            <p className="mt-3 text-3xl font-bold text-ink-900">{value}</p>
          </Card>
        ))}
      </section>

      <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_380px]">
        <Card className="space-y-5">
          <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px]">
            <Input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="Tìm POI..." />
            <Select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as Poi["status"] | "all")}>
              <option value="all">Tất cả trạng thái</option>
              <option value="draft">Draft</option>
              <option value="pending">Chờ duyệt</option>
              <option value="published">Đã duyệt</option>
              <option value="rejected">Từ chối</option>
              <option value="archived">Lưu trữ</option>
            </Select>
          </div>

          <OpenStreetMapPicker
            editable={false}
            isVisible
            pois={mapPois}
            selectedPoiId={selectedPoiId}
            lat={selectedPoi?.lat ?? DEFAULT_LAT}
            lng={selectedPoi?.lng ?? DEFAULT_LNG}
            onPoiSelect={handlePoiSelect}
            onPoiHover={(poiId) => {
              void prefetchPoiNarration(poiId);
            }}
            onVisiblePoiIdsChange={handleVisiblePoiIdsChange}
          />
        </Card>

        <Card className="space-y-4">
          <h2 className="section-heading">Thông tin POI đang chọn</h2>
          {selectedPoi ? (
            <>
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-xl font-semibold text-ink-900">
                  {selectedDisplayTitle}
                </p>
                {selectedPoiStatusBadge ? (
                  <StatusBadge status={selectedPoiStatusBadge.status} label={selectedPoiStatusBadge.label} />
                ) : null}
              </div>
              <p className="text-sm text-ink-600">{selectedPoi.address}</p>
              {selectedPoi.lockedBySuperAdmin ? (
                <div className="rounded-3xl border border-amber-100 bg-amber-50 px-5 py-4">
                  <p className="text-sm font-semibold tracking-[0.12em] text-amber-700">POI đang bị khóa hoạt động</p>
                  <p className="mt-2 text-sm leading-6 text-amber-800">
                    Super Admin đã đưa POI này về trạng thái ngừng hoạt động. Chủ quán không thể tự bật lại hoặc chỉnh sửa nội dung POI này.
                  </p>
                </div>
              ) : null}
              <div className="grid gap-3 sm:grid-cols-2">
                {[
                  ["Slug", selectedPoi.slug],
                  ["Phân loại", getCategoryName(state, selectedPoi.categoryId)],
                  ["Khoảng giá", selectedPoi.priceRange || "Chưa cập nhật"],
                  ["Bán kính kích hoạt", `${selectedPoi.triggerRadius}m`],
                  ["Chủ quản lý", getOwnerName(state, selectedPoi.ownerUserId)],
                  ["Ngôn ngữ", languageLabels[selectedNarrationLanguage]],
                  ["Khu vực", `${selectedPoi.ward}, ${selectedPoi.district}`],
                  ["Hoạt động", isApprovedLifecyclePoi(selectedPoi) ? (selectedPoi.isActive ? poiActivityLabels.active : poiActivityLabels.inactive) : "Chưa áp dụng"],
                  ["Cập nhật", formatDateTime(selectedPoi.updatedAt)],
                ].map(([label, value]) => (
                  <div key={label} className="rounded-2xl border border-sand-100 bg-sand-50 px-4 py-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-ink-500">{label}</p>
                    <p className="mt-2 text-sm font-medium text-ink-800">{value}</p>
                  </div>
                ))}
              </div>
              <div>
                <label className="field-label !mb-0 flex min-h-[3.25rem] items-end leading-6">
                  Ngôn ngữ thuyết minh
                </label>
                <Select
                  className="mt-2"
                  value={selectedNarrationLanguage}
                  onChange={(event) =>
                    setSelectedNarrationLanguage(event.target.value as LanguageCode)
                  }
                >
                  {availableNarrationLanguages.map((languageCode) => (
                    <option key={languageCode} value={languageCode}>
                      {languageLabels[languageCode]}
                    </option>
                  ))}
                </Select>
              </div>
              {selectedPoi.rejectionReason ? (
                <div className="rounded-3xl border border-rose-100 bg-rose-50 px-5 py-4">
                  <p className="text-sm font-semibold tracking-[0.12em] text-rose-600">Lý do từ chối</p>
                  <p className="mt-2 text-sm leading-6 text-rose-700">{selectedPoi.rejectionReason}</p>
                  <p className="mt-2 text-xs text-rose-600">
                    Từ chối lúc {formatDateTime(selectedPoi.rejectedAt)}
                  </p>
                  {isOwner ? (
                    <p className="mt-2 text-xs text-rose-600">
                      Bạn có thể sửa POI này rồi gửi duyệt lại để admin xem xét lần nữa.
                    </p>
                  ) : null}
                </div>
              ) : null}
              <div className="rounded-3xl border border-sand-100 bg-sand-50 p-4">
                <p className="text-sm font-semibold text-ink-900">Mô tả POI</p>
                <p className="mt-3 text-sm leading-6 text-ink-600">
                  {selectedDisplayText || "Chưa có bài thuyết minh cho POI này."}
                </p>
                <p className="mt-3 text-xs text-ink-500">
                  Audio: {selectedAudio ? `${selectedAudio.sourceType} / ${selectedAudio.generationStatus} / ${selectedAudio.status}` : "Chưa có"}
                </p>
                {selectedNarration?.fallbackMessage ? (
                  <p className="mt-2 text-xs text-amber-700">{selectedNarration.fallbackMessage}</p>
                ) : null}
                <p className="mt-2 text-xs text-ink-500">
                  {isResolvingNarration
                    ? "Đang đồng bộ nội dung và audio theo ngôn ngữ đã chọn..."
                    : `Transcript để generate audio: ${selectedNarration?.ttsInputText ? "sẵn sàng" : "chưa có"}`}
                </p>
                <p className="mt-2 text-xs text-ink-500">
                  Ngôn ngữ phát: {selectedNarration?.ttsLocale ?? languageLocales[selectedNarrationLanguage]}
                </p>
                <p
                  className={`mt-3 text-xs ${
                    playbackState.status === "error"
                      ? "text-rose-700"
                      : playbackState.status === "playing"
                        ? "text-emerald-700"
                        : playbackState.status === "paused"
                          ? "text-amber-700"
                          : "text-ink-500"
                  }`}
                >
                  {playbackState.isLoadingPOI
                    ? "Đang tải bài thuyết minh..."
                    : playbackState.message || "Chọn một POI trên bản đồ để tự động phát bài thuyết minh."}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                {selectedPoi.tags.length ? (
                  selectedPoi.tags.map((tag) => (
                    <span key={tag} className="rounded-full bg-sand-50 px-3 py-1 text-xs font-medium text-ink-700 ring-1 ring-sand-200">
                      {tag}
                    </span>
                  ))
                ) : (
                  <p className="text-sm text-ink-500">POI này chưa có thẻ tag.</p>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Button onClick={handleReplaySelectedNarration}>
                  {playbackState.isLoadingPOI
                    ? "Đang tải..."
                    : isSelectedPoiNarrationPlaying
                      ? "Tạm dừng"
                      : isSelectedPoiNarrationPaused
                        ? "Tiếp tục"
                        : "Phát lại"}
                </Button>
                <Button variant="ghost" onClick={() => stopCurrentAudio("Đã dừng thuyết minh.")}>
                  Dừng
                </Button>
                {isSuperAdmin ? (
                  <Button
                    variant="ghost"
                    onClick={() => {
                      void openEditModal(selectedPoi);
                    }}
                  >
                    Xem chi tiết
                  </Button>
                ) : null}
                {isSuperAdmin && selectedPoi.status === "pending" ? (
                  <Button
                    onClick={() => {
                      void handleApprovePoi(selectedPoi);
                    }}
                    disabled={isSaving}
                  >
                    Duyệt POI này
                  </Button>
                ) : null}
                {isSuperAdmin && selectedPoi.status === "pending" ? (
                  <Button variant="danger" onClick={() => openRejectModal(selectedPoi)} disabled={isSaving}>
                    Từ chối POI này
                  </Button>
                ) : null}
                {canToggleApprovedPoi(selectedPoi, user?.role) ? (
                  <Button
                    variant={selectedPoi.isActive ? "secondary" : "danger"}
                    onClick={() => openActiveToggleModal(selectedPoi, !selectedPoi.isActive)}
                    disabled={isSaving}
                  >
                    {selectedPoi.isActive ? "Chuyển sang ngừng hoạt động" : "Bật hoạt động POI này"}
                  </Button>
                ) : null}
                {isOwner && !selectedPoi.lockedBySuperAdmin ? (
                  <Button
                    variant="secondary"
                    onClick={() => {
                      void openEditModal(selectedPoi);
                    }}
                  >
                    {selectedPoi.status === "rejected" ? "Sửa để gửi duyệt lại" : "Sửa POI này"}
                  </Button>
                ) : null}
              </div>
            </>
          ) : (
            <EmptyState
              title="Chưa có POI được chọn"
              description="Hãy chọn một dòng trong danh sách hoặc chạm lên bản đồ để hiện thông tin POI."
            />
          )}
        </Card>
      </section>

      <Card className="space-y-5">
        <div className="flex items-end justify-between gap-3">
          <div>
            <h2 className="section-heading">Danh sách POI</h2>
            <p className="mt-2 text-sm text-ink-500">Module này hiện chỉ quản lý dữ liệu POI.</p>
          </div>
          <p className="text-sm text-ink-500">{formatNumber(filteredPois.length)} POI</p>
        </div>
        {filteredPois.length ? (
          <DataTable data={filteredPois} columns={columns} rowKey={(row) => row.id} />
        ) : (
          <EmptyState title="Không có POI phù hợp" description="Thử đổi từ khóa hoặc bộ lọc để xem lại danh sách." />
        )}
      </Card>

      <Modal
        open={isModalOpen}
        onClose={closePoiModal}
        title={poiModalTitle}
        description={poiModalDescription}
        maxWidthClassName="max-w-5xl"
      >
        <form className="space-y-6" onSubmit={handleSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
          {isPoiFormLoading ? (
            <div className="rounded-3xl border border-sand-100 bg-sand-50 px-5 py-6 text-sm text-ink-600">
              Đang tải dữ liệu POI từ API. Form sẽ chỉ hiển thị nội dung sau khi tải đúng bản ghi cần sửa.
            </div>
          ) : null}

          <fieldset disabled={isPoiFormLoading} className={isPoiFormLoading ? "hidden" : "contents"}>
          {editingPoi?.rejectionReason ? (
            <div className="rounded-3xl border border-rose-100 bg-rose-50 px-5 py-4">
              <p className="text-sm font-semibold tracking-[0.12em] text-rose-600">POI đang bị từ chối</p>
              <p className="mt-2 text-sm leading-6 text-rose-700">{editingPoi.rejectionReason}</p>
              <p className="mt-2 text-xs text-rose-600">
                Từ chối lúc {formatDateTime(editingPoi.rejectedAt)}
              </p>
              {isOwner ? (
                <p className="mt-2 text-xs text-rose-600">
                  Sau khi bạn lưu lại, POI này sẽ quay về trạng thái chờ duyệt.
                </p>
              ) : null}
            </div>
          ) : null}
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Tên POI</label>
                  <Input
                    value={form.title}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        title: event.target.value,
                        slug: hasSlugBeenManuallyEdited ? current.slug : slugify(event.target.value),
                      }))
                    }
                    required
                  />
                </div>
                <div>
                  <label className="field-label">ID POI</label>
                  <Input
                    value={form.poiId}
                    disabled={isPoiModalViewOnly || Boolean(form.id)}
                    onChange={(event) => setForm((current) => ({ ...current, poiId: event.target.value }))}
                    placeholder="Ví dụ: poi-oc-phat"
                  />
                </div>
                <div>
                  <label className="field-label">Slug</label>
                  <Input
                    value={form.slug}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => {
                      setHasSlugBeenManuallyEdited(true);
                      setForm((current) => ({ ...current, slug: event.target.value }));
                    }}
                    placeholder="Ví dụ: nha-hang-oc-loan"
                  />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Phân loại</label>
                  <Select
                    value={form.categoryId}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, categoryId: event.target.value }))}
                  >
                    <option value="">Chọn phân loại</option>
                    {state.categories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Trạng thái</label>
                  <Select
                    value={form.status}
                    disabled={poiStatusFieldDisabled}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, status: event.target.value as Poi["status"] }))
                    }
                  >
                    {(
                      editingApprovedLifecyclePoi
                        ? [
                            { value: "published", label: poiActivityLabels.active },
                            { value: "draft", label: poiActivityLabels.inactive },
                          ]
                        : isOwner
                          ? [
                              ...(form.status === "rejected"
                                ? [{ value: "rejected" as const, label: contentStatusLabels.rejected }]
                                : []),
                              { value: "pending" as const, label: contentStatusLabels.pending },
                            ]
                          : [
                              { value: "draft" as const, label: contentStatusLabels.draft },
                              { value: "pending" as const, label: contentStatusLabels.pending },
                              { value: "published" as const, label: contentStatusLabels.published },
                              { value: "rejected" as const, label: contentStatusLabels.rejected },
                              { value: "archived" as const, label: contentStatusLabels.archived },
                              { value: "deleted" as const, label: contentStatusLabels.deleted },
                            ]
                    ).map((statusOption) => (
                      <option key={statusOption.value} value={statusOption.value}>
                        {statusOption.label}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Ngôn ngữ nội dung đang sửa</label>
                  <Select
                    value={form.contentLanguageCode}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => handleContentLanguageChange(event.target.value as LanguageCode)}
                  >
                    {state.settings.supportedLanguages.map((code) => (
                      <option key={code} value={code}>
                        {languageLabels[code]}
                      </option>
                    ))}
                  </Select>
                </div>
              </div>

              <div>
                <label className="field-label">Địa chỉ</label>
                <Input
                  value={form.address}
                  disabled={isPoiModalViewOnly}
                  onChange={(event) => setForm((current) => ({ ...current, address: event.target.value }))}
                  onBlur={triggerAddressSearch}
                  onKeyDown={handleAddressKeyDown}
                  required
                />
              </div>

              <div className="grid gap-5 md:grid-cols-4">
                <div>
                  <label className="field-label">Lat</label>
                  <Input
                    value={form.lat}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, lat: event.target.value }))}
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Lng</label>
                  <Input
                    value={form.lng}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, lng: event.target.value }))}
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Quận</label>
                  <Input
                    value={form.district}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, district: event.target.value }))}
                  />
                </div>
                <div>
                  <label className="field-label">Phường</label>
                  <Input
                    value={form.ward}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, ward: event.target.value }))}
                  />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Khoảng giá</label>
                  <Input
                    value={form.priceRange}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, priceRange: event.target.value }))}
                    placeholder="Ví dụ: 50.000 - 150.000 VND"
                  />
                </div>
                <div>
                  <label className="field-label">Bán kính kích hoạt (m)</label>
                  <Input
                    type="number"
                    min={20}
                    value={form.triggerRadius}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, triggerRadius: event.target.value }))}
                    placeholder="Ví dụ: 20, 25, 35"
                  />
                </div>
                <div>
                  <label className="field-label">Độ ưu tiên</label>
                  <Input
                    type="number"
                    min={0}
                    value={form.priority}
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => setForm((current) => ({ ...current, priority: event.target.value }))}
                    placeholder="0"
                  />
                </div>
                <div>
                  <label className="field-label">Người quản lý</label>
                  <Select value={form.ownerUserId} onChange={(event) => setForm((current) => ({ ...current, ownerUserId: event.target.value }))} disabled={isOwner || isPoiModalViewOnly}>
                    <option value="">Chưa gán</option>
                    {state.users.filter((account) => account.role === "PLACE_OWNER").map((account) => (
                      <option key={account.id} value={account.id}>
                        {account.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Tags</label>
                  <Input value={form.tags} disabled={isPoiModalViewOnly} onChange={(event) => setForm((current) => ({ ...current, tags: event.target.value }))} />
                </div>
              </div>

              <div>
                <label className="field-label">Thuyết minh</label>
                <Textarea value={form.fullText} disabled={isPoiModalViewOnly} onChange={(event) => setForm((current) => ({ ...current, fullText: event.target.value }))} />
              </div>

              <div className="space-y-5 rounded-3xl border border-sand-100 bg-sand-50/70 p-5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold text-ink-900">Hình ảnh đại diện</p>
                    <p className="mt-2 text-xs text-ink-500">
                      {isPoiModalViewOnly
                        ? "Phần ảnh trong form này chỉ để xem. Super Admin không được thay đổi ảnh quán hay ảnh món ăn tại đây."
                        : "Admin và chủ quán có thể cập nhật ảnh quán và ảnh món ăn ngay trong form POI."}
                    </p>
                  </div>
                  {activeImageUploads ? (
                    <p className="text-xs text-ink-500">Đang upload ảnh...</p>
                  ) : null}
                </div>

                <ImageSourceField
                  label="Ảnh đại diện quán"
                  value={poiImageForm.url}
                  disabled={isPoiModalViewOnly}
                  onChange={(value) =>
                    setPoiImageForm((current) => ({
                      ...current,
                      url: value,
                    }))
                  }
                  onUpload={(file) => uploadImageAsset(file, "images/pois")}
                  helperText="Ảnh mới sẽ được dùng làm hình đại diện chính của quán/POI này."
                  emptyText="POI này chưa có ảnh đại diện."
                />

                <div className="space-y-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold text-ink-900">Món ăn thuộc POI</p>
                      <p className="mt-2 text-xs text-ink-500">
                        {isPoiModalViewOnly
                          ? "Danh sách món ăn chỉ để xem. Super Admin không được thêm mới hay cập nhật món ăn trong form POI."
                          : "Thêm món mới và chỉnh sửa toàn bộ nội dung món ăn ngay trong màn sửa POI."}
                      </p>
                    </div>
                    {!isPoiModalViewOnly ? (
                      <Button
                        variant="secondary"
                        className="shrink-0"
                        onClick={addPoiFoodItemForm}
                      >
                        Thêm món ăn
                      </Button>
                    ) : null}
                  </div>

                  {poiFoodItemForms.length ? (
                    <div className="grid gap-4 lg:grid-cols-2">
                      {poiFoodItemForms.map((foodItem, index) => (
                        <div
                          key={foodItem.clientId}
                          className="space-y-4 rounded-3xl border border-sand-200 bg-white p-4"
                        >
                          <div className="flex flex-wrap items-start justify-between gap-3">
                            <div>
                              <p className="text-sm font-semibold text-ink-900">
                                {foodItem.name.trim() || `Món ăn ${index + 1}`}
                              </p>
                              <p className="mt-1 text-xs text-ink-500">
                                {foodItem.id
                                  ? "Món đã có trong POI."
                                  : "Món mới sẽ được tạo khi bạn lưu POI."}
                              </p>
                            </div>
                            {!foodItem.id && !isPoiModalViewOnly ? (
                              <Button
                                variant="ghost"
                                className="px-3 py-2 text-xs"
                                onClick={() => removePoiFoodItemForm(foodItem.clientId)}
                              >
                                Bỏ món mới
                              </Button>
                            ) : null}
                          </div>

                          <div className="grid gap-4 md:grid-cols-2">
                            <div>
                              <label className="field-label">Tên món</label>
                              <Input
                                value={foodItem.name}
                                disabled={isPoiModalViewOnly}
                                onChange={(event) =>
                                  updatePoiFoodItemForm(foodItem.clientId, {
                                    name: event.target.value,
                                  })
                                }
                                placeholder="Ví dụ: Bánh xèo tôm mực"
                              />
                            </div>
                            <div>
                              <label className="field-label">Khoảng giá</label>
                              <Input
                                value={foodItem.priceRange}
                                disabled={isPoiModalViewOnly}
                                onChange={(event) =>
                                  updatePoiFoodItemForm(foodItem.clientId, {
                                    priceRange: event.target.value,
                                  })
                                }
                                placeholder="Ví dụ: 80.000 - 120.000đ"
                              />
                            </div>
                          </div>

                          <div className="grid gap-4 md:grid-cols-2">
                            <div>
                              <label className="field-label">Độ cay</label>
                              <Select
                                value={foodItem.spicyLevel}
                                disabled={isPoiModalViewOnly}
                                onChange={(event) =>
                                  updatePoiFoodItemForm(foodItem.clientId, {
                                    spicyLevel: event.target.value as FoodItem["spicyLevel"],
                                  })
                                }
                              >
                                <option value="mild">Mild</option>
                                <option value="medium">Medium</option>
                                <option value="hot">Hot</option>
                              </Select>
                            </div>
                            <div className="rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3">
                              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-ink-500">
                                Trạng thái
                              </p>
                              <p className="mt-2 text-sm text-ink-700">
                                {foodItem.id ? "Đang chỉnh sửa món hiện có" : "Món mới chưa lưu"}
                              </p>
                            </div>
                          </div>

                          <div>
                            <label className="field-label">Mô tả món ăn</label>
                            <Textarea
                              value={foodItem.description}
                              disabled={isPoiModalViewOnly}
                              onChange={(event) =>
                                updatePoiFoodItemForm(foodItem.clientId, {
                                  description: event.target.value,
                                })
                              }
                              placeholder="Mô tả ngắn về nguyên liệu, hương vị hoặc điểm nổi bật."
                            />
                          </div>

                          <ImageSourceField
                            label={`Ảnh món: ${foodItem.name.trim() || `Món ăn ${index + 1}`}`}
                            value={foodItem.imageUrl}
                            disabled={isPoiModalViewOnly}
                            onChange={(value) =>
                              updatePoiFoodItemForm(foodItem.clientId, { imageUrl: value })
                            }
                            onUpload={(file) => uploadImageAsset(file, "images/food-items")}
                            helperText="Upload ảnh mới để thay hình đại diện của món ăn."
                            emptyText="Món này chưa có ảnh đại diện."
                          />
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="rounded-2xl border border-dashed border-sand-200 bg-white px-4 py-3 text-sm text-ink-500">
                      Chưa có món ăn nào trong form này. Bấm “Thêm món ăn” để tạo ngay trong lúc chỉnh sửa POI.
                    </div>
                  )}
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Nguồn audio</label>
                  <Select disabled={isPoiModalViewOnly} value={form.audioSourceType} onChange={(event) => setForm((current) => ({ ...current, audioSourceType: event.target.value as AudioGuide["sourceType"] }))}>
                    <option value="generated">Pre-generated từ backend</option>
                    <option value="uploaded">Uploaded MP3</option>
                  </Select>
                </div>
                <div>
                  <label className="field-label">Trạng thái audio</label>
                  <div className="field-input flex min-h-[3rem] items-center bg-sand-50 text-sm text-ink-700">
                    {currentFormAudioStatusLabel}
                  </div>
                </div>
              </div>

              {form.audioSourceType === "generated" ? (
                <div className="space-y-3 rounded-3xl border border-sand-200 bg-sand-50 p-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <Button
                      type="button"
                      variant="secondary"
                      disabled={isPoiModalViewOnly || !currentFormPoiId || isGeneratingAudio}
                      onClick={() => {
                        void runPoiAudioAction("generate");
                      }}
                    >
                      Generate audio ngôn ngữ này
                    </Button>
                    <Button
                      type="button"
                      variant="ghost"
                      disabled={isPoiModalViewOnly || !currentFormPoiId || isGeneratingAudio}
                      onClick={() => {
                        void runPoiAudioAction("regenerate");
                      }}
                    >
                      Regenerate audio
                    </Button>
                    <Button
                      type="button"
                      variant="ghost"
                      disabled={isPoiModalViewOnly || !currentFormPoiId || isGeneratingAudio}
                      onClick={() => {
                        void runPoiAudioAction("generateAll");
                      }}
                    >
                      Generate tất cả ngôn ngữ
                    </Button>
                  </div>
                  <p className="text-xs text-ink-500">
                    Khi bạn sửa nội dung thuyết minh, audio hiện tại sẽ được đánh dấu cũ và backend có thể generate lại theo cấu hình.
                  </p>
                  {isGeneratingAudio ? (
                    <p className="text-sm text-ink-600">Đang generate audio pre-generated...</p>
                  ) : null}
                  {audioActionMessage ? (
                    <p className="text-sm text-emerald-700">{audioActionMessage}</p>
                  ) : null}
                  <div className="grid gap-3 md:grid-cols-2">
                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 text-sm text-ink-600">
                      <p><span className="font-semibold text-ink-900">Audio URL:</span> {currentFormAudioGuide?.audioUrl || "Chưa có"}</p>
                      <p className="mt-2"><span className="font-semibold text-ink-900">File path:</span> {currentFormAudioGuide?.audioFilePath || "Chưa có"}</p>
                      <p className="mt-2"><span className="font-semibold text-ink-900">GeneratedAt:</span> {currentFormAudioGuide?.generatedAt ? formatDateTime(currentFormAudioGuide.generatedAt) : "Chưa có"}</p>
                    </div>
                    <div className="rounded-2xl border border-white/80 bg-white px-4 py-3 text-sm text-ink-600">
                      <p><span className="font-semibold text-ink-900">Voice / Model:</span> {currentFormAudioGuide ? `${currentFormAudioGuide.voiceId || "default"} / ${currentFormAudioGuide.modelId || "default"}` : "Chưa có"}</p>
                      <p className="mt-2"><span className="font-semibold text-ink-900">Output:</span> {currentFormAudioGuide?.outputFormat || "mp3_44100_128"}</p>
                      <p className="mt-2"><span className="font-semibold text-ink-900">Lỗi gần nhất:</span> {currentFormAudioGuide?.errorMessage || "Không có"}</p>
                    </div>
                  </div>
                  {currentFormAudioGuide?.audioUrl ? (
                    <audio controls src={currentFormAudioGuide.audioUrl} className="w-full" />
                  ) : (
                    <div className="rounded-2xl border border-dashed border-sand-200 bg-white px-4 py-3 text-sm text-ink-500">
                      Chưa có file audio pre-generated cho ngôn ngữ này.
                    </div>
                  )}
                </div>
              ) : null}

              {form.audioSourceType === "uploaded" ? (
                <div className="space-y-3">
                  <label className="field-label">Upload MP3</label>
                  <input
                    type="file"
                    accept=".mp3,audio/mpeg"
                    autoComplete="off"
                    disabled={isPoiModalViewOnly}
                    onChange={(event) => {
                      void handleAudioFileChange(event);
                    }}
                    className="field-input file:mr-4 file:rounded-2xl file:border-0 file:bg-primary-50 file:px-4 file:py-2 file:font-semibold file:text-primary-700"
                  />
                  {isUploadingAudio ? <p className="text-sm text-ink-500">Đang upload audio...</p> : null}
                  {form.audioUrl ? (
                    <div className="space-y-2">
                      <audio controls src={form.audioUrl} className="w-full" />
                    </div>
                  ) : (
                    <div className="rounded-2xl border border-dashed border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-500">
                      Chưa có file MP3 nào được tải lên.
                    </div>
                  )}
                </div>
              ) : null}
            </div>

            <OpenStreetMapPicker
              key={`${poiFormLoadState.mode}:${poiFormLoadState.dataLoaded ? "ready" : "loading"}:${poiFormLoadState.mode === "create" ? "create" : form.id || form.poiId || "new"}`}
              editable={poiModalEditable}
              isVisible={isModalOpen && poiFormLoadState.dataLoaded}
              address={form.address}
              lat={parseCoordinate(form.lat, DEFAULT_LAT)}
              lng={parseCoordinate(form.lng, DEFAULT_LNG)}
              selectedTriggerRadius={resolveTriggerRadiusPreview(form.triggerRadius)}
              addressSearchVersion={addressSearchVersion}
              onLocationResolved={(location) =>
                setForm((current) => ({
                  ...current,
                  address: location.address || current.address,
                  district: location.district || current.district,
                  ward: location.ward || current.ward,
                  lat: location.lat.toFixed(6),
                  lng: location.lng.toFixed(6),
                }))
              }
              onChange={(latValue, lngValue) =>
                setForm((current) => ({
                  ...current,
                  lat: latValue.toFixed(6),
                  lng: lngValue.toFixed(6),
                }))
              }
            />
          </div>
          </fieldset>

          {formError ? <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div> : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={closePoiModal}>
              {isPoiModalViewOnly ? "Đóng" : "Hủy"}
            </Button>
            {!isPoiModalViewOnly ? (
              <Button type="submit" disabled={isPoiFormLoading || isSaving || isUploadingAudio || activeImageUploads > 0 || (isPoiModalViewOnly && poiStatusFieldDisabled)}>
                {isSaving
                  ? "Đang lưu..."
                  : activeImageUploads > 0
                    ? "Đợi upload ảnh..."
                    : isOwner
                        ? form.id
                          ? editingPoi?.status === "rejected"
                            ? "Sửa và gửi duyệt lại"
                            : editingApprovedLifecyclePoi
                              ? "Lưu thay đổi"
                              : "Gửi cập nhật để duyệt lại"
                          : "Gửi duyệt POI"
                        : form.id
                          ? "Lưu cập nhật POI"
                          : "Tạo POI"}
              </Button>
            ) : null}
          </div>
        </form>
      </Modal>

      <Modal
        open={!!activeToggleTarget && !!poiBeingToggled}
        onClose={closeActiveToggleModal}
        title="Đổi trạng thái hoạt động POI"
        description={
          isSuperAdmin
            ? "Super Admin có thể bật hoặc tắt trạng thái hoạt động của POI đã được duyệt."
            : "Chủ quán có thể tự bật hoặc tắt trạng thái hoạt động của POI đã được duyệt, trừ khi POI đã bị Super Admin khóa."
        }
        maxWidthClassName="max-w-xl"
      >
        {activeToggleTarget && poiBeingToggled ? (
          <div className="space-y-5">
            <div className="rounded-3xl border border-sand-100 bg-sand-50 px-5 py-4">
              <p className="text-sm text-ink-500">POI đang xử lý</p>
              <p className="mt-2 text-lg font-semibold text-ink-900">
                {getDisplayedPoiTranslation(poiBeingToggled, selectedNarrationLanguage)?.title ?? poiBeingToggled.slug}
              </p>
              <p className="mt-2 text-sm text-ink-600">{poiBeingToggled.address}</p>
            </div>

            <div className="rounded-2xl border border-sand-100 bg-white px-4 py-4 text-sm text-ink-700">
              Bạn có chắc muốn thay đổi trạng thái hoạt động của POI này?
              <span className="ml-2 font-semibold text-ink-900">
                {activeToggleTarget.nextIsActive
                  ? "POI sẽ hiển thị lại trên hệ thống."
                  : isSuperAdmin
                    ? "POI sẽ bị ẩn khỏi hệ thống và chủ quán không thể tự bật lại."
                    : "POI sẽ bị chuyển sang ngừng hoạt động."}
              </span>
            </div>

            <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
              <Button variant="ghost" onClick={closeActiveToggleModal}>
                Hủy
              </Button>
              <Button
                variant={activeToggleTarget.nextIsActive ? "primary" : "danger"}
                onClick={() => void handleTogglePoiActive()}
                disabled={isSaving}
              >
                {isSaving
                  ? "Đang cập nhật..."
                  : activeToggleTarget.nextIsActive
                    ? "Bật hoạt động"
                    : isSuperAdmin
                      ? "Ngừng hoạt động và khóa"
                      : "Ngừng hoạt động"}
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>

      <Modal
        open={isRejectModalOpen && !!poiBeingRejected}
        onClose={closeRejectModal}
        title="Từ chối duyệt POI"
        description="Nhập lý do từ chối để chủ quán có thể xem, chỉnh sửa và gửi duyệt lại."
        maxWidthClassName="max-w-2xl"
      >
        {poiBeingRejected ? (
          <div className="space-y-5">
            <div className="rounded-3xl border border-sand-100 bg-sand-50 px-5 py-4">
              <p className="text-sm text-ink-500">POI đang xử lý</p>
              <p className="mt-2 text-lg font-semibold text-ink-900">
                {getDisplayedPoiTranslation(poiBeingRejected, selectedNarrationLanguage)?.title ?? poiBeingRejected.slug}
              </p>
              <p className="mt-2 text-sm text-ink-600">{poiBeingRejected.address}</p>
            </div>

            <div>
              <label className="field-label">Lý do từ chối</label>
              <Textarea
                value={rejectReason}
                onChange={(event) => setRejectReason(event.target.value)}
                placeholder="Ví dụ: Sai địa chỉ, mô tả chưa đủ rõ hoặc nội dung chưa đạt yêu cầu."
              />
              <p className="mt-2 text-xs text-ink-500">
                Trường này bắt buộc và sẽ hiển thị lại cho chủ quán sau khi tải lại trang.
              </p>
            </div>

            {rejectError ? (
              <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{rejectError}</div>
            ) : null}

            <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
              <Button variant="ghost" onClick={closeRejectModal}>
                Hủy
              </Button>
              <Button variant="danger" onClick={() => void handleRejectPoi()} disabled={isSaving}>
                {isSaving ? "Đang từ chối..." : "Xác nhận từ chối"}
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
};

