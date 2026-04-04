import { useCallback, useEffect, useRef, useState } from "react";
import type {
  AdminDataState,
  AudioGuide,
  LanguageCode,
  Poi,
  PoiDetail,
  RegionVoice,
} from "../../data/types";
import {
  buildGoogleTtsAudioUrls,
  buildUiPlaybackKey,
  findPoiAudioGuide,
  hasValidAudioUrl,
  isPlaceholderAudioUrl,
  logNarrationDebug,
  resolvePoiNarration,
  type NarrationResolutionStatus,
  type ResolvedPoiNarration,
} from "../../lib/narration";
import { languageLabels } from "../../lib/utils";

type PlaybackKind = "audio" | "tts" | null;
type PlaybackStatus = "idle" | "loading" | "playing" | "paused" | "error";

export type PoiNarrationPlaybackState = {
  playbackKey: string | null;
  poiId: string | null;
  audioGuideId: string | null;
  status: PlaybackStatus;
  kind: PlaybackKind;
  message: string;
  isLoadingPOI: boolean;
  isGeneratingTTS: boolean;
  isPlayingAudio: boolean;
};

type AudioPreviewCandidate = Pick<
  AudioGuide,
  "id" | "entityId" | "languageCode" | "audioUrl" | "sourceType" | "voiceType"
> & {
  previewText?: string;
};

type PoiAudioSourceBase = {
  uiPlaybackKey: string;
  audioCacheKey: string;
  poiId: string;
  audioGuideId: string | null;
  requestedLanguageCode: LanguageCode;
  effectiveLanguageCode: LanguageCode;
  voiceType: RegionVoice;
  ttsLocale: string;
  translationStatus: NarrationResolutionStatus;
  fallbackMessage: string | null;
  displayText: string;
  ttsInputText: string;
};

type PoiAudioSource = PoiAudioSourceBase & {
  kind: "audio";
  audioUrls: string[];
  fallbackText?: string;
  generatedFromTts?: boolean;
  usesGoogleTranslateFallback?: boolean;
};

type PlayPoiNarrationOptions = {
  poi: Poi;
  language: LanguageCode;
  voice: RegionVoice;
  detail?: PoiDetail | null;
};

const DEFAULT_PLAYBACK_STATE: PoiNarrationPlaybackState = {
  playbackKey: null,
  poiId: null,
  audioGuideId: null,
  status: "idle",
  kind: null,
  message: "",
  isLoadingPOI: false,
  isGeneratingTTS: false,
  isPlayingAudio: false,
};

const SILENT_AUDIO_DATA_URI =
  "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=";

const createAudioSourceFromResolvedNarration = (
  narration: ResolvedPoiNarration,
): PoiAudioSource => {
  if (
    narration.translationStatus !== "auto_translated" &&
    narration.audioGuide &&
    hasValidAudioUrl(narration.audioGuide.audioUrl) &&
    !isPlaceholderAudioUrl(narration.audioGuide.audioUrl)
  ) {
    return {
      kind: "audio",
      uiPlaybackKey: narration.uiPlaybackKey,
      audioCacheKey: narration.audioCacheKey,
      poiId: narration.poiId,
      audioGuideId: narration.audioGuide.id,
      audioUrls: [narration.audioGuide.audioUrl.trim()],
      fallbackText: narration.ttsInputText || undefined,
      requestedLanguageCode: narration.requestedLanguageCode,
      effectiveLanguageCode: narration.effectiveLanguageCode,
      voiceType: narration.selectedVoice,
      ttsLocale: narration.ttsLocale,
      translationStatus: narration.translationStatus,
      fallbackMessage: narration.fallbackMessage,
      displayText: narration.displayText,
      ttsInputText: narration.ttsInputText,
      generatedFromTts: narration.audioGuide.sourceType === "tts",
      usesGoogleTranslateFallback: false,
    };
  }

  return {
    kind: "audio",
    uiPlaybackKey: narration.uiPlaybackKey,
    audioCacheKey: narration.audioCacheKey,
    poiId: narration.poiId,
    audioGuideId: narration.audioGuide?.id ?? null,
    audioUrls: buildGoogleTtsAudioUrls(
      narration.ttsInputText,
      narration.effectiveLanguageCode,
    ),
    fallbackText: narration.ttsInputText,
    requestedLanguageCode: narration.requestedLanguageCode,
    effectiveLanguageCode: narration.effectiveLanguageCode,
    voiceType: narration.selectedVoice,
    ttsLocale: narration.ttsLocale,
    translationStatus: narration.translationStatus,
    fallbackMessage: narration.fallbackMessage,
    displayText: narration.displayText,
    ttsInputText: narration.ttsInputText,
    generatedFromTts: true,
    usesGoogleTranslateFallback: true,
  };
};

