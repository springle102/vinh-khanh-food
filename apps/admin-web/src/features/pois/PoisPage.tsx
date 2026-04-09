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
import { useAdminData } from "../../data/store";
import type { AdminDataState, AudioGuide, FoodItem, LanguageCode, MediaAsset, Poi, PoiDetail, RegionVoice, Translation } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { getCategoryName, getOwnerName, searchPois } from "../../lib/selectors";
import { formatDateTime, formatNumber, languageLabels, slugify } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";
import { OpenStreetMapPicker, type PoiMapItem } from "./OpenStreetMapPicker";
import { usePoiNarrationPlayback } from "./usePoiNarrationPlayback";
import {
  supportedNarrationLanguages,
  type ResolvedPoiNarration,
} from "../../lib/narration";

const DEFAULT_LAT = 10.7578;
const DEFAULT_LNG = 106.7033;

const voiceLabels: Record<RegionVoice, string> = {
  standard: "Tiêu chuẩn",
  north: "Miền Bắc",
  central: "Miền Trung",
  south: "Miền Nam",
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
  featured: boolean;
  contentLanguageCode: LanguageCode;
  district: string;
  ward: string;
  priceRange: string;
  averageVisitDuration: string;
  popularityScore: string;
  tags: string;
  ownerUserId: string;
  shortText: string;
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

const createDefaultForm = (categoryId: string, status: Poi["status"] = "draft"): PoiFormState => ({
  poiId: "",
  title: "",
  slug: "",
  address: "",
  lat: DEFAULT_LAT.toFixed(6),
  lng: DEFAULT_LNG.toFixed(6),
  categoryId,
  status,
  featured: false,
  contentLanguageCode: "vi",
  district: "Quận 4",
  ward: "Khánh Hội",
  priceRange: "",
  averageVisitDuration: "30",
  popularityScore: "75",
  tags: "",
  ownerUserId: "",
  shortText: "",
  fullText: "",
  audioUrl: "",
  audioSourceType: "tts",
  audioStatus: "ready",
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

const findPoiRepresentativeImage = (mediaAssets: MediaAsset[], poiId?: string) => {
  if (!poiId) {
    return null;
  }

  return mediaAssets.reduce<MediaAsset | null>((latestAsset, item) => {
    if (item.entityType !== "poi" || item.entityId !== poiId || item.type !== "image") {
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
): PoiFoodItemFormState[] =>
  foodItems
    .filter((item) => item.poiId === poiId)
    .map((item) => ({
      clientId: item.id,
      id: item.id,
      poiId: item.poiId,
      name: item.name,
      description: item.description,
      priceRange: item.priceRange,
      imageUrl: item.imageUrl,
      spicyLevel: item.spicyLevel,
    }));

const hasFoodItemContent = (foodItem: PoiFoodItemFormState) =>
  Boolean(
    foodItem.name.trim() ||
      foodItem.description.trim() ||
      foodItem.priceRange.trim() ||
      foodItem.imageUrl.trim(),
  );

const parseCoordinate = (value: string, fallback: number) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const findPoiAudioGuide = (
  audioGuides: AudioGuide[],
  poiId?: string,
  languageCode?: AudioGuide["languageCode"],
  voiceType?: AudioGuide["voiceType"],
) =>
  audioGuides.find(
    (item) =>
      item.entityType === "poi" &&
      item.entityId === poiId &&
      item.languageCode === languageCode &&
      (!voiceType || item.voiceType === voiceType),
  ) ??
  audioGuides.find(
    (item) =>
      item.entityType === "poi" &&
      item.entityId === poiId &&
      item.languageCode === languageCode,
  ) ??
  null;

const findPoiTranslationForLanguage = (
  translations: Translation[],
  poi: Poi,
  languageCode: LanguageCode,
) =>
  translations.find(
    (item) =>
      item.entityType === "poi" &&
      item.entityId === poi.id &&
      item.languageCode === languageCode,
  ) ?? null;

const findPoiTranslationWithFallback = (
  translations: Translation[],
  poi: Poi,
  preferredLanguage: LanguageCode | undefined,
  settings: Pick<AdminDataState["settings"], "defaultLanguage" | "fallbackLanguage">,
) => {
  const languages = [
    preferredLanguage,
    settings.defaultLanguage,
    settings.fallbackLanguage,
  ].filter(Boolean) as LanguageCode[];

  for (const language of languages) {
    const matched = findPoiTranslationForLanguage(translations, poi, language);
    if (matched) {
      return matched;
    }
  }

  return (
    translations.find(
      (item) => item.entityType === "poi" && item.entityId === poi.id,
    ) ?? null
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
      shortText: "",
      fullText: "",
    };
  }

  const exactTranslation = findPoiTranslationForLanguage(translations, poi, languageCode);
  return {
    title: exactTranslation?.title ?? "",
    shortText: exactTranslation?.shortText ?? "",
    fullText: exactTranslation?.fullText ?? "",
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

const getSubmissionStatus = (role: "SUPER_ADMIN" | "PLACE_OWNER" | undefined, status: Poi["status"]) =>
  role === "PLACE_OWNER" ? "pending" : status;

export const PoisPage = () => {
  const { state, saveAudioGuide, saveFoodItem, saveMediaAsset, savePoi } = useAdminData();
  const { user } = useAuth();
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
  const [selectedVoice, setSelectedVoice] = useState<RegionVoice>("standard");
  const [isModalOpen, setModalOpen] = useState(false);
  const [isSaving, setSaving] = useState(false);
  const [isUploadingAudio, setUploadingAudio] = useState(false);
  const [formError, setFormError] = useState("");
  const [addressSearchVersion, setAddressSearchVersion] = useState(0);
  const [hasNarrationInteraction, setHasNarrationInteraction] = useState(false);
  const [isFetchingPoiDetail, setFetchingPoiDetail] = useState(false);
  const [visiblePoiIds, setVisiblePoiIds] = useState<string[]>([]);
  const [hasSlugBeenManuallyEdited, setHasSlugBeenManuallyEdited] = useState(false);
  const [selectedNarration, setSelectedNarration] = useState<ResolvedPoiNarration | null>(null);
  const [isResolvingNarration, setResolvingNarration] = useState(false);
  const [form, setForm] = useState<PoiFormState>(() =>
    createDefaultForm(state.categories[0]?.id ?? "", getSubmissionStatus(user?.role, "draft")),
  );
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
    voice: RegionVoice;
    detail: PoiDetail | null;
  }>({
    token: 0,
    poiId: null,
    language: "vi",
    voice: "standard",
    detail: null,
  });
  const poiDetailCacheRef = useRef(new Map<string, PoiDetail>());
  const poiDetailAbortRef = useRef<AbortController | null>(null);
  const narrationAbortRef = useRef<AbortController | null>(null);
  const poiFoodItemDraftSequenceRef = useRef(0);
  const lastHandledPlaybackIntentTokenRef = useRef(0);
  const selectedNarrationRequestRef = useRef(0);
  const lastNarrationSelectionRef = useRef<{
    poiId: string | null;
    language: LanguageCode;
    voice: RegionVoice;
  }>({
    poiId: null,
    language: "vi",
    voice: "standard",
  });

  const filteredPois = useMemo(() => {
    const searched = searchPois(state.pois, state, keyword);
    return statusFilter === "all" ? searched : searched.filter((item) => item.status === statusFilter);
  }, [keyword, state, statusFilter]);
  const pendingPois = useMemo(
    () => state.pois.filter((item) => item.status === "pending"),
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
  const selectedPoiTranslations = useMemo(
    () =>
      selectedPoi && selectedPoiDetail?.poi.id === selectedPoi.id
        ? selectedPoiDetail.translations
        : state.translations.filter(
            (item) => item.entityType === "poi" && item.entityId === selectedPoi?.id,
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
  const selectedDisplayTitle =
    selectedNarration?.displayTitle ??
    selectedNarrationTranslation?.title ??
    selectedPoi?.slug ??
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
    ? buildPlaybackKey(selectedPoi.id, selectedNarrationLanguage, selectedVoice)
    : null;
  const selectedAudio = selectedNarration?.audioGuide ?? null;
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
              (item) => item.entityType === "poi" && item.entityId === poi.id,
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
        featured: poi.featured,
        lat: poi.lat,
        lng: poi.lng,
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
        voice: selectedVoice,
        detail: cachedDetail ?? null,
      }));
    },
    [primePlayback, selectedNarrationLanguage, selectedVoice, state.pois],
  );

  const openCreateModal = () => {
    stopCurrentAudio();
    setHasSlugBeenManuallyEdited(false);
    setFormError("");
    setAddressSearchVersion(0);
    setPoiImageForm(createDefaultPoiImageForm());
    setPoiFoodItemForms([]);
    setForm({
      ...createDefaultForm(
        state.categories[0]?.id ?? "",
        getSubmissionStatus(user?.role, "draft"),
      ),
      contentLanguageCode: selectedNarrationLanguage,
      ownerUserId: isOwner ? user?.id ?? "" : "",
    });
    setModalOpen(true);
  };

  const openEditModal = (poi: Poi) => {
    stopCurrentAudio();
    const translations =
      selectedPoiDetail?.poi.id === poi.id
        ? selectedPoiDetail.translations
        : state.translations.filter(
            (item) => item.entityType === "poi" && item.entityId === poi.id,
          );
    const editableLanguageCode = resolvePoiTranslationLanguage(
      translations,
      poi,
      selectedNarrationLanguage,
      state.settings,
    );
    const translationFields = buildPoiTranslationFields(
      poi,
      translations,
      editableLanguageCode,
    );
    const audioGuides =
      selectedPoiDetail?.poi.id === poi.id
        ? selectedPoiDetail.audioGuides
        : state.audioGuides.filter(
            (item) => item.entityType === "poi" && item.entityId === poi.id,
          );
    const audioGuide = findPoiAudioGuide(audioGuides, poi.id, editableLanguageCode);
    const representativeImage = findPoiRepresentativeImage(state.mediaAssets, poi.id);

    setHasSlugBeenManuallyEdited(false);
    setFormError("");
    setAddressSearchVersion(0);
    setSelectedPoiId(poi.id);
    setPoiImageForm({
      id: representativeImage?.id,
      url: representativeImage?.url ?? "",
    });
    setPoiFoodItemForms(buildPoiFoodItemForms(state.foodItems, poi.id));
    setForm({
      id: poi.id,
      poiId: poi.id,
      title: translationFields.title,
      slug: poi.slug,
      address: poi.address,
      lat: poi.lat.toString(),
      lng: poi.lng.toString(),
      categoryId: poi.categoryId,
      status: getSubmissionStatus(user?.role, poi.status),
      featured: isOwner ? false : poi.featured,
      contentLanguageCode: editableLanguageCode,
      district: poi.district,
      ward: poi.ward,
      priceRange: poi.priceRange,
      averageVisitDuration: poi.averageVisitDuration.toString(),
      popularityScore: poi.popularityScore.toString(),
      tags: poi.tags.join(", "),
      ownerUserId: isOwner ? user?.id ?? "" : poi.ownerUserId ?? "",
      shortText: translationFields.shortText,
      fullText: translationFields.fullText,
      audioUrl: audioGuide?.sourceType === "uploaded" ? audioGuide.audioUrl : "",
      audioSourceType: audioGuide?.sourceType ?? "tts",
      audioStatus: audioGuide?.status ?? "ready",
    });
    setModalOpen(true);
  };

  const handleContentLanguageChange = useCallback(
    (languageCode: LanguageCode) => {
      setForm((current) => {
        const currentPoi =
          current.id ? state.pois.find((item) => item.id === current.id) ?? null : null;
        const translations =
          current.id && selectedPoiDetail?.poi.id === current.id
            ? selectedPoiDetail.translations
            : state.translations.filter(
                (item) => item.entityType === "poi" && item.entityId === current.id,
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
                (item) => item.entityType === "poi" && item.entityId === current.id,
              );
        const audioGuide = currentPoi
          ? findPoiAudioGuide(audioGuides, currentPoi.id, languageCode)
          : null;

        return {
          ...current,
          contentLanguageCode: languageCode,
          title: translationFields.title,
          shortText: translationFields.shortText,
          fullText: translationFields.fullText,
          audioUrl: audioGuide?.sourceType === "uploaded" ? audioGuide.audioUrl : "",
          audioSourceType: audioGuide?.sourceType ?? "tts",
          audioStatus: audioGuide?.status ?? "ready",
        };
      });
    },
    [selectedPoiDetail, state.audioGuides, state.pois, state.translations],
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
      selectedVoice,
      undefined,
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
    selectedVoice,
    syncVersion,
  ]);

  useEffect(() => {
    const previousSelection = lastNarrationSelectionRef.current;
    lastNarrationSelectionRef.current = {
      poiId: selectedPoiId,
      language: selectedNarrationLanguage,
      voice: selectedVoice,
    };

    if (!hasNarrationInteraction || !selectedPoiId) {
      return;
    }

    const shouldReplayCurrentPoi =
      previousSelection.poiId === selectedPoiId &&
      (previousSelection.language !== selectedNarrationLanguage ||
        previousSelection.voice !== selectedVoice);

    if (!shouldReplayCurrentPoi) {
      return;
    }

    setPlaybackIntent((current) => ({
      token: current.token + 1,
      poiId: selectedPoiId,
      language: selectedNarrationLanguage,
      voice: selectedVoice,
      detail: selectedPoiDetail,
    }));
  }, [hasNarrationInteraction, selectedNarrationLanguage, selectedPoiDetail, selectedPoiId, selectedVoice]);

  useEffect(() => {
    if (!hasNarrationInteraction || !playbackIntent.poiId || playbackIntent.token === 0) {
      return;
    }

    if (lastHandledPlaybackIntentTokenRef.current === playbackIntent.token) {
      return;
    }

    const fallbackPoi = state.pois.find((item) => item.id === playbackIntent.poiId);
    const effectivePoi = playbackIntent.detail?.poi ?? fallbackPoi;
    if (!effectivePoi) {
      return;
    }

    // Each intent token should trigger playback once, even if callback identities change later.
    lastHandledPlaybackIntentTokenRef.current = playbackIntent.token;

    void playPoiNarration({
      poi: effectivePoi,
      language: playbackIntent.language,
      voice: playbackIntent.voice,
      detail: playbackIntent.detail,
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
      voice: selectedVoice,
      detail: selectedPoiDetail,
    });
  }, [
    playPoiNarration,
    playResolvedNarration,
    primePlayback,
    selectedNarration,
    selectedNarrationLanguage,
    selectedPoi,
    selectedPoiDetail,
    selectedVoice,
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
        shortTextLength: form.shortText.length,
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
      const savedPoi = await savePoi(
        {
          id: form.id,
          requestedId: form.poiId.trim() || undefined,
          slug: nextSlug,
          address: form.address,
          lat: parseCoordinate(form.lat, DEFAULT_LAT),
          lng: parseCoordinate(form.lng, DEFAULT_LNG),
          categoryId: form.categoryId,
          status: getSubmissionStatus(user.role, form.status),
          featured: isOwner ? false : form.featured,
          district: form.district,
          ward: form.ward,
          priceRange: form.priceRange,
          averageVisitDuration: Number(form.averageVisitDuration),
          popularityScore: Number(form.popularityScore),
          tags: form.tags.split(",").map((item) => item.trim()).filter(Boolean),
          ownerUserId: (isOwner ? user.id : form.ownerUserId) || null,
          translationLanguageCode: form.contentLanguageCode,
          title: form.title,
          shortText: form.shortText,
          fullText: form.fullText,
          seoTitle: form.title,
          seoDescription: form.shortText || form.title,
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

      for (const foodItemDraft of normalizedFoodItemDrafts) {
        const existingFoodItem = foodItemDraft.id
          ? existingFoodItemsById.get(foodItemDraft.id)
          : null;
        const hasFoodItemChanged =
          !existingFoodItem ||
          existingFoodItem.poiId !== savedPoi.id ||
          existingFoodItem.name !== foodItemDraft.name ||
          existingFoodItem.description !== foodItemDraft.description ||
          existingFoodItem.priceRange !== foodItemDraft.priceRange ||
          existingFoodItem.imageUrl !== foodItemDraft.imageUrl ||
          existingFoodItem.spicyLevel !== foodItemDraft.spicyLevel;

        if (!hasFoodItemChanged) {
          continue;
        }

        await saveFoodItem(
          {
            id: foodItemDraft.id,
            poiId: savedPoi.id,
            name: foodItemDraft.name,
            description: foodItemDraft.description,
            priceRange: foodItemDraft.priceRange,
            imageUrl: foodItemDraft.imageUrl,
            spicyLevel: foodItemDraft.spicyLevel,
          },
          user,
        );
      }

      let optimisticAudioGuide: AudioGuide | null = null;
      if (form.audioUrl || form.audioSourceType === "tts") {
        await saveAudioGuide(
          {
            id: findPoiAudioGuide(state.audioGuides, form.id, form.contentLanguageCode)?.id,
            entityType: "poi",
            entityId: savedPoi.id,
            languageCode: form.contentLanguageCode,
            audioUrl: form.audioUrl,
            voiceType: "standard",
            sourceType: form.audioUrl ? form.audioSourceType : "tts",
            status: form.audioStatus,
          },
          user,
        );

        const existingAudioGuide =
          findPoiAudioGuide(
            existingDetail?.audioGuides ?? [],
            savedPoi.id,
            form.contentLanguageCode,
            "standard",
          ) ??
          findPoiAudioGuide(
            state.audioGuides,
            savedPoi.id,
            form.contentLanguageCode,
            "standard",
          );

        optimisticAudioGuide = {
          id: existingAudioGuide?.id ?? `audio-${savedPoi.id}-${form.contentLanguageCode}`,
          entityType: "poi",
          entityId: savedPoi.id,
          languageCode: form.contentLanguageCode,
          audioUrl: form.audioUrl,
          voiceType: "standard",
          sourceType: form.audioUrl ? form.audioSourceType : "tts",
          status: form.audioStatus,
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
            (item) => item.entityType === "poi" && item.entityId === savedPoi.id,
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
        shortText: form.shortText,
        fullText: form.fullText,
        seoTitle: form.title,
        seoDescription: form.shortText || form.title,
        isPremium: !state.settings.freeLanguages.includes(form.contentLanguageCode),
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
      setModalOpen(false);
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const buildPoiSaveDraft = useCallback(
    (poi: Poi) => {
      const translations =
        selectedPoiDetail?.poi.id === poi.id
          ? selectedPoiDetail.translations
          : state.translations.filter(
              (item) => item.entityType === "poi" && item.entityId === poi.id,
            );
      const translationLanguageCode = resolvePoiTranslationLanguage(
        translations,
        poi,
        selectedNarrationLanguage,
        state.settings,
      );
      const translation = findPoiTranslationWithFallback(
        translations,
        poi,
        translationLanguageCode,
        state.settings,
      );

      return {
        id: poi.id,
        requestedId: poi.id,
        slug: poi.slug,
        address: poi.address,
        lat: poi.lat,
        lng: poi.lng,
        categoryId: poi.categoryId,
        status: poi.status,
        featured: poi.featured,
        district: poi.district,
        ward: poi.ward,
        priceRange: poi.priceRange,
        averageVisitDuration: poi.averageVisitDuration,
        popularityScore: poi.popularityScore,
        tags: poi.tags,
        ownerUserId: poi.ownerUserId,
        translationLanguageCode,
        title: translation?.title ?? poi.slug,
        shortText: translation?.shortText ?? "",
        fullText: translation?.fullText ?? "",
        seoTitle: translation?.seoTitle ?? translation?.title ?? poi.slug,
        seoDescription: translation?.seoDescription ?? translation?.shortText ?? translation?.title ?? poi.slug,
      };
    },
    [selectedNarrationLanguage, selectedPoiDetail, state.settings, state.translations],
  );

  const handleApprovePoi = useCallback(
    async (poi: Poi) => {
      if (!user || !isSuperAdmin) {
        return;
      }

      setSaving(true);
      setFormError("");

      try {
        const savedPoi = await savePoi(
          {
            ...buildPoiSaveDraft(poi),
            status: "published",
          },
          user,
        );

        poiDetailCacheRef.current.delete(savedPoi.id);
        setSelectedPoiDetail((current) => (current?.poi.id === savedPoi.id ? null : current));
        setSelectedPoiId(savedPoi.id);
        void fetchPOIDetail(savedPoi.id, {
          useCache: false,
          updateSelected: true,
        }).catch(() => undefined);
      } catch (error) {
        setFormError(getErrorMessage(error));
      } finally {
        setSaving(false);
      }
    },
    [buildPoiSaveDraft, fetchPOIDetail, isSuperAdmin, savePoi, user],
  );

  const columns: DataColumn<Poi>[] = [
    {
      key: "poi",
      header: "POI",
      widthClassName: "min-w-[260px]",
      render: (poi) => {
        const translation = getDisplayedPoiTranslation(poi, selectedNarrationLanguage);
        return (
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-semibold text-ink-900">{translation?.title ?? poi.slug}</p>
              <StatusBadge status={poi.status} />
              {poi.featured ? <StatusBadge status="active" label="Featured" /> : null}
            </div>
            <p className="mt-2 text-sm text-ink-500">{translation?.shortText || poi.address}</p>
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
      widthClassName: "min-w-[180px]",
      render: (poi) => (
        <div className="flex gap-2">
          <Button variant="ghost" onClick={() => handlePoiSelect(poi.id)}>
            Xem
          </Button>
          {isSuperAdmin && poi.status === "pending" ? (
            <Button
              onClick={() => {
                void handleApprovePoi(poi);
              }}
              disabled={isSaving}
            >
              Duyệt
            </Button>
          ) : null}
          <Button variant="secondary" onClick={() => openEditModal(poi)}>
            Sửa
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
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
            <Button onClick={openCreateModal}>Tạo POI</Button>
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
          ["Published", formatNumber(state.pois.filter((item) => item.status === "published").length)],
          ["Có audio", formatNumber(state.audioGuides.filter((item) => item.entityType === "poi").length)],
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
              <option value="published">Published</option>
              <option value="archived">Archived</option>
            </Select>
          </div>

          <OpenStreetMapPicker
            editable={false}
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
                <StatusBadge status={selectedPoi.status} />
              </div>
              <p className="text-sm text-ink-600">{selectedPoi.address}</p>
              <div className="grid gap-3 sm:grid-cols-2">
                {[
                  ["Slug", selectedPoi.slug],
                  ["Phân loại", getCategoryName(state, selectedPoi.categoryId)],
                  ["Chủ quản lý", getOwnerName(state, selectedPoi.ownerUserId)],
                  ["Ngôn ngữ", languageLabels[selectedNarrationLanguage]],
                  ["Khu vực", `${selectedPoi.ward}, ${selectedPoi.district}`],
                  ["Cập nhật", formatDateTime(selectedPoi.updatedAt)],
                ].map(([label, value]) => (
                  <div key={label} className="rounded-2xl border border-sand-100 bg-sand-50 px-4 py-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-ink-500">{label}</p>
                    <p className="mt-2 text-sm font-medium text-ink-800">{value}</p>
                  </div>
                ))}
              </div>
              <div className="grid gap-3 sm:grid-cols-2">
                <div>
                  <label className="field-label">Ngôn ngữ thuyết minh</label>
                  <Select
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
                <div>
                  <label className="field-label">Giọng đọc</label>
                  <Select
                    value={selectedVoice}
                    onChange={(event) => setSelectedVoice(event.target.value as RegionVoice)}
                  >
                    {Object.entries(voiceLabels).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </Select>
                </div>
              </div>
              <div className="rounded-3xl border border-sand-100 bg-sand-50 p-4">
                <p className="text-sm font-semibold text-ink-900">Mô tả POI</p>
                <p className="mt-3 text-sm leading-6 text-ink-600">
                  {selectedDisplayText || "Chưa có bài thuyết minh cho POI này."}
                </p>
                <p className="mt-3 text-xs text-ink-500">
                  Audio: {selectedAudio ? `${selectedAudio.sourceType} / ${selectedAudio.status}` : "Chưa có"}
                </p>
                {selectedNarration?.fallbackMessage ? (
                  <p className="mt-2 text-xs text-amber-700">{selectedNarration.fallbackMessage}</p>
                ) : null}
                <p className="mt-2 text-xs text-ink-500">
                  {isResolvingNarration
                    ? "Đang đồng bộ nội dung và TTS theo ngôn ngữ đã chọn..."
                    : `Nội dung TTS: ${selectedNarration?.ttsInputText ? "sẵn sàng" : "chưa có"}`}
                </p>
                <p className="mt-2 text-xs text-ink-500">
                  {selectedNarration
                    ? `Voice/locale: ${selectedVoice} / ${selectedNarration.ttsLocale}`
                    : `Voice/locale: ${selectedVoice}`}
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
                <Button variant="secondary" onClick={() => openEditModal(selectedPoi)}>
                  Sửa POI này
                </Button>
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
        onClose={() => setModalOpen(false)}
        title={isOwner ? (form.id ? "Cập nhật POI để gửi duyệt" : "Tạo POI để gửi duyệt") : form.id ? "Cập nhật POI" : "Tạo POI"}
        description={isOwner ? "Điền thông tin POI và gửi cho super admin duyệt trước khi xuất bản." : "Điền thông tin POI, chỉnh vị trí trên bản đồ và lưu nội dung thuyết minh theo ngôn ngữ đang sửa."}
        maxWidthClassName="max-w-5xl"
      >
        <form className="space-y-6" onSubmit={handleSubmit} onKeyDown={preventImplicitFormSubmit}>
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Tên POI</label>
                  <Input
                    value={form.title}
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
                  <label className="field-label">ID quán</label>
                  <Input
                    value={form.poiId}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, poiId: event.target.value }))
                    }
                    placeholder="Tự tạo nếu để trống"
                  />
                </div>
                <div>
                  <label className="field-label">Slug</label>
                  <Input
                    value={form.slug}
                    onChange={(event) => {
                      setHasSlugBeenManuallyEdited(true);
                      setForm((current) => ({ ...current, slug: event.target.value }));
                    }}
                    required
                  />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-4">
                <div>
                  <label className="field-label">Phân loại</label>
                  <Select value={form.categoryId} onChange={(event) => setForm((current) => ({ ...current, categoryId: event.target.value }))}>
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
                    onChange={(event) => setForm((current) => ({ ...current, status: event.target.value as Poi["status"] }))}
                    disabled={isOwner}
                  >
                    {!isOwner ? <option value="draft">Draft</option> : null}
                    <option value="pending">Chờ duyệt</option>
                    {!isOwner ? <option value="published">Published</option> : null}
                    <option value="archived">Archived</option>
                  </Select>
                  {isOwner ? <p className="mt-2 text-xs text-ink-500">Chủ quán gửi POI lên để super admin duyệt trước khi xuất bản.</p> : null}
                </div>
                <div>
                  <label className="field-label">Khoảng giá</label>
                  <Input value={form.priceRange} onChange={(event) => setForm((current) => ({ ...current, priceRange: event.target.value }))} />
                </div>
              </div>

              <div>
                <div>
                  <label className="field-label">Ngôn ngữ nội dung đang sửa</label>
                  <Select
                    value={form.contentLanguageCode}
                    onChange={(event) =>
                      handleContentLanguageChange(event.target.value as LanguageCode)
                    }
                  >
                    {Object.entries(languageLabels).map(([code, label]) => (
                      <option key={code} value={code}>
                        {label}
                      </option>
                    ))}
                  </Select>
                </div>
              </div>

              <div>
                <label className="field-label">Địa chỉ</label>
                <Input
                  value={form.address}
                  onChange={(event) => setForm((current) => ({ ...current, address: event.target.value }))}
                  onBlur={triggerAddressSearch}
                  onKeyDown={handleAddressKeyDown}
                  required
                />
              </div>

              <div className="grid gap-5 md:grid-cols-4">
                <div>
                  <label className="field-label">Lat</label>
                  <Input value={form.lat} onChange={(event) => setForm((current) => ({ ...current, lat: event.target.value }))} required />
                </div>
                <div>
                  <label className="field-label">Lng</label>
                  <Input value={form.lng} onChange={(event) => setForm((current) => ({ ...current, lng: event.target.value }))} required />
                </div>
                <div>
                  <label className="field-label">Quận</label>
                  <Input value={form.district} onChange={(event) => setForm((current) => ({ ...current, district: event.target.value }))} />
                </div>
                <div>
                  <label className="field-label">Phường</label>
                  <Input value={form.ward} onChange={(event) => setForm((current) => ({ ...current, ward: event.target.value }))} />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Thời lượng</label>
                  <Input type="number" value={form.averageVisitDuration} onChange={(event) => setForm((current) => ({ ...current, averageVisitDuration: event.target.value }))} />
                </div>
                <div>
                  <label className="field-label">Độ phổ biến</label>
                  <Input type="number" value={form.popularityScore} onChange={(event) => setForm((current) => ({ ...current, popularityScore: event.target.value }))} />
                </div>
                <div className="flex items-end">
                  <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                    <input type="checkbox" checked={form.featured} onChange={(event) => setForm((current) => ({ ...current, featured: event.target.checked }))} disabled={isOwner} />
                    {isOwner ? "Featured (chỉ super admin)" : "Featured"}
                  </label>
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Người quản lý</label>
                  <Select value={form.ownerUserId} onChange={(event) => setForm((current) => ({ ...current, ownerUserId: event.target.value }))} disabled={isOwner}>
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
                  <Input value={form.tags} onChange={(event) => setForm((current) => ({ ...current, tags: event.target.value }))} />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Mô tả ngắn</label>
                  <Textarea value={form.shortText} onChange={(event) => setForm((current) => ({ ...current, shortText: event.target.value }))} />
                </div>
                <div>
                  <label className="field-label">Thuyết minh</label>
                  <Textarea value={form.fullText} onChange={(event) => setForm((current) => ({ ...current, fullText: event.target.value }))} />
                </div>
              </div>

              <div className="space-y-5 rounded-3xl border border-sand-100 bg-sand-50/70 p-5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold text-ink-900">Hình ảnh đại diện</p>
                    <p className="mt-2 text-xs text-ink-500">
                      Admin và chủ quán có thể cập nhật ảnh quán và ảnh món ăn ngay trong form POI.
                    </p>
                  </div>
                  {activeImageUploads ? (
                    <p className="text-xs text-ink-500">Đang upload ảnh...</p>
                  ) : null}
                </div>

                <ImageSourceField
                  label="Ảnh đại diện quán"
                  value={poiImageForm.url}
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
                        Thêm món mới và chỉnh sửa toàn bộ nội dung món ăn ngay trong màn sửa POI.
                      </p>
                    </div>
                    <Button
                      variant="secondary"
                      className="shrink-0"
                      onClick={addPoiFoodItemForm}
                    >
                      Thêm món ăn
                    </Button>
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
                            {!foodItem.id ? (
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
                  <Select value={form.audioSourceType} onChange={(event) => setForm((current) => ({ ...current, audioSourceType: event.target.value as AudioGuide["sourceType"] }))}>
                    <option value="tts">Text-to-Speech</option>
                    <option value="uploaded">Uploaded MP3</option>
                  </Select>
                </div>
                <div>
                  <label className="field-label">Trạng thái audio</label>
                  <Select value={form.audioStatus} onChange={(event) => setForm((current) => ({ ...current, audioStatus: event.target.value as AudioGuide["status"] }))}>
                    <option value="ready">Ready</option>
                    <option value="processing">Processing</option>
                    <option value="missing">Missing</option>
                  </Select>
                </div>
              </div>

              {form.audioSourceType === "uploaded" ? (
                <div className="space-y-3">
                  <label className="field-label">Upload MP3</label>
                  <input
                    type="file"
                    accept=".mp3,audio/mpeg"
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
              editable
              address={form.address}
              lat={parseCoordinate(form.lat, DEFAULT_LAT)}
              lng={parseCoordinate(form.lng, DEFAULT_LNG)}
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

          {formError ? <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div> : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving || isUploadingAudio || activeImageUploads > 0}>
              {isSaving
                ? "Đang lưu..."
                : activeImageUploads > 0
                  ? "Đợi upload ảnh..."
                  : isOwner
                    ? form.id
                      ? "Gửi cập nhật để duyệt lại"
                      : "Gửi duyệt POI"
                    : form.id
                      ? "Lưu cập nhật POI"
                      : "Tạo POI"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};







