import { useState, type ChangeEvent, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { ImageSourceField } from "../../components/ui/ImageSourceField";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AudioGuide, MediaAsset } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { getPoiTitle, getPoiTranslation } from "../../lib/selectors";
import { cn, formatDateTime, languageLabels } from "../../lib/utils";
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

type MediaAssetForm = {
  id?: string;
  entityType: MediaAsset["entityType"];
  entityId: string;
  type: MediaAsset["type"];
  url: string;
  altText: string;
};

type NarrationForm = {
  id?: string;
  title: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
};

const defaultAudioForm: AudioForm = {
  entityId: "",
  languageCode: "vi",
  audioUrl: "",
  sourceType: "generated",
  status: "missing",
};

const defaultMediaAssetForm: MediaAssetForm = {
  entityType: "poi",
  entityId: "",
  type: "image",
  url: "",
  altText: "",
};

const entityTypeLabels: Record<MediaAsset["entityType"], string> = {
  poi: "POI",
  food_item: "Món ăn",
  route: "Tour",
  promotion: "Ưu đãi",
};

const defaultNarrationForm: NarrationForm = {
  title: "",
  shortText: "",
  fullText: "",
  seoTitle: "",
  seoDescription: "",
};

const createBlankNarrationForm = (): NarrationForm => ({
  ...defaultNarrationForm,
});

const buildNarrationPreviewText = (title: string, shortText: string, fullText: string) => {
  const normalizedTitle = title.trim();
  const narrationBody = (fullText || shortText).trim();

  if (!normalizedTitle && !narrationBody) {
    return "";
  }

  if (!narrationBody) {
    return normalizedTitle;
  }

  return narrationBody.startsWith(normalizedTitle)
    ? narrationBody
    : `${normalizedTitle}. ${narrationBody}`;
};

const isPoiEntityType = (entityType: string) => entityType === "poi" || entityType === "place";