const createGoogleTtsAudioSource = (audioSource: PoiAudioSource): PoiAudioSource => ({
  kind: "audio",
  uiPlaybackKey: audioSource.uiPlaybackKey,
  audioCacheKey: `${audioSource.audioCacheKey}|google-tts`,
  poiId: audioSource.poiId,
  audioGuideId: audioSource.audioGuideId,
  audioUrls: buildGoogleTtsAudioUrls(
    audioSource.ttsInputText,
    audioSource.effectiveLanguageCode,
  ),
  fallbackText: audioSource.ttsInputText,
  generatedFromTts: true,
  usesGoogleTranslateFallback: true,
  requestedLanguageCode: audioSource.requestedLanguageCode,
  effectiveLanguageCode: audioSource.effectiveLanguageCode,
  voiceType: audioSource.voiceType,
  ttsLocale: audioSource.ttsLocale,
  translationStatus: audioSource.translationStatus,
  fallbackMessage: audioSource.fallbackMessage,
  displayText: audioSource.displayText,
  ttsInputText: audioSource.ttsInputText,
});

const getPlaybackMessage = (
  audioSource: PoiAudioSource,
  mode: "loading" | "playing" | "paused" | "finished" | "error",
) => {
  const requestedLabel = languageLabels[audioSource.requestedLanguageCode];
  const effectiveLabel = languageLabels[audioSource.effectiveLanguageCode];
  const fallbackSuffix = audioSource.fallbackMessage
    ? ` ${audioSource.fallbackMessage}`
    : "";

  if (mode === "loading") {
    if (audioSource.generatedFromTts) {
      return `Đang tạo TTS cho ${requestedLabel}.${fallbackSuffix}`;
    }

    return `Đang tải audio cho ${requestedLabel}.${fallbackSuffix}`;
  }

  if (mode === "playing") {
    return audioSource.generatedFromTts
      ? `Đang phát audio TTS (${effectiveLabel}).${fallbackSuffix}`
      : `Đang phát audio của POI (${effectiveLabel}).${fallbackSuffix}`;
  }

  if (mode === "paused") {
    return "Đã tạm dừng bài thuyết minh.";
  }

  if (mode === "finished") {
    return "Đã phát xong bài thuyết minh.";
  }

  return "Không thể phát bài thuyết minh cho POI này.";
};

