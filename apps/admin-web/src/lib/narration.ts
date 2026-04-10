import type {
  AdminDataState,
  AudioGuide,
  LanguageCode,
  Poi,
  PoiDetail,
  RegionVoice,
} from "../data/types";
import { adminApi, resolveApiUrl } from "./api";
import { languageLabels } from "./utils";

export type {
  NarrationResolutionStatus,
  ResolvedPoiNarration,
} from "../data/types";

export const languageLocales: Record<LanguageCode, string> = {
  vi: "vi-VN",
  en: "en-US",
  "zh-CN": "zh-CN",
  ko: "ko-KR",
  ja: "ja-JP",
};

const TTS_PROXY_MAX_CHARS = 180;
const TTS_PROXY_MODEL_ID = "eleven_flash_v2_5";

export const supportedNarrationLanguages = Object.keys(languageLabels) as LanguageCode[];

const voiceKeywords: Record<RegionVoice, string[]> = {
  north: ["bac", "north"],
  central: ["trung", "central"],
  south: ["nam", "south"],
  standard: ["standard", "default"],
};

const normalize = (value: string) => value.trim().toLowerCase();

export const hasValidAudioUrl = (value: string | null | undefined) => Boolean(value?.trim());

export const isPlaceholderAudioUrl = (value: string | null | undefined) => {
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

export const buildUiPlaybackKey = (
  poiId: string,
  language: LanguageCode,
  voice: RegionVoice,
) => `${poiId}:${language}:${voice}`;

export const selectSpeechVoice = (
  availableVoices: SpeechSynthesisVoice[],
  languageCode: LanguageCode,
  voiceType: RegionVoice,
) => {
  const locale = normalize(languageLocales[languageCode]);
  const localePrefix = locale.split("-")[0];
  const languageVoices = availableVoices.filter((voice) =>
    normalize(voice.lang).startsWith(localePrefix),
  );
  const keywords = voiceKeywords[voiceType];
  const findByKeyword = (voices: SpeechSynthesisVoice[]) =>
    voices.find((voice) =>
      keywords.some((keyword) => normalize(voice.name).includes(keyword)),
    );

  return (
    findByKeyword(languageVoices.filter((voice) => normalize(voice.lang) === locale)) ??
    findByKeyword(languageVoices) ??
    languageVoices.find((voice) => normalize(voice.lang) === locale) ??
    languageVoices[0] ??
    availableVoices[0] ??
    null
  );
};

export const canUseBrowserSpeechSynthesis = () =>
  typeof window !== "undefined" &&
  "speechSynthesis" in window &&
  typeof SpeechSynthesisUtterance !== "undefined";

export const loadBrowserSpeechVoices = async () => {
  if (!canUseBrowserSpeechSynthesis()) {
    return [] as SpeechSynthesisVoice[];
  }

  const speechSynthesis = window.speechSynthesis;
  const readyVoices = speechSynthesis.getVoices();
  if (readyVoices.length > 0) {
    return readyVoices;
  }

  return new Promise<SpeechSynthesisVoice[]>((resolve) => {
    let settled = false;
    const settle = () => {
      if (settled) {
        return;
      }

      settled = true;
      window.clearTimeout(timeoutId);
      speechSynthesis.removeEventListener("voiceschanged", settle);
      resolve(speechSynthesis.getVoices());
    };
    const timeoutId = window.setTimeout(settle, 600);
    speechSynthesis.addEventListener("voiceschanged", settle);
  });
};

export const findPoiAudioGuide = (
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
    matchingGuides.find(
      (item) => item.voiceType === voiceType && hasValidAudioUrl(item.audioUrl),
    ) ??
    matchingGuides.find((item) => hasValidAudioUrl(item.audioUrl)) ??
    matchingGuides.find((item) => item.voiceType === voiceType) ??
    matchingGuides[0] ??
    null
  );
};

export const logNarrationDebug = (
  scope: string,
  payload: Record<string, unknown>,
) => {
  if (typeof console === "undefined" || typeof console.debug !== "function") {
    return;
  }

  console.debug(`[poi-narration:${scope}]`, payload);
};

const splitNarrationIntoChunks = (text: string, maxLength = TTS_PROXY_MAX_CHARS) => {
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

    const chunk = text.slice(start, end).trim();
    if (chunk) {
      chunks.push(chunk);
    }

    start = end;
  }

  return chunks;
};

export const buildTtsAudioUrls = (
  text: string,
  languageCode: LanguageCode,
) => {
  const normalizedText = text.trim();
  if (!normalizedText) {
    return [];
  }

  const chunks = splitNarrationIntoChunks(normalizedText);
  return chunks.map((chunk, index) => {
    const query = new URLSearchParams({
      languageCode,
      text: chunk,
      total: chunks.length.toString(),
      idx: index.toString(),
      model_id: TTS_PROXY_MODEL_ID,
    });

    return resolveApiUrl(`/api/v1/tts?${query.toString()}`);
  });
};

type TtsPlaybackFetchResult =
  | { audioUrl: string; error: null }
  | { audioUrl: null; error: string };

