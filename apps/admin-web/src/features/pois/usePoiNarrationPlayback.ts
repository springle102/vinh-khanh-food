import { useCallback, useEffect, useRef, useState } from "react";
import type {
  AdminDataState,
  AudioGuide,
  LanguageCode,
  Poi,
} from "../../data/types";
import {
  buildUiPlaybackKey,
  findPoiAudioGuide,
  hasValidAudioUrl,
  isPlayablePreparedAudioGuide,
  isPlaceholderAudioUrl,
  logNarrationDebug,
  resolvePoiNarration,
  type ResolvedPoiNarration,
} from "../../lib/narration";
import { languageLabels } from "../../lib/utils";

type PlaybackKind = "audio" | null;
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
  "id" | "entityId" | "languageCode" | "audioUrl" | "sourceType"
> & {
  previewText?: string;
};

type PoiAudioSource = {
  kind: "audio";
  uiPlaybackKey: string;
  audioCacheKey: string;
  poiId: string;
  audioGuideId: string | null;
  requestedLanguageCode: LanguageCode;
  effectiveLanguageCode: LanguageCode;
  fallbackMessage: string | null;
  audioUrl: string;
};

type PlayPoiNarrationOptions = {
  poi: Poi;
  language: LanguageCode;
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
  if (!isPlayablePreparedAudioGuide(narration.audioGuide)) {
    throw new Error("NO_PREGENERATED_AUDIO");
  }

  return {
    kind: "audio",
    uiPlaybackKey: narration.uiPlaybackKey,
    audioCacheKey: narration.audioCacheKey,
    poiId: narration.poiId,
    audioGuideId: narration.audioGuide?.id ?? null,
    audioUrl: narration.audioGuide?.audioUrl.trim() ?? "",
    requestedLanguageCode: narration.requestedLanguageCode,
    effectiveLanguageCode: narration.effectiveLanguageCode,
    fallbackMessage: narration.fallbackMessage,
  };
};