export const usePoiNarrationPlayback = (state: AdminDataState) => {
  const [playbackState, setPlaybackState] = useState<PoiNarrationPlaybackState>(
    DEFAULT_PLAYBACK_STATE,
  );
  const syncVersion = state.syncState?.version ?? "bootstrap";
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const audioCacheRef = useRef<Map<string, PoiAudioSource>>(new Map());
  const currentPlaybackKeyRef = useRef<string | null>(null);
  const currentPoiIdRef = useRef<string | null>(null);
  const currentKindRef = useRef<PlaybackKind>(null);
  const currentAudioGuideIdRef = useRef<string | null>(null);
  const requestIdRef = useRef(0);
  const hasUnlockedPlaybackRef = useRef(false);
  const playbackStatusRef = useRef<PlaybackStatus>(DEFAULT_PLAYBACK_STATE.status);
  const resolveAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    playbackStatusRef.current = playbackState.status;
  }, [playbackState.status]);

  useEffect(() => {
    audioCacheRef.current.clear();
  }, [syncVersion]);

  const resetPlaybackRefs = useCallback(() => {
    currentPlaybackKeyRef.current = null;
    currentPoiIdRef.current = null;
    currentKindRef.current = null;
    currentAudioGuideIdRef.current = null;
  }, []);

  const stopCurrentAudio = useCallback(
    (
      message = "",
      options?: {
        invalidateRequest?: boolean;
        keepState?: boolean;
      },
    ) => {
      if (options?.invalidateRequest !== false) {
        requestIdRef.current += 1;
      }

      resolveAbortRef.current?.abort();
      resolveAbortRef.current = null;

      if (audioRef.current) {
        audioRef.current.onplay = null;
        audioRef.current.onpause = null;
        audioRef.current.onended = null;
        audioRef.current.onerror = null;
        audioRef.current.pause();
        audioRef.current.currentTime = 0;
        audioRef.current = null;
      }

      resetPlaybackRefs();

      if (!options?.keepState) {
        setPlaybackState({
          ...DEFAULT_PLAYBACK_STATE,
          message,
        });
      }
    },
    [resetPlaybackRefs],
  );

  useEffect(() => () => stopCurrentAudio(), [stopCurrentAudio]);

  const primePlayback = useCallback(() => {
    if (hasUnlockedPlaybackRef.current || typeof window === "undefined") {
      return;
    }

    hasUnlockedPlaybackRef.current = true;

    try {
      const probeAudio = new Audio(SILENT_AUDIO_DATA_URI);
      probeAudio.muted = true;
      void probeAudio.play().catch(() => undefined);
    } catch {
      // Ignore unlock probe errors.
    }
  }, []);

  const resolvePoiNarrationForSelection = useCallback(
    async (
      poi: Poi,
      language: LanguageCode,
      voice: RegionVoice,
      detail?: PoiDetail | null,
      signal?: AbortSignal,
    ) =>
      resolvePoiNarration({
        state,
        poi,
        language,
        voice,
        detail,
        signal,
      }),
    [state],
  );

  const getPOIAudio = useCallback(
    async (
      poi: Poi,
      language: LanguageCode,
      voice: RegionVoice,
      detail?: PoiDetail | null,
      signal?: AbortSignal,
    ) => {
      const narration = await resolvePoiNarrationForSelection(
        poi,
        language,
        voice,
        detail,
        signal,
      );
      const hasPlayableAudioGuide =
        narration.audioGuide &&
        hasValidAudioUrl(narration.audioGuide.audioUrl) &&
        !isPlaceholderAudioUrl(narration.audioGuide.audioUrl);

      if (!narration.ttsInputText.trim() && !hasPlayableAudioGuide) {
        throw new Error("NO_POI_NARRATION_TEXT");
      }

      const cached = audioCacheRef.current.get(narration.audioCacheKey);
      if (cached) {
        logNarrationDebug("audio-cache-hit", {
          poiId: poi.id,
          languageSelected: language,
          selectedVoice: voice,
          cacheKey: narration.audioCacheKey,
        });
        return cached;
      }

      const audioSource = createAudioSourceFromResolvedNarration(narration);
      audioCacheRef.current.set(narration.audioCacheKey, audioSource);
      return audioSource;
    },
    [resolvePoiNarrationForSelection],
  );

  const playPOIAudio = useCallback(
    async (audioSource: PoiAudioSource, requestId = requestIdRef.current) => {
      if (requestId !== requestIdRef.current) {
        return;
      }

      currentPlaybackKeyRef.current = audioSource.uiPlaybackKey;
      currentPoiIdRef.current = audioSource.poiId;
      currentKindRef.current = audioSource.kind;
      currentAudioGuideIdRef.current = audioSource.audioGuideId;

      const fallbackToGoogleTts = async () => {
        if (
          !audioSource.fallbackText ||
          audioSource.usesGoogleTranslateFallback ||
          requestId !== requestIdRef.current
        ) {
          return false;
        }

        const ttsSource = createGoogleTtsAudioSource(audioSource);
        if (ttsSource.audioUrls.length === 0) {
          return false;
        }

        audioCacheRef.current.set(ttsSource.audioCacheKey, ttsSource);

        setPlaybackState((current) => ({
          ...current,
          status: "loading",
          kind: "audio",
          message: `File audio không phát được. Đang chuyển sang Google Translate TTS cho ${languageLabels[ttsSource.effectiveLanguageCode]}.`,
          isLoadingPOI: true,
          isGeneratingTTS: true,
          isPlayingAudio: false,
        }));

        await playPOIAudio(ttsSource, requestId);
        return true;
      };

      const nextAudio = new Audio();
      nextAudio.preload = "auto";
      audioRef.current = nextAudio;
      let currentSegmentIndex = 0;

      const playSegment = async (segmentIndex: number) => {
        if (
          segmentIndex >= audioSource.audioUrls.length ||
          requestId !== requestIdRef.current
        ) {
          return;
        }

        currentSegmentIndex = segmentIndex;
        nextAudio.src = audioSource.audioUrls[segmentIndex];
        await nextAudio.play();
      };

      nextAudio.onplay = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPlaybackState({
          playbackKey: audioSource.uiPlaybackKey,
          poiId: audioSource.poiId,
          audioGuideId: audioSource.audioGuideId,
          status: "playing",
          kind: "audio",
          message: getPlaybackMessage(audioSource, "playing"),
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: true,
        });
      };

      nextAudio.onpause = () => {
        if (
          requestId !== requestIdRef.current ||
          !audioRef.current ||
          audioRef.current.ended ||
          audioRef.current.currentTime === 0
        ) {
          return;
        }

        setPlaybackState((current) => ({
          ...current,
          status: "paused",
          message: getPlaybackMessage(audioSource, "paused"),
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        }));
      };

      nextAudio.onended = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        if (currentSegmentIndex + 1 < audioSource.audioUrls.length) {
          void playSegment(currentSegmentIndex + 1).catch(() => {
            nextAudio.onerror?.(new Event("error"));
          });
          return;
        }

        audioRef.current = null;
        resetPlaybackRefs();
        setPlaybackState({
          ...DEFAULT_PLAYBACK_STATE,
          message: getPlaybackMessage(audioSource, "finished"),
        });
      };

      nextAudio.onerror = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        audioRef.current = null;
        resetPlaybackRefs();
        void (async () => {
          const handled = await fallbackToGoogleTts();
          if (handled || requestId !== requestIdRef.current) {
            return;
          }

          setPlaybackState({
            playbackKey: audioSource.uiPlaybackKey,
            poiId: audioSource.poiId,
            audioGuideId: audioSource.audioGuideId,
            status: "error",
            kind: "audio",
            message: getPlaybackMessage(audioSource, "error"),
            isLoadingPOI: false,
            isGeneratingTTS: false,
            isPlayingAudio: false,
          });
        })();
      };

      try {
        await playSegment(0);
      } catch (error) {
        if (requestId !== requestIdRef.current) {
          return;
        }

        audioRef.current = null;
        resetPlaybackRefs();
        const handled = await fallbackToGoogleTts();
        if (handled || requestId !== requestIdRef.current) {
          return;
        }

        setPlaybackState({
          playbackKey: audioSource.uiPlaybackKey,
          poiId: audioSource.poiId,
          audioGuideId: audioSource.audioGuideId,
          status: "error",
          kind: "audio",
          message:
            error instanceof DOMException && error.name === "NotAllowedError"
              ? "Trình duyệt đã chặn tự động phát audio. Hãy bấm Phát lại để nghe thuyết minh."
              : getPlaybackMessage(audioSource, "error"),
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      }
    },
    [resetPlaybackRefs],
  );

  const toggleCurrentAudio = useCallback(
    async (playbackKey: string) => {
      if (currentPlaybackKeyRef.current !== playbackKey) {
        return false;
      }

      const playbackStatus = playbackStatusRef.current;

      if (currentKindRef.current === "audio" && audioRef.current) {
        if (playbackStatus === "playing") {
          audioRef.current.pause();
          setPlaybackState((current) => ({
            ...current,
            status: "paused",
            message: "Đã tạm dừng bài thuyết minh.",
            isPlayingAudio: false,
          }));
          return true;
        }

        if (playbackStatus === "paused") {
          try {
            await audioRef.current.play();
            return true;
          } catch {
            setPlaybackState((current) => ({
              ...current,
              status: "error",
              message: "Không thể tiếp tục phát bài thuyết minh.",
              isLoadingPOI: false,
              isGeneratingTTS: false,
              isPlayingAudio: false,
            }));
            return true;
          }
        }
      }

      return false;
    },
    [],
  );

  const playPoiNarration = useCallback(
    async ({ poi, language, voice, detail }: PlayPoiNarrationOptions) => {
      const playbackKey = buildUiPlaybackKey(poi.id, language, voice);
      const handledByToggle = await toggleCurrentAudio(playbackKey);
      if (handledByToggle) {
        return;
      }

      const requestId = requestIdRef.current + 1;
      requestIdRef.current = requestId;

      stopCurrentAudio("", {
        invalidateRequest: false,
        keepState: true,
      });

      const controller = new AbortController();
      resolveAbortRef.current = controller;

      setPlaybackState({
        playbackKey,
        poiId: poi.id,
        audioGuideId: null,
        status: "loading",
        kind: null,
        message: "Đang đồng bộ nội dung và audio theo ngôn ngữ hiện tại...",
        isLoadingPOI: true,
        isGeneratingTTS: false,
        isPlayingAudio: false,
      });

      try {
        const audioSource = await getPOIAudio(
          poi,
          language,
          voice,
          detail,
          controller.signal,
        );
        if (requestId !== requestIdRef.current) {
          return;
        }

        resolveAbortRef.current = null;
        setPlaybackState((current) => ({
          ...current,
          audioGuideId: audioSource.audioGuideId,
          isGeneratingTTS: Boolean(audioSource.generatedFromTts),
          message: getPlaybackMessage(audioSource, "loading"),
        }));

        await playPOIAudio(audioSource, requestId);
      } catch (error) {
        if (
          requestId !== requestIdRef.current ||
          (error instanceof DOMException && error.name === "AbortError")
        ) {
          return;
        }

        resolveAbortRef.current = null;
        resetPlaybackRefs();
        setPlaybackState({
          playbackKey,
          poiId: poi.id,
          audioGuideId: null,
          status: "error",
          kind: null,
          message:
            error instanceof Error && error.message === "NO_POI_NARRATION_TEXT"
              ? "POI này chưa có nội dung thuyết minh để phát."
              : "Không thể phát bài thuyết minh cho POI này.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      }
    },
    [getPOIAudio, playPOIAudio, resetPlaybackRefs, stopCurrentAudio, toggleCurrentAudio],
  );

  const playResolvedNarration = useCallback(
    async (narration: ResolvedPoiNarration) => {
      const handledByToggle = await toggleCurrentAudio(narration.uiPlaybackKey);
      if (handledByToggle) {
        return;
      }

      const hasPlayableAudioGuide =
        narration.audioGuide &&
        hasValidAudioUrl(narration.audioGuide.audioUrl) &&
        !isPlaceholderAudioUrl(narration.audioGuide.audioUrl);

      if (!narration.ttsInputText.trim() && !hasPlayableAudioGuide) {
        setPlaybackState({
          playbackKey: narration.uiPlaybackKey,
          poiId: narration.poiId,
          audioGuideId: narration.audioGuide?.id ?? null,
          status: "error",
          kind: null,
          message: "POI này chưa có nội dung thuyết minh để phát.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
        return;
      }

      const requestId = requestIdRef.current + 1;
      requestIdRef.current = requestId;
      resolveAbortRef.current = null;

      stopCurrentAudio("", {
        invalidateRequest: false,
        keepState: true,
      });

      const audioSource = createAudioSourceFromResolvedNarration(narration);
      audioCacheRef.current.set(narration.audioCacheKey, audioSource);

      setPlaybackState({
        playbackKey: narration.uiPlaybackKey,
        poiId: narration.poiId,
        audioGuideId: audioSource.audioGuideId,
        status: "loading",
        kind: audioSource.kind,
        message: getPlaybackMessage(audioSource, "loading"),
        isLoadingPOI: true,
        isGeneratingTTS: Boolean(audioSource.generatedFromTts),
        isPlayingAudio: false,
      });

      try {
        await playPOIAudio(audioSource, requestId);
      } catch (error) {
        if (
          requestId !== requestIdRef.current ||
          (error instanceof DOMException && error.name === "AbortError")
        ) {
          return;
        }

        resetPlaybackRefs();
        setPlaybackState({
          playbackKey: narration.uiPlaybackKey,
          poiId: narration.poiId,
          audioGuideId: audioSource.audioGuideId,
          status: "error",
          kind: audioSource.kind,
          message:
            error instanceof DOMException && error.name === "NotAllowedError"
              ? "Trình duyệt đã chặn tự động phát audio. Hãy bấm Phát lại một lần nữa."
              : "Không thể phát bài thuyết minh cho POI này.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      }
    },
    [playPOIAudio, resetPlaybackRefs, stopCurrentAudio, toggleCurrentAudio],
  );

  const previewAudioGuide = useCallback(
    async (guide: AudioPreviewCandidate) => {
      const playbackKey = guide.id;
      const handledByToggle = await toggleCurrentAudio(playbackKey);
      if (handledByToggle) {
        return;
      }

      const poi = state.pois.find((item) => item.id === guide.entityId);
      if (!poi) {
        setPlaybackState({
          playbackKey,
          poiId: guide.entityId,
          audioGuideId: guide.id,
          status: "error",
          kind: guide.sourceType === "uploaded" ? "audio" : "tts",
          message: "Không tìm thấy POI cho audio này.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
        return;
      }

      const requestId = requestIdRef.current + 1;
      requestIdRef.current = requestId;

      stopCurrentAudio("", {
        invalidateRequest: false,
        keepState: true,
      });

      const controller = new AbortController();
      resolveAbortRef.current = controller;

      try {
        const narration = guide.previewText?.trim()
          ? {
              ...(await resolvePoiNarrationForSelection(
                poi,
                guide.languageCode,
                guide.voiceType,
                undefined,
                controller.signal,
              )),
              displayText: guide.previewText,
              ttsInputText: guide.previewText,
              translatedText: guide.previewText,
              translationStatus: "stored" as const,
              fallbackMessage: null,
            }
          : await resolvePoiNarrationForSelection(
              poi,
              guide.languageCode,
              guide.voiceType,
              undefined,
              controller.signal,
            );
        if (!narration.ttsInputText.trim() && !hasValidAudioUrl(guide.audioUrl)) {
          throw new Error("NO_POI_NARRATION_TEXT");
        }

        const audioGuide =
          findPoiAudioGuide(state.audioGuides, guide.entityId, guide.languageCode, guide.voiceType) ??
          (hasValidAudioUrl(guide.audioUrl)
            ? ({
                id: guide.id,
                entityType: "poi",
                entityId: guide.entityId,
                languageCode: guide.languageCode,
                audioUrl: guide.audioUrl,
                sourceType: guide.sourceType,
                status: "ready",
                updatedAt: new Date().toISOString(),
                updatedBy: "preview",
                voiceType: guide.voiceType,
              } satisfies AudioGuide)
            : null);

        const audioSource = createAudioSourceFromResolvedNarration({
          ...narration,
          audioGuide,
        });

        await playPOIAudio(audioSource, requestId);
      } catch (error) {
        if (
          requestId !== requestIdRef.current ||
          (error instanceof DOMException && error.name === "AbortError")
        ) {
          return;
        }

        resolveAbortRef.current = null;
        setPlaybackState({
          playbackKey,
          poiId: guide.entityId,
          audioGuideId: guide.id,
          status: "error",
          kind: guide.sourceType === "uploaded" ? "audio" : "tts",
          message:
            error instanceof Error && error.message === "NO_POI_NARRATION_TEXT"
              ? "Chưa có nội dung để đọc TTS cho ngôn ngữ này."
              : "Không thể phát thử audio này.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      }
    },
    [playPOIAudio, resolvePoiNarrationForSelection, state.audioGuides, state.pois, stopCurrentAudio, toggleCurrentAudio],
  );

  return {
    playbackState,
    previewState: playbackState,
    previewAudioGuide,
    stopPreview: stopCurrentAudio,
    stopCurrentAudio,
    primePlayback,
    resolvePoiNarration: resolvePoiNarrationForSelection,
    getPOIAudio,
    playPOIAudio,
    playPoiNarration,
    playResolvedNarration,
    buildPlaybackKey: buildUiPlaybackKey,
  };
};
