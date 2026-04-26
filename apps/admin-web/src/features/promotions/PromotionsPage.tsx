import { useState, type FormEvent } from "react";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Button } from "../../components/ui/Button";
import { Modal } from "../../components/ui/Modal";
import { Input, Textarea } from "../../components/ui/Input";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import { useAuth } from "../auth/AuthContext";
import type { Promotion } from "../../data/types";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { formatDateTime } from "../../lib/utils";
import { getPoiTitle } from "../../lib/selectors";
import { adminApi, getErrorMessage } from "../../lib/api";

type PromotionForm = {
  id?: string;
  poiId: string;
  title: string;
  description: string;
  startAt: string;
  endAt: string;
  visibleFrom: string;
  status: Promotion["status"];
};

const defaultPromotionForm: PromotionForm = {
  poiId: "",
  title: "",
  description: "",
  startAt: "",
  endAt: "",
  visibleFrom: "",
  status: "upcoming",
};

export const PromotionsPage = () => {
  const { state, isBootstrapping, refreshData, savePromotion } = useAdminData();
  const { user } = useAuth();
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<PromotionForm>(defaultPromotionForm);
  const [formError, setFormError] = useState("");

  const openModal = (promotion?: Promotion) => {
    setForm(
      promotion
        ? {
            id: promotion.id,
            poiId: promotion.poiId,
            title: promotion.title,
            description: promotion.description,
            startAt: promotion.startAt.slice(0, 16),
            endAt: promotion.endAt.slice(0, 16),
            visibleFrom: (promotion.visibleFrom ?? promotion.startAt).slice(0, 16),
            status: promotion.status,
          }
        : { ...defaultPromotionForm },
    );
    setFormError("");
    setModalOpen(true);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setFormError("");
    if (form.status === "upcoming" && !form.visibleFrom && !form.startAt) {
      setFormError("Ưu đãi ở trạng thái sắp diễn ra bắt buộc có thời gian áp dụng/hiển thị.");
      return;
    }

    try {
      await savePromotion(
        {
          id: form.id,
          poiId: form.poiId,
          title: form.title,
          description: form.description,
          startAt: new Date(form.startAt).toISOString(),
          endAt: new Date(form.endAt).toISOString(),
          visibleFrom: new Date(form.visibleFrom || form.startAt).toISOString(),
          status: form.status,
        },
        user,
      );

      setModalOpen(false);
    } catch (error) {
      setFormError(getErrorMessage(error));
    }
  };

  const handleDelete = async (promotion: Promotion) => {
    if (!window.confirm(`Xóa ưu đãi "${promotion.title}"?`)) {
      return;
    }

    try {
      setFormError("");
      await adminApi.deletePromotion(promotion.id);
      await refreshData();
    } catch (error) {
      setFormError(getErrorMessage(error));
    }
  };

  const columns: DataColumn<Promotion>[] = [
    {
      key: "promotion",
      header: "Ưu đãi",
      render: (item) => (
        <div>
          <p className="font-semibold text-ink-900">{item.title}</p>
          <p className="mt-1 text-sm text-ink-500">{item.description}</p>
        </div>
      ),
    },
    {
      key: "poi",
      header: "POI",
      render: (item) => <p className="font-medium text-ink-800">{getPoiTitle(state, item.poiId)}</p>,
    },
    {
      key: "window",
      header: "Hiệu lực",
      render: (item) => (
        <div>
          <p className="text-sm text-ink-600">{formatDateTime(item.startAt)}</p>
          <p className="mt-1 text-sm text-ink-600">{formatDateTime(item.endAt)}</p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      render: (item) => <StatusBadge status={item.status} />,
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (item) => (
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => openModal(item)}>
            Chỉnh sửa
          </Button>
          <Button variant="danger" onClick={() => void handleDelete(item)}>
            Xóa
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">Ưu đãi</p>
            <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý ưu đãi, sự kiện và thông báo chiến dịch</h1>
          </div>
          <Button onClick={() => openModal()} disabled={isBootstrapping}>
            {isBootstrapping ? "Đang tải dữ liệu..." : "Tạo ưu đãi"}
          </Button>
        </div>
      </Card>

      <Card>
        <DataTable data={state.promotions} columns={columns} rowKey={(row) => row.id} />
      </Card>

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={form.id ? "Cập nhật ưu đãi" : "Tạo ưu đãi mới"}
        description="Nhập một bộ nội dung gốc cho ưu đãi. Hệ thống sẽ tự dịch và hiển thị theo ngôn ngữ người dùng chọn trong app."
      >
        <form className="space-y-5" onSubmit={handleSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
          <div>
            <label className="field-label">POI</label>
            <Select
              value={form.poiId}
              required
              onChange={(event) => setForm((current) => ({ ...current, poiId: event.target.value }))}
            >
              <option value="">Chọn POI</option>
              {state.pois.map((poi) => (
                <option key={poi.id} value={poi.id}>
                  {getPoiTitle(state, poi.id)}
                </option>
              ))}
            </Select>
          </div>
          <div className="grid gap-5 md:grid-cols-3">
            <div>
              <label className="field-label">Tên ưu đãi</label>
              <Input
                value={form.title}
                onChange={(event) => setForm((current) => ({ ...current, title: event.target.value }))}
                required
              />
            </div>
            <div>
              <label className="field-label">Hiển thị trên app từ</label>
              <Input
                type="datetime-local"
                value={form.visibleFrom}
                onChange={(event) => setForm((current) => ({ ...current, visibleFrom: event.target.value }))}
                required={form.status === "upcoming"}
              />
            </div>
            <div>
              <label className="field-label">Trạng thái</label>
              <Select
                value={form.status}
                onChange={(event) =>
                  setForm((current) => ({ ...current, status: event.target.value as Promotion["status"] }))
                }
              >
                <option value="upcoming">Sắp diễn ra</option>
                <option value="active">Đang hoạt động</option>
              </Select>
            </div>
          </div>
          <div>
            <label className="field-label">Mô tả</label>
            <Textarea
              value={form.description}
              onChange={(event) =>
                setForm((current) => ({ ...current, description: event.target.value }))
              }
            />
          </div>
          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <label className="field-label">Bắt đầu</label>
              <Input
                type="datetime-local"
                value={form.startAt}
                onChange={(event) => setForm((current) => ({ ...current, startAt: event.target.value }))}
                required
              />
            </div>
            <div>
              <label className="field-label">Kết thúc</label>
              <Input
                type="datetime-local"
                value={form.endAt}
                onChange={(event) => setForm((current) => ({ ...current, endAt: event.target.value }))}
                required
              />
            </div>
          </div>
          {formError ? <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div> : null}
          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" type="button" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit">Lưu ưu đãi</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
