import { useCallback, useEffect, useMemo, useState, type ChangeEvent, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AudioGuide, Poi } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { getCategoryName, getOwnerName, getPoiTranslation, searchPois } from "../../lib/selectors";
import { formatDateTime, formatNumber, languageLabels, slugify } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";
import { useNarrationPreview } from "../media/useNarrationPreview";
import { OpenStreetMapPicker, type PoiMapItem } from "./OpenStreetMapPicker";

const DEFAULT_LAT = 10.7578;
const DEFAULT_LNG = 106.7033;

type PoiFormState = {
  id?: string;
  title: string;
  slug: string;
  address: string;
  lat: string;
  lng: string;
  categoryId: string;
  status: Poi["status"];
  featured: boolean;
  defaultLanguageCode: Poi["defaultLanguageCode"];
  district: string;
  ward: string;
  priceRange: string;
  averageVisitDuration: string;
  popularityScore: string;
  tags: string;
  ownerUserId: string;
  shortText: string;
  fullText: string;
  audioUrl: string;
  audioSourceType: AudioGuide["sourceType"];
  audioStatus: AudioGuide["status"];
};

const createDefaultForm = (categoryId: string): PoiFormState => ({
  title: "",
  slug: "",
  address: "",
  lat: DEFAULT_LAT.toFixed(6),
  lng: DEFAULT_LNG.toFixed(6),
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
  audioUrl: "",
  audioSourceType: "tts",
  audioStatus: "ready",
});

const parseCoordinate = (value: string, fallback: number) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const findPoiAudioGuide = (
  audioGuides: AudioGuide[],
  poiId?: string,
  languageCode?: AudioGuide["languageCode"],
) =>
  audioGuides.find(
    (item) =>
      item.entityType === "poi" &&
      item.entityId === poiId &&
      item.languageCode === languageCode,
  ) ?? null;

const createPoiNarrationCandidate = (
  poi: Poi,
  audioGuide: AudioGuide | null,
  previewText: string,
) => {
  const shouldUseTts = Boolean(previewText);

  return {
    id: audioGuide?.id ?? `poi-preview-${poi.id}-${poi.defaultLanguageCode}`,
    entityId: poi.id,
    languageCode: poi.defaultLanguageCode,
    audioUrl: shouldUseTts ? "" : audioGuide?.audioUrl ?? "",
    sourceType: shouldUseTts ? ("tts" as const) : (audioGuide?.sourceType ?? "tts"),
    previewText: previewText || undefined,
  };
};

