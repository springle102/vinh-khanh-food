import { useState, type ChangeEvent, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AudioGuide } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { getPlaceTitle, getPlaceTranslation } from "../../lib/selectors";
import { cn, languageLabels } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";
import { useNarrationPreview } from "./useNarrationPreview";

type AudioForm = {
  id?: string;
  entityId: string;
  languageCode: AudioGuide["languageCode"];
  audioUrl: string;
  sourceType: AudioGuide["sourceType"];
  status: AudioGuide["status"];
};

type NarrationForm = {
  id?: string;
  title: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
  isPremium: boolean;
};

const defaultAudioForm: AudioForm = {
  entityId: "",
  languageCode: "vi",
  audioUrl: "",
  sourceType: "tts",
  status: "ready",
};

const defaultNarrationForm: NarrationForm = {
  title: "",
  shortText: "",
  fullText: "",
  seoTitle: "",
  seoDescription: "",
  isPremium: false,
};

const createBlankNarrationForm = (isPremium = false): NarrationForm => ({
  ...defaultNarrationForm,
  isPremium,
});

export const MediaPage = () => {
  const { state, saveAudioGuide, saveTranslation } = useAdminData();
  const { user } = useAuth();
  const { previewState, previewAudioGuide, stopPreview } = useNarrationPreview(state);
  const [audioModalOpen, setAudioModalOpen] = useState(false);
  const [audioForm, setAudioForm] = useState<AudioForm>(defaultAudioForm);
  const [narrationForm, setNarrationForm] = useState<NarrationForm>(defaultNarrationForm);
  const [formError, setFormError] = useState("");
  const [isSaving, setSaving] = useState(false);
  const [isUploadingAudio, setUploadingAudio] = useState(false);

  const loadNarrationForm = (
    entityId: string,
    languageCode: AudioGuide["languageCode"],
  ): NarrationForm => {
    if (!entityId) {
      return createBlankNarrationForm(state.settings.premiumLanguages.includes(languageCode));
    }

    const existing = state.translations.find(
      (item) =>
        item.entityType === "place" &&
        item.entityId === entityId &&
        item.languageCode === languageCode,
    );

    if (existing) {
      return {
        id: existing.id,
        title: existing.title,
        shortText: existing.shortText,
        fullText: existing.fullText,
        seoTitle: existing.seoTitle,
        seoDescription: existing.seoDescription,
        isPremium: existing.isPremium,
      };
    }

    return createBlankNarrationForm(state.settings.premiumLanguages.includes(languageCode));
  };

  const updateAudioSelection = (
    entityId: string,
    languageCode: AudioGuide["languageCode"],
  ) => {
    setAudioForm((current) => ({
      ...current,
      entityId,
      languageCode,
    }));

    setNarrationForm(loadNarrationForm(entityId, languageCode));
  };

  const openAudioModal = (item?: AudioGuide) => {
    stopPreview();
    setFormError("");

    const nextAudioForm: AudioForm = item
      ? {
          id: item.id,
          entityId: item.entityId,
          languageCode: item.languageCode,
          audioUrl: item.audioUrl,
          sourceType: item.sourceType,
          status: item.status,
        }
      : { ...defaultAudioForm };

    setAudioForm(nextAudioForm);
    setNarrationForm(
      item
        ? loadNarrationForm(nextAudioForm.entityId, nextAudioForm.languageCode)
        : createBlankNarrationForm(
            state.settings.premiumLanguages.includes(nextAudioForm.languageCode),
          ),
    );
    setAudioModalOpen(true);
  };

  const handleAudioFileChange = async (event: ChangeEvent<HTMLInputElement>) => {
    const nextFile = event.target.files?.[0];
    event.target.value = "";

    if (!nextFile) {
      return;
    }

    setUploadingAudio(true);
    setFormError("");

    try {
      const uploaded = await adminApi.uploadFile(nextFile, "audio/guides");
      setAudioForm((current) => ({
        ...current,
        audioUrl: uploaded.url,
        sourceType: "uploaded",
      }));
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setUploadingAudio(false);
    }
  };

  const handleAudioSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      await saveAudioGuide(
        {
          id: audioForm.id,
          entityType: "place",
          entityId: audioForm.entityId,
          languageCode: audioForm.languageCode,
          audioUrl: audioForm.audioUrl,
          voiceType: "standard",
          sourceType: audioForm.sourceType,
          status: audioForm.status,
        },
        user,
      );

      await saveTranslation(
        {
          id: narrationForm.id,
          entityType: "place",
          entityId: audioForm.entityId,
          languageCode: audioForm.languageCode,
          title: narrationForm.title,
          shortText: narrationForm.shortText,
          fullText: narrationForm.fullText,
          seoTitle: narrationForm.seoTitle,
          seoDescription: narrationForm.seoDescription,
          isPremium: narrationForm.isPremium,
        },
        user,
      );

      stopPreview();
      setAudioModalOpen(false);
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const audioRecordsWithNarration = state.audioGuides.filter((item) =>
    state.translations.some(
      (translation) =>
        translation.entityType === "place" &&
        translation.entityId === item.entityId &&
        translation.languageCode === item.languageCode &&
        Boolean(translation.fullText || translation.shortText),
    ),
  ).length;

  const previewNarrationText = narrationForm.fullText || narrationForm.shortText;
  const previewAudioDraft: AudioGuide & { previewText: string } = {
    id: audioForm.id ?? "__draft-audio__",
    entityType: "place",
    entityId: audioForm.entityId,
    languageCode: audioForm.languageCode,
    audioUrl: audioForm.audioUrl,
    voiceType: "standard",
    sourceType: audioForm.sourceType,
    status: audioForm.status,
    updatedBy: user?.name ?? "Preview",
    updatedAt: new Date().toISOString(),
    previewText: previewNarrationText,
  };

  const isDraftPreviewActive =
    previewState.audioGuideId === previewAudioDraft.id &&
    previewState.status === "playing";

  const draftPreviewMessage =
    previewState.audioGuideId === previewAudioDraft.id ? previewState.message : "";

  const audioColumns: DataColumn<AudioGuide>[] = [
    {
      key: "entity",
      header: "Điểm đến",
      widthClassName: "min-w-[220px]",
      render: (item) => (
        <div>
          <p className="font-semibold text-ink-900">{getPlaceTitle(state, item.entityId)}</p>
          <p className="mt-1 text-xs text-ink-500">{languageLabels[item.languageCode]}</p>
        </div>
      ),
    },
    {
      key: "narration",
      header: "Nội dung phát",
      widthClassName: "min-w-[320px]",
      render: (item) => {
        const translation = getPlaceTranslation(state, item.entityId, item.languageCode);

        return translation ? (
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-medium text-ink-800">{translation.title}</p>
              <StatusBadge
                status={translation.isPremium ? "processing" : "published"}
                label={translation.isPremium ? "Premium" : "Free"}
              />
            </div>
            <p className="mt-1 text-sm text-ink-500">
              {translation.shortText || "Đã có tiêu đề, chưa có mô tả ngắn."}
            </p>
          </div>
        ) : (
          <p className="font-medium text-rose-700">Chưa có nội dung thuyết minh</p>
        );
      },
    },
    {
      key: "voice",
      header: "Nguồn phát",
      widthClassName: "min-w-[280px]",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">
            {item.sourceType === "tts" ? "Text-to-speech" : "Upload audio"}
          </p>
          <p className="mt-1 truncate text-xs text-ink-500">
            {item.audioUrl || "Chưa có URL audio"}
          </p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      widthClassName: "min-w-[130px]",
      cellClassName: "whitespace-nowrap",
      render: (item) => <StatusBadge status={item.status} />,
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[240px]",
      render: (item) => (
        <div className="flex flex-wrap gap-2">
          <Button
            variant="ghost"
            onClick={() => {
              void previewAudioGuide(item);
            }}
          >
            {previewState.audioGuideId === item.id && previewState.status === "playing"
              ? "Dừng nghe thử"
              : "Nghe thử"}
          </Button>
          <Button variant="secondary" onClick={() => openAudioModal(item)}>
            Chỉnh sửa
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
          Audio & media
        </p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý audio và nội dung</h1>
      </Card>

      <section className="grid gap-4 md:grid-cols-3">
        {[
          ["Audio records", state.audioGuides.length],
          ["Narration ready", audioRecordsWithNarration],
          ["Media assets", state.mediaAssets.length],
        ].map(([label, value]) => (
          <Card key={label}>
            <p className="text-sm text-ink-500">{label}</p>
            <p className="mt-3 text-3xl font-bold text-ink-900">{value}</p>
          </Card>
        ))}
      </section>

      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h2 className="section-heading">Audio và nội dung phát</h2>
          </div>
          <Button onClick={() => openAudioModal()}>Thêm audio</Button>
        </div>
        <div className="mt-6">
          {previewState.message ? (
            <div
              className={cn(
                "mb-4 rounded-2xl px-4 py-3 text-sm",
                previewState.status === "error"
                  ? "bg-rose-50 text-rose-700"
                  : previewState.status === "playing"
                    ? "bg-emerald-50 text-emerald-700"
                    : "bg-sand-50 text-ink-600",
              )}
            >
              {previewState.message}
            </div>
          ) : null}
          <DataTable
            data={state.audioGuides}
            columns={audioColumns}
            rowKey={(row) => row.id}
            tableClassName="min-w-[1500px]"
          />
        </div>
      </Card>

      <Modal
        open={audioModalOpen}
        onClose={() => {
          stopPreview();
          setAudioModalOpen(false);
        }}
        title="Audio và nội dung thuyết minh"
        description=""
        maxWidthClassName="max-w-6xl"
      >
        <form className="space-y-6" onSubmit={handleAudioSubmit}>
          <div className="grid gap-6 lg:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Địa điểm</label>
                  <Select
                    value={audioForm.entityId}
                    required
                    onChange={(event) =>
                      updateAudioSelection(event.target.value, audioForm.languageCode)
                    }
                  >
                    <option value="">Chọn địa điểm</option>
                    {state.places.map((place) => (
                      <option key={place.id} value={place.id}>
                        {getPlaceTitle(state, place.id)}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Ngôn ngữ</label>
                  <Select
                    value={audioForm.languageCode}
                    onChange={(event) =>
                      updateAudioSelection(
                        audioForm.entityId,
                        event.target.value as AudioGuide["languageCode"],
                      )
                    }
                  >
                    {Object.entries(languageLabels).map(([key, label]) => (
                      <option key={key} value={key}>
                        {label}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Nguồn audio</label>
                  <Select
                    value={audioForm.sourceType}
                    onChange={(event) =>
                      setAudioForm((current) => ({
                        ...current,
                        sourceType: event.target.value as AudioGuide["sourceType"],
                      }))
                    }
                  >
                    <option value="uploaded">Uploaded</option>
                    <option value="tts">Text-to-speech</option>
                  </Select>
                </div>
              </div>

              {audioForm.sourceType === "uploaded" ? (
                <div className="space-y-3">
                  <label className="field-label">Tệp MP3</label>
                  <input
                    type="file"
                    accept=".mp3,audio/mpeg"
                    onChange={(event) => {
                      void handleAudioFileChange(event);
                    }}
                    className="field-input file:mr-4 file:rounded-2xl file:border-0 file:bg-primary-50 file:px-4 file:py-2 file:font-semibold file:text-primary-700"
                  />
                  <p className="text-xs text-ink-500">
                    File MP3 sẽ được upload lên backend storage trước khi lưu audio guide.
                  </p>
                  {isUploadingAudio ? (
                    <div className="rounded-2xl bg-sand-50 px-4 py-3 text-sm text-ink-600">
                      Đang upload file audio...
                    </div>
                  ) : null}
                  {audioForm.audioUrl ? (
                    <audio controls src={audioForm.audioUrl} className="w-full" />
                  ) : (
                    <div className="rounded-2xl border border-dashed border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-500">
                      Chưa có file MP3 nào được chọn.
                    </div>
                  )}
                </div>
              ) : (
                <div className="rounded-2xl border border-dashed border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-500">
                  Chế độ Text-to-Speech không cần tải file MP3 từ thiết bị.
                </div>
              )}

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Trạng thái</label>
                  <Select
                    value={audioForm.status}
                    onChange={(event) =>
                      setAudioForm((current) => ({
                        ...current,
                        status: event.target.value as AudioGuide["status"],
                      }))
                    }
                  >
                    <option value="ready">Ready</option>
                    <option value="processing">Processing</option>
                    <option value="missing">Missing</option>
                  </Select>
                </div>
                <div className="flex items-end">
                  <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                    <input
                      type="checkbox"
                      checked={narrationForm.isPremium}
                      onChange={(event) =>
                        setNarrationForm((current) => ({
                          ...current,
                          isPremium: event.target.checked,
                        }))
                      }
                    />
                    Nội dung thuộc gói premium
                  </label>
                </div>
              </div>

              <div className="rounded-3xl border border-sand-100 bg-sand-50 p-4">
                <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                  <div className="space-y-3">
                    <p className="text-sm font-semibold text-ink-900">Chế độ nghe thử</p>
                    <div className="max-h-36 overflow-y-auto rounded-2xl bg-white px-4 py-3 text-sm leading-6 text-ink-600">
                      {previewNarrationText ||
                        "Chưa có nội dung để phát. Hãy nhập nội dung thuyết minh vào phần 'Nội dung phát'."}
                    </div>
                    {draftPreviewMessage ? (
                      <p
                        className={cn(
                          "text-xs font-medium",
                          previewState.audioGuideId === previewAudioDraft.id &&
                            previewState.status === "error"
                            ? "text-rose-700"
                            : previewState.audioGuideId === previewAudioDraft.id &&
                                previewState.status === "playing"
                              ? "text-emerald-700"
                              : "text-ink-500",
                        )}
                      >
                        {draftPreviewMessage}
                      </p>
                    ) : null}
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      variant="secondary"
                      onClick={() => {
                        void previewAudioGuide(previewAudioDraft);
                      }}
                    >
                      {isDraftPreviewActive
                        ? "Dừng"
                        : audioForm.sourceType === "tts" || !audioForm.audioUrl
                          ? "Nghe thử"
                          : "Phát"}
                    </Button>
                    <Button
                      variant="ghost"
                      onClick={() => stopPreview("Đã dừng audio.")}
                      disabled={!isDraftPreviewActive}
                    >
                      Dừng
                    </Button>
                  </div>
                </div>
              </div>
            </div>

            <div className="space-y-5">
              <div>
                <label className="field-label">Tiêu đề hiển thị</label>
                <Input
                  value={narrationForm.title}
                  onChange={(event) =>
                    setNarrationForm((current) => ({
                      ...current,
                      title: event.target.value,
                    }))
                  }
                  required
                  placeholder="Nhập tiêu đề..."
                />
              </div>
              <div>
                <label className="field-label">Short text</label>
                <Textarea
                  value={narrationForm.shortText}
                  onChange={(event) =>
                    setNarrationForm((current) => ({
                      ...current,
                      shortText: event.target.value,
                    }))
                  }
                />
              </div>
              <div>
                <label className="field-label">Nội dung phát</label>
                <Textarea
                  value={narrationForm.fullText}
                  onChange={(event) =>
                    setNarrationForm((current) => ({
                      ...current,
                      fullText: event.target.value,
                    }))
                  }
                />
              </div>
              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">SEO title</label>
                  <Input
                    value={narrationForm.seoTitle}
                    onChange={(event) =>
                      setNarrationForm((current) => ({
                        ...current,
                        seoTitle: event.target.value,
                      }))
                    }
                  />
                </div>
                <div>
                  <label className="field-label">SEO description</label>
                  <Input
                    value={narrationForm.seoDescription}
                    onChange={(event) =>
                      setNarrationForm((current) => ({
                        ...current,
                        seoDescription: event.target.value,
                      }))
                    }
                  />
                </div>
              </div>
            </div>
          </div>

          {formError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div>
          ) : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button
              variant="ghost"
              onClick={() => {
                stopPreview();
                setAudioModalOpen(false);
              }}
            >
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving || isUploadingAudio}>
              {isSaving ? "Đang lưu..." : "Lưu"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
