import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Button } from "../../components/ui/Button";
import { Input, Textarea } from "../../components/ui/Input";
import { Select } from "../../components/ui/Select";
import { Modal } from "../../components/ui/Modal";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { EmptyState } from "../../components/ui/EmptyState";
import { useAdminData } from "../../data/store";
import { useAuth } from "../auth/AuthContext";
import type { Place } from "../../data/types";
import { formatDateTime, formatNumber, slugify } from "../../lib/utils";
import { getCategoryName, getPlaceTranslation, searchPlaces } from "../../lib/selectors";
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

const DEFAULT_LAT = 10.7578;
const DEFAULT_LNG = 106.7033;

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

export const PlacesPage = () => {
  const { state, savePlace } = useAdminData();
  const { user } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const [keyword, setKeyword] = useState(searchParams.get("keyword") ?? "");
  const [statusFilter, setStatusFilter] = useState<Place["status"] | "all">("all");
  const [isModalOpen, setModalOpen] = useState(false);
  const [isSaving, setSaving] = useState(false);
  const [hasSlugBeenManuallyEdited, setHasSlugBeenManuallyEdited] = useState(false);
  const [form, setForm] = useState<PlaceFormState>(() =>
    createDefaultForm(state.categories[0]?.id ?? ""),
  );

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

  const selectedLat = parseCoordinate(form.lat, DEFAULT_LAT);
  const selectedLng = parseCoordinate(form.lng, DEFAULT_LNG);

  const openCreateModal = () => {
    setHasSlugBeenManuallyEdited(false);
    setForm(createDefaultForm(state.categories[0]?.id ?? ""));
    setModalOpen(true);
  };

  const openEditModal = (place: Place) => {
    const translation = getPlaceTranslation(state, place.id, place.defaultLanguageCode);
    setHasSlugBeenManuallyEdited(false);
    setForm({
      id: place.id,
      title: translation?.title ?? "",
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
      shortText: translation?.shortText ?? "",
      fullText: translation?.fullText ?? "",
      seoTitle: translation?.seoTitle ?? "",
      seoDescription: translation?.seoDescription ?? "",
    });
    setModalOpen(true);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSaving(true);
    await savePlace(
      {
        id: form.id,
        slug: form.slug || slugify(form.title),
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
        title: form.title,
        shortText: form.shortText,
        fullText: form.fullText,
        seoTitle: form.seoTitle,
        seoDescription: form.seoDescription,
      },
      user,
    );
    setSaving(false);
    setModalOpen(false);
  };

  const columns: DataColumn<Place>[] = [
    {
      key: "place",
      header: "Địa điểm",
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
      render: (place) => (
        <div>
          <p className="font-medium text-ink-800">{getCategoryName(state, place.categoryId)}</p>
          <p className="mt-1 text-xs text-ink-500">{place.priceRange}</p>
        </div>
      ),
    },
    {
      key: "location",
      header: "Vị trí",
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
      key: "status",
      header: "Trạng thái",
      render: (place) => <StatusBadge status={place.status} />,
    },
    {
      key: "updated",
      header: "Cập nhật",
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
              Places management
            </p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">
              Quản lý địa điểm, quán ăn và điểm tham quan
            </h1>
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            {[
              ["Tổng điểm", formatNumber(state.places.length)],
              ["Featured", formatNumber(featuredPlaces)],
              ["Draft", formatNumber(draftPlaces)],
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
            <DataTable data={filteredPlaces} columns={columns} rowKey={(row) => row.id} />
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
          <h2 className="section-heading">Bản đồ dữ liệu POI hiện tại</h2>
          <div className="mt-5 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {state.places.slice(0, 4).map((place) => {
              const translation = getPlaceTranslation(state, place.id);
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
                  </div>
                </div>
              );
            })}
          </div>
        </Card>
      </section>

      <Modal
        open={isModalOpen}
        onClose={() => setModalOpen(false)}
        title={form.id ? "Cập nhật địa điểm" : "Tạo địa điểm mới"}
        description="Điền thông tin cơ bản, chọn vị trí trực tiếp trên OpenStreetMap và hoàn thiện nội dung mặc định cho place."
        maxWidthClassName="max-w-6xl"
      >
        <form className="space-y-6" onSubmit={handleSubmit}>
          <div className="grid gap-5 lg:grid-cols-2">
            <div className="space-y-5">
              <div className="grid gap-5 md:grid-cols-2">
                <div>
                  <label className="field-label">Tên địa điểm</label>
                  <Input
                    value={form.title}
                    onChange={(event) => {
                      const nextTitle = event.target.value;

                      setForm((current) => ({
                        ...current,
                        title: nextTitle,
                        slug: hasSlugBeenManuallyEdited ? current.slug : slugify(nextTitle),
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
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        defaultLanguageCode: event.target.value as Place["defaultLanguageCode"],
                      }))
                    }
                  >
                    <option value="vi">Tiếng Việt</option>
                    <option value="en">English</option>
                    <option value="zh-CN">中文</option>
                    <option value="ko">한국어</option>
                    <option value="ja">日本語</option>
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
              <label className="field-label">Mô tả ngắn</label>
              <Textarea
                value={form.shortText}
                onChange={(event) =>
                  setForm((current) => ({ ...current, shortText: event.target.value }))
                }
              />
            </div>
            <div>
              <label className="field-label">Nội dung thuyết minh</label>
              <Textarea
                value={form.fullText}
                onChange={(event) =>
                  setForm((current) => ({ ...current, fullText: event.target.value }))
                }
              />
            </div>
            <div>
              <label className="field-label">SEO Title</label>
              <Input
                value={form.seoTitle}
                onChange={(event) =>
                  setForm((current) => ({ ...current, seoTitle: event.target.value }))
                }
              />
            </div>
            <div>
              <label className="field-label">SEO Description</label>
              <Input
                value={form.seoDescription}
                onChange={(event) =>
                  setForm((current) => ({ ...current, seoDescription: event.target.value }))
                }
              />
            </div>
          </div>

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving}>
              {isSaving ? "Đang lưu..." : form.id ? "Lưu cập nhật" : "Tạo địa điểm"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
