import { useEffect, useMemo, useState, type ChangeEvent, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AudioGuide, Place } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { getCategoryName, getPlaceTranslation, searchPlaces } from "../../lib/selectors";
import { cn, formatDateTime, formatNumber, languageLabels, slugify } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";
import { useNarrationPreview } from "../media/useNarrationPreview";
import { OpenStreetMapPicker } from "./OpenStreetMapPicker";

type PlaceFormState = {
  id?: string;
  title: string;
  slug: string;
  address: string;
  lat: string;
  lng: string;
  categoryId: string;
  status: Place["status"];
  featured: boolean;
  defaultLanguageCode: Place["defaultLanguageCode"];
  district: string;
  ward: string;
  priceRange: string;
  averageVisitDuration: string;
  popularityScore: string;
  tags: string;
  ownerUserId: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
};

type PlaceAudioFormState = {
  id?: string;
  audioUrl: string;
  sourceType: AudioGuide["sourceType"];
  status: AudioGuide["status"];
};

type NarrationFormState = {
  id?: string;
  languageCode: AudioGuide["languageCode"];
  title: string;
  shortText: string;
  fullText: string;
  seoTitle: string;
  seoDescription: string;
  isPremium: boolean;
};

const DEFAULT_LAT = 10.7578;
const DEFAULT_LNG = 106.7033;

const defaultAudioForm: PlaceAudioFormState = {
  audioUrl: "",
  sourceType: "tts",
  status: "ready",
};

const createDefaultForm = (categoryId: string): PlaceFormState => ({
  title: "",
  slug: "",
  address: "",
  lat: DEFAULT_LAT.toFixed(5),
  lng: DEFAULT_LNG.toFixed(5),
  categoryId,
  status: "draft",
  featured: false,
  defaultLanguageCode: "vi",
  district: "Quận 4",
  ward: "Khánh Hội",
  priceRange: "",
  averageVisitDuration: "30",
  popularityScore: "75",
  tags: "",
  ownerUserId: "",
  shortText: "",
  fullText: "",
  seoTitle: "",
  seoDescription: "",
});

const parseCoordinate = (value: string, fallback: number) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const findPlaceAudioGuide = (
  audioGuides: AudioGuide[],
  placeId?: string,
  languageCode?: Place["defaultLanguageCode"],
) => {
  if (!placeId || !languageCode) {
    return null;
  }

  return (
    audioGuides.find(
      (item) =>
        item.entityType === "place" &&
        item.entityId === placeId &&
        item.languageCode === languageCode,
    ) ?? null
  );
};

const buildAudioForm = (audioGuide: AudioGuide | null): PlaceAudioFormState =>
  audioGuide
    ? {
        id: audioGuide.id,
        audioUrl: audioGuide.audioUrl,
        sourceType: audioGuide.sourceType,
        status: audioGuide.status,
      }
    : { ...defaultAudioForm };

const hasNarrationContent = (
  form: Pick<NarrationFormState, "shortText" | "fullText" | "title">,
) =>
  Boolean(form.title.trim() || form.shortText.trim() || form.fullText.trim());