const createAudioSourceFromGuide = (
  guide: AudioPreviewCandidate,
  narration: ResolvedPoiNarration,
  audioGuide: AudioGuide | null,
): PoiAudioSource => {
  const directAudioUrl = hasValidAudioUrl(guide.audioUrl) && !isPlaceholderAudioUrl(guide.audioUrl)
    ? guide.audioUrl.trim()
    : "";
  const storedAudioUrl = isPlayablePreparedAudioGuide(audioGuide)
    ? audioGuide?.audioUrl.trim() ?? ""
    : "";
  const audioUrl = directAudioUrl || storedAudioUrl;

  if (!hasValidAudioUrl(audioUrl) || isPlaceholderAudioUrl(audioUrl)) {
    throw new Error("NO_PREGENERATED_AUDIO");
  }

  return {
    kind: "audio",
    uiPlaybackKey: guide.id,
    audioCacheKey: `${guide.id}:${guide.languageCode}`,
    poiId: guide.entityId,
    audioGuideId: guide.id,
    audioUrl,
    requestedLanguageCode: guide.languageCode,
    effectiveLanguageCode: narration.effectiveLanguageCode,
    fallbackMessage: narration.fallbackMessage,
  };
};

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
    return `Đang tải audio tạo sẵn cho ${requestedLabel}.${fallbackSuffix}`;
  }

  if (mode === "playing") {
    return `Đang phát audio tạo sẵn (${effectiveLabel}).${fallbackSuffix}`;
  }

  if (mode === "paused") {
    return "Đã tạm dừng bài thuyết minh.";
  }

  if (mode === "finished") {
    return "Đã phát xong bài thuyết minh.";
  }

  return "Không thể phát audio tạo sẵn cho POI này.";
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
        audioRef.current.removeAttribute("src");
        audioRef.current.load();
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
      signal?: AbortSignal,
    ) =>
      resolvePoiNarration({
        poi,
        language,
        signal,
      }),
    [],
  );

  const getPOIAudio = useCallback(
    async (
      poi: Poi,
      language: LanguageCode,
      signal?: AbortSignal,
    ) => {
      const narration = await resolvePoiNarrationForSelection(
        poi,
        language,
        signal,
      );
      const cached = audioCacheRef.current.get(narration.audioCacheKey);
      if (cached) {
        logNarrationDebug("audio-cache-hit", {
          poiId: poi.id,
          languageSelected: language,
          cacheKey: narration.audioCacheKey,
          source: "prepared_audio_url",
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
      currentAudioGuideIdRef.current = audioSource.audioGuideId;

      const nextAudio = new Audio();
      nextAudio.preload = "auto";
      audioRef.current = nextAudio;

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
      };

      try {
        nextAudio.src = audioSource.audioUrl;
        await nextAudio.play();
      } catch (error) {
        if (requestId !== requestIdRef.current) {
          return;
        }

        audioRef.current = null;
        resetPlaybackRefs();
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
      if (currentPlaybackKeyRef.current !== playbackKey || !audioRef.current) {
        return false;
      }

      const playbackStatus = playbackStatusRef.current;

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

      return false;
    },
    [],
  );

  const playPoiNarration = useCallback(
    async ({ poi, language }: PlayPoiNarrationOptions) => {
      const playbackKey = buildUiPlaybackKey(poi.id, language);
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
        message: "Đang đồng bộ metadata audio theo ngôn ngữ hiện tại...",
        isLoadingPOI: true,
        isGeneratingTTS: false,
        isPlayingAudio: false,
      });

      try {
        const audioSource = await getPOIAudio(
          poi,
          language,
          controller.signal,
        );
        if (requestId !== requestIdRef.current) {
          return;
        }

        resolveAbortRef.current = null;
        setPlaybackState((current) => ({
          ...current,
          audioGuideId: audioSource.audioGuideId,
          isGeneratingTTS: false,
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
            error instanceof Error && error.message === "NO_PREGENERATED_AUDIO"
              ? "POI này chưa có audio tạo sẵn cho ngôn ngữ đã chọn."
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

      const requestId = requestIdRef.current + 1;
      requestIdRef.current = requestId;
      resolveAbortRef.current = null;

      stopCurrentAudio("", {
        invalidateRequest: false,
        keepState: true,
      });

      let audioSource: PoiAudioSource;
      try {
        audioSource = createAudioSourceFromResolvedNarration(narration);
      } catch {
        setPlaybackState({
          playbackKey: narration.uiPlaybackKey,
          poiId: narration.poiId,
          audioGuideId: narration.audioGuide?.id ?? null,
          status: "error",
          kind: null,
          message: "POI này chưa có audio tạo sẵn cho ngôn ngữ đã chọn.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
        return;
      }

      audioCacheRef.current.set(narration.audioCacheKey, audioSource);

      setPlaybackState({
        playbackKey: narration.uiPlaybackKey,
        poiId: narration.poiId,
        audioGuideId: audioSource.audioGuideId,
        status: "loading",
        kind: audioSource.kind,
        message: getPlaybackMessage(audioSource, "loading"),
        isLoadingPOI: true,
        isGeneratingTTS: false,
        isPlayingAudio: false,
      });

      await playPOIAudio(audioSource, requestId);
    },
    [playPOIAudio, stopCurrentAudio, toggleCurrentAudio],
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
          kind: hasValidAudioUrl(guide.audioUrl) ? "audio" : null,
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
        const narration = await resolvePoiNarrationForSelection(
          poi,
          guide.languageCode,
          controller.signal,
        );

        const audioGuide =
          findPoiAudioGuide(state.audioGuides, guide.entityId, guide.languageCode) ??
          null;
        const audioSource = createAudioSourceFromGuide(guide, narration, audioGuide);

        if (requestId !== requestIdRef.current) {
          return;
        }

        setPlaybackState({
          playbackKey,
          poiId: guide.entityId,
          audioGuideId: guide.id,
          status: "loading",
          kind: "audio",
          message: getPlaybackMessage(audioSource, "loading"),
          isLoadingPOI: true,
          isGeneratingTTS: false,
          isPlayingAudio: false,
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
          kind: hasValidAudioUrl(guide.audioUrl) ? "audio" : null,
          message:
            error instanceof Error && error.message === "NO_PREGENERATED_AUDIO"
              ? "Chưa có audio tạo sẵn để nghe thử."
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