export const MediaPage = () => {
  const { state, isBootstrapping, saveAudioGuide, saveMediaAsset, saveTranslation } = useAdminData();
  const { user } = useAuth();
  const { previewState, previewAudioGuide, stopPreview } = useNarrationPreview(state);
  const [audioModalOpen, setAudioModalOpen] = useState(false);
  const [mediaModalOpen, setMediaModalOpen] = useState(false);
  const [audioForm, setAudioForm] = useState<AudioForm>(defaultAudioForm);
  const [mediaForm, setMediaForm] = useState<MediaAssetForm>(defaultMediaAssetForm);
  const [narrationForm, setNarrationForm] = useState<NarrationForm>(defaultNarrationForm);
  const [formError, setFormError] = useState("");
  const [isSaving, setSaving] = useState(false);
  const [mediaFormError, setMediaFormError] = useState("");
  const [isSavingMedia, setSavingMedia] = useState(false);
  const [isUploadingAudio, setUploadingAudio] = useState(false);

  const getEntityOptions = (entityType: MediaAsset["entityType"]) => {
    switch (entityType) {
      case "food_item":
        return state.foodItems.map((item) => ({
          id: item.id,
          label: item.name,
        }));
      case "route":
        return state.routes.map((item) => ({
          id: item.id,
          label: item.name,
        }));
      case "promotion":
        return state.promotions.map((item) => ({
          id: item.id,
          label: item.title,
        }));
      case "poi":
      default:
        return state.pois.map((item) => ({
          id: item.id,
          label: getPoiTitle(state, item.id),
        }));
    }
  };

  const getEntityDisplayName = (entityType: MediaAsset["entityType"], entityId: string) => {
    switch (entityType) {
      case "food_item":
        return state.foodItems.find((item) => item.id === entityId)?.name ?? entityId;
      case "route":
        return state.routes.find((item) => item.id === entityId)?.name ?? entityId;
      case "promotion":
        return state.promotions.find((item) => item.id === entityId)?.title ?? entityId;
      case "poi":
      default:
        return getPoiTitle(state, entityId);
    }
  };

  const loadNarrationForm = (
    entityId: string,
    languageCode: AudioGuide["languageCode"],
  ): NarrationForm => {
    if (!entityId) {
      return createBlankNarrationForm();
    }

    const existing = state.translations.find(
      (item) =>
        isPoiEntityType(item.entityType) &&
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
      };
    }

    return createBlankNarrationForm();
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
      : {
          ...defaultAudioForm,
          entityId: "",
        };

    setAudioForm(nextAudioForm);
    setNarrationForm(
      item
        ? loadNarrationForm(nextAudioForm.entityId, nextAudioForm.languageCode)
        : createBlankNarrationForm(),
    );
    setAudioModalOpen(true);
  };

  const openMediaModal = (item?: MediaAsset) => {
    setMediaFormError("");
    setMediaForm(
      item
        ? {
            id: item.id,
            entityType: item.entityType,
            entityId: item.entityId,
            type: item.type,
            url: item.url,
            altText: item.altText,
          }
        : defaultMediaAssetForm,
    );
    setMediaModalOpen(true);
  };

  const updateMediaEntityType = (entityType: MediaAsset["entityType"]) => {
    setMediaForm((current) => ({
      ...current,
      entityType,
      entityId: "",
    }));
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

    if (audioForm.sourceType === "uploaded" && !audioForm.audioUrl.trim()) {
      setFormError("Hãy tải file MP3 từ thiết bị trước khi lưu audio guide.");
      setSaving(false);
      return;
    }

    try {
      await saveAudioGuide(
        {
          id: audioForm.id,
          entityType: "poi",
          entityId: audioForm.entityId,
          languageCode: audioForm.languageCode,
          audioUrl: audioForm.audioUrl,
          sourceType: audioForm.sourceType,
          status: audioForm.status,
        },
        user,
      );

      await saveTranslation(
        {
          id: narrationForm.id,
          entityType: "poi",
          entityId: audioForm.entityId,
          languageCode: audioForm.languageCode,
          title: narrationForm.title,
          shortText: narrationForm.shortText,
          fullText: narrationForm.fullText,
          seoTitle: narrationForm.seoTitle,
          seoDescription: narrationForm.seoDescription,
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

  const handleMediaSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSavingMedia(true);
    setMediaFormError("");

    if (!mediaForm.url.trim()) {
      setMediaFormError("Hãy tải file media từ thiết bị trước khi lưu.");
      setSavingMedia(false);
      return;
    }

    try {
      await saveMediaAsset(
        {
          id: mediaForm.id,
          entityType: mediaForm.entityType,
          entityId: mediaForm.entityId,
          type: mediaForm.type,
          url: mediaForm.url,
          altText: mediaForm.altText,
        },
        user,
      );

      setMediaModalOpen(false);
    } catch (error) {
      setMediaFormError(getErrorMessage(error));
    } finally {
      setSavingMedia(false);
    }
  };

  const audioRecordsWithNarration = state.audioGuides.filter((item) =>
    state.translations.some(
      (translation) =>
        isPoiEntityType(translation.entityType) &&
        translation.entityId === item.entityId &&
        translation.languageCode === item.languageCode &&
        Boolean(translation.fullText || translation.shortText),
    ),
  ).length;

  const previewNarrationText = buildNarrationPreviewText(
    narrationForm.title,
    narrationForm.shortText,
    narrationForm.fullText,
  );
  const previewAudioDraft: AudioGuide & { previewText: string } = {
    id: audioForm.id ?? "__draft-audio__",
    entityType: "poi",
    entityId: audioForm.entityId,
    languageCode: audioForm.languageCode,
    transcriptText: previewNarrationText,
    audioUrl: audioForm.audioUrl,
    audioFilePath: "",
    audioFileName: "",
    voiceType: "standard",
    sourceType: audioForm.sourceType,
    provider: audioForm.sourceType === "uploaded" ? "uploaded" : "elevenlabs",
    voiceId: "",
    modelId: "",
    outputFormat: "mp3_44100_128",
    durationInSeconds: null,
    fileSizeBytes: null,
    textHash: "",
    contentVersion: "",
    generatedAt: null,
    generationStatus: audioForm.audioUrl ? "success" : "none",
    errorMessage: null,
    isOutdated: false,
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

  const editingAudioRecord = audioForm.id
    ? state.audioGuides.find((item) => item.id === audioForm.id) ?? null
    : null;
  const editingMediaAsset = mediaForm.id
    ? state.mediaAssets.find((item) => item.id === mediaForm.id) ?? null
    : null;

  const audioColumns: DataColumn<AudioGuide>[] = [
    {
      key: "entity",
      header: "POI",
      widthClassName: "min-w-[220px]",
      render: (item) => (
        <div>
          <p className="font-semibold text-ink-900">{getPoiTitle(state, item.entityId)}</p>
          <p className="mt-1 text-xs text-ink-500">{languageLabels[item.languageCode]}</p>
          <p className="mt-1 text-xs text-ink-500">ID: {item.id}</p>
        </div>
      ),
    },
    {
      key: "narration",
      header: "Nội dung phát",
      widthClassName: "min-w-[320px]",
      render: (item) => {
        const translation = getPoiTranslation(state, item.entityId, item.languageCode);

        return translation ? (
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-medium text-ink-800">{translation.title}</p>
              <StatusBadge status="published" label="Public" />
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
      key: "source",
      header: "Nguồn phát",
      widthClassName: "min-w-[280px]",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">
            {item.sourceType === "generated" ? "Pre-generated audio" : "Uploaded audio"}
          </p>
          <p className="mt-1 truncate text-xs text-ink-500">
            {item.audioUrl || "Chưa có file audio"}
          </p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      widthClassName: "min-w-[130px]",
      cellClassName: "whitespace-nowrap",
      render: (item) => (
        <div className="space-y-2">
          <StatusBadge status={item.status} />
          <p className="text-xs text-ink-500">{formatDateTime(item.updatedAt)}</p>
          <p className="text-xs text-ink-500">{item.updatedBy}</p>
        </div>
      ),
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

  const mediaAssetColumns: DataColumn<MediaAsset>[] = [
    {
      key: "asset",
      header: "Media",
      widthClassName: "min-w-[280px]",
      render: (item) => (
        <div className="flex items-center gap-3">
          {item.type === "image" ? (
            <img
              src={item.url}
              alt={item.altText}
              className="h-16 w-16 rounded-3xl object-cover"
            />
          ) : (
            <div className="flex h-16 w-16 items-center justify-center rounded-3xl bg-sand-100 text-[11px] font-semibold uppercase tracking-[0.18em] text-ink-500">
              Video
            </div>
          )}
          <div>
            <p className="font-semibold text-ink-900">{item.altText || "Chưa có alt text"}</p>
            <p className="mt-1 text-xs text-ink-500">ID: {item.id}</p>
          </div>
        </div>
      ),
    },
    {
      key: "entity",
      header: "Liên kết",
      widthClassName: "min-w-[260px]",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">{getEntityDisplayName(item.entityType, item.entityId)}</p>
          <p className="mt-1 text-xs text-ink-500">
            {entityTypeLabels[item.entityType]} / {item.entityId}
          </p>
        </div>
      ),
    },
    {
      key: "type",
      header: "Loại",
      widthClassName: "min-w-[180px]",
      render: (item) => (
        <div>
          <StatusBadge status={item.type === "image" ? "published" : "processing"} label={item.type} />
          <p className="mt-2 truncate text-xs text-ink-500">{item.url}</p>
        </div>
      ),
    },
    {
      key: "createdAt",
      header: "Tạo lúc",
      widthClassName: "min-w-[160px]",
      render: (item) => <p className="text-sm text-ink-600">{formatDateTime(item.createdAt)}</p>,
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[160px]",
      render: (item) => (
        <Button variant="secondary" onClick={() => openMediaModal(item)}>
          Chỉnh sửa
        </Button>
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
          <Button onClick={() => openAudioModal()} disabled={isBootstrapping}>
            {isBootstrapping ? "Đang tải dữ liệu..." : "Thêm audio"}
          </Button>
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

      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h2 className="section-heading">Media assets</h2>
            <p className="mt-2 text-sm text-ink-500">
              Hiển thị đầy đủ các cột của bảng media asset để admin không bỏ sót dữ liệu liên kết.
            </p>
          </div>
          <Button onClick={() => openMediaModal()} disabled={isBootstrapping}>
            {isBootstrapping ? "Đang tải dữ liệu..." : "Thêm media asset"}
          </Button>
        </div>
        <div className="mt-6">
          <DataTable
            data={state.mediaAssets}
            columns={mediaAssetColumns}
            rowKey={(row) => row.id}
            tableClassName="min-w-[1200px]"
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
        <form className="space-y-6" onSubmit={handleAudioSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
          <div className="grid gap-6 lg:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-1">
                <div>
                  <label className="field-label">POI</label>
                  <Select
                    value={audioForm.entityId}
                    required
                    onChange={(event) =>
                      updateAudioSelection(event.target.value, audioForm.languageCode)
                    }
                  >
                    <option value="">Chọn POI</option>
                    {state.pois.map((poi) => (
                      <option key={poi.id} value={poi.id}>
                        {getPoiTitle(state, poi.id)}
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
                    {state.settings.supportedLanguages.map((code) => (
                      <option key={code} value={code}>
                        {languageLabels[code]}
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
                    <option value="generated">Pre-generated</option>
                  </Select>
                </div>
              </div>

              {audioForm.sourceType === "uploaded" ? (
                <div className="space-y-3">
                  <label className="field-label">Tệp MP3</label>
                  <input
                    type="file"
                    accept=".mp3,audio/mpeg"
                    autoComplete="off"
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
                  Audio generate trước được tạo ở backend. Nếu chưa có file, hãy dùng màn POI để generate hoặc regenerate audio theo ngôn ngữ.
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
              </div>

              {editingAudioRecord ? (
                <div className="grid gap-4 md:grid-cols-3">
                  <Card className="border border-sand-100 bg-sand-50">
                    <p className="text-sm text-ink-500">Audio ID</p>
                    <p className="mt-2 break-all font-semibold text-ink-900">{editingAudioRecord.id}</p>
                  </Card>
                  <Card className="border border-sand-100 bg-sand-50">
                    <p className="text-sm text-ink-500">Cập nhật bởi</p>
                    <p className="mt-2 font-semibold text-ink-900">{editingAudioRecord.updatedBy}</p>
                  </Card>
                  <Card className="border border-sand-100 bg-sand-50">
                    <p className="text-sm text-ink-500">Cập nhật lúc</p>
                    <p className="mt-2 font-semibold text-ink-900">{formatDateTime(editingAudioRecord.updatedAt)}</p>
                  </Card>
                </div>
              ) : null}

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
                        : audioForm.sourceType === "generated" || !audioForm.audioUrl
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

      <Modal
        open={mediaModalOpen}
        onClose={() => setMediaModalOpen(false)}
        title={mediaForm.id ? "Cập nhật media asset" : "Tạo media asset"}
        description="Hiển thị và lưu đầy đủ các cột đang có trong bảng MediaAssets."
        maxWidthClassName="max-w-4xl"
      >
        <form className="space-y-6" onSubmit={handleMediaSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <label className="field-label">Entity type</label>
              <Select
                value={mediaForm.entityType}
                onChange={(event) =>
                  updateMediaEntityType(event.target.value as MediaAsset["entityType"])
                }
              >
                {Object.entries(entityTypeLabels).map(([key, label]) => (
                  <option key={key} value={key}>
                    {label}
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="field-label">Entity ID</label>
              <Select
                value={mediaForm.entityId}
                required
                onChange={(event) =>
                  setMediaForm((current) => ({ ...current, entityId: event.target.value }))
                }
              >
                <option value="">Chọn entity</option>
                {getEntityOptions(mediaForm.entityType).map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.label}
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="field-label">Media type</label>
              <Select
                value={mediaForm.type}
                onChange={(event) =>
                  setMediaForm((current) => ({
                    ...current,
                    type: event.target.value as MediaAsset["type"],
                  }))
                }
              >
                <option value="image">image</option>
                <option value="video">video</option>
              </Select>
            </div>
            <div>
              <label className="field-label">Alt text</label>
              <Input
                value={mediaForm.altText}
                onChange={(event) =>
                  setMediaForm((current) => ({ ...current, altText: event.target.value }))
                }
              />
            </div>
          </div>

          <ImageSourceField
            label={mediaForm.type === "image" ? "Tải ảnh media" : "Tải video media"}
            value={mediaForm.url}
            onChange={(value) => setMediaForm((current) => ({ ...current, url: value }))}
            onUpload={async (file) =>
              (
                await adminApi.uploadFile(
                  file,
                  mediaForm.type === "image" ? "images/media-assets" : "videos/media-assets",
                )
              ).url
            }
            accept={mediaForm.type === "image" ? "image/*" : "video/*"}
            previewType={mediaForm.type === "image" ? "image" : "video"}
            helperText={
              mediaForm.type === "image"
                ? "Ảnh từ thiết bị sẽ được upload lên backend storage trước khi lưu media asset."
                : "Video từ thiết bị sẽ được upload lên backend storage trước khi lưu media asset."
            }
            emptyText={
              mediaForm.type === "image"
                ? "Chưa có ảnh nào được tải lên."
                : "Chưa có video nào được tải lên."
            }
          />

          {editingMediaAsset ? (
            <div className="grid gap-4 md:grid-cols-2">
              <Card className="border border-sand-100 bg-sand-50">
                <p className="text-sm text-ink-500">Media ID</p>
                <p className="mt-2 break-all font-semibold text-ink-900">{editingMediaAsset.id}</p>
              </Card>
              <Card className="border border-sand-100 bg-sand-50">
                <p className="text-sm text-ink-500">Tạo lúc</p>
                <p className="mt-2 font-semibold text-ink-900">{formatDateTime(editingMediaAsset.createdAt)}</p>
              </Card>
            </div>
          ) : null}

          {mediaFormError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{mediaFormError}</div>
          ) : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setMediaModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSavingMedia || !mediaForm.entityId || !mediaForm.url.trim()}>
              {isSavingMedia ? "Đang lưu..." : "Lưu media asset"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