const fetchTtsPlaybackUrl = async (
  url: string,
  signal?: AbortSignal,
): Promise<TtsPlaybackFetchResult> => {
  const response = await fetch(url, {
    method: "GET",
    cache: "default",
    signal,
  });
  const contentType = response.headers.get("content-type") ?? "";

  if (!response.ok) {
    if (contentType.includes("application/json")) {
      try {
        const payload = (await response.json()) as {
          success?: boolean;
          message?: string | null;
        };

        return {
          audioUrl: null,
          error: payload.message?.trim() || `TTS request failed (${response.status}).`,
        };
      } catch {
        return {
          audioUrl: null,
          error: `TTS request failed (${response.status}).`,
        };
      }
    }

    return {
      audioUrl: null,
      error: `TTS request failed (${response.status}).`,
    };
  }

  if (!contentType.includes("audio")) {
    return {
      audioUrl: null,
      error: "Backend khong tra ve audio TTS hop le.",
    };
  }

  const blob = await response.blob();
  return {
    audioUrl: URL.createObjectURL(blob),
    error: null,
  };
};

export type TtsPlaybackQueue = {
  readonly segmentCount: number;
  getSegmentUrl: (index: number) => Promise<string>;
  prefetch: (index: number) => void;
  dispose: () => void;
};

export const createTtsPlaybackQueue = (urls: string[]): TtsPlaybackQueue => {
  const controller = new AbortController();
  const objectUrls: string[] = [];
  const readyUrls = new Map<number, string>();
  const pendingUrls = new Map<number, Promise<string>>();
  let disposed = false;

  const getSegmentUrl = async (index: number) => {
    if (disposed) {
      throw new DOMException("TTS playback was stopped.", "AbortError");
    }

    if (index < 0 || index >= urls.length) {
      throw new Error("TTS segment index is out of range.");
    }

    const readyUrl = readyUrls.get(index);
    if (readyUrl) {
      return readyUrl;
    }

    const pendingUrl = pendingUrls.get(index);
    if (pendingUrl) {
      return pendingUrl;
    }

    const nextUrl = fetchTtsPlaybackUrl(urls[index], controller.signal)
      .then((result) => {
        if (result.error || !result.audioUrl) {
          throw new Error(result.error || "Unable to prepare TTS audio.");
        }

        if (disposed) {
          URL.revokeObjectURL(result.audioUrl);
          throw new DOMException("TTS playback was stopped.", "AbortError");
        }

        objectUrls.push(result.audioUrl);
        readyUrls.set(index, result.audioUrl);
        return result.audioUrl;
      })
      .finally(() => {
        pendingUrls.delete(index);
      });

    pendingUrls.set(index, nextUrl);
    return nextUrl;
  };

  return {
    segmentCount: urls.length,
    getSegmentUrl,
    prefetch: (index: number) => {
      if (index < 0 || index >= urls.length || disposed) {
        return;
      }

      void getSegmentUrl(index).catch(() => undefined);
    },
    dispose: () => {
      disposed = true;
      controller.abort();
      objectUrls.forEach((value) => URL.revokeObjectURL(value));
      objectUrls.length = 0;
      readyUrls.clear();
      pendingUrls.clear();
    },
  };
};

export const fetchTtsPlaybackUrls = async (urls: string[]) => {
  const objectUrls: string[] = [];
  const dispose = () => {
    objectUrls.forEach((value) => URL.revokeObjectURL(value));
    objectUrls.length = 0;
  };

  try {
    const playbackUrls: string[] = [];

    for (const url of urls) {
      const response = await fetch(url, {
        method: "GET",
        cache: "default",
      });
      const contentType = response.headers.get("content-type") ?? "";

      if (!response.ok) {
        if (contentType.includes("application/json")) {
          try {
            const payload = (await response.json()) as {
              success?: boolean;
              message?: string | null;
            };

            dispose();
            return {
              audioUrls: [],
              dispose,
              error: payload.message?.trim() || `TTS request failed (${response.status}).`,
            };
          } catch {
            dispose();
            return {
              audioUrls: [],
              dispose,
              error: `TTS request failed (${response.status}).`,
            };
          }
        }

        dispose();
        return {
          audioUrls: [],
          dispose,
          error: `TTS request failed (${response.status}).`,
        };
      }

      if (!contentType.includes("audio")) {
        dispose();
        return {
          audioUrls: [],
          dispose,
          error: "Backend không trả về audio TTS hợp lệ.",
        };
      }

      const blob = await response.blob();
      const objectUrl = URL.createObjectURL(blob);
      objectUrls.push(objectUrl);
      playbackUrls.push(objectUrl);
    }

    return {
      audioUrls: playbackUrls,
      dispose,
      error: null,
    };
  } catch {
    dispose();
    return {
      audioUrls: [],
      dispose,
      error: "Không thể kết nối backend để tạo audio ElevenLabs TTS.",
    };
  }
};

export const resolvePoiNarration = async ({
  poi,
  language,
  voice,
  signal,
}: {
  state: AdminDataState;
  poi: Poi;
  language: LanguageCode;
  voice: RegionVoice;
  detail?: PoiDetail | null;
  signal?: AbortSignal;
}) => {
  const resolved = await adminApi.getPoiNarration(
    poi.id,
    language,
    voice,
    signal,
  );

  logNarrationDebug("resolve", {
    poiId: resolved.poiId,
    languageSelected: resolved.requestedLanguageCode,
    sourceLanguage: resolved.sourceLanguageCode,
    effectiveLanguage: resolved.effectiveLanguageCode,
    sourceText: resolved.sourceText,
    translatedText: resolved.translatedText,
    ttsInputText: resolved.ttsInputText,
    selectedVoice: resolved.selectedVoice,
    cacheKey: resolved.audioCacheKey,
    translationStatus: resolved.translationStatus,
    fallbackMessage: resolved.fallbackMessage,
  });

  return resolved;
};
