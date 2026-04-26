import { useMemo, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { EmptyState } from "../../components/ui/EmptyState";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { Poi, TourRoute } from "../../data/types";
import { getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { canEditFeaturedRoute, canManageRoute } from "../../lib/rbac";
import { getCategoryName, getPoiById, getPoiTitle } from "../../lib/selectors";
import { formatDateTime, formatNumber, normalizeSearchText } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";
import { TourPoiMap, type TourMapPoi } from "./TourPoiMap";

const legacyTourCopy: Record<string, string> = {
  "Khoi dau 45 phut": "Khởi đầu 45 phút",
  "Hai san buoi toi": "Hải sản buổi tối",
  "Tour ngan cho khach moi den, uu tien cac POI noi bat va nhung mon de tiep can.":
    "Tour ngắn cho khách mới đến, ưu tiên các POI nổi bật và những món dễ tiếp cận.",
  "Tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.":
    "Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.",
  "Minh Anh": "Minh Ánh",
};

const feedbackToneClasses = {
  success: "bg-emerald-50 text-emerald-700",
  error: "bg-rose-50 text-rose-700",
} as const;

const getTourCopy = (value: string) => legacyTourCopy[value.trim()] ?? value.trim();

const buildPoiSearchText = (poi: Poi, state: Parameters<typeof getPoiTitle>[0]) =>
  normalizeSearchText(
    [
      getPoiTitle(state, poi.id),
      poi.address,
      poi.slug,
      getCategoryName(state, poi.categoryId),
      poi.tags.join(" "),
    ].join(" "),
  );

const buildRouteSearchText = (route: TourRoute, state: Parameters<typeof getPoiTitle>[0]) =>
  normalizeSearchText(
    [
      getTourCopy(route.name),
      getTourCopy(route.description),
      route.stopPoiIds.map((poiId) => getPoiTitle(state, poiId)).join(" "),
    ].join(" "),
  );

type TourForm = {
  id?: string;
  name: string;
  description: string;
  stopPoiIds: string[];
  isActive: boolean;
  isFeatured: boolean;
};

const createDefaultTourForm = (): TourForm => ({
  name: "",
  description: "",
  stopPoiIds: [],
  isActive: true,
  isFeatured: false,
});

export const ToursPage = () => {
  const { state, isBootstrapping, saveRoute, deleteRoute } = useAdminData();
  const { user } = useAuth();
  const canFeatureRoute = canEditFeaturedRoute(user?.role);
  const canManageTours = user?.role === "SUPER_ADMIN";

  const [keyword, setKeyword] = useState("");
  const [statusFilter, setStatusFilter] = useState<"all" | "active" | "inactive">("all");
  const [poiKeyword, setPoiKeyword] = useState("");
  const [focusedPoiId, setFocusedPoiId] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<TourForm>(createDefaultTourForm);
  const [feedback, setFeedback] = useState<{
    tone: keyof typeof feedbackToneClasses;
    message: string;
  } | null>(null);
  const [formError, setFormError] = useState("");
  const [deleteError, setDeleteError] = useState("");
  const [isSaving, setSaving] = useState(false);
  const [isDeleting, setDeleting] = useState(false);
  const [routeBeingDeleted, setRouteBeingDeleted] = useState<TourRoute | null>(null);

  const linkedPoiCount = useMemo(
    () => new Set(state.routes.flatMap((route) => route.stopPoiIds)).size,
    [state.routes],
  );

  const averageStopCount = useMemo(() => {
    if (!state.routes.length) {
      return "0";
    }

    const totalStops = state.routes.reduce((sum, route) => sum + route.stopPoiIds.length, 0);
    return (totalStops / state.routes.length).toFixed(1);
  }, [state.routes]);

  const filteredRoutes = useMemo(() => {
    const normalizedKeyword = normalizeSearchText(keyword);

    return [...state.routes]
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

        return buildRouteSearchText(route, state).includes(normalizedKeyword);
      })
      .sort((left, right) => new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime());
  }, [keyword, state, statusFilter]);

  const editingRoute = form.id
    ? state.routes.find((route) => route.id === form.id) ?? null
    : null;

  const selectedStops = useMemo(
    () =>
      form.stopPoiIds.map((poiId, index) => ({
        order: index + 1,
        poiId,
        poi: getPoiById(state, poiId),
      })),
    [form.stopPoiIds, state],
  );

  const selectedMissingCount = useMemo(
    () => selectedStops.filter((item) => !item.poi).length,
    [selectedStops],
  );

  const normalizedPoiKeyword = normalizeSearchText(poiKeyword);
  const mapPois = useMemo<TourMapPoi[]>(() => {
    const selectedPoiIds = new Set(form.stopPoiIds);

    return state.pois
      .filter((poi) => {
        if (selectedPoiIds.has(poi.id)) {
          return true;
        }

        if (!normalizedPoiKeyword) {
          return true;
        }

        return buildPoiSearchText(poi, state).includes(normalizedPoiKeyword);
      })
      .sort((left, right) => getPoiTitle(state, left.id).localeCompare(getPoiTitle(state, right.id)))
      .map((poi) => ({
        id: poi.id,
        title: getPoiTitle(state, poi.id),
        address: poi.address,
        category: getCategoryName(state, poi.categoryId),
        status: poi.status,
        lat: poi.lat,
        lng: poi.lng,
      }));
  }, [form.stopPoiIds, normalizedPoiKeyword, state]);

  const openModal = (route?: TourRoute) => {
    if (!canManageTours) {
      return;
    }

    if (route && !canManageRoute(user, route)) {
      return;
    }

    setFormError("");
    setPoiKeyword("");
    setFocusedPoiId(route?.stopPoiIds[0] ?? null);
    setForm(
      route
        ? {
            id: route.id,
            name: getTourCopy(route.name),
            description: getTourCopy(route.description),
            stopPoiIds: [...route.stopPoiIds],
            isActive: route.isActive,
            isFeatured: route.isFeatured,
          }
        : createDefaultTourForm(),
    );
    setModalOpen(true);
  };

  const closeModal = () => {
    setModalOpen(false);
    setFormError("");
    setPoiKeyword("");
    setFocusedPoiId(null);
  };

  const togglePoiSelection = (poiId: string) => {
    setForm((current) => {
      const exists = current.stopPoiIds.includes(poiId);
      return {
        ...current,
        stopPoiIds: exists
          ? current.stopPoiIds.filter((item) => item !== poiId)
          : [...current.stopPoiIds, poiId],
      };
    });
    setFocusedPoiId(poiId);
    setFormError("");
  };

  const removeStop = (poiId: string) => {
    setForm((current) => ({
      ...current,
      stopPoiIds: current.stopPoiIds.filter((item) => item !== poiId),
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

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    if (!canManageTours) {
      setFormError("Chu quan chi duoc xem tour, khong duoc them/sua/xoa tour.");
      return;
    }

    const normalizedName = form.name.trim();
    if (!normalizedName) {
      setFormError("Tên tour là bắt buộc.");
      return;
    }

    if (form.stopPoiIds.length === 0) {
      setFormError("Tour phải có ít nhất 1 POI được chọn.");
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      await saveRoute(
        {
          id: form.id,
          name: normalizedName,
          description: form.description.trim(),
          isFeatured: canFeatureRoute ? form.isFeatured : editingRoute?.isFeatured ?? false,
          stopPoiIds: form.stopPoiIds,
          isActive: form.isActive,
        },
        user,
      );

      closeModal();
      setFeedback({
        tone: "success",
        message: form.id ? "Cập nhật tour thành công." : "Tạo tour thành công.",
      });
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const openDeleteModal = (route: TourRoute) => {
    if (!canManageRoute(user, route)) {
      return;
    }

    setDeleteError("");
    setRouteBeingDeleted(route);
  };

  const closeDeleteModal = () => {
    if (isDeleting) {
      return;
    }

    setDeleteError("");
    setRouteBeingDeleted(null);
  };

  const handleDelete = async () => {
    if (!routeBeingDeleted) {
      return;
    }

    setDeleting(true);
    setDeleteError("");

    try {
      await deleteRoute(routeBeingDeleted.id);
      setRouteBeingDeleted(null);
      setFeedback({
        tone: "success",
        message: "Xóa tour thành công.",
      });
    } catch (error) {
      setDeleteError(getErrorMessage(error));
    } finally {
      setDeleting(false);
    }
  };

  const columns: DataColumn<TourRoute>[] = [
    {
      key: "tour",
      header: "Tour",
      widthClassName: "min-w-[320px]",
      render: (route) => (
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <p className="font-semibold text-ink-900">{getTourCopy(route.name)}</p>
            <StatusBadge
              status={route.isActive ? "active" : "inactive"}
              label={route.isActive ? "Đang hoạt động" : "Tạm dừng"}
            />
            <StatusBadge
              status={route.isSystemRoute ? "published" : "draft"}
              label={route.isSystemRoute ? "Hệ thống" : "Riêng quán"}
            />
            {route.isFeatured ? <StatusBadge status="published" label="Nổi bật" /> : null}
          </div>
          <p className="mt-2 text-sm text-ink-600">{getTourCopy(route.description) || "Chưa có mô tả ngắn."}</p>
          <p className="mt-2 text-xs text-ink-500">ID: {route.id}</p>
        </div>
      ),
    },
    {
      key: "pois",
      header: "POI Trong Tour",
      widthClassName: "min-w-[280px]",
      render: (route) => (
        <div className="space-y-2">
          {route.stopPoiIds.slice(0, 4).map((poiId, index) => {
            const poi = getPoiById(state, poiId);
            return (
              <div key={`${route.id}-${poiId}-${index}`} className="flex items-center gap-2 text-sm">
                <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-primary-50 text-xs font-semibold text-primary-700">
                  {index + 1}
                </span>
                <span className="font-medium text-ink-800">
                  {poi ? getPoiTitle(state, poi.id) : "POI đã bị xóa"}
                </span>
              </div>
            );
          })}
          {route.stopPoiIds.length > 4 ? (
            <p className="text-xs text-ink-500">+ {route.stopPoiIds.length - 4} POI khác</p>
          ) : null}
          <p className="text-xs text-ink-500">{formatNumber(route.stopPoiIds.length)} điểm dừng</p>
        </div>
      ),
    },
    {
      key: "updated",
      header: "Cập Nhật",
      widthClassName: "min-w-[190px]",
      render: (route) => (
        <div>
          <p className="font-medium text-ink-800">{getTourCopy(route.updatedBy)}</p>
          <p className="mt-1 text-sm text-ink-500">{formatDateTime(route.updatedAt)}</p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "Thao Tác",
      widthClassName: "min-w-[210px]",
      render: (route) => (
        <div className="flex flex-wrap gap-2">
          {canManageTours ? (
            <>
          <Button
            variant="secondary"
            onClick={() => openModal(route)}
            disabled={!canManageRoute(user, route)}
          >
            Chỉnh sửa
          </Button>
          <Button
            variant="danger"
            onClick={() => openDeleteModal(route)}
            disabled={!canManageRoute(user, route)}
          >
            Xóa
          </Button>
            </>
          ) : (
            <span className="rounded-full bg-sand-50 px-3 py-2 text-xs font-semibold text-ink-500">
              Chỉ xem
            </span>
          )}
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
            <h1 className="mt-3 text-3xl font-bold text-ink-900">Tạo và quản lý tour trực tiếp từ bản đồ</h1>
            <p className="mt-3 text-sm leading-6 text-ink-600">
              Chọn POI ngay trên bản đồ để ghép thành tour, theo dõi thứ tự các điểm dừng
              và cập nhật nhanh danh sách POI bất cứ khi nào cần.
            </p>
          </div>
          {canManageTours ? (
          <Button onClick={() => openModal()} disabled={isBootstrapping || !state.pois.length}>
            {isBootstrapping ? "Đang tải dữ liệu..." : "Tạo tour"}
          </Button>
          ) : null}
        </div>
      </Card>

      {feedback ? (
        <div className={`rounded-2xl px-4 py-3 text-sm ${feedbackToneClasses[feedback.tone]}`}>
          {feedback.message}
        </div>
      ) : null}

      <section className="grid gap-4 md:grid-cols-4">
        <Card>
          <p className="text-sm text-ink-500">Tổng tour</p>
          <p className="mt-3 text-3xl font-bold text-ink-900">{formatNumber(state.routes.length)}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Tour đang hoạt động</p>
          <p className="mt-3 text-3xl font-bold text-ink-900">
            {formatNumber(state.routes.filter((route) => route.isActive).length)}
          </p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">POI đã được dùng</p>
          <p className="mt-3 text-3xl font-bold text-ink-900">{formatNumber(linkedPoiCount)}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">TB điểm dừng / tour</p>
          <p className="mt-3 text-3xl font-bold text-ink-900">{averageStopCount}</p>
        </Card>
      </section>

      <Card className="space-y-4">
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_240px]">
          <div>
            <label className="field-label">Tìm tour</label>
            <Input
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="Tên tour, mô tả ngắn, POI..."
            />
          </div>
          <div>
            <label className="field-label">Trạng thái</label>
            <Select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value as "all" | "active" | "inactive")}
            >
              <option value="all">Tất cả</option>
              <option value="active">Đang hoạt động</option>
              <option value="inactive">Tạm dừng</option>
            </Select>
          </div>
        </div>
      </Card>

      <Card>
        {filteredRoutes.length ? (
          <DataTable data={filteredRoutes} columns={columns} rowKey={(row) => row.id} />
        ) : (
          <EmptyState
            title="Chưa có tour phù hợp"
            description="Hãy tạo tour mới hoặc thay đổi bộ lọc để xem danh sách khác."
          />
        )}
      </Card>

      <Modal
        open={modalOpen}
        onClose={closeModal}
        title={form.id ? "Chỉnh sửa tour trên bản đồ" : "Tạo tour trên bản đồ"}
        description="Chạm trực tiếp vào các POI trên bản đồ để thêm hoặc bỏ chọn, sau đó lưu tour với tên và mô tả ngắn."
        maxWidthClassName="max-w-7xl"
      >
        <form
          className="space-y-6"
          onSubmit={handleSubmit}
          onKeyDown={preventImplicitFormSubmit}
          autoComplete="off"
        >
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.65fr)_380px]">
            <Card className="space-y-4 border border-sand-100">
              <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
                <div className="flex-1">
                  <label className="field-label">Lọc POI trên bản đồ</label>
                  <Input
                    value={poiKeyword}
                    onChange={(event) => setPoiKeyword(event.target.value)}
                    placeholder="Tên POI, địa chỉ, loại hình..."
                  />
                </div>
                <div className="rounded-2xl bg-sand-50 px-4 py-3 text-sm font-medium text-ink-600">
                  {formatNumber(mapPois.length)} POI đang hiển thị
                </div>
              </div>

              <TourPoiMap
                pois={mapPois}
                selectedPoiIds={form.stopPoiIds}
                focusedPoiId={focusedPoiId}
                onTogglePoi={togglePoiSelection}
                isVisible={modalOpen}
              />
            </Card>

            <div className="space-y-4">
              <Card className="space-y-4 border border-sand-100">
                <div>
                  <h2 className="section-heading">Thông tin tour</h2>
                  <p className="mt-2 text-sm text-ink-500">
                    Chỉ có thể lưu khi đã nhập tên tour và chọn ít nhất 1 POI.
                  </p>
                </div>

                <div>
                  <label className="field-label">Tên tour</label>
                  <Input
                    value={form.name}
                    onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                    placeholder="Ví dụ: Tour ốc và hải sản Quận 4"
                    required
                  />
                </div>

                <div>
                  <label className="field-label">Mô tả ngắn</label>
                  <Textarea
                    value={form.description}
                    onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
                    placeholder="Mô tả nhanh trải nghiệm, nhóm khách phù hợp hoặc nhịp đi tour."
                    className="min-h-[140px]"
                  />
                </div>

                <div className="space-y-3">
                  <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                    <input
                      type="checkbox"
                      checked={form.isActive}
                      onChange={(event) =>
                        setForm((current) => ({ ...current, isActive: event.target.checked }))
                      }
                    />
                    Đang hoạt động
                  </label>

                  {canFeatureRoute ? (
                    <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm font-medium text-ink-700">
                      <input
                        type="checkbox"
                        checked={form.isFeatured}
                        onChange={(event) =>
                          setForm((current) => ({ ...current, isFeatured: event.target.checked }))
                        }
                      />
                      Đánh dấu nổi bật
                    </label>
                  ) : null}
                </div>

                {editingRoute ? (
                  <div className="grid gap-3 sm:grid-cols-2">
                    <Card className="border border-sand-100 bg-sand-50">
                      <p className="text-sm text-ink-500">Route ID</p>
                      <p className="mt-2 break-all font-semibold text-ink-900">{editingRoute.id}</p>
                    </Card>
                    <Card className="border border-sand-100 bg-sand-50">
                      <p className="text-sm text-ink-500">Cập nhật gần nhất</p>
                      <p className="mt-2 font-semibold text-ink-900">{formatDateTime(editingRoute.updatedAt)}</p>
                    </Card>
                  </div>
                ) : null}
              </Card>

              <Card className="space-y-4 border border-sand-100">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h2 className="section-heading">POI đã chọn</h2>
                    <p className="mt-2 text-sm text-ink-500">
                      Thứ tự dưới đây cũng chính là thứ tự POI trong tour.
                    </p>
                  </div>
                  <div className="rounded-2xl bg-primary-50 px-3 py-2 text-sm font-semibold text-primary-700">
                    {formatNumber(selectedStops.length)} POI
                  </div>
                </div>

                {selectedMissingCount > 0 ? (
                  <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">
                    Có {selectedMissingCount} POI không còn tồn tại. Hãy bỏ các POI này trước khi lưu.
                  </div>
                ) : null}

                <div className="max-h-[420px] space-y-3 overflow-y-auto pr-1">
                  {selectedStops.length ? (
                    selectedStops.map((item, index) => (
                      <div
                        key={`${item.poiId}-${index}`}
                        className="rounded-3xl border border-sand-100 bg-white p-4 shadow-sm"
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0 flex-1">
                            <button
                              type="button"
                              className="w-full text-left"
                              onClick={() => setFocusedPoiId(item.poiId)}
                            >
                              <div className="flex items-center gap-2">
                                <span className="inline-flex h-8 w-8 items-center justify-center rounded-full bg-primary-50 text-sm font-semibold text-primary-700">
                                  {item.order}
                                </span>
                                <p className="truncate font-semibold text-ink-900">
                                  {item.poi ? getPoiTitle(state, item.poi.id) : "POI đã bị xóa"}
                                </p>
                              </div>
                            </button>

                            <p className="mt-2 text-sm text-ink-500">
                              {item.poi?.address ?? "POI này không còn trong hệ thống."}
                            </p>

                            <div className="mt-3 flex flex-wrap gap-2">
                              {item.poi ? (
                                <>
                                  <StatusBadge status={item.poi.status} />
                                  <StatusBadge
                                    status="draft"
                                    label={getCategoryName(state, item.poi.categoryId)}
                                  />
                                </>
                              ) : (
                                <StatusBadge status="hidden" label="Không hợp lệ" />
                              )}
                            </div>
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
                            <Button variant="ghost" onClick={() => removeStop(item.poiId)}>
                              Bỏ chọn
                            </Button>
                          </div>
                        </div>
                      </div>
                    ))
                  ) : (
                    <EmptyState
                      title="Chưa có POI trong tour"
                      description="Chạm trực tiếp vào các POI trên bản đồ để bắt đầu ghép tour."
                    />
                  )}
                </div>
              </Card>
            </div>
          </div>

          {formError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div>
          ) : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={closeModal}>
              Hủy
            </Button>
            <Button
              type="submit"
              disabled={isSaving || !form.name.trim() || form.stopPoiIds.length === 0}
            >
              {isSaving ? "Đang lưu..." : form.id ? "Cập nhật tour" : "Lưu tour"}
            </Button>
          </div>
        </form>
      </Modal>

      <Modal
        open={!!routeBeingDeleted}
        onClose={closeDeleteModal}
        title="Xác nhận xóa tour"
        description="Chỉ xóa tour khi bạn thực sự chắc chắn."
        maxWidthClassName="max-w-xl"
      >
        <div className="space-y-5">
          <p className="text-sm leading-6 text-ink-600">
            Bạn có chắc chắn muốn xóa tour này không?
          </p>

          {routeBeingDeleted ? (
            <div className="rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3">
              <p className="font-semibold text-ink-900">{getTourCopy(routeBeingDeleted.name)}</p>
              <p className="mt-1 text-sm text-ink-500">
                {routeBeingDeleted.stopPoiIds.length} POI trong tour này sẽ bị gỡ liên kết.
              </p>
            </div>
          ) : null}

          {deleteError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{deleteError}</div>
          ) : null}

          <div className="flex justify-end gap-3">
            <Button variant="ghost" onClick={closeDeleteModal} disabled={isDeleting}>
              Hủy
            </Button>
            <Button variant="danger" onClick={handleDelete} disabled={isDeleting}>
              {isDeleting ? "Đang xóa..." : "Xóa tour"}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
};
