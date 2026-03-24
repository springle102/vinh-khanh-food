import { useCallback, useEffect, useRef, useState } from "react";
import type { AdminDataState, AudioGuide } from "../../data/types";
import { getPoiTranslation } from "../../lib/selectors";

type PreviewStatus = "idle" | "playing" | "error";
type PreviewKind = "audio" | "tts" | null;

type PreviewState = {
  audioGuideId: string | null;
  status: PreviewStatus;
  kind: PreviewKind;
  message: string;
};

type AudioPreviewCandidate = Pick<
  AudioGuide,
  "id" | "entityId" | "languageCode" | "audioUrl" | "sourceType"
> & {
  previewText?: string;
};

const DEFAULT_PREVIEW_STATE: PreviewState = {
  audioGuideId: null,
  status: "idle",
  kind: null,
  message: "",
};

const languageLocales: Record<AudioGuide["languageCode"], string> = {
  vi: "vi-VN",
  en: "en-US",
  "zh-CN": "zh-CN",
  ko: "ko-KR",
  ja: "ja-JP",
};

const normalize = (value: string) => value.trim().toLowerCase();

const resolveNarrationText = (
  state: AdminDataState,
  entityId: string,
  languageCode: AudioGuide["languageCode"],
) => {
  const translation = getPoiTranslation(state, entityId, languageCode);
  const title = translation?.title?.trim() || state.pois.find((poi) => poi.id === entityId)?.slug || "";
  const narrationBody = (translation?.fullText || translation?.shortText || "").trim();

  if (!title && !narrationBody) {
    return "";
  }

  if (!narrationBody) {
    return title;
  }

  return narrationBody.startsWith(title) ? narrationBody : `${title}. ${narrationBody}`;
};

const selectVoice = (
  availableVoices: SpeechSynthesisVoice[],
  languageCode: AudioGuide["languageCode"],
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

export const useNarrationPreview = (state: AdminDataState) => {
  const [previewState, setPreviewState] = useState<PreviewState>(DEFAULT_PREVIEW_STATE);
  const availableVoicesRef = useRef<SpeechSynthesisVoice[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);

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

  const stopPreview = useCallback((message = "") => {
    audioRef.current?.pause();

    if (audioRef.current) {
      audioRef.current.currentTime = 0;
      audioRef.current = null;
    }

    if (typeof window !== "undefined" && "speechSynthesis" in window) {
      window.speechSynthesis.cancel();
    }

    setPreviewState({
      ...DEFAULT_PREVIEW_STATE,
      message,
    });
  }, []);

  useEffect(() => () => stopPreview(), [stopPreview]);

  const previewAudioGuide = useCallback(
    async (guide: AudioPreviewCandidate) => {
      if (
        previewState.audioGuideId === guide.id &&
        previewState.status === "playing"
      ) {
        stopPreview("Đã dừng phát thử");
        return;
      }

      stopPreview();

      const narrationText =
        guide.previewText ??
        resolveNarrationText(state, guide.entityId, guide.languageCode);
      const shouldUseUploadedAudio = guide.sourceType === "uploaded" && Boolean(guide.audioUrl);

      if (shouldUseUploadedAudio) {
        try {
          const previewAudio = new Audio(guide.audioUrl);
          audioRef.current = previewAudio;

          previewAudio.onplay = () => {
            setPreviewState({
              audioGuideId: guide.id,
              status: "playing",
              kind: "audio",
              message: "Đang phát file audio đã upload.",
            });
          };

          previewAudio.onended = () => {
            setPreviewState({
              ...DEFAULT_PREVIEW_STATE,
              message: "Đã phát xong audio.",
            });
          };

          previewAudio.onerror = () => {
            setPreviewState({
              audioGuideId: guide.id,
              status: "error",
              kind: "audio",
              message: "Không thể phát file audio từ URL hiện tại.",
            });
          };

          await previewAudio.play();
          return;
        } catch {
          setPreviewState({
            audioGuideId: guide.id,
            status: "error",
            kind: "audio",
            message: "",
          });
          return;
        }
      }

      if (!narrationText) {
        setPreviewState({
          audioGuideId: guide.id,
          status: "error",
          kind: "tts",
          message: "Chưa có nội dung để đọc TTS cho POI và ngôn ngữ này.",
        });
        return;
      }

      if (typeof window === "undefined" || !("speechSynthesis" in window)) {
        setPreviewState({
          audioGuideId: guide.id,
          status: "error",
          kind: "tts",
          message: "Trình duyệt hiện tại không hỗ trợ Text-to-Speech preview.",
        });
        return;
      }

      const utterance = new SpeechSynthesisUtterance(narrationText);
      utterance.lang = languageLocales[guide.languageCode];
      utterance.rate = 1;

      const selectedVoice = selectVoice(availableVoicesRef.current, guide.languageCode);

      if (selectedVoice) {
        utterance.voice = selectedVoice;
      }

      utterance.onstart = () => {
        setPreviewState({
          audioGuideId: guide.id,
          status: "playing",
          kind: "tts",
          message:
            guide.sourceType === "uploaded"
              ? "Không có file audio, đang dùng TTS fallback để nghe thử."
              : "Đang đọc nội dung bằng Text-to-Speech.",
        });
      };

      utterance.onend = () => {
        setPreviewState({
          ...DEFAULT_PREVIEW_STATE,
          message: "Đã phát xong TTS preview.",
        });
      };

      utterance.onerror = () => {
        setPreviewState({
          audioGuideId: guide.id,
          status: "error",
          kind: "tts",
          message: "Không thể khởi động TTS preview trên trình duyệt này.",
        });
      };

      window.speechSynthesis.cancel();
      window.speechSynthesis.speak(utterance);
    },
    [previewState.audioGuideId, previewState.status, state, stopPreview],
  );

  return {
    previewState,
    previewAudioGuide,
    stopPreview,
    getNarrationText: (entityId: string, languageCode: AudioGuide["languageCode"]) =>
      resolveNarrationText(state, entityId, languageCode),
  };
};
