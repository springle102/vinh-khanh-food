import { useCallback, useEffect, useRef, useState } from "react";
import type { AdminDataState, AudioGuide } from "../../data/types";
import {
  hasValidAudioUrl,
  resolvePoiNarration,
} from "../../lib/narration";

type PreviewStatus = "idle" | "playing" | "error";
type PreviewKind = "audio" | null;

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

const missingAudioMessage =
  "Chưa có audio tạo sẵn để nghe thử. Hãy tạo audio ở backend trước.";

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
      audioRef.current.removeAttribute("src");
      audioRef.current.load();
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

  const playAudioUrl = useCallback(
    async ({
      audioGuideId,
      audioUrl,
      requestId,
      startMessage,
    }: {
      audioGuideId: string;
      audioUrl: string;
      requestId: number;
      startMessage: string;
    }) => {
      const previewAudio = new Audio();
      previewAudio.preload = "auto";
      audioRef.current = previewAudio;

      previewAudio.onplay = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        setPreviewState({
          audioGuideId,
          status: "playing",
          kind: "audio",
          message: startMessage,
        });
      };

      previewAudio.onended = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        resetAudioElement();
        setPreviewState({
          ...DEFAULT_PREVIEW_STATE,
          message: "Đã phát xong audio.",
        });
      };

      previewAudio.onerror = () => {
        if (requestId !== requestIdRef.current) {
          return;
        }

        resetAudioElement();
        setPreviewState({
          audioGuideId,
          status: "error",
          kind: "audio",
          message: "Không thể phát file audio từ URL hiện tại.",
        });
      };

      previewAudio.src = audioUrl;
      await previewAudio.play();
    },
    [resetAudioElement],
  );

  const previewAudioGuide = useCallback(
    async (guide: AudioPreviewCandidate) => {
      if (
        previewState.audioGuideId === guide.id &&
        previewState.status === "playing"
      ) {
        stopPreview("Đã dừng nghe thử.");
        return;
      }

      stopPreview();

      const requestId = requestIdRef.current;
      const poi = state.pois.find((item) => item.id === guide.entityId);
      const controller = new AbortController();
      resolveAbortRef.current = controller;

      let resolvedAudioUrl = hasValidAudioUrl(guide.audioUrl) ? guide.audioUrl.trim() : "";
      let fallbackMessage: string | null = null;

      if (!resolvedAudioUrl && poi) {
        try {
          const resolved = await resolvePoiNarration({
            poi,
            language: guide.languageCode,
            signal: controller.signal,
          });

          if (requestId !== requestIdRef.current) {
            return;
          }

          fallbackMessage = resolved.fallbackMessage;
          resolvedAudioUrl =
            hasValidAudioUrl(resolved.audioGuide?.audioUrl)
              ? resolved.audioGuide?.audioUrl?.trim() ?? ""
              : "";
        } catch (error) {
          if (error instanceof DOMException && error.name === "AbortError") {
            return;
          }
        }
      }

      if (!resolvedAudioUrl) {
        setPreviewState({
          audioGuideId: guide.id,
          status: "error",
          kind: null,
          message: missingAudioMessage,
        });
        return;
      }

      try {
        await playAudioUrl({
          audioGuideId: guide.id,
          audioUrl: resolvedAudioUrl,
          requestId,
          startMessage: fallbackMessage
            ? `Đang phát audio tạo sẵn. ${fallbackMessage}`
            : "Đang phát audio tạo sẵn.",
        });
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") {
          return;
        }

        setPreviewState({
          audioGuideId: guide.id,
          status: "error",
          kind: "audio",
          message: "Không thể phát file audio từ URL hiện tại.",
        });
      }
    },
    [
      playAudioUrl,
      previewState.audioGuideId,
      previewState.status,
      state.pois,
      stopPreview,
    ],
  );

  return {
    previewState,
    previewAudioGuide,
    stopPreview,
  };
};
