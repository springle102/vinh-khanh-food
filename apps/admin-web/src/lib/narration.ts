import type {
  AudioGuide,
  LanguageCode,
  Poi,
} from "../data/types";
import { adminApi } from "./api";
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

export const supportedNarrationLanguages = Object.keys(languageLabels) as LanguageCode[];

const normalize = (value: string) => value.trim().toLowerCase();
const isPoiEntityType = (entityType: string) => entityType === "poi" || entityType === "place";
const runtimeTtsDisabledMessage =
  "Runtime TTS preview is disabled. Generate backend MP3 audio first.";

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

export const isPlayablePreparedAudioGuide = (audioGuide: AudioGuide | null | undefined) =>
  Boolean(
    audioGuide &&
      audioGuide.status === "ready" &&
      !audioGuide.isOutdated &&
      hasValidAudioUrl(audioGuide.audioUrl) &&
      !isPlaceholderAudioUrl(audioGuide.audioUrl) &&
      (audioGuide.sourceType === "uploaded" || audioGuide.generationStatus === "success"),
  );

export const buildUiPlaybackKey = (
  poiId: string,
  language: LanguageCode,
) => `${poiId}:${language}`;

export const selectSpeechVoice = (
  availableVoices: SpeechSynthesisVoice[],
  languageCode: LanguageCode,
) => {
  const locale = normalize(languageLocales[languageCode]);
  const localePrefix = locale.split("-")[0];
  const languageVoices = availableVoices.filter((voice) =>
    normalize(voice.lang).startsWith(localePrefix),
  );

  return (
    languageVoices.find((voice) => normalize(voice.lang) === locale) ??
    languageVoices[0] ??
    availableVoices[0] ??
    null
  );
};

export const canUseBrowserSpeechSynthesis = () => false;

export const loadBrowserSpeechVoices = async () => [] as SpeechSynthesisVoice[];

export const findPoiAudioGuide = (
  audioGuides: AudioGuide[],
  poiId: string,
  languageCode: LanguageCode,
) => {
  const matchingGuides = audioGuides.filter(
    (item) =>
      isPoiEntityType(item.entityType) &&
      item.entityId === poiId &&
      item.languageCode === languageCode,
  );

  return (
    matchingGuides.find(isPlayablePreparedAudioGuide) ??
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

export const buildTtsAudioUrls = (
  _text: string,
  _languageCode: LanguageCode,
) => [];

export type TtsPlaybackQueue = {
  readonly segmentCount: number;
  getSegmentUrl: (index: number) => Promise<string>;
  prefetch: (index: number) => void;
  dispose: () => void;
};

export const createTtsPlaybackQueue = (_urls: string[]): TtsPlaybackQueue => ({
  segmentCount: 0,
  getSegmentUrl: async () => {
    throw new Error(runtimeTtsDisabledMessage);
  },
  prefetch: () => undefined,
  dispose: () => undefined,
});

export const fetchTtsPlaybackUrls = async (urls: string[]) => ({
  audioUrls: [] as string[],
  dispose: () => undefined,
  error: urls.length > 0 ? runtimeTtsDisabledMessage : null,
});

export const resolvePoiNarration = async ({
  poi,
  language,
  signal,
}: {
  poi: Poi;
  language: LanguageCode;
  signal?: AbortSignal;
}) => {
  const resolved = await adminApi.getPoiNarration(
    poi.id,
    language,
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
    cacheKey: resolved.audioCacheKey,
    translationStatus: resolved.translationStatus,
    fallbackMessage: resolved.fallbackMessage,
  });

  return resolved;
};
