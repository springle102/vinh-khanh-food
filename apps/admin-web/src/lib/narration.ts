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
    });

    return resolveApiUrl(`/api/v1/tts?${query.toString()}`);
  });
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
        cache: "no-store",
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
