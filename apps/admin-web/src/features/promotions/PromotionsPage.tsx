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
import type { LanguageCode, Promotion } from "../../data/types";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { formatDateTime, languageLabels } from "../../lib/utils";
import { getEntityTranslation, getPoiTitle } from "../../lib/selectors";

type PromotionForm = {
  id?: string;
  poiId: string;
  title: string;
  description: string;
  startAt: string;
  endAt: string;
  status: Promotion["status"];
  languageCode: LanguageCode;
};

const defaultPromotionForm: PromotionForm = {
  poiId: "",
  title: "",
  description: "",
  startAt: "",
  endAt: "",
  status: "upcoming",
  languageCode: "vi",
};

export const PromotionsPage = () => {
  const { state, isBootstrapping, savePromotion, saveTranslation } = useAdminData();
  const { user } = useAuth();
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<PromotionForm>(defaultPromotionForm);

  const resolvePromotionTranslationFields = (promotion: Promotion, languageCode: LanguageCode) => {
    const translation = getEntityTranslation(state, "promotion", promotion.id, languageCode);

    return {
      title: translation?.title ?? promotion.title,
      description: translation?.fullText || translation?.shortText || promotion.description,
    };
  };

  const openModal = (promotion?: Promotion) => {
    const languageCode = state.settings.defaultLanguage;
    setForm(
      promotion
        ? {
          id: promotion.id,
          poiId: promotion.poiId,
          ...resolvePromotionTranslationFields(promotion, languageCode),
          startAt: promotion.startAt.slice(0, 16),
          endAt: promotion.endAt.slice(0, 16),
          status: promotion.status,
          languageCode,
        }
        : { ...defaultPromotionForm, languageCode },
    );
    setModalOpen(true);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    const existingPromotion = form.id
      ? state.promotions.find((item) => item.id === form.id)
      : null;
    const shouldWriteBaseText =
      !existingPromotion ||
      form.languageCode === state.settings.defaultLanguage;
    const savedPromotion = await savePromotion(
      {
        id: form.id,
        poiId: form.poiId,
        title: shouldWriteBaseText ? form.title : existingPromotion?.title ?? form.title,
        description: shouldWriteBaseText ? form.description : existingPromotion?.description ?? form.description,
        startAt: new Date(form.startAt).toISOString(),
        endAt: new Date(form.endAt).toISOString(),
        status: form.status,
      },
      user,
    );
    const existingTranslation = state.translations.find(
      (item) =>
        item.entityType === "promotion" &&
        item.entityId === savedPromotion.id &&
        item.languageCode === form.languageCode,
    );
    await saveTranslation(
      {
        id: existingTranslation?.id,
        entityType: "promotion",
        entityId: savedPromotion.id,
        languageCode: form.languageCode,
        title: form.title,
        shortText: form.description,
        fullText: form.description,
        seoTitle: form.title,
        seoDescription: form.description || form.title,
      },
      user,
    );
    setModalOpen(false);
  };

  const handlePromotionLanguageChange = (languageCode: LanguageCode) => {
    setForm((current) => {
      const promotion = current.id
        ? state.promotions.find((item) => item.id === current.id)
        : null;
      if (!promotion) {
        return { ...current, languageCode };
      }

      return {
        ...current,
        languageCode,
        ...resolvePromotionTranslationFields(promotion, languageCode),
      };
    });
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
        <Button variant="secondary" onClick={() => openModal(item)}>
          Chỉnh sửa
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">Promotions</p>
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
        description="Thông tin khuyến mãi có thể dùng cho banner, push notification và poi detail."
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
          <div>
            <label className="field-label">Ngôn ngữ nội dung ưu đãi</label>
            <Select
              value={form.languageCode}
              onChange={(event) => handlePromotionLanguageChange(event.target.value as LanguageCode)}
            >
              {Object.entries(languageLabels).map(([code, label]) => (
                <option key={code} value={code}>
                  {label}
                </option>
              ))}
            </Select>
          </div>
          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <label className="field-label">Tên ưu đãi</label>
              <Input
                value={form.title}
                onChange={(event) => setForm((current) => ({ ...current, title: event.target.value }))}
                required
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
                <option value="upcoming">Upcoming</option>
                <option value="active">Active</option>
                <option value="expired">Expired</option>
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
          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit">Lưu ưu đãi</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
