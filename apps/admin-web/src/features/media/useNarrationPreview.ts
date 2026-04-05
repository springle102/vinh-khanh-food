import { useCallback, useEffect, useRef, useState } from "react";
import type { AdminDataState, AudioGuide } from "../../data/types";
import {
  buildGoogleTtsAudioUrls,
  hasValidAudioUrl,
  resolvePoiNarration,
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
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const requestIdRef = useRef(0);
  const resolveAbortRef = useRef<AbortController | null>(null);

  const resetAudioElement = useCallback(() => {
    audioRef.current?.pause();

    if (audioRef.current) {
      audioRef.current.currentTime = 0;
      audioRef.current.onplay = null;
      audioRef.current.onended = null;
      audioRef.current.onerror = null;
      audioRef.current = null;
    }
  }, []);

  const stopPreview = useCallback((message = "") => {
    requestIdRef.current += 1;
    resolveAbortRef.current?.abort();
    resolveAbortRef.current = null;

    resetAudioElement();

    setPreviewState({
      ...DEFAULT_PREVIEW_STATE,
      message,
    });
  }, [resetAudioElement]);

  useEffect(() => () => stopPreview(), [stopPreview]);

  const playAudioSequence = useCallback(
    async ({
      audioGuideId,
      audioUrls,
      kind,
      startMessage,
      requestId,
      onPlaybackError,
    }: {
      audioGuideId: string;
      audioUrls: string[];
      kind: Exclude<PreviewKind, null>;
      startMessage: string;
      requestId: number;
      onPlaybackError?: () => void | Promise<void>;
    }) => {
      const previewAudio = new Audio();
      previewAudio.preload = "auto";
      audioRef.current = previewAudio;
      let currentSegmentIndex = 0;

      const playSegment = async (segmentIndex: number) => {
        if (
          segmentIndex >= audioUrls.length ||
          requestId !== requestIdRef.current
        ) {
          return;
        }

        currentSegmentIndex = segmentIndex;
        previewAudio.src = audioUrls[segmentIndex];
        await previewAudio.play();
      };

      previewAudio.onplay = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPreviewState({
          audioGuideId,
          status: "playing",
          kind,
          message: startMessage,
        });
      };

      previewAudio.onended = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        if (currentSegmentIndex + 1 < audioUrls.length) {
          void playSegment(currentSegmentIndex + 1).catch(() => {
            previewAudio.onerror?.(new Event("error"));
          });
          return;
        }

        resetAudioElement();
        setPreviewState({
          ...DEFAULT_PREVIEW_STATE,
          message: kind === "tts" ? "Đã phát xong bản xem thử TTS." : "Đã phát xong audio.",
        });
      };

      previewAudio.onerror = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        resetAudioElement();

        if (onPlaybackError) {
          void onPlaybackError();
          return;
        }

        setPreviewState({
          audioGuideId,
          status: "error",
          kind,
          message:
            kind === "tts"
              ? "Không thể phát bản xem thử Google Translate TTS."
              : "Không thể phát file audio từ URL hiện tại.",
        });
      };

      await playSegment(0);
    },
    [resetAudioElement],
  );

  const playGoogleTts = useCallback(
    async ({
      audioGuideId,
      text,
      languageCode,
      fallbackMessage,
      requestId,
    }: {
      audioGuideId: string;
      text: string;
      languageCode: AudioGuide["languageCode"];
      fallbackMessage?: string | null;
      requestId: number;
    }) => {
      const audioUrls = buildGoogleTtsAudioUrls(text, languageCode);
      if (audioUrls.length === 0) {
        setPreviewState({
          audioGuideId,
          status: "error",
          kind: "tts",
          message: "Không có nội dung hợp lệ để tạo bản xem thử Google Translate TTS.",
        });
        return;
      }

      await playAudioSequence({
        audioGuideId,
        audioUrls,
        kind: "tts",
        requestId,
        startMessage: fallbackMessage
          ? `Đang phát Google Translate TTS. ${fallbackMessage}`
          : "Đang phát Google Translate TTS.",
      });
    },
    [playAudioSequence],
  );

  const previewAudioGuide = useCallback(
    async (guide: AudioPreviewCandidate) => {
      if (
        previewState.audioGuideId === guide.id &&
        previewState.status === "playing"
      ) {
        stopPreview("Đã dừng phát thử.");
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
      let resolvedAudioUrl = guide.sourceType === "uploaded" ? guide.audioUrl.trim() : "";

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
          resolvedAudioUrl = resolved.audioGuide?.audioUrl?.trim() ?? resolvedAudioUrl;
        } catch (error) {
          if (error instanceof DOMException && error.name === "AbortError") {
            return;
          }
        }
      }

      const playGoogleTtsAudio = async () => {
        if (!narrationText) {
          setPreviewState({
            audioGuideId: guide.id,
            status: "error",
            kind: "tts",
            message: "Chưa có nội dung để đọc TTS cho POI và ngôn ngữ này.",
          });
          return;
        }

        try {
          await playGoogleTts({
            audioGuideId: guide.id,
            text: narrationText,
            languageCode: effectiveLanguage,
            fallbackMessage,
            requestId,
          });
        } catch (error) {
          if (error instanceof DOMException && error.name === "AbortError") {
            return;
          }
        }
      };

      if (guide.sourceType === "uploaded" && hasValidAudioUrl(guide.audioUrl)) {
        try {
          await playAudioSequence({
            audioGuideId: guide.id,
            audioUrls: [guide.audioUrl],
            kind: "audio",
            requestId,
            startMessage: fallbackMessage
              ? `Đang phát file audio. ${fallbackMessage}`
              : "Đang phát file audio đã tải lên.",
            onPlaybackError: () => {
              void playGoogleTtsAudio();
            },
          });
          return;
        } catch {
          await playGoogleTtsAudio();
          return;
        }
      }

      if (!guide.previewText?.trim() && hasValidAudioUrl(resolvedAudioUrl)) {
        try {
          await playAudioSequence({
            audioGuideId: guide.id,
            audioUrls: [resolvedAudioUrl],
            kind: "audio",
            requestId,
            startMessage: fallbackMessage
              ? `Đang phát audio của POI. ${fallbackMessage}`
              : "Đang phát audio của POI.",
            onPlaybackError: () => {
              void playGoogleTtsAudio();
            },
          });
          return;
        } catch {
          await playGoogleTtsAudio();
          return;
        }
      }

      await playGoogleTtsAudio();
    },
    [
      playAudioSequence,
      playGoogleTts,
      previewState.audioGuideId,
      previewState.status,
      state,
      stopPreview,
    ],
  );

  return {
    previewState,
    previewAudioGuide,
    stopPreview,
  };
};