export const PoisPage = () => {
  const { state, saveAudioGuide, savePoi } = useAdminData();
  const { user } = useAuth();
  const { previewState, previewAudioGuide, stopPreview } = useNarrationPreview(state);
  const [keyword, setKeyword] = useState("");
  const [statusFilter, setStatusFilter] = useState<Poi["status"] | "all">("all");
  const [selectedPoiId, setSelectedPoiId] = useState<string | null>(null);
  const [isModalOpen, setModalOpen] = useState(false);
  const [isSaving, setSaving] = useState(false);
  const [isUploadingAudio, setUploadingAudio] = useState(false);
  const [formError, setFormError] = useState("");
  const [hasSlugBeenManuallyEdited, setHasSlugBeenManuallyEdited] = useState(false);
  const [form, setForm] = useState<PoiFormState>(() => createDefaultForm(state.categories[0]?.id ?? ""));

  const filteredPois = useMemo(() => {
    const searched = searchPois(state.pois, state, keyword);
    return statusFilter === "all" ? searched : searched.filter((item) => item.status === statusFilter);
  }, [keyword, state, statusFilter]);

  useEffect(() => {
    if (!filteredPois.length) {
      setSelectedPoiId(null);
      return;
    }

    if (!selectedPoiId || !filteredPois.some((item) => item.id === selectedPoiId)) {
      setSelectedPoiId(filteredPois[0].id);
    }
  }, [filteredPois, selectedPoiId]);

  const selectedPoi = state.pois.find((item) => item.id === selectedPoiId) ?? null;
  const selectedTranslation = selectedPoi
    ? getPoiTranslation(state, selectedPoi.id, selectedPoi.defaultLanguageCode)
    : null;
  const selectedAudio = selectedPoi
    ? findPoiAudioGuide(state.audioGuides, selectedPoi.id, selectedPoi.defaultLanguageCode)
    : null;
  const selectedNarrationText = selectedTranslation?.fullText || selectedTranslation?.shortText || "";
  const selectedNarrationCandidate = selectedPoi
    ? createPoiNarrationCandidate(selectedPoi, selectedAudio, selectedNarrationText)
    : null;
  const isSelectedPoiNarrationPlaying =
    selectedNarrationCandidate?.id === previewState.audioGuideId &&
    previewState.status === "playing";

  const mapPois = useMemo<PoiMapItem[]>(
    () =>
      filteredPois.map((poi) => ({
        id: poi.id,
        title: getPoiTranslation(state, poi.id, poi.defaultLanguageCode)?.title ?? poi.slug,
        address: poi.address,
        category: getCategoryName(state, poi.categoryId),
        status: poi.status,
        featured: poi.featured,
        lat: poi.lat,
        lng: poi.lng,
      })),
    [filteredPois, state],
  );

  const handlePoiSelect = useCallback(
    (poiId: string) => {
      setSelectedPoiId(poiId);

      const poi = state.pois.find((item) => item.id === poiId);
      if (!poi) {
        return;
      }

      const translation = getPoiTranslation(state, poi.id, poi.defaultLanguageCode);
      const audioGuide = findPoiAudioGuide(state.audioGuides, poi.id, poi.defaultLanguageCode);
      const previewText = translation?.fullText || translation?.shortText || "";

      void previewAudioGuide(createPoiNarrationCandidate(poi, audioGuide, previewText));
    },
    [previewAudioGuide, state],
  );

  const openCreateModal = () => {
    stopPreview();
    setHasSlugBeenManuallyEdited(false);
    setFormError("");
    setForm(createDefaultForm(state.categories[0]?.id ?? ""));
    setModalOpen(true);
  };

  const openEditModal = (poi: Poi) => {
    stopPreview();
    const translation = getPoiTranslation(state, poi.id, poi.defaultLanguageCode);
    const audioGuide = findPoiAudioGuide(state.audioGuides, poi.id, poi.defaultLanguageCode);

    setHasSlugBeenManuallyEdited(false);
    setFormError("");
    setSelectedPoiId(poi.id);
    setForm({
      id: poi.id,
      title: translation?.title ?? "",
      slug: poi.slug,
      address: poi.address,
      lat: poi.lat.toString(),
      lng: poi.lng.toString(),
      categoryId: poi.categoryId,
      status: poi.status,
      featured: poi.featured,
      defaultLanguageCode: poi.defaultLanguageCode,
      district: poi.district,
      ward: poi.ward,
      priceRange: poi.priceRange,
      averageVisitDuration: poi.averageVisitDuration.toString(),
      popularityScore: poi.popularityScore.toString(),
      tags: poi.tags.join(", "),
      ownerUserId: poi.ownerUserId ?? "",
      shortText: translation?.shortText ?? "",
      fullText: translation?.fullText ?? "",
      audioUrl: audioGuide?.audioUrl ?? "",
      audioSourceType: audioGuide?.sourceType ?? "tts",
      audioStatus: audioGuide?.status ?? "ready",
    });
    setModalOpen(true);
  };

  useEffect(() => {
    if (!selectedPoiId) {
      stopPreview();
    }
  }, [selectedPoiId, stopPreview]);

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
      setForm((current) => ({
        ...current,
        audioUrl: uploaded.url,
        audioSourceType: "uploaded",
        audioStatus: current.audioStatus === "missing" ? "ready" : current.audioStatus,
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
      const savedPoi = await savePoi(
        {
          id: form.id,
          slug: form.slug || slugify(form.title),
          address: form.address,
          lat: parseCoordinate(form.lat, DEFAULT_LAT),
          lng: parseCoordinate(form.lng, DEFAULT_LNG),
          categoryId: form.categoryId,
          status: form.status,
          featured: form.featured,
          defaultLanguageCode: form.defaultLanguageCode,
          district: form.district,
          ward: form.ward,
          priceRange: form.priceRange,
          averageVisitDuration: Number(form.averageVisitDuration),
          popularityScore: Number(form.popularityScore),
          tags: form.tags.split(",").map((item) => item.trim()).filter(Boolean),
          ownerUserId: form.ownerUserId || null,
          title: form.title,
          shortText: form.shortText,
          fullText: form.fullText,
          seoTitle: form.title,
          seoDescription: form.shortText || form.title,
        },
        user,
      );

      if (form.audioUrl || form.audioSourceType === "tts") {
        await saveAudioGuide(
          {
            id: findPoiAudioGuide(state.audioGuides, form.id, form.defaultLanguageCode)?.id,
            entityType: "poi",
            entityId: savedPoi.id,
            languageCode: form.defaultLanguageCode,
            audioUrl: form.audioUrl,
            voiceType: "standard",
            sourceType: form.audioUrl ? form.audioSourceType : "tts",
            status: form.audioStatus,
          },
          user,
        );
      }

      setSelectedPoiId(savedPoi.id);
      setModalOpen(false);
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const columns: DataColumn<Poi>[] = [
    {
      key: "poi",
      header: "POI",
      widthClassName: "min-w-[260px]",
      render: (poi) => {
        const translation = getPoiTranslation(state, poi.id, poi.defaultLanguageCode);
        return (
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-semibold text-ink-900">{translation?.title ?? poi.slug}</p>
              <StatusBadge status={poi.status} />
              {poi.featured ? <StatusBadge status="active" label="Featured" /> : null}
            </div>
            <p className="mt-2 text-sm text-ink-500">{translation?.shortText || poi.address}</p>
          </div>
        );
      },
    },
    {
      key: "map",
      header: "Bản đồ",
      widthClassName: "min-w-[180px]",
      render: (poi) => (
        <div>
          <p className="font-medium text-ink-800">{getCategoryName(state, poi.categoryId)}</p>
          <p className="mt-1 text-xs text-ink-500">{poi.lat.toFixed(5)}, {poi.lng.toFixed(5)}</p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[180px]",
      render: (poi) => (
        <div className="flex gap-2">
          <Button variant="ghost" onClick={() => handlePoiSelect(poi.id)}>
            Xem
          </Button>
          <Button variant="secondary" onClick={() => openEditModal(poi)}>
            Sửa
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
          <div>
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">POI</p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý POI và bản đồ tương tác</h1>
            <p className="mt-3 max-w-3xl text-sm leading-6 text-ink-600">
              Khi chạm bất kỳ điểm nào gần marker trên bản đồ, hệ thống sẽ tự chọn POI gần nhất, hiện thông tin chi tiết và tự động phát thuyết minh của POI đó.
            </p>
          </div>
          <Button onClick={openCreateModal}>Tạo POI</Button>
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-4">
        {[
          ["Tổng POI", formatNumber(state.pois.length)],
          ["Published", formatNumber(state.pois.filter((item) => item.status === "published").length)],
          ["Featured", formatNumber(state.pois.filter((item) => item.featured).length)],
          ["Có audio", formatNumber(state.audioGuides.filter((item) => item.entityType === "poi").length)],
        ].map(([label, value]) => (
          <Card key={label}>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-ink-500">{label}</p>
            <p className="mt-3 text-3xl font-bold text-ink-900">{value}</p>
          </Card>
        ))}
      </section>

      <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_380px]">
        <Card className="space-y-5">
          <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px]">
            <Input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="Tìm POI..." />
            <Select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as Poi["status"] | "all")}>
              <option value="all">Tất cả trạng thái</option>
              <option value="draft">Draft</option>
              <option value="published">Published</option>
              <option value="archived">Archived</option>
            </Select>
          </div>

          <OpenStreetMapPicker
            editable={false}
            pois={mapPois}
            selectedPoiId={selectedPoiId}
            lat={selectedPoi?.lat ?? DEFAULT_LAT}
            lng={selectedPoi?.lng ?? DEFAULT_LNG}
            onPoiSelect={handlePoiSelect}
          />
        </Card>

        <Card className="space-y-4">
          <h2 className="section-heading">Thông tin POI đang chọn</h2>
          {selectedPoi ? (
            <>
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-xl font-semibold text-ink-900">{selectedTranslation?.title ?? selectedPoi.slug}</p>
                <StatusBadge status={selectedPoi.status} />
              </div>
              <p className="text-sm text-ink-600">{selectedPoi.address}</p>
              <div className="grid gap-3 sm:grid-cols-2">
                {[
                  ["Slug", selectedPoi.slug],
                  ["Phân loại", getCategoryName(state, selectedPoi.categoryId)],
                  ["Chủ quản lý", getOwnerName(state, selectedPoi.ownerUserId)],
                  ["Ngôn ngữ", languageLabels[selectedPoi.defaultLanguageCode]],
                  ["Khu vực", `${selectedPoi.ward}, ${selectedPoi.district}`],
                  ["Cập nhật", formatDateTime(selectedPoi.updatedAt)],
                ].map(([label, value]) => (
                  <div key={label} className="rounded-2xl border border-sand-100 bg-sand-50 px-4 py-3">
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-ink-500">{label}</p>
                    <p className="mt-2 text-sm font-medium text-ink-800">{value}</p>
                  </div>
                ))}
              </div>
              <div className="rounded-3xl border border-sand-100 bg-sand-50 p-4">
                <p className="text-sm font-semibold text-ink-900">Mô tả POI</p>
                <p className="mt-3 text-sm leading-6 text-ink-600">
                  {selectedTranslation?.fullText || selectedTranslation?.shortText || "Chưa có thuyết minh cho POI này."}
                </p>
                <p className="mt-3 text-xs text-ink-500">
                  Audio: {selectedAudio ? `${selectedAudio.sourceType} / ${selectedAudio.status}` : "Chưa có"}
                </p>
                {previewState.audioGuideId === selectedNarrationCandidate?.id ? (
                  <p className="mt-3 text-xs text-ink-500">{previewState.message}</p>
                ) : null}
              </div>
              <div className="flex flex-wrap gap-2">
                {selectedPoi.tags.length ? (
                  selectedPoi.tags.map((tag) => (
                    <span key={tag} className="rounded-full bg-sand-50 px-3 py-1 text-xs font-medium text-ink-700 ring-1 ring-sand-200">
                      {tag}
                    </span>
                  ))
                ) : (
                  <p className="text-sm text-ink-500">POI này chưa có tag.</p>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Button onClick={() => handlePoiSelect(selectedPoi.id)}>
                  {isSelectedPoiNarrationPlaying ? "Dung thuyet minh" : "Phat thuyet minh"}
                </Button>
                <Button variant="secondary" onClick={() => openEditModal(selectedPoi)}>
                  Sửa POI này
                </Button>
              </div>
            </>
          ) : (
            <EmptyState
              title="Chưa có POI được chọn"
              description="Hãy chọn một dòng trong danh sách hoặc chạm lên bản đồ để hiện thông tin POI."
            />
          )}
        </Card>
      </section>

      <Card className="space-y-5">
        <div className="flex items-end justify-between gap-3">
          <div>
            <h2 className="section-heading">Danh sách POI</h2>
            <p className="mt-2 text-sm text-ink-500">Module này hiện chỉ quản lý dữ liệu POI.</p>
          </div>
          <p className="text-sm text-ink-500">{formatNumber(filteredPois.length)} POI</p>
        </div>
        {filteredPois.length ? (
          <DataTable data={filteredPois} columns={columns} rowKey={(row) => row.id} />
        ) : (
          <EmptyState title="Không có POI phù hợp" description="Thử đổi từ khóa hoặc bộ lọc để xem lại danh sách." />
        )}
      </Card>

      <Modal
        open={isModalOpen}
        onClose={() => setModalOpen(false)}
        title={form.id ? "Cập nhật POI" : "Tạo POI"}
        description="Điền thông tin POI, chỉnh vị trí trên bản đồ và lưu nội dung thuyết minh mặc định."
        maxWidthClassName="max-w-5xl"
      >
        <form className="space-y-6" onSubmit={handleSubmit}>
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Tên POI</label>
                  <Input
                    value={form.title}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        title: event.target.value,
                        slug: hasSlugBeenManuallyEdited ? current.slug : slugify(event.target.value),
                      }))
                    }
                    required
                  />
                </div>
                <div>
                  <label className="field-label">Slug</label>
                  <Input
                    value={form.slug}
                    onChange={(event) => {
                      setHasSlugBeenManuallyEdited(true);
                      setForm((current) => ({ ...current, slug: event.target.value }));
                    }}
                    required
                  />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-4">
                <div>
                  <label className="field-label">Phân loại</label>
                  <Select value={form.categoryId} onChange={(event) => setForm((current) => ({ ...current, categoryId: event.target.value }))}>
                    {state.categories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Trạng thái</label>
                  <Select value={form.status} onChange={(event) => setForm((current) => ({ ...current, status: event.target.value as Poi["status"] }))}>
                    <option value="draft">Draft</option>
                    <option value="published">Published</option>
                    <option value="archived">Archived</option>
                  </Select>
                </div>
                <div>
                  <label className="field-label">Ngôn ngữ</label>
                  <Select value={form.defaultLanguageCode} onChange={(event) => setForm((current) => ({ ...current, defaultLanguageCode: event.target.value as Poi["defaultLanguageCode"] }))}>
                    {Object.entries(languageLabels).map(([code, label]) => (
                      <option key={code} value={code}>
                        {label}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Khoảng giá</label>
                  <Input value={form.priceRange} onChange={(event) => setForm((current) => ({ ...current, priceRange: event.target.value }))} />
                </div>
              </div>

              <div>
                <label className="field-label">Địa chỉ</label>
                <Input value={form.address} onChange={(event) => setForm((current) => ({ ...current, address: event.target.value }))} required />
              </div>

              <div className="grid gap-5 md:grid-cols-4">
                <div>
                  <label className="field-label">Lat</label>
                  <Input value={form.lat} onChange={(event) => setForm((current) => ({ ...current, lat: event.target.value }))} required />
                </div>
                <div>
                  <label className="field-label">Lng</label>
                  <Input value={form.lng} onChange={(event) => setForm((current) => ({ ...current, lng: event.target.value }))} required />
                </div>
                <div>
                  <label className="field-label">Quận</label>
                  <Input value={form.district} onChange={(event) => setForm((current) => ({ ...current, district: event.target.value }))} />
                </div>
                <div>
                  <label className="field-label">Phường</label>
                  <Input value={form.ward} onChange={(event) => setForm((current) => ({ ...current, ward: event.target.value }))} />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Thời lượng</label>
                  <Input type="number" value={form.averageVisitDuration} onChange={(event) => setForm((current) => ({ ...current, averageVisitDuration: event.target.value }))} />
                </div>
                <div>
                  <label className="field-label">Độ phổ biến</label>
                  <Input type="number" value={form.popularityScore} onChange={(event) => setForm((current) => ({ ...current, popularityScore: event.target.value }))} />
                </div>
                <div className="flex items-end">
                  <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                    <input type="checkbox" checked={form.featured} onChange={(event) => setForm((current) => ({ ...current, featured: event.target.checked }))} />
                    Featured
                  </label>
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Người quản lý</label>
                  <Select value={form.ownerUserId} onChange={(event) => setForm((current) => ({ ...current, ownerUserId: event.target.value }))}>
                    <option value="">Chưa gán</option>
                    {state.users.filter((account) => account.role === "PLACE_OWNER").map((account) => (
                      <option key={account.id} value={account.id}>
                        {account.name}
                      </option>
                    ))}
                  </Select>
                </div>
                <div>
                  <label className="field-label">Tags</label>
                  <Input value={form.tags} onChange={(event) => setForm((current) => ({ ...current, tags: event.target.value }))} />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Mô tả ngắn</label>
                  <Textarea value={form.shortText} onChange={(event) => setForm((current) => ({ ...current, shortText: event.target.value }))} />
                </div>
                <div>
                  <label className="field-label">Thuyết minh</label>
                  <Textarea value={form.fullText} onChange={(event) => setForm((current) => ({ ...current, fullText: event.target.value }))} />
                </div>
              </div>

              <div className="grid gap-5 md:grid-cols-3">
                <div>
                  <label className="field-label">Nguồn audio</label>
                  <Select value={form.audioSourceType} onChange={(event) => setForm((current) => ({ ...current, audioSourceType: event.target.value as AudioGuide["sourceType"] }))}>
                    <option value="tts">Text-to-Speech</option>
                    <option value="uploaded">Uploaded MP3</option>
                  </Select>
                </div>
                <div>
                  <label className="field-label">Trạng thái audio</label>
                  <Select value={form.audioStatus} onChange={(event) => setForm((current) => ({ ...current, audioStatus: event.target.value as AudioGuide["status"] }))}>
                    <option value="ready">Ready</option>
                    <option value="processing">Processing</option>
                    <option value="missing">Missing</option>
                  </Select>
                </div>
                <div>
                  <label className="field-label">Audio URL</label>
                  <Input value={form.audioUrl} onChange={(event) => setForm((current) => ({ ...current, audioUrl: event.target.value }))} placeholder="https://..." />
                </div>
              </div>

              <div className="space-y-3">
                <label className="field-label">Upload MP3</label>
                <input
                  type="file"
                  accept=".mp3,audio/mpeg"
                  onChange={(event) => {
                    void handleAudioFileChange(event);
                  }}
                  className="field-input file:mr-4 file:rounded-2xl file:border-0 file:bg-primary-50 file:px-4 file:py-2 file:font-semibold file:text-primary-700"
                />
                {isUploadingAudio ? <p className="text-sm text-ink-500">Đang upload audio...</p> : null}
              </div>
            </div>

            <OpenStreetMapPicker
              editable
              address={form.address}
              lat={parseCoordinate(form.lat, DEFAULT_LAT)}
              lng={parseCoordinate(form.lng, DEFAULT_LNG)}
              onAddressResolved={(addressValue) => setForm((current) => ({ ...current, address: addressValue }))}
              onChange={(latValue, lngValue) =>
                setForm((current) => ({
                  ...current,
                  lat: latValue.toFixed(6),
                  lng: lngValue.toFixed(6),
                }))
              }
            />
          </div>

          {formError ? <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div> : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving || isUploadingAudio}>
              {isSaving ? "Đang lưu..." : form.id ? "Lưu cập nhật POI" : "Tạo POI"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
