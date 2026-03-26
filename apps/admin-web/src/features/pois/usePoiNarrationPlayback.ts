import { useCallback, useEffect, useRef, useState } from "react";
import type {
  AdminDataState,
  AudioGuide,
  LanguageCode,
  Poi,
  PoiDetail,
  RegionVoice,
  Translation,
} from "../../data/types";
import { getPoiTranslation } from "../../lib/selectors";

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

type PoiAudioSource =
  | {
      kind: "audio";
      playbackKey: string;
      poiId: string;
      audioGuideId: string | null;
      audioUrls: string[];
      fallbackText?: string;
      languageCode: LanguageCode;
      voiceType: RegionVoice;
      generatedFromTts?: boolean;
    }
  | {
      kind: "tts";
      playbackKey: string;
      poiId: string;
      audioGuideId: string | null;
      text: string;
      languageCode: LanguageCode;
      voiceType: RegionVoice;
    };

type PlayPoiNarrationOptions = {
  poi: Poi;
  language: LanguageCode;
  voice: RegionVoice;
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
const GOOGLE_TTS_MAX_CHARS = 180;

const languageLocales: Record<LanguageCode, string> = {
  vi: "vi-VN",
  en: "en-US",
  "zh-CN": "zh-CN",
  ko: "ko-KR",
  ja: "ja-JP",
};

const googleTtsLanguages: Record<LanguageCode, string> = {
  vi: "vi",
  en: "en",
  "zh-CN": "zh-CN",
  ko: "ko",
  ja: "ja",
};

const voiceKeywords: Record<RegionVoice, string[]> = {
  north: ["bac", "bắc", "north"],
  central: ["trung", "central"],
  south: ["nam", "south"],
  standard: ["standard", "default"],
};

const normalize = (value: string) => value.trim().toLowerCase();

const hasValidAudioUrl = (value: string | null | undefined) => Boolean(value?.trim());

const isPlaceholderAudioUrl = (value: string | null | undefined) => {
  if (!value?.trim()) {
    return false;
  }

  try {
    const parsed = new URL(value);
    return parsed.hostname.includes("example.com");
  } catch {
    return false;
  }
};

const buildPlaybackKey = (poiId: string, language: LanguageCode, voice: RegionVoice) =>
  `${poiId}:${language}:${voice}`;

const selectVoice = (
  availableVoices: SpeechSynthesisVoice[],
  languageCode: LanguageCode,
  voiceType: RegionVoice,
) => {
  const locale = normalize(languageLocales[languageCode]);
  const localePrefix = locale.split("-")[0];
  const languageVoices = availableVoices.filter((voice) =>
    normalize(voice.lang).startsWith(localePrefix),
  );

  const keywordMatches = voiceKeywords[voiceType];
  const findByKeywords = (voices: SpeechSynthesisVoice[]) =>
    voices.find((voice) =>
      keywordMatches.some((keyword) => normalize(voice.name).includes(keyword)),
    );

  return (
    findByKeywords(
      languageVoices.filter((voice) => normalize(voice.lang) === locale),
    ) ??
    findByKeywords(languageVoices) ??
    languageVoices.find((voice) => normalize(voice.lang) === locale) ??
    languageVoices[0] ??
    availableVoices[0] ??
    null
  );
};

const findPoiAudioGuide = (
  audioGuides: AudioGuide[],
  poiId: string,
  languageCode: LanguageCode,
  voiceType: RegionVoice,
) => {
  const matchingGuides = audioGuides.filter(
    (item) =>
      item.entityType === "poi" &&
      item.entityId === poiId &&
      item.languageCode === languageCode,
  );

  return (
    matchingGuides.find((item) => item.voiceType === voiceType && hasValidAudioUrl(item.audioUrl)) ??
    matchingGuides.find((item) => hasValidAudioUrl(item.audioUrl)) ??
    matchingGuides.find((item) => item.voiceType === voiceType) ??
    matchingGuides[0] ??
    null
  );
};

const findPoiTranslation = (
  translations: Translation[],
  poi: Poi,
  language: LanguageCode,
  fallbackLanguage: LanguageCode,
  defaultLanguage: LanguageCode,
) => {
  const languages = [
    language,
    poi.defaultLanguageCode,
    defaultLanguage,
    fallbackLanguage,
  ];

  for (const currentLanguage of languages) {
    const matched = translations.find(
      (item) =>
        item.entityType === "poi" &&
        item.entityId === poi.id &&
        item.languageCode === currentLanguage,
    );

    if (matched) {
      return matched;
    }
  }

  return (
    translations.find((item) => item.entityType === "poi" && item.entityId === poi.id) ??
    null
  );
};

const splitNarrationIntoChunks = (text: string, maxLength = GOOGLE_TTS_MAX_CHARS) => {
  const chunks: string[] = [];
  let start = 0;

  while (start < text.length) {
    let end = Math.min(start + maxLength, text.length);

    if (end < text.length) {
      const searchStart = start + Math.floor(maxLength * 0.6);
      for (let index = end; index > searchStart; index -= 1) {
        if (/[.!?,;:\s]/.test(text[index] ?? "")) {
          end = index + 1;
          break;
        }
      }
    }

    chunks.push(text.slice(start, end));
    start = end;
  }

  return chunks.filter((chunk) => chunk.length > 0);
};

const buildGoogleTtsAudioUrls = (text: string, languageCode: LanguageCode) => {
  const chunks = splitNarrationIntoChunks(text);
  const language = googleTtsLanguages[languageCode];

  return chunks.map((chunk, index) => {
    const query = new URLSearchParams({
      ie: "UTF-8",
      client: "tw-ob",
      tl: language,
      q: chunk,
      total: chunks.length.toString(),
      idx: index.toString(),
      textlen: chunk.length.toString(),
      ttsspeed: "1",
    });

    return `https://translate.google.com/translate_tts?${query.toString()}`;
  });
};

const createTtsAudioSource = (
  playbackKey: string,
  poiId: string,
  audioGuideId: string | null,
  text: string,
  languageCode: LanguageCode,
  voiceType: RegionVoice,
): PoiAudioSource => ({
  kind: "audio",
  playbackKey,
  poiId,
  audioGuideId,
  audioUrls: buildGoogleTtsAudioUrls(text, languageCode),
  fallbackText: text,
  languageCode,
  voiceType,
  generatedFromTts: true,
});

const createBrowserTtsAudioSource = (
  playbackKey: string,
  poiId: string,
  audioGuideId: string | null,
  text: string,
  languageCode: LanguageCode,
  voiceType: RegionVoice,
): PoiAudioSource => ({
  kind: "tts",
  playbackKey,
  poiId,
  audioGuideId,
  text,
  languageCode,
  voiceType,
});

export const usePoiNarrationPlayback = (state: AdminDataState) => {
  const [playbackState, setPlaybackState] = useState<PoiNarrationPlaybackState>(
    DEFAULT_PLAYBACK_STATE,
  );
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const utteranceRef = useRef<SpeechSynthesisUtterance | null>(null);
  const availableVoicesRef = useRef<SpeechSynthesisVoice[]>([]);
  const audioCacheRef = useRef<Map<string, PoiAudioSource>>(new Map());
  const currentPlaybackKeyRef = useRef<string | null>(null);
  const currentPoiIdRef = useRef<string | null>(null);
  const currentKindRef = useRef<PlaybackKind>(null);
  const currentAudioGuideIdRef = useRef<string | null>(null);
  const requestIdRef = useRef(0);
  const hasUnlockedPlaybackRef = useRef(false);
  const playbackStatusRef = useRef<PlaybackStatus>(DEFAULT_PLAYBACK_STATE.status);

  useEffect(() => {
    if (typeof window === "undefined" || !("speechSynthesis" in window)) {
      return;
    }

    const loadVoices = () => {
      availableVoicesRef.current = window.speechSynthesis.getVoices();
    };

    loadVoices();
    window.speechSynthesis.addEventListener("voiceschanged", loadVoices);

    return () => {
      window.speechSynthesis.removeEventListener("voiceschanged", loadVoices);
    };
  }, []);

  useEffect(() => {
    playbackStatusRef.current = playbackState.status;
  }, [playbackState.status]);

  const resetPlaybackRefs = useCallback(() => {
    currentPlaybackKeyRef.current = null;
    currentPoiIdRef.current = null;
    currentKindRef.current = null;
    currentAudioGuideIdRef.current = null;
    utteranceRef.current = null;
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

      if (audioRef.current) {
        audioRef.current.onplay = null;
        audioRef.current.onpause = null;
        audioRef.current.onended = null;
        audioRef.current.onerror = null;
        audioRef.current.pause();
        audioRef.current.currentTime = 0;
        audioRef.current = null;
      }

      if (typeof window !== "undefined" && "speechSynthesis" in window) {
        window.speechSynthesis.cancel();
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

    if ("speechSynthesis" in window) {
      window.speechSynthesis.resume();

      try {
        const warmupUtterance = new SpeechSynthesisUtterance(" ");
        warmupUtterance.volume = 0;
        window.speechSynthesis.speak(warmupUtterance);
        window.speechSynthesis.cancel();
      } catch {
        // Ignore warmup errors. Real playback still attempts normally.
      }
    }

    try {
      const probeAudio = new Audio(SILENT_AUDIO_DATA_URI);
      probeAudio.muted = true;
      void probeAudio.play().then(() => {
        probeAudio.pause();
        probeAudio.currentTime = 0;
      }).catch(() => undefined);
    } catch {
      // Ignore unlock probe errors. Real playback still attempts normally.
    }
  }, []);

  const getPOINarrationText = useCallback(
    (poi: Poi, language: LanguageCode, detail?: PoiDetail | null) => {
      const translation = detail
        ? findPoiTranslation(
            detail.translations,
            poi,
            language,
            state.settings.fallbackLanguage,
            state.settings.defaultLanguage,
          )
        : getPoiTranslation(state, poi.id, language);
      const title = translation?.title?.trim() || poi.slug;
      const narrationBody = (translation?.fullText || translation?.shortText || "").trim();

      if (!narrationBody) {
        return "";
      }

      return narrationBody.startsWith(title) ? narrationBody : `${title}. ${narrationBody}`;
    },
    [state],
  );

  const getPOIAudio = useCallback(
    async (poi: Poi, language: LanguageCode, voice: RegionVoice, detail?: PoiDetail | null) => {
      const playbackKey = buildPlaybackKey(poi.id, language, voice);
      const cached = audioCacheRef.current.get(playbackKey);
      if (cached) {
        return cached;
      }

      const audioGuide = findPoiAudioGuide(
        detail?.audioGuides ?? state.audioGuides,
        poi.id,
        language,
        voice,
      );
      const narrationText = getPOINarrationText(poi, language, detail);
      if (audioGuide && hasValidAudioUrl(audioGuide.audioUrl) && !isPlaceholderAudioUrl(audioGuide.audioUrl)) {
        const audioSource: PoiAudioSource = {
          kind: "audio",
          playbackKey,
          poiId: poi.id,
          audioGuideId: audioGuide.id,
          audioUrls: [audioGuide.audioUrl.trim()],
          fallbackText: narrationText || undefined,
          languageCode: language,
          voiceType: voice,
          generatedFromTts: audioGuide.sourceType === "tts",
        };

        audioCacheRef.current.set(playbackKey, audioSource);
        return audioSource;
      }

      if (!narrationText) {
        throw new Error("NO_POI_NARRATION_TEXT");
      }

      const ttsSource = createTtsAudioSource(
        playbackKey,
        poi.id,
        audioGuide?.id ?? null,
        narrationText,
        language,
        voice,
      );

      audioCacheRef.current.set(playbackKey, ttsSource);
      return ttsSource;
    },
    [getPOINarrationText, state.audioGuides],
  );

  const getCachedPOIAudio = useCallback(
    (poiId: string, language: LanguageCode, voice: RegionVoice) =>
      audioCacheRef.current.get(buildPlaybackKey(poiId, language, voice)) ?? null,
    [],
  );

  const playPOIAudio = useCallback(
    async (audioSource: PoiAudioSource, requestId = requestIdRef.current) => {
      if (requestId !== requestIdRef.current) {
        return;
      }

      currentPlaybackKeyRef.current = audioSource.playbackKey;
      currentPoiIdRef.current = audioSource.poiId;
      currentKindRef.current = audioSource.kind;
      currentAudioGuideIdRef.current = audioSource.audioGuideId;

      if (audioSource.kind === "audio") {
        const fallbackToTts = async () => {
          if (!audioSource.fallbackText || requestId !== requestIdRef.current) {
            return false;
          }

          const ttsSource = createBrowserTtsAudioSource(
            audioSource.playbackKey,
            audioSource.poiId,
            audioSource.audioGuideId,
            audioSource.fallbackText,
            audioSource.languageCode,
            audioSource.voiceType,
          );

          audioCacheRef.current.set(audioSource.playbackKey, ttsSource);
          setPlaybackState((current) => ({
            ...current,
            status: "loading",
            kind: "tts",
            message: "File audio không phát được, đang chuyển sang TTS...",
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
          if (segmentIndex >= audioSource.audioUrls.length || requestId !== requestIdRef.current) {
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
            playbackKey: audioSource.playbackKey,
            poiId: audioSource.poiId,
            audioGuideId: audioSource.audioGuideId,
            status: "playing",
            kind: "audio",
            message: audioSource.generatedFromTts
              ? "Đang phát thuyết minh tiếng Việt."
              : "Đang phát thuyết minh của địa điểm đã chọn.",
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
            message: "Đã tạm dừng thuyết minh.",
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
            message: "Đã phát xong thuyết minh.",
          });
        };

        nextAudio.onerror = () => {
          if (requestId !== requestIdRef.current) {
            return;
          }

          audioRef.current = null;
          resetPlaybackRefs();
          void (async () => {
            const handled = await fallbackToTts();
            if (handled || requestId !== requestIdRef.current) {
              return;
            }

            setPlaybackState({
              playbackKey: audioSource.playbackKey,
              poiId: audioSource.poiId,
              audioGuideId: audioSource.audioGuideId,
              status: "error",
              kind: "audio",
              message: "Không thể phát thuyết minh cho địa điểm này.",
              isLoadingPOI: false,
              isGeneratingTTS: false,
              isPlayingAudio: false,
            });
          })();
        };

        try {
          await playSegment(0);
        } catch {
          if (requestId !== requestIdRef.current) {
            return;
          }

          audioRef.current = null;
          resetPlaybackRefs();
          const handled = await fallbackToTts();
          if (handled || requestId !== requestIdRef.current) {
            return;
          }

          setPlaybackState({
            playbackKey: audioSource.playbackKey,
            poiId: audioSource.poiId,
            audioGuideId: audioSource.audioGuideId,
            status: "error",
            kind: "audio",
            message: "Không thể phát thuyết minh cho địa điểm này.",
            isLoadingPOI: false,
            isGeneratingTTS: false,
            isPlayingAudio: false,
          });
        }

        return;
      }

      if (typeof window === "undefined" || !("speechSynthesis" in window)) {
        resetPlaybackRefs();
        setPlaybackState({
          playbackKey: audioSource.playbackKey,
          poiId: audioSource.poiId,
          audioGuideId: audioSource.audioGuideId,
          status: "error",
          kind: "tts",
          message: "Trình duyệt hiện tại không hỗ trợ phát TTS.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
        return;
      }

      const utterance = new SpeechSynthesisUtterance(audioSource.text);
      utterance.lang = languageLocales[audioSource.languageCode];
      utterance.rate = 1;
      utterance.voice = selectVoice(
        availableVoicesRef.current,
        audioSource.languageCode,
        audioSource.voiceType,
      );
      utteranceRef.current = utterance;

      utterance.onstart = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPlaybackState({
          playbackKey: audioSource.playbackKey,
          poiId: audioSource.poiId,
          audioGuideId: audioSource.audioGuideId,
          status: "playing",
          kind: "tts",
          message: "Đang phát thuyết minh bằng TTS.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: true,
        });
      };

      utterance.onend = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        resetPlaybackRefs();
        setPlaybackState({
          ...DEFAULT_PLAYBACK_STATE,
          message: "Đã phát xong thuyết minh.",
        });
      };

      utterance.onerror = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        resetPlaybackRefs();
        setPlaybackState({
          playbackKey: audioSource.playbackKey,
          poiId: audioSource.poiId,
          audioGuideId: audioSource.audioGuideId,
          status: "error",
          kind: "tts",
          message: "Không thể phát thuyết minh cho địa điểm này.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      };

      window.speechSynthesis.cancel();
      window.speechSynthesis.speak(utterance);
    },
    [resetPlaybackRefs],
  );

  const toggleCurrentAudio = useCallback(
    async (playbackKey: string) => {
      if (currentPlaybackKeyRef.current !== playbackKey) {
        return false;
      }

      const playbackStatus = playbackStatusRef.current;

      // UX choice: clicking the same POI again pauses/resumes instead of restarting.
      // This keeps the narration position and feels more natural while the user explores the map.
      if (currentKindRef.current === "audio" && audioRef.current) {
        if (playbackStatus === "playing") {
          audioRef.current.pause();
          setPlaybackState((current) => ({
            ...current,
            status: "paused",
            message: "Đã tạm dừng thuyết minh.",
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
              message: "Không thể tiếp tục phát thuyết minh.",
              isLoadingPOI: false,
              isGeneratingTTS: false,
              isPlayingAudio: false,
            }));
            return true;
          }
        }
      }

      if (
        currentKindRef.current === "tts" &&
        typeof window !== "undefined" &&
        "speechSynthesis" in window
      ) {
        if (playbackStatus === "playing") {
          window.speechSynthesis.pause();
          setPlaybackState((current) => ({
            ...current,
            status: "paused",
            message: "Đã tạm dừng thuyết minh.",
            isPlayingAudio: false,
          }));
          return true;
        }

        if (playbackStatus === "paused") {
          window.speechSynthesis.resume();
          setPlaybackState((current) => ({
            ...current,
            status: "playing",
            message: "Đang tiếp tục phát thuyết minh.",
            isPlayingAudio: true,
          }));
          return true;
        }
      }

      return false;
    },
    [],
  );

  const playPoiNarration = useCallback(
    async ({ poi, language, voice, detail }: PlayPoiNarrationOptions & { detail?: PoiDetail | null }) => {
      const playbackKey = buildPlaybackKey(poi.id, language, voice);
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

      setPlaybackState({
        playbackKey,
        poiId: poi.id,
        audioGuideId: null,
        status: "loading",
        kind: null,
        message: "Đang tải thuyết minh...",
        isLoadingPOI: true,
        isGeneratingTTS: false,
        isPlayingAudio: false,
      });

      try {
        const cachedAudioSource = getCachedPOIAudio(poi.id, language, voice);
        if (cachedAudioSource) {
          setPlaybackState((current) => ({
            ...current,
            audioGuideId: cachedAudioSource.audioGuideId,
            isGeneratingTTS: false,
            message:
              cachedAudioSource.kind === "tts"
                ? "Đang phát thuyết minh đã lưu trong bộ nhớ..."
                : cachedAudioSource.generatedFromTts
                  ? "Đang phát thuyết minh MP3 đã lưu trong bộ nhớ..."
                : "Đang phát thuyết minh của địa điểm đã chọn.",
          }));

          await playPOIAudio(cachedAudioSource, requestId);
          return;
        }

        const audioSource = await getPOIAudio(poi, language, voice, detail);
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPlaybackState((current) => ({
          ...current,
          audioGuideId: audioSource.audioGuideId,
          isGeneratingTTS: false,
          message:
            audioSource.kind === "tts"
              ? "Đang tạo thuyết minh bằng TTS..."
              : audioSource.generatedFromTts
                ? "Đang chuẩn bị giọng đọc tiếng Việt..."
              : "Đang tải thuyết minh...",
        }));

        await playPOIAudio(audioSource, requestId);
      } catch (error) {
        if (requestId !== requestIdRef.current) {
          return;
        }

        const message =
          error instanceof Error && error.message === "NO_POI_NARRATION_TEXT"
            ? "POI này chưa có nội dung thuyết minh."
            : "Không thể phát thuyết minh cho địa điểm này.";

        resetPlaybackRefs();
        setPlaybackState({
          playbackKey,
          poiId: poi.id,
          audioGuideId: null,
          status: "error",
          kind: null,
          message,
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      }
    },
    [getCachedPOIAudio, getPOIAudio, playPOIAudio, resetPlaybackRefs, stopCurrentAudio, toggleCurrentAudio],
  );

  const previewAudioGuide = useCallback(
    async (guide: AudioPreviewCandidate) => {
      const playbackKey = guide.id;
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

      const narrationText =
        guide.previewText ?? getPOINarrationText({ id: guide.entityId, slug: "", address: "", lat: 0, lng: 0, categoryId: "", status: "draft", featured: false, defaultLanguageCode: guide.languageCode, district: "", ward: "", priceRange: "", averageVisitDuration: 0, popularityScore: 0, tags: [], ownerUserId: null, updatedBy: "", createdAt: "", updatedAt: "" }, guide.languageCode);

      try {
        const audioSource: PoiAudioSource =
          guide.sourceType === "uploaded" && hasValidAudioUrl(guide.audioUrl)
            ? {
                kind: "audio",
                playbackKey,
                poiId: guide.entityId,
                audioGuideId: guide.id,
                audioUrls: [guide.audioUrl.trim()],
                fallbackText: narrationText || undefined,
                languageCode: guide.languageCode,
                voiceType: guide.voiceType,
              }
            : narrationText
              ? createTtsAudioSource(
                  playbackKey,
                  guide.entityId,
                  guide.id,
                  narrationText,
                  guide.languageCode,
                  guide.voiceType,
                )
              : (() => {
                  throw new Error("NO_POI_NARRATION_TEXT");
                })();

        setPlaybackState({
          playbackKey,
          poiId: guide.entityId,
          audioGuideId: guide.id,
          status: "loading",
          kind: null,
          message: "Đang tải thuyết minh...",
          isLoadingPOI: true,
          isGeneratingTTS: audioSource.kind === "tts",
          isPlayingAudio: false,
        });

        await playPOIAudio(audioSource, requestId);
      } catch (error) {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPlaybackState({
          playbackKey,
          poiId: guide.entityId,
          audioGuideId: guide.id,
          status: "error",
          kind: guide.sourceType === "uploaded" ? "audio" : "tts",
          message:
            error instanceof Error && error.message === "NO_POI_NARRATION_TEXT"
              ? "Chưa có nội dung để đọc TTS cho POI và ngôn ngữ này."
              : "Không thể phát thuyết minh cho địa điểm này.",
          isLoadingPOI: false,
          isGeneratingTTS: false,
          isPlayingAudio: false,
        });
      }
    },
    [getPOINarrationText, playPOIAudio, stopCurrentAudio, toggleCurrentAudio],
  );

  return {
    playbackState,
    previewState: playbackState,
    previewAudioGuide,
    stopPreview: stopCurrentAudio,
    stopCurrentAudio,
    primePlayback,
    getPOINarrationText,
    getPOIAudio,
    getCachedPOIAudio,
    playPOIAudio,
    playPoiNarration,
    buildPlaybackKey,
  };
};