export const PlacesPage = () => {
  const { state, saveAudioGuide, savePlace, saveTranslation } = useAdminData();
  const { user } = useAuth();
  const { previewState, previewAudioGuide, stopPreview } = useNarrationPreview(state);
  const [searchParams, setSearchParams] = useSearchParams();
  const [keyword, setKeyword] = useState(searchParams.get("keyword") ?? "");
  const [statusFilter, setStatusFilter] = useState<Place["status"] | "all">("all");
  const [isModalOpen, setModalOpen] = useState(false);
  const [isSaving, setSaving] = useState(false);
  const [isUploadingAudio, setUploadingAudio] = useState(false);
  const [formError, setFormError] = useState("");
  const [hasSlugBeenManuallyEdited, setHasSlugBeenManuallyEdited] = useState(false);
  const [form, setForm] = useState<PlaceFormState>(() =>
    createDefaultForm(state.categories[0]?.id ?? ""),
  );
  const [audioForm, setAudioForm] = useState<PlaceAudioFormState>(defaultAudioForm);
  const [narrationForm, setNarrationForm] = useState<NarrationFormState>({
    id: undefined,
    languageCode: "vi",
    title: "",
    shortText: "",
    fullText: "",
    seoTitle: "",
    seoDescription: "",
    isPremium: false,
  });

  const createBlankNarrationForm = (
    languageCode: AudioGuide["languageCode"],
    seed?: Partial<NarrationFormState>,
  ): NarrationFormState => ({
    id: undefined,
    languageCode,
    title: seed?.title ?? "",
    shortText: seed?.shortText ?? "",
    fullText: seed?.fullText ?? "",
    seoTitle: seed?.seoTitle ?? (seed?.title ?? ""),
    seoDescription: seed?.seoDescription ?? "",
    isPremium: seed?.isPremium ?? !state.settings.freeLanguages.includes(languageCode),
  });

  const loadNarrationForm = (
    placeId: string | undefined,
    languageCode: AudioGuide["languageCode"],
    seed?: Partial<NarrationFormState>,
  ): NarrationFormState => {
    if (!placeId) {
      return createBlankNarrationForm(languageCode, seed);
    }

    const existing = state.translations.find(
      (item) =>
        item.entityType === "place" &&
        item.entityId === placeId &&
        item.languageCode === languageCode,
    );

    if (!existing) {
      return createBlankNarrationForm(languageCode, seed);
    }

    return {
      id: existing.id,
      languageCode,
      title: existing.title,
      shortText: existing.shortText,
      fullText: existing.fullText,
      seoTitle: existing.seoTitle,
      seoDescription: existing.seoDescription,
      isPremium: existing.isPremium,
    };
  };

  const updateNarrationLanguage = (
    placeId: string | undefined,
    languageCode: AudioGuide["languageCode"],
    seed?: Partial<NarrationFormState>,
  ) => {
    stopPreview();
    setFormError("");
    setNarrationForm(loadNarrationForm(placeId, languageCode, seed));
    setAudioForm(buildAudioForm(findPlaceAudioGuide(state.audioGuides, placeId, languageCode)));
  };

  useEffect(() => {
    setKeyword(searchParams.get("keyword") ?? "");
  }, [searchParams]);

  const filteredPlaces = useMemo(() => {
    const searched = searchPlaces(state.places, state, keyword);
    if (statusFilter === "all") {
      return searched;
    }

    return searched.filter((item) => item.status === statusFilter);
  }, [keyword, state, statusFilter]);

  const featuredPlaces = state.places.filter((item) => item.featured).length;
  const draftPlaces = state.places.filter((item) => item.status === "draft").length;
  const narrationReadyPlaces = state.places.filter((place) => {
    const translation = getPlaceTranslation(state, place.id, place.defaultLanguageCode);
    return Boolean(translation?.fullText || translation?.shortText);
  }).length;
  const readyAudioPlaces = state.places.filter((place) =>
    state.audioGuides.some(
      (item) =>
        item.entityType === "place" &&
        item.entityId === place.id &&
        item.languageCode === place.defaultLanguageCode &&
        item.status === "ready",
    ),
  ).length;

  const selectedLat = parseCoordinate(form.lat, DEFAULT_LAT);
  const selectedLng = parseCoordinate(form.lng, DEFAULT_LNG);
  const previewNarrationText = narrationForm.fullText || narrationForm.shortText;
  const previewAudioDraft: AudioGuide & { previewText: string } = {
    id: audioForm.id ?? "__place-audio-draft__",
    entityType: "place",
    entityId: form.id ?? "__draft-place__",
    languageCode: narrationForm.languageCode,
    audioUrl: audioForm.audioUrl,
    voiceType: "standard",
    sourceType: audioForm.sourceType,
    status: audioForm.status,
    updatedBy: user?.name ?? "Preview",
    updatedAt: new Date().toISOString(),
    previewText: previewNarrationText,
  };
  const isDraftPreviewActive =
    previewState.audioGuideId === previewAudioDraft.id && previewState.status === "playing";
  const draftPreviewMessage =
    previewState.audioGuideId === previewAudioDraft.id ? previewState.message : "";

  const closeModal = () => {
    stopPreview();
    setFormError("");
    setModalOpen(false);
  };

  const openCreateModal = () => {
    stopPreview();
    setFormError("");
    setHasSlugBeenManuallyEdited(false);
    setForm(createDefaultForm(state.categories[0]?.id ?? ""));
    setAudioForm({ ...defaultAudioForm });
    setNarrationForm(createBlankNarrationForm("vi"));
    setModalOpen(true);
  };

  const openEditModal = (place: Place) => {
    const translation = getPlaceTranslation(state, place.id, place.defaultLanguageCode);

    stopPreview();
    setFormError("");
    setHasSlugBeenManuallyEdited(false);
    setForm({
      id: place.id,
      slug: place.slug,
      address: place.address,
      lat: place.lat.toString(),
      lng: place.lng.toString(),
      categoryId: place.categoryId,
      status: place.status,
      featured: place.featured,
      defaultLanguageCode: place.defaultLanguageCode,
      district: place.district,
      ward: place.ward,
      priceRange: place.priceRange,
      averageVisitDuration: place.averageVisitDuration.toString(),
      popularityScore: place.popularityScore.toString(),
      tags: place.tags.join(", "),
      ownerUserId: place.ownerUserId ?? "",
      title: translation?.title ?? "",
      shortText: translation?.shortText ?? "",
      fullText: translation?.fullText ?? "",
      seoTitle: translation?.seoTitle ?? "",
      seoDescription: translation?.seoDescription ?? "",
    });
    updateNarrationLanguage(place.id, place.defaultLanguageCode);
    setModalOpen(true);
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
        status: current.status === "missing" ? "ready" : current.status,
      }));
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setUploadingAudio(false);
    }
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      const defaultNarrationDraft =
        narrationForm.languageCode === form.defaultLanguageCode || !form.id
          ? narrationForm
          : loadNarrationForm(form.id, form.defaultLanguageCode);

      const savedPlace = await savePlace(
        {
          id: form.id,
          slug: form.slug || slugify(defaultNarrationDraft.title),
          address: form.address,
          lat: selectedLat,
          lng: selectedLng,
          categoryId: form.categoryId,
          status: form.status,
          featured: form.featured,
          defaultLanguageCode: form.defaultLanguageCode,
          district: form.district,
          ward: form.ward,
          priceRange: form.priceRange,
          averageVisitDuration: Number(form.averageVisitDuration),
          popularityScore: Number(form.popularityScore),
          tags: form.tags
            .split(",")
            .map((item) => item.trim())
            .filter(Boolean),
          ownerUserId: form.ownerUserId || null,
          title: defaultNarrationDraft.title,
          shortText: defaultNarrationDraft.shortText,
          fullText: defaultNarrationDraft.fullText,
          seoTitle: defaultNarrationDraft.seoTitle,
          seoDescription: defaultNarrationDraft.seoDescription,
        },
        user,
      );

      setForm((current) => ({
        ...current,
        id: savedPlace.id,
      }));

      if (
        narrationForm.languageCode !== form.defaultLanguageCode &&
        hasNarrationContent(narrationForm)
      ) {
        await saveTranslation(
          {
            id: narrationForm.id,
            entityType: "place",
            entityId: savedPlace.id,
            languageCode: narrationForm.languageCode,
            title: narrationForm.title,
            shortText: narrationForm.shortText,
            fullText: narrationForm.fullText,
            seoTitle: narrationForm.seoTitle,
            seoDescription: narrationForm.seoDescription,
            isPremium: narrationForm.isPremium,
          },
          user,
        );
      }

      if (hasNarrationContent(narrationForm) || audioForm.audioUrl || audioForm.id) {
        if (audioForm.sourceType === "uploaded" && !audioForm.audioUrl) {
          throw new Error("Hãy upload file MP3 hoặc chuyển sang chế độ Text-to-Speech trước khi lưu.");
        }

        await saveAudioGuide(
          {
            id: audioForm.id,
            entityType: "place",
            entityId: savedPlace.id,
            languageCode: narrationForm.languageCode,
            audioUrl: audioForm.audioUrl,
            voiceType: "standard",
            sourceType: audioForm.audioUrl ? audioForm.sourceType : "tts",
            status: audioForm.status,
          },
          user,
        );
      }

      closeModal();
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const columns: DataColumn<Place>[] = [
    {
      key: "place",
      header: "Địa điểm",
      widthClassName: "min-w-[240px]",
      render: (place) => {
        const translation = getPlaceTranslation(state, place.id);
        return (
          <div>
            <div className="flex flex-wrap items-center gap-3">
              <p className="font-semibold text-ink-900">{translation?.title ?? place.slug}</p>
              {place.featured ? <StatusBadge status="active" label="Featured" /> : null}
            </div>
            <p className="mt-1 text-sm text-ink-500">
              {translation?.shortText ?? "Chưa có mô tả ngắn."}
            </p>
          </div>
        );
      },
    },
    {
      key: "category",
      header: "Phân loại",
      widthClassName: "min-w-[170px]",
      render: (place) => (
        <div>
          <p className="font-medium text-ink-800">{getCategoryName(state, place.categoryId)}</p>
          <p className="mt-1 text-xs text-ink-500">{place.priceRange || "Chưa cập nhật"}</p>
        </div>
      ),
    },
    {
      key: "location",
      header: "Vị trí",
      widthClassName: "min-w-[170px]",
      render: (place) => (
        <div>
          <p className="font-medium text-ink-800">{place.ward}</p>
          <p className="mt-1 text-xs text-ink-500">
            {place.lat.toFixed(5)}, {place.lng.toFixed(5)}
          </p>
        </div>
      ),
    },
    {
      key: "audio",
      header: "Audio thuyết minh",
      widthClassName: "min-w-[240px]",
      render: (place) => {
        const audioGuide = findPlaceAudioGuide(
          state.audioGuides,
          place.id,
          place.defaultLanguageCode,
        );
        const translation = getPlaceTranslation(state, place.id, place.defaultLanguageCode);
        const hasNarration = Boolean(translation?.fullText || translation?.shortText);

        return audioGuide ? (
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={audioGuide.status} />
              <p className="text-sm font-medium text-ink-800">
                {audioGuide.sourceType === "uploaded" ? "Upload MP3" : "TTS"}
              </p>
            </div>
            <p className="mt-1 text-xs text-ink-500">
              {hasNarration ? "Đã có nội dung thuyết minh." : "Chưa có nội dung để phát."}
            </p>
          </div>
        ) : (
          <div>
            <p className="font-medium text-rose-700">Chưa có audio</p>
            <p className="mt-1 text-xs text-ink-500">
              {hasNarration
                ? "Có thể dùng TTS từ nội dung hiện tại."
                : "Thêm nội dung thuyết minh hoặc tải MP3 trong lúc chỉnh sửa."}
            </p>
          </div>
        );
      },
    },
    {
      key: "status",
      header: "Trạng thái",
      widthClassName: "min-w-[130px]",
      cellClassName: "whitespace-nowrap",
      render: (place) => <StatusBadge status={place.status} />,
    },
    {
      key: "updated",
      header: "Cập nhật",
      widthClassName: "min-w-[180px]",
      render: (place) => (
        <div>
          <p className="font-medium text-ink-800">{place.updatedBy}</p>
          <p className="mt-1 text-xs text-ink-500">{formatDateTime(place.updatedAt)}</p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[140px]",
      render: (place) => (
        <Button variant="secondary" onClick={() => openEditModal(place)}>
          Chỉnh sửa
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
              Places & narration
            </p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">
              Quản lý địa điểm, audio và nội dung thuyết minh
            </h1>
          </div>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            {[
              ["Tổng điểm", formatNumber(state.places.length)],
              ["Featured", formatNumber(featuredPlaces)],
              ["Audio ready", formatNumber(readyAudioPlaces)],
              ["Có thuyết minh", formatNumber(narrationReadyPlaces)],
            ].map(([label, value]) => (
              <div key={label} className="rounded-3xl border border-sand-100 bg-sand-50 px-5 py-4">
                <p className="text-sm text-ink-500">{label}</p>
                <p className="mt-2 text-2xl font-bold text-ink-900">{value}</p>
              </div>
            ))}
          </div>
        </div>
      </Card>

      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div className="grid flex-1 gap-4 md:grid-cols-[minmax(0,1fr)_220px]">
            <div>
              <label className="field-label">Tìm theo tên, tag, địa chỉ</label>
              <Input
                value={keyword}
                onChange={(event) => {
                  const nextKeyword = event.target.value;
                  setKeyword(nextKeyword);

                  const nextParams = new URLSearchParams(searchParams);
                  if (nextKeyword.trim()) {
                    nextParams.set("keyword", nextKeyword);
                  } else {
                    nextParams.delete("keyword");
                  }

                  setSearchParams(nextParams, { replace: true });
                }}
                placeholder="Ví dụ: quán ốc, BBQ, Khánh Hội..."
              />
            </div>
            <div>
              <label className="field-label">Lọc trạng thái</label>
              <Select
                value={statusFilter}
                onChange={(event) =>
                  setStatusFilter(event.target.value as Place["status"] | "all")
                }
              >
                <option value="all">Tất cả</option>
                <option value="draft">Draft</option>
                <option value="published">Published</option>
                <option value="archived">Archived</option>
              </Select>
            </div>
          </div>
          <Button onClick={openCreateModal}>Thêm địa điểm</Button>
        </div>

        <div className="mt-6">
          {filteredPlaces.length ? (
            <DataTable
              data={filteredPlaces}
              columns={columns}
              rowKey={(row) => row.id}
              tableClassName="min-w-[1480px]"
            />
          ) : (
            <EmptyState
              title="Chưa có địa điểm phù hợp"
              description="Hãy tạo địa điểm mới hoặc thay đổi bộ lọc để xem danh sách."
            />
          )}
        </div>
      </Card>

      <section className="grid gap-6">
        <Card>
          <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
            <div>
              <h2 className="section-heading">Bản đồ dữ liệu POI hiện tại</h2>
              <p className="mt-2 text-sm text-ink-500">
                Xem nhanh vị trí, trạng thái và mức sẵn sàng audio mặc định của các điểm nổi bật.
              </p>
            </div>
            <div className="rounded-2xl bg-sand-50 px-4 py-3 text-sm text-ink-600">
              Draft: {formatNumber(draftPlaces)} điểm
            </div>
          </div>
          <div className="mt-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {state.places.slice(0, 4).map((place) => {
              const translation = getPlaceTranslation(state, place.id);
              const audioGuide = findPlaceAudioGuide(
                state.audioGuides,
                place.id,
                place.defaultLanguageCode,
              );

              return (
                <div key={place.id} className="rounded-3xl border border-sand-100 bg-sand-50 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <h3 className="font-semibold text-ink-900">
                      {translation?.title ?? place.slug}
                    </h3>
                    <StatusBadge status={place.status} />
                  </div>
                  <p className="mt-2 text-sm text-ink-500">{place.address}</p>
                  <div className="mt-4 rounded-2xl bg-white px-4 py-3 text-sm text-ink-600">
                    <p>
                      GPS: {place.lat.toFixed(5)}, {place.lng.toFixed(5)}
                    </p>
                    <p className="mt-1">Ward: {place.ward}</p>
                    <p className="mt-2 font-medium text-ink-700">
                      Audio: {audioGuide ? (audioGuide.sourceType === "uploaded" ? "MP3" : "TTS") : "Chưa có"}
                    </p>
                  </div>
                </div>
              );
            })}
          </div>
        </Card>
      </section>

      <Modal
        open={isModalOpen}
        onClose={closeModal}
        title={form.id ? "Cập nhật địa điểm" : "Tạo địa điểm mới"}
        description="Điền thông tin cơ bản, chọn vị trí trên OpenStreetMap và gắn luôn audio thuyết minh trong cùng một form."
        maxWidthClassName="max-w-6xl"
      >
        <form className="space-y-6" onSubmit={handleSubmit}>
          <div className="grid gap-5 lg:grid-cols-2">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Tên địa điểm</label>
                  <Input
                    value={narrationForm.title}
                    onChange={(event) => {
                      const nextTitle = event.target.value;

                      if (
                        !hasSlugBeenManuallyEdited &&
                        (!form.id || narrationForm.languageCode === form.defaultLanguageCode)
                      ) {
                        setForm((current) => ({
                          ...current,
                          slug: slugify(nextTitle),
                        }));
                      }

                      setNarrationForm((current) => ({
                        ...current,
                        title: nextTitle,
                        seoTitle: current.id ? current.seoTitle : nextTitle,
                      }));
                    }}
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Slug</label>
                  <Input
                    value={form.slug}
                    onChange={(event) => {
                      setHasSlugBeenManuallyEdited(true);
                      setForm((current) => ({
                        ...current,
                        slug: slugify(event.target.value),
                      }));
                    }}
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Danh mục</label>
                  <Select
                    value={form.categoryId}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, categoryId: event.target.value }))
                    }
                  >
                    {state.categories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Ngôn ngữ mặc định</label>
                  <Select
                    value={form.defaultLanguageCode}
                    onChange={(event) => {
                      const previousDefaultLanguage = form.defaultLanguageCode;
                      const nextLanguage = event.target.value as Place["defaultLanguageCode"];
                      setForm((current) => ({
                        ...current,
                        defaultLanguageCode: nextLanguage,
                      }));
                      setFormError("");

                      if (
                        narrationForm.languageCode === previousDefaultLanguage ||
                        !form.id
                      ) {
                        updateNarrationLanguage(form.id, nextLanguage, !form.id ? narrationForm : undefined);
                      }
                    }}
                  >
                    {Object.entries(languageLabels).map(([code, label]) => (
                      <option key={code} value={code}>
                        {label}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Trạng thái</label>
                  <Select
                    value={form.status}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        status: event.target.value as Place["status"],
                      }))
                    }
                  >
                    <option value="draft">Draft</option>
                    <option value="published">Published</option>
                    <option value="archived">Archived</option>
                  </Select>
                </div>
                <div>
                  <label className="field-label">Khoảng giá</label>
                  <Input
                    value={form.priceRange}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, priceRange: event.target.value }))
                    }
                    placeholder="50.000 - 250.000 VND"
                  />
                </div>
              </div>

              <div>
                <label className="field-label">Địa chỉ</label>
                <Input
                  value={form.address}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, address: event.target.value }))
                  }
                  required
                />
              </div>

              <div className="grid gap-5 md:grid-cols-4">
                <div>
                  <label className="field-label">Lat</label>
                  <Input
                    value={form.lat}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, lat: event.target.value }))
                    }
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Lng</label>
                  <Input
                    value={form.lng}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, lng: event.target.value }))
                    }
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Quận</label>
                  <Input
                    value={form.district}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, district: event.target.value }))
                    }
                  />
                </div>
                <div>
                  <label className="field-label">Phường</label>
                  <Input
                    value={form.ward}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, ward: event.target.value }))
                    }
                  />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Thời lượng tham quan (phút)</label>
                  <Input
                    type="number"
                    value={form.averageVisitDuration}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        averageVisitDuration: event.target.value,
                      }))
                    }
                  />
                </div>
                <div>
                  <label className="field-label">Popularity score</label>
                  <Input
                    type="number"
                    value={form.popularityScore}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        popularityScore: event.target.value,
                      }))
                    }
                  />
                </div>
                <div className="flex items-end">
                  <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                    <input
                      type="checkbox"
                      checked={form.featured}
                      onChange={(event) =>
                        setForm((current) => ({
                          ...current,
                          featured: event.target.checked,
                        }))
                      }
                    />
                    Featured location
                  </label>
                </div>
              </div>

              <div>
                <label className="field-label">Tags</label>
                <Input
                  value={form.tags}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, tags: event.target.value }))
                  }
                  placeholder="ốc, hải sản, nhóm bạn"
                />
              </div>
            </div>

            <div>
              <OpenStreetMapPicker
                address={form.address}
                lat={selectedLat}
                lng={selectedLng}
                onAddressResolved={(addressValue) =>
                  setForm((current) => ({
                    ...current,
                    address: addressValue,
                  }))
                }
                onChange={(lat, lng) =>
                  setForm((current) => ({
                    ...current,
                    lat: lat.toFixed(6),
                    lng: lng.toFixed(6),
                  }))
                }
              />
            </div>
          </div>

          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <div className="flex items-center justify-between gap-3">
                <label className="field-label">Mô tả ngắn</label>
                <Select
                  value={narrationForm.languageCode}
                  onChange={(event) =>
                    updateNarrationLanguage(
                      form.id,
                      event.target.value as AudioGuide["languageCode"],
                      !form.id ? narrationForm : undefined,
                    )
                  }
                  className="max-w-[220px]"
                >
                  {Object.entries(languageLabels).map(([code, label]) => (
                    <option key={code} value={code}>
                      {label}
                    </option>
                  ))}
                </Select>
              </div>
              <Textarea
                value={narrationForm.shortText}
                onChange={(event) =>
                  setNarrationForm((current) => ({ ...current, shortText: event.target.value }))
                }
              />
            </div>
            <div>
              <label className="field-label">Nội dung thuyết minh</label>
              <Textarea
                value={narrationForm.fullText}
                onChange={(event) =>
                  setNarrationForm((current) => ({ ...current, fullText: event.target.value }))
                }
              />
            </div>
            <div>
              <label className="field-label">SEO Title</label>
              <Input
                value={narrationForm.seoTitle}
                onChange={(event) =>
                  setNarrationForm((current) => ({ ...current, seoTitle: event.target.value }))
                }
              />
            </div>
            <div>
              <label className="field-label">SEO Description</label>
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

          <div className="space-y-5 rounded-3xl border border-sand-100 bg-sand-50 p-5">
            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
              <div>
                <h2 className="text-base font-semibold text-ink-900">Audio thuyết minh</h2>
                <p className="mt-1 text-sm text-ink-500">
                  Gắn file MP3 hoặc dùng Text-to-Speech từ nội dung thuyết minh mặc định.
                </p>
              </div>
              <div className="rounded-2xl bg-white px-4 py-3 text-sm font-medium text-ink-700">
                Ngôn ngữ TTS: {languageLabels[narrationForm.languageCode]}
              </div>
            </div>

            {narrationForm.languageCode !== form.defaultLanguageCode ? (
              <div className="rounded-2xl bg-white px-4 py-3 text-sm text-ink-600">
                Bạn đang chỉnh bản thuyết minh bằng {languageLabels[narrationForm.languageCode]}.
                Ngôn ngữ mặc định của địa điểm vẫn là {languageLabels[form.defaultLanguageCode]}.
              </div>
            ) : null}

            <div className="grid gap-5 md:grid-cols-2">
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
                  <option value="tts">Text-to-Speech</option>
                  <option value="uploaded">Uploaded MP3</option>
                </Select>
              </div>
              <div>
                <label className="field-label">Trạng thái audio</label>
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
                  File MP3 sẽ được upload lên backend storage trước khi gắn vào địa điểm.
                </p>
                {isUploadingAudio ? (
                  <div className="rounded-2xl bg-white px-4 py-3 text-sm text-ink-600">
                    Đang upload file audio...
                  </div>
                ) : null}
                {audioForm.audioUrl ? (
                  <audio controls src={audioForm.audioUrl} className="w-full" />
                ) : (
                  <div className="rounded-2xl border border-dashed border-sand-200 bg-white px-4 py-3 text-sm text-ink-500">
                    Chưa có file MP3 nào được chọn.
                  </div>
                )}
              </div>
            ) : (
              <div className="rounded-2xl border border-dashed border-sand-200 bg-white px-4 py-3 text-sm text-ink-500">
                Hệ thống sẽ dùng nội dung thuyết minh ở trên để phát bằng Text-to-Speech.
              </div>
            )}

            <div className="rounded-3xl border border-sand-100 bg-white p-4">
              <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                <div className="space-y-3">
                  <p className="text-sm font-semibold text-ink-900">Chế độ nghe thử</p>
                  <div className="max-h-36 overflow-y-auto rounded-2xl bg-sand-50 px-4 py-3 text-sm leading-6 text-ink-600">
                    {previewNarrationText ||
                      "Chưa có nội dung để phát. Hãy nhập mô tả ngắn hoặc nội dung thuyết minh."}
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

          {formError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div>
          ) : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={closeModal}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving || isUploadingAudio}>
              {isSaving ? "Đang lưu..." : form.id ? "Lưu cập nhật" : "Tạo địa điểm"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
