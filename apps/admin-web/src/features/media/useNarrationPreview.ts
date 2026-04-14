import { useCallback, useEffect, useRef, useState } from "react";
import type { AdminDataState, AudioGuide } from "../../data/types";
import {
  buildTtsAudioUrls,
  canUseBrowserSpeechSynthesis,
  createTtsPlaybackQueue,
  hasValidAudioUrl,
  languageLocales,
  loadBrowserSpeechVoices,
  resolvePoiNarration,
  selectSpeechVoice,
  type TtsPlaybackQueue,
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

export const useNarrationPreview = (state: AdminDataState) => {
  const [previewState, setPreviewState] = useState<PreviewState>(DEFAULT_PREVIEW_STATE);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const requestIdRef = useRef(0);
  const resolveAbortRef = useRef<AbortController | null>(null);
  const ttsPlaybackQueueRef = useRef<TtsPlaybackQueue | null>(null);
  const browserSpeechCancelRef = useRef<(() => void) | null>(null);

  const disposeTtsPlaybackQueue = useCallback(() => {
    ttsPlaybackQueueRef.current?.dispose();
    ttsPlaybackQueueRef.current = null;
  }, []);

  const stopBrowserSpeech = useCallback(() => {
    browserSpeechCancelRef.current?.();
    browserSpeechCancelRef.current = null;
  }, []);

  const resetAudioElement = useCallback(() => {
    disposeTtsPlaybackQueue();
    stopBrowserSpeech();
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
  }, [disposeTtsPlaybackQueue, stopBrowserSpeech]);

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
      const playbackQueue = kind === "tts" && audioUrls.length > 0
        ? createTtsPlaybackQueue(audioUrls)
        : null;
      if (playbackQueue) {
        disposeTtsPlaybackQueue();
        ttsPlaybackQueueRef.current = playbackQueue;
      }

      const releasePlaybackQueue = () => {
        if (ttsPlaybackQueueRef.current === playbackQueue) {
          ttsPlaybackQueueRef.current = null;
        }

        playbackQueue?.dispose();
      };

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
        const segmentUrl = playbackQueue
          ? await playbackQueue.getSegmentUrl(segmentIndex)
          : audioUrls[segmentIndex];
        if (requestId !== requestIdRef.current) {
          releasePlaybackQueue();
          return;
        }

        previewAudio.src = segmentUrl;
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
        playbackQueue?.prefetch(currentSegmentIndex + 1);
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

        releasePlaybackQueue();
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

        releasePlaybackQueue();
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
              ? "Không thể phát bản xem thử ElevenLabs TTS."
              : "Không thể phát file audio từ URL hiện tại.",
        });
      };

      await playSegment(0);
    },
    [disposeTtsPlaybackQueue, resetAudioElement],
  );

  const playBrowserSpeechPreview = useCallback(
    async ({
      audioGuideId,
      text,
      languageCode,
      requestId,
      fallbackMessage,
    }: {
      audioGuideId: string;
      text: string;
      languageCode: AudioGuide["languageCode"];
      requestId: number;
      fallbackMessage?: string | null;
    }) => {
      const narrationText = text.trim();
      if (!narrationText || !canUseBrowserSpeechSynthesis()) {
        return false;
      }

      setPreviewState({
        audioGuideId,
        status: "playing",
        kind: "tts",
        message: fallbackMessage
          ? `Đang phát bằng giọng đọc của trình duyệt. ${fallbackMessage}`
          : "Đang phát bằng giọng đọc của trình duyệt.",
      });

      const voices = await loadBrowserSpeechVoices();
      if (requestId !== requestIdRef.current) {
        return true;
      }

      const speechSynthesis = window.speechSynthesis;
      stopBrowserSpeech();

      const utterance = new SpeechSynthesisUtterance(narrationText);
      utterance.lang = languageLocales[languageCode];
      utterance.voice = selectSpeechVoice(voices, languageCode);

      let cancelled = false;
      browserSpeechCancelRef.current = () => {
        cancelled = true;
        speechSynthesis.cancel();
      };

      utterance.onend = () => {
        if (requestId !== requestIdRef.current || cancelled) {
          return;
        }

        browserSpeechCancelRef.current = null;
        setPreviewState({
          ...DEFAULT_PREVIEW_STATE,
          message: "Đã phát xong bản xem thử TTS.",
        });
      };

      utterance.onerror = () => {
        if (requestId !== requestIdRef.current || cancelled) {
          return;
        }

        browserSpeechCancelRef.current = null;
        setPreviewState({
          audioGuideId,
          status: "error",
          kind: "tts",
          message: "Không thể phát TTS bằng ElevenLabs hoặc giọng đọc của trình duyệt.",
        });
      };

      speechSynthesis.cancel();
      speechSynthesis.speak(utterance);
      return true;
    },
    [stopBrowserSpeech],
  );

  const playTtsPreview = useCallback(
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
      const audioUrls = buildTtsAudioUrls(text, languageCode);
      if (audioUrls.length === 0) {
        setPreviewState({
          audioGuideId,
          status: "error",
          kind: "tts",
          message: "Không có nội dung hợp lệ để tạo bản xem thử ElevenLabs TTS.",
        });
        return;
      }

      const fallbackToBrowserSpeech = async () => {
        await playBrowserSpeechPreview({
          audioGuideId,
          text,
          languageCode,
          fallbackMessage,
          requestId,
        });
      };

      try {
        await playAudioSequence({
          audioGuideId,
          audioUrls,
          kind: "tts",
          requestId,
          startMessage: fallbackMessage
            ? `Đang phát ElevenLabs TTS. ${fallbackMessage}`
            : "Đang phát ElevenLabs TTS.",
          onPlaybackError: fallbackToBrowserSpeech,
        });
      } catch {
        await fallbackToBrowserSpeech();
      }
    },
    [playAudioSequence, playBrowserSpeechPreview],
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
            poi,
            language: guide.languageCode,
            signal: controller.signal,
          });

          if (requestId !== requestIdRef.current) {
            return;
          }

          narrationText = resolved.ttsInputText;
          effectiveLanguage = resolved.effectiveLanguageCode;
          fallbackMessage = resolved.fallbackMessage;
          resolvedAudioUrl =
            resolved.audioGuide?.sourceType === "uploaded"
              ? resolved.audioGuide.audioUrl?.trim() ?? resolvedAudioUrl
              : resolvedAudioUrl;
        } catch (error) {
          if (error instanceof DOMException && error.name === "AbortError") {
            return;
          }
        }
      }

      const playTtsAudio = async () => {
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
          await playTtsPreview({
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
              void playTtsAudio();
            },
          });
          return;
        } catch {
          await playTtsAudio();
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
              void playTtsAudio();
            },
          });
          return;
        } catch {
          await playTtsAudio();
          return;
        }
      }

      await playTtsAudio();
    },
    [
      playAudioSequence,
      playTtsPreview,
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
