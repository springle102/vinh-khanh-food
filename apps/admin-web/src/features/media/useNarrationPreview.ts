import { useCallback, useEffect, useRef, useState } from "react";
import type { AdminDataState, AudioGuide } from "../../data/types";
import {
  hasValidAudioUrl,
  languageLocales,
  logNarrationDebug,
  resolvePoiNarration,
  selectSpeechVoice,
} from "../../lib/narration";

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
  "id" | "entityId" | "languageCode" | "audioUrl" | "sourceType" | "voiceType"
> & {
  previewText?: string;
};

const DEFAULT_PREVIEW_STATE: PreviewState = {
  audioGuideId: null,
  status: "idle",
  kind: null,
  message: "",
};

export const useNarrationPreview = (state: AdminDataState) => {
  const [previewState, setPreviewState] = useState<PreviewState>(DEFAULT_PREVIEW_STATE);
  const availableVoicesRef = useRef<SpeechSynthesisVoice[]>([]);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const requestIdRef = useRef(0);
  const resolveAbortRef = useRef<AbortController | null>(null);

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
    requestIdRef.current += 1;
    resolveAbortRef.current?.abort();
    resolveAbortRef.current = null;

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

  const playBrowserTts = useCallback(
    async ({
      audioGuideId,
      text,
      languageCode,
      voiceType,
      fallbackMessage,
      requestId,
    }: {
      audioGuideId: string;
      text: string;
      languageCode: AudioGuide["languageCode"];
      voiceType: AudioGuide["voiceType"];
      fallbackMessage?: string | null;
      requestId: number;
    }) => {
      if (typeof window === "undefined" || !("speechSynthesis" in window)) {
        setPreviewState({
          audioGuideId,
          status: "error",
          kind: "tts",
          message: "Trinh duyet hien tai khong ho tro Text-to-Speech preview.",
        });
        return;
      }

      const utterance = new SpeechSynthesisUtterance(text);
      utterance.lang = languageLocales[languageCode];

      const selectedVoice = selectSpeechVoice(
        availableVoicesRef.current,
        languageCode,
        voiceType,
      );
      if (selectedVoice) {
        utterance.voice = selectedVoice;
      }

      logNarrationDebug("preview-voice", {
        audioGuideId,
        languageSelected: languageCode,
        selectedVoicePreference: voiceType,
        selectedVoice: selectedVoice?.name ?? null,
      });

      utterance.onstart = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPreviewState({
          audioGuideId,
          status: "playing",
          kind: "tts",
          message: fallbackMessage
            ? `Dang doc bang TTS. ${fallbackMessage}`
            : "Dang doc noi dung bang Text-to-Speech.",
        });
      };

      utterance.onend = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPreviewState({
          ...DEFAULT_PREVIEW_STATE,
          message: "Da phat xong TTS preview.",
        });
      };

      utterance.onerror = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPreviewState({
          audioGuideId,
          status: "error",
          kind: "tts",
          message: "Khong the khoi dong TTS preview tren trinh duyet nay.",
        });
      };

      window.speechSynthesis.cancel();
      window.speechSynthesis.speak(utterance);
    },
    [],
  );

  const previewAudioGuide = useCallback(
    async (guide: AudioPreviewCandidate) => {
      if (
        previewState.audioGuideId === guide.id &&
        previewState.status === "playing"
      ) {
        stopPreview("Da dung phat thu.");
        return;
      }

      stopPreview();

      const requestId = requestIdRef.current;
      const poi = state.pois.find((item) => item.id === guide.entityId);
      const controller = new AbortController();
      resolveAbortRef.current = controller;

      let narrationText = guide.previewText?.trim() ?? "";
      let effectiveLanguage = guide.languageCode;
      let fallbackMessage: string | null = null;

      if (!narrationText && poi) {
        try {
          const resolved = await resolvePoiNarration({
            state,
            poi,
            language: guide.languageCode,
            voice: guide.voiceType,
            signal: controller.signal,
          });

          if (requestId !== requestIdRef.current) {
            return;
          }

          narrationText = resolved.ttsInputText;
          effectiveLanguage = resolved.effectiveLanguageCode;
          fallbackMessage = resolved.fallbackMessage;
        } catch (error) {
          if (error instanceof DOMException && error.name === "AbortError") {
            return;
          }
        }
      }

      if (guide.sourceType === "uploaded" && hasValidAudioUrl(guide.audioUrl)) {
        try {
          const previewAudio = new Audio(guide.audioUrl);
          audioRef.current = previewAudio;

          previewAudio.onplay = () => {
            if (requestId !== requestIdRef.current) {
              return;
            }

            setPreviewState({
              audioGuideId: guide.id,
              status: "playing",
              kind: "audio",
              message: fallbackMessage
                ? `Dang phat file audio. ${fallbackMessage}`
                : "Dang phat file audio da upload.",
            });
          };

          previewAudio.onended = () => {
            if (requestId !== requestIdRef.current) {
              return;
            }

            setPreviewState({
              ...DEFAULT_PREVIEW_STATE,
              message: "Da phat xong audio.",
            });
          };

          previewAudio.onerror = () => {
            if (!narrationText) {
              setPreviewState({
                audioGuideId: guide.id,
                status: "error",
                kind: "audio",
                message: "Khong the phat file audio tu URL hien tai.",
              });
              return;
            }

            void playBrowserTts({
              audioGuideId: guide.id,
              text: narrationText,
              languageCode: effectiveLanguage,
              voiceType: guide.voiceType,
              fallbackMessage: "File audio loi, dang fallback sang TTS.",
              requestId,
            });
          };

          await previewAudio.play();
          return;
        } catch {
          if (!narrationText) {
            setPreviewState({
              audioGuideId: guide.id,
              status: "error",
              kind: "audio",
              message: "Khong the phat file audio tu URL hien tai.",
            });
            return;
          }
        }
      }

      if (!narrationText) {
        setPreviewState({
          audioGuideId: guide.id,
          status: "error",
          kind: "tts",
          message: "Chua co noi dung de doc TTS cho POI va ngon ngu nay.",
        });
        return;
      }

      await playBrowserTts({
        audioGuideId: guide.id,
        text: narrationText,
        languageCode: effectiveLanguage,
        voiceType: guide.voiceType,
        fallbackMessage,
        requestId,
      });
    },
    [playBrowserTts, previewState.audioGuideId, previewState.status, state, stopPreview],
  );

  return {
    previewState,
    previewAudioGuide,
    stopPreview,
  };
};
