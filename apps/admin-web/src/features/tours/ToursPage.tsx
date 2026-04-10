import { useMemo, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { ImageSourceField } from "../../components/ui/ImageSourceField";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { Poi, TourRoute } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { canEditFeaturedRoute, canManageRoute } from "../../lib/rbac";
import { getCategoryName, getPoiById, getPoiTitle } from "../../lib/selectors";
import { formatDateTime, formatNumber, normalizeSearchText } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

const suggestedThemes = [
  "Ăn vặt",
  "Hải sản",
  "Buổi tối",
  "Khách quốc tế",
  "Gia đình",
];

const legacyTourThemeLabels: Record<string, string> = {
  "an vat": "Ăn vặt",
  "hai san": "Hải sản",
  "buoi toi": "Buổi tối",
  "khach quoc te": "Khách quốc tế",
  "gia dinh": "Gia đình",
  "tong hop": "Tổng hợp",
};

const legacyTourCopy: Record<string, string> = {
  "Khoi dau 45 phut": "Khởi đầu 45 phút",
  "Hai san buoi toi": "Hải sản buổi tối",
  "Tour ngan cho khach moi den, uu tien cac POI noi bat va nhung mon de tiep can.":
    "Tour ngắn cho khách mới đến, ưu tiên các POI nổi bật và những món dễ tiếp cận.",
  "Tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.":
    "Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.",
  "Minh Anh": "Minh Ánh",
};

const getTourThemeLabel = (value: string) =>
  legacyTourThemeLabels[normalizeSearchText(value)] ?? value.trim();

const getTourCopy = (value: string) => legacyTourCopy[value.trim()] ?? value.trim();

const routeDifficultyLabels: Record<string, string> = {
  custom: "Tùy chỉnh",
  easy: "Dễ",
  foodie: "Ẩm thực",
};

const getRouteDifficultyLabel = (value: string) =>
  routeDifficultyLabels[normalizeSearchText(value)] ?? value.trim();

type TourForm = {
  id?: string;
  name: string;
  theme: string;
  description: string;
  durationMinutes: string;
  difficulty: string;
  coverImageUrl: string;
  stopPoiIds: string[];
  isFeatured: boolean;
  isActive: boolean;
};

const createDefaultTourForm = (): TourForm => ({
  name: "",
  theme: "",
  description: "",
  durationMinutes: "",
  difficulty: "custom",
  coverImageUrl: "",
  stopPoiIds: [],
  isFeatured: false,
  isActive: true,
});

const getRouteIssues = (
  route: Pick<TourRoute, "stopPoiIds">,
  pois: Poi[],
  foodCountByPoiId: Map<string, number>,
) => {
  let missingPoiCount = 0;
  let inactiveStopCount = 0;
  let noMenuCount = 0;

  route.stopPoiIds.forEach((poiId) => {
    const poi = pois.find((item) => item.id === poiId);
    if (!poi) {
      missingPoiCount++;
      return;
    }

    if (poi.status !== "published") {
      inactiveStopCount++;
    }

    if ((foodCountByPoiId.get(poiId) ?? 0) === 0) {
      noMenuCount++;
    }
  });

  return {
    missingPoiCount,
    inactiveStopCount,
    noMenuCount,
  };
};

export const ToursPage = () => {
  const { state, isBootstrapping, saveRoute } = useAdminData();
  const { user } = useAuth();
  const canFeatureRoute = canEditFeaturedRoute(user?.role);
  const [keyword, setKeyword] = useState("");
  const [themeFilter, setThemeFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState<"all" | "active" | "inactive">("all");
  const [modalOpen, setModalOpen] = useState(false);
  const [poiKeyword, setPoiKeyword] = useState("");
  const [form, setForm] = useState<TourForm>(createDefaultTourForm);
  const [formError, setFormError] = useState("");
  const [pageError, setPageError] = useState("");
  const [isSaving, setSaving] = useState(false);
  const [busyRouteId, setBusyRouteId] = useState<string | null>(null);

  const foodCountByPoiId = useMemo(
    () =>
      state.foodItems.reduce((map, item) => {
        map.set(item.poiId, (map.get(item.poiId) ?? 0) + 1);
        return map;
      }, new Map<string, number>()),
    [state.foodItems],
  );

  const availableThemes = useMemo(
    () =>
      Array.from(
        new Set(
          [...suggestedThemes, ...state.routes.map((item) => getTourThemeLabel(item.theme)).filter(Boolean)].sort(
            (left, right) => left.localeCompare(right),
          ),
        ),
      ),
    [state.routes],
  );

  const filteredRoutes = useMemo(() => {
    const normalizedKeyword = normalizeSearchText(keyword);

    return [...state.routes]
      .filter((route) => themeFilter === "all" || getTourThemeLabel(route.theme) === themeFilter)
      .filter((route) => {
        if (statusFilter === "all") {
          return true;
        }

        return statusFilter === "active" ? route.isActive : !route.isActive;
      })
      .filter((route) => {
        if (!normalizedKeyword) {
          return true;
        }

        const stopTitles = route.stopPoiIds.map((poiId) => getPoiTitle(state, poiId)).join(" ");
        const haystack = normalizeSearchText(
          [
            getTourCopy(route.name),
            getTourThemeLabel(route.theme),
            getTourCopy(route.description),
            getRouteDifficultyLabel(route.difficulty),
            stopTitles,
          ].join(" "),
        );
        return haystack.includes(normalizedKeyword);
      })
      .sort((left, right) => {
        const featuredCompare = Number(right.isFeatured) - Number(left.isFeatured);
        if (featuredCompare !== 0) {
          return featuredCompare;
        }

        const activeCompare = Number(right.isActive) - Number(left.isActive);
        if (activeCompare !== 0) {
          return activeCompare;
        }

        return new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime();
      });
  }, [keyword, state, statusFilter, themeFilter]);

  const routesNeedingReview = useMemo(
    () =>
      state.routes.filter((route) => {
        const issues = getRouteIssues(route, state.pois, foodCountByPoiId);
        return issues.missingPoiCount > 0 || issues.inactiveStopCount > 0 || issues.noMenuCount > 0;
      }).length,
    [foodCountByPoiId, state.pois, state.routes],
  );

  const linkedPoiCount = useMemo(
    () => new Set(state.routes.flatMap((route) => route.stopPoiIds)).size,
    [state.routes],
  );

  const selectedStops = useMemo(
    () =>
      form.stopPoiIds.map((poiId, index) => ({
        order: index + 1,
        poiId,
        poi: getPoiById(state, poiId),
        foodCount: foodCountByPoiId.get(poiId) ?? 0,
      })),
    [foodCountByPoiId, form.stopPoiIds, state],
  );

  const availablePois = useMemo(() => {
    const selectedPoiIds = new Set(form.stopPoiIds);
    const normalizedKeyword = normalizeSearchText(poiKeyword);

    return state.pois
      .filter((poi) => !selectedPoiIds.has(poi.id))
      .filter((poi) => {
        if (!normalizedKeyword) {
          return true;
        }

        const haystack = normalizeSearchText(
          [getPoiTitle(state, poi.id), poi.address, poi.slug, getCategoryName(state, poi.categoryId)].join(
            " ",
          ),
        );
        return haystack.includes(normalizedKeyword);
      })
      .sort((left, right) =>
        getPoiTitle(state, left.id).localeCompare(getPoiTitle(state, right.id)),
      );
  }, [form.stopPoiIds, poiKeyword, state]);

  const openModal = (route?: TourRoute) => {
    if (route && !canManageRoute(user, route)) {
      return;
    }

    setFormError("");
    setPoiKeyword("");
    setForm(
      route
        ? {
            id: route.id,
            name: getTourCopy(route.name),
            theme: getTourThemeLabel(route.theme),
            description: getTourCopy(route.description),
            durationMinutes: route.durationMinutes.toString(),
            difficulty: route.difficulty,
            coverImageUrl: route.coverImageUrl,
            stopPoiIds: [...route.stopPoiIds],
            isFeatured: route.isFeatured,
            isActive: route.isActive,
          }
        : createDefaultTourForm(),
    );
    setModalOpen(true);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      await saveRoute(
        {
          id: form.id,
          name: getTourCopy(form.name.trim()),
          theme: getTourThemeLabel(form.theme.trim()),
          description: getTourCopy(form.description.trim()),
          durationMinutes: Number(form.durationMinutes),
          difficulty: form.difficulty,
          coverImageUrl: form.coverImageUrl.trim(),
          stopPoiIds: form.stopPoiIds,
          isFeatured: form.isFeatured,
          isActive: form.isActive,
        },
        user,
      );

      setModalOpen(false);
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const handleToggleRoute = async (route: TourRoute) => {
    if (!user || !canManageRoute(user, route)) {
      return;
    }

    setBusyRouteId(route.id);
    setPageError("");

    try {
      await saveRoute(
        {
          id: route.id,
          name: getTourCopy(route.name),
          theme: getTourThemeLabel(route.theme),
          description: getTourCopy(route.description),
          durationMinutes: route.durationMinutes,
          difficulty: route.difficulty,
          coverImageUrl: route.coverImageUrl,
          stopPoiIds: route.stopPoiIds,
          isFeatured: route.isFeatured,
          isActive: !route.isActive,
        },
        user,
      );
    } catch (error) {
      setPageError(getErrorMessage(error));
    } finally {
      setBusyRouteId(null);
    }
  };

  const addStop = (poiId: string) => {
    setForm((current) => ({
      ...current,
      stopPoiIds: current.stopPoiIds.includes(poiId)
        ? current.stopPoiIds
        : [...current.stopPoiIds, poiId],
    }));
  };

  const removeStop = (index: number) => {
    setForm((current) => ({
      ...current,
      stopPoiIds: current.stopPoiIds.filter((_, itemIndex) => itemIndex !== index),
    }));
  };

  const moveStop = (index: number, direction: -1 | 1) => {
    setForm((current) => {
      const nextIndex = index + direction;
      if (nextIndex < 0 || nextIndex >= current.stopPoiIds.length) {
        return current;
      }

      const stopPoiIds = [...current.stopPoiIds];
      const currentValue = stopPoiIds[index];
      stopPoiIds[index] = stopPoiIds[nextIndex];
      stopPoiIds[nextIndex] = currentValue;

      return {
        ...current,
        stopPoiIds,
      };
    });
  };

  const editingRoute = form.id ? state.routes.find((item) => item.id === form.id) ?? null : null;

  const columns: DataColumn<TourRoute>[] = [
    {
      key: "tour",
      header: "Tên tour",
      widthClassName: "min-w-[320px]",
      render: (item) => (
        <div className="flex items-center gap-3">
          {item.coverImageUrl ? (
            <img
              src={item.coverImageUrl}
              alt={getTourCopy(item.name)}
              className="h-16 w-16 rounded-3xl object-cover"
            />
          ) : (
            <div className="flex h-16 w-16 items-center justify-center rounded-3xl bg-sand-100 text-[11px] font-semibold uppercase tracking-[0.18em] text-ink-500">
              Tour
            </div>
          )}
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-semibold text-ink-900">{getTourCopy(item.name)}</p>
              <StatusBadge status="draft" label={getTourThemeLabel(item.theme)} />
              {item.isFeatured ? <StatusBadge status="published" label="Nổi bật" /> : null}
              <StatusBadge status={item.isSystemRoute ? "active" : "draft"} label={item.isSystemRoute ? "Hệ thống" : "Riêng quán"} />
            </div>
            <p className="mt-1 text-sm text-ink-500">{getTourCopy(item.description)}</p>
            <p className="mt-1 text-xs text-ink-500">ID: {item.id}</p>
          </div>
        </div>
      ),
    },
    {
      key: "stops",
      header: "Điểm đến",
      widthClassName: "min-w-[280px]",
      render: (item) => {
        const issues = getRouteIssues(item, state.pois, foodCountByPoiId);

        return (
          <div className="space-y-2">
            {item.stopPoiIds.slice(0, 3).map((poiId, index) => {
              const poi = getPoiById(state, poiId);

              return (
                <div key={`${item.id}-${poiId}-${index}`} className="flex items-center gap-2 text-sm">
                  <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-sand-100 text-xs font-semibold text-ink-700">
                    {index + 1}
                  </span>
                  <span className="font-medium text-ink-800">
                    {poi ? getPoiTitle(state, poi.id) : "POI đã bị xóa"}
                  </span>
                  {poi ? (
                    <StatusBadge status={poi.status} />
                  ) : (
                    <StatusBadge status="hidden" label="Thiếu" />
                  )}
                </div>
              );
            })}
            {item.stopPoiIds.length > 3 ? (
              <p className="text-xs text-ink-500">+ {item.stopPoiIds.length - 3} điểm đến khác</p>
            ) : null}
            {issues.missingPoiCount > 0 || issues.inactiveStopCount > 0 || issues.noMenuCount > 0 ? (
              <p className="text-xs text-amber-700">
                Cần soát lại: {issues.missingPoiCount} điểm thiếu, {issues.inactiveStopCount} điểm chưa xuất
                bản, {issues.noMenuCount} điểm chưa có món ăn.
              </p>
            ) : (
              <p className="text-xs text-emerald-700">Đang bám vào dữ liệu POI hiện tại.</p>
            )}
          </div>
        );
      },
    },
    {
      key: "duration",
      header: "Thời lượng",
      widthClassName: "min-w-[120px]",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">{formatNumber(item.durationMinutes)} phút</p>
          <p className="mt-1 text-xs text-ink-500">Độ khó: {getRouteDifficultyLabel(item.difficulty)}</p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      widthClassName: "min-w-[180px]",
      render: (item) => (
        <div className="space-y-2">
          <StatusBadge
            status={item.isActive ? "active" : "inactive"}
            label={item.isActive ? "Đang hoạt động" : "Tạm dừng"}
          />
          <StatusBadge status={item.isFeatured ? "published" : "draft"} label={item.isFeatured ? "Nổi bật" : "Thường"} />
        </div>
      ),
    },
    {
      key: "updated",
      header: "Cập nhật",
      widthClassName: "min-w-[180px]",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">{getTourCopy(item.updatedBy)}</p>
          <p className="mt-1 text-sm text-ink-500">{formatDateTime(item.updatedAt)}</p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[210px]",
      render: (item) => (
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => openModal(item)} disabled={!canManageRoute(user, item)}>
            Chỉnh sửa
          </Button>
          <Button
            variant="ghost"
            onClick={() => {
              void handleToggleRoute(item);
            }}
            disabled={busyRouteId === item.id || !canManageRoute(user, item)}
          >
            {busyRouteId === item.id
              ? "Đang cập nhật..."
              : item.isActive
                ? "Tắt tour"
                : "Bật tour"}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-4xl">
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">Quản lý tour</p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý tour theo chủ đề và lộ trình điểm đến</h1>
            <p className="mt-3 text-sm leading-6 text-ink-600">
              Tour lưu theo liên kết POI, vì vậy khi quán đổi menu, giờ mở cửa hoặc tạm dừng kinh doanh,
              bạn chỉ cần cập nhật POI hoặc món ăn rồi quay lại đây để sắp xếp lại điểm dừng khi cần.
            </p>
          </div>
          <Button onClick={() => openModal()} disabled={isBootstrapping || !state.pois.length}>
            {isBootstrapping ? "Đang tải dữ liệu..." : "Tạo tour"}
          </Button>
        </div>
      </Card>

      <section className="grid gap-4 md:grid-cols-4">
        {[
          ["Tổng tour", state.routes.length],
          ["Tour đang hoạt động", state.routes.filter((item) => item.isActive).length],
          ["POI đang được gán", linkedPoiCount],
          ["Tour cần soát lại", routesNeedingReview],
        ].map(([label, value]) => (
          <Card key={label}>
            <p className="text-sm text-ink-500">{label}</p>
            <p className="mt-3 text-3xl font-bold text-ink-900">{formatNumber(Number(value))}</p>
          </Card>
        ))}
      </section>

      <Card className="space-y-4">
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_220px_220px]">
          <div>
            <label className="field-label">Tìm tour</label>
            <Input
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="Tên tour, chủ đề, điểm đến..."
            />
          </div>
          <div>
            <label className="field-label">Chủ đề</label>
            <Select value={themeFilter} onChange={(event) => setThemeFilter(event.target.value)}>
              <option value="all">Tất cả chủ đề</option>
              {availableThemes.map((theme) => (
                <option key={theme} value={theme}>
                  {theme}
                </option>
              ))}
            </Select>
          </div>
          <div>
            <label className="field-label">Trạng thái</label>
            <Select
              value={statusFilter}
              onChange={(event) =>
                setStatusFilter(event.target.value as "all" | "active" | "inactive")
              }
            >
              <option value="all">Tất cả</option>
              <option value="active">Đang hoạt động</option>
              <option value="inactive">Tạm dừng</option>
            </Select>
          </div>
        </div>
        {pageError ? (
          <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{pageError}</div>
        ) : null}
      </Card>

      <Card>
        {filteredRoutes.length ? (
          <DataTable data={filteredRoutes} columns={columns} rowKey={(row) => row.id} />
        ) : (
          <EmptyState
            title="Chưa có tour phù hợp"
            description="Hãy tạo tour mới hoặc đổi bộ lọc để xem danh sách khác."
          />
        )}
      </Card>

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={form.id ? "Cập nhật tour" : "Tạo tour mới"}
        description="Gắn nhiều địa điểm vào một tour, sắp xếp thứ tự điểm đến và bật hoặc tắt tour khi cần."
        maxWidthClassName="max-w-6xl"
      >
        <form className="space-y-6" onSubmit={handleSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
          <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-4">
            <div className="xl:col-span-2">
              <label className="field-label">Tên tour</label>
              <Input
                value={form.name}
                onChange={(event) =>
                  setForm((current) => ({ ...current, name: event.target.value }))
                }
                required
              />
            </div>
            <div>
              <label className="field-label">Chủ đề</label>
              <Input
                value={form.theme}
                onChange={(event) =>
                  setForm((current) => ({ ...current, theme: event.target.value }))
                }
                required
              />
            </div>
            <div>
              <label className="field-label">Thời lượng (phút)</label>
              <Input
                type="number"
                min={1}
                value={form.durationMinutes}
                onChange={(event) =>
                  setForm((current) => ({ ...current, durationMinutes: event.target.value }))
                }
                required
              />
            </div>
            <div>
              <label className="field-label">Độ khó</label>
              <Select
                value={form.difficulty}
                onChange={(event) =>
                  setForm((current) => ({ ...current, difficulty: event.target.value }))
                }
              >
                <option value="custom">Tùy chỉnh</option>
                <option value="easy">Dễ</option>
                <option value="foodie">Ẩm thực</option>
              </Select>
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            {suggestedThemes.map((theme) => (
              <Button
                key={theme}
                variant={form.theme === theme ? "primary" : "secondary"}
                onClick={() => setForm((current) => ({ ...current, theme }))}
              >
                {theme}
              </Button>
            ))}
          </div>

          <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_280px]">
            <div>
              <label className="field-label">Mô tả tour</label>
              <Textarea
                value={form.description}
                onChange={(event) =>
                  setForm((current) => ({ ...current, description: event.target.value }))
                }
                placeholder="Tóm tắt trải nghiệm, đối tượng phù hợp và cách đi tour."
              />
            </div>
            <div className="flex items-end">
              <div className="w-full space-y-3">
                <label className="flex w-full items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                  <input
                    type="checkbox"
                    checked={form.isActive}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, isActive: event.target.checked }))
                    }
                  />
                  Đang hoạt động
                </label>
                <label className="flex w-full items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                  <input
                    type="checkbox"
                    checked={form.isFeatured}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, isFeatured: event.target.checked }))
                    }
                    disabled={!canFeatureRoute}
                  />
                  {canFeatureRoute ? "Tour nổi bật" : "Tour nổi bật (chỉ super admin)"}
                </label>
              </div>
            </div>
          </div>

          {editingRoute ? (
            <div className="grid gap-4 md:grid-cols-3">
              <Card className="border border-sand-100 bg-sand-50">
                <p className="text-sm text-ink-500">Route ID</p>
                <p className="mt-2 break-all font-semibold text-ink-900">{editingRoute.id}</p>
              </Card>
              <Card className="border border-sand-100 bg-sand-50">
                <p className="text-sm text-ink-500">Cập nhật bởi</p>
                <p className="mt-2 font-semibold text-ink-900">{getTourCopy(editingRoute.updatedBy)}</p>
              </Card>
              <Card className="border border-sand-100 bg-sand-50">
                <p className="text-sm text-ink-500">Cập nhật lúc</p>
                <p className="mt-2 font-semibold text-ink-900">{formatDateTime(editingRoute.updatedAt)}</p>
              </Card>
            </div>
          ) : null}

          <ImageSourceField
            label="Ảnh đại diện tour"
            value={form.coverImageUrl}
            onChange={(value) => setForm((current) => ({ ...current, coverImageUrl: value }))}
            onUpload={async (file) => (await adminApi.uploadFile(file, "images/tours")).url}
          />

          <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
            <Card className="space-y-4 border border-sand-100">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <h2 className="section-heading">POI có sẵn</h2>
                  <p className="mt-2 text-sm text-ink-500">Chọn các quán và địa điểm để đưa vào tour.</p>
                </div>
                <p className="text-sm text-ink-500">{availablePois.length} POI</p>
              </div>
              <Input
                value={poiKeyword}
                onChange={(event) => setPoiKeyword(event.target.value)}
                placeholder="Tìm theo tên, địa chỉ, phân loại..."
              />
              <div className="max-h-[420px] space-y-3 overflow-y-auto pr-1">
                {availablePois.length ? (
                  availablePois.map((poi) => (
                    <div
                      key={poi.id}
                      className="rounded-3xl border border-sand-100 bg-white p-4 shadow-sm"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="font-semibold text-ink-900">{getPoiTitle(state, poi.id)}</p>
                          <p className="mt-1 text-sm text-ink-500">{poi.address}</p>
                          <div className="mt-2 flex flex-wrap gap-2">
                            <StatusBadge status={poi.status} />
                            <StatusBadge status="draft" label={getCategoryName(state, poi.categoryId)} />
                          </div>
                        </div>
                        <Button onClick={() => addStop(poi.id)}>Thêm</Button>
                      </div>
                    </div>
                  ))
                ) : (
                  <EmptyState
                    title="Không còn POI phù hợp"
                    description="Thử đổi từ khóa tìm kiếm hoặc bớt các điểm đến đã chọn."
                  />
                )}
              </div>
            </Card>

            <Card className="space-y-4 border border-sand-100">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <h2 className="section-heading">Lộ trình tour</h2>
                  <p className="mt-2 text-sm text-ink-500">
                    Sắp xếp điểm dừng theo đúng thứ tự di chuyển. Thông tin menu và trạng thái sẽ lấy từ POI hiện tại.
                  </p>
                </div>
                <p className="text-sm text-ink-500">{selectedStops.length} điểm dừng</p>
              </div>
              <div className="max-h-[420px] space-y-3 overflow-y-auto pr-1">
                {selectedStops.length ? (
                  selectedStops.map((item, index) => (
                    <div
                      key={`${item.poiId}-${index}`}
                      className="rounded-3xl border border-sand-100 bg-white p-4 shadow-sm"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="inline-flex h-8 w-8 items-center justify-center rounded-full bg-primary-50 text-sm font-semibold text-primary-700">
                              {item.order}
                            </span>
                            <p className="font-semibold text-ink-900">
                              {item.poi ? getPoiTitle(state, item.poi.id) : "POI đã bị xóa"}
                            </p>
                          </div>
                          <p className="mt-2 text-sm text-ink-500">
                            {item.poi?.address ?? "Cần thay điểm dừng này bằng một POI còn tồn tại."}
                          </p>
                          <div className="mt-3 flex flex-wrap gap-2">
                            {item.poi ? (
                              <>
                                <StatusBadge status={item.poi.status} />
                                <StatusBadge status="draft" label={`${item.foodCount} món`} />
                              </>
                            ) : (
                              <StatusBadge status="hidden" label="Thiếu" />
                            )}
                          </div>
                          {item.poi ? (
                            <p className="mt-3 text-xs text-ink-500">
                              Cập nhật POI: {formatDateTime(item.poi.updatedAt)}
                            </p>
                          ) : null}
                          {!item.poi ? (
                            <p className="mt-2 text-xs text-rose-700">
                              Điểm dừng này không còn hợp lệ. Hãy xóa hoặc thay bằng một POI khác.
                            </p>
                          ) : null}
                          {item.poi && item.poi.status !== "published" ? (
                            <p className="mt-2 text-xs text-amber-700">
                              POI này chưa ở trạng thái xuất bản. Cần cân nhắc tắt tour hoặc đổi điểm dừng.
                            </p>
                          ) : null}
                          {item.poi && item.foodCount === 0 ? (
                            <p className="mt-2 text-xs text-amber-700">
                              POI này chưa có món ăn. Nếu quán đổi menu, hãy cập nhật bên module Món ăn.
                            </p>
                          ) : null}
                        </div>
                        <div className="flex flex-col gap-2">
                          <Button
                            variant="secondary"
                            onClick={() => moveStop(index, -1)}
                            disabled={index === 0}
                          >
                            Lên
                          </Button>
                          <Button
                            variant="secondary"
                            onClick={() => moveStop(index, 1)}
                            disabled={index === selectedStops.length - 1}
                          >
                            Xuống
                          </Button>
                          <Button variant="ghost" onClick={() => removeStop(index)}>
                            Bỏ
                          </Button>
                        </div>
                      </div>
                    </div>
                  ))
                ) : (
                  <EmptyState
                    title="Chưa có điểm đến"
                    description="Thêm POI ở cột bên trái để tạo lộ trình tour."
                  />
                )}
              </div>
            </Card>
          </div>

          {formError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div>
          ) : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving || form.stopPoiIds.length === 0}>
              {isSaving ? "Đang lưu..." : form.id ? "Lưu tour" : "Tạo tour"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
