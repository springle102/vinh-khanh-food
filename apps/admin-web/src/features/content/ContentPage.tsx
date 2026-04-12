import { useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { ImageSourceField } from "../../components/ui/ImageSourceField";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { Select } from "../../components/ui/Select";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { FoodItem, LanguageCode } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { getEntityTranslation, getPoiTitle } from "../../lib/selectors";
import { languageLabels } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

type FoodForm = {
  id?: string;
  poiId: string;
  name: string;
  description: string;
  priceRange: string;
  imageUrl: string;
  spicyLevel: FoodItem["spicyLevel"];
  languageCode: LanguageCode;
};

const defaultFoodForm: FoodForm = {
  poiId: "",
  name: "",
  description: "",
  priceRange: "",
  imageUrl: "",
  spicyLevel: "mild",
  languageCode: "vi",
};

export const ContentPage = () => {
  const { state, isBootstrapping, saveFoodItem, saveTranslation } = useAdminData();
  const { user } = useAuth();
  const [foodModalOpen, setFoodModalOpen] = useState(false);
  const [foodForm, setFoodForm] = useState<FoodForm>(defaultFoodForm);
  const [formError, setFormError] = useState("");
  const [isSaving, setSaving] = useState(false);

  const poisWithDishes = new Set(state.foodItems.map((item) => item.poiId)).size;
  const hotDishes = state.foodItems.filter((item) => item.spicyLevel === "hot").length;

  const resolveFoodTranslationFields = (item: FoodItem, languageCode: LanguageCode) => {
    const translation = getEntityTranslation(state, "food_item", item.id, languageCode);

    return {
      name: translation?.title ?? item.name,
      description: translation?.fullText || translation?.shortText || item.description,
    };
  };

  const openFoodModal = (item?: FoodItem) => {
    const languageCode = state.settings.defaultLanguage;
    setFormError("");
    setFoodForm(
      item
        ? {
            id: item.id,
            poiId: item.poiId,
            ...resolveFoodTranslationFields(item, languageCode),
            priceRange: item.priceRange,
            imageUrl: item.imageUrl,
            spicyLevel: item.spicyLevel,
            languageCode,
          }
        : { ...defaultFoodForm, languageCode },
    );
    setFoodModalOpen(true);
  };

  const handleFoodSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user) {
      return;
    }

    setSaving(true);
    setFormError("");

    try {
      const existingFoodItem = foodForm.id
        ? state.foodItems.find((item) => item.id === foodForm.id)
        : null;
      const shouldWriteBaseText =
        !existingFoodItem ||
        foodForm.languageCode === state.settings.defaultLanguage;
      const savedFoodItem = await saveFoodItem(
        {
          id: foodForm.id,
          poiId: foodForm.poiId,
          name: shouldWriteBaseText ? foodForm.name : existingFoodItem?.name ?? foodForm.name,
          description: shouldWriteBaseText ? foodForm.description : existingFoodItem?.description ?? foodForm.description,
          priceRange: foodForm.priceRange,
          imageUrl: foodForm.imageUrl,
          spicyLevel: foodForm.spicyLevel,
        },
        user,
      );
      const existingTranslation = state.translations.find(
        (item) =>
          item.entityType === "food_item" &&
          item.entityId === savedFoodItem.id &&
          item.languageCode === foodForm.languageCode,
      );
      await saveTranslation(
        {
          id: existingTranslation?.id,
          entityType: "food_item",
          entityId: savedFoodItem.id,
          languageCode: foodForm.languageCode,
          title: foodForm.name,
          shortText: foodForm.description,
          fullText: foodForm.description,
          seoTitle: foodForm.name,
          seoDescription: foodForm.description || foodForm.name,
          isPremium: false,
        },
        user,
      );
      setFoodModalOpen(false);
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const handleFoodLanguageChange = (languageCode: LanguageCode) => {
    setFoodForm((current) => {
      const item = current.id ? state.foodItems.find((value) => value.id === current.id) : null;
      if (!item) {
        return { ...current, languageCode };
      }

      return {
        ...current,
        languageCode,
        ...resolveFoodTranslationFields(item, languageCode),
      };
    });
  };

  const foodColumns: DataColumn<FoodItem>[] = [
    {
      key: "dish",
      header: "Món ăn",
      widthClassName: "min-w-[320px]",
      render: (item) => (
        <div className="flex items-center gap-3">
          <img src={item.imageUrl} alt={item.name} className="h-14 w-14 rounded-2xl object-cover" />
          <div>
            <p className="font-semibold text-ink-900">{item.name}</p>
            <p className="mt-1 text-sm text-ink-500">{item.description}</p>
          </div>
        </div>
      ),
    },
    {
      key: "poi",
      header: "POI",
      widthClassName: "min-w-[220px]",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">{getPoiTitle(state, item.poiId)}</p>
          <p className="mt-1 text-xs text-ink-500">{item.priceRange}</p>
        </div>
      ),
    },
    {
      key: "spicy",
      header: "Độ cay",
      widthClassName: "min-w-[120px]",
      render: (item) => <StatusBadge status="draft" label={item.spicyLevel} />,
    },
    {
      key: "actions",
      header: "Thao tác",
      widthClassName: "min-w-[160px]",
      render: (item) => (
        <Button variant="secondary" onClick={() => openFoodModal(item)}>
          Chỉnh sửa
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">
          Food content
        </p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý món ăn</h1>
      </Card>

      <section className="grid gap-4 md:grid-cols-3">
        {[
          ["Tổng món ăn", state.foodItems.length],
          ["POI có món", poisWithDishes],
          ["Món cay", hotDishes],
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
            <h2 className="section-heading">Danh sách món ăn</h2>
          </div>
          <Button onClick={() => openFoodModal()} disabled={isBootstrapping}>
            {isBootstrapping ? "Đang tải dữ liệu..." : "Thêm món ăn"}
          </Button>
        </div>
        <div className="mt-6">
          <DataTable data={state.foodItems} columns={foodColumns} rowKey={(row) => row.id} />
        </div>
      </Card>

      <Modal open={foodModalOpen} onClose={() => setFoodModalOpen(false)} title="Quản lý món ăn">
        <form className="space-y-5" onSubmit={handleFoodSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
          <div>
            <label className="field-label">POI</label>
            <Select
              value={foodForm.poiId}
              required
              onChange={(event) =>
                setFoodForm((current) => ({ ...current, poiId: event.target.value }))
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
            <label className="field-label">Ngôn ngữ nội dung món ăn</label>
            <Select
              value={foodForm.languageCode}
              onChange={(event) => handleFoodLanguageChange(event.target.value as LanguageCode)}
            >
              {state.settings.supportedLanguages.map((code) => (
                <option key={code} value={code}>
                  {languageLabels[code]}
                </option>
              ))}
            </Select>
          </div>

          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <label className="field-label">Tên món</label>
              <Input
                value={foodForm.name}
                onChange={(event) =>
                  setFoodForm((current) => ({ ...current, name: event.target.value }))
                }
                required
              />
            </div>
            <div>
              <label className="field-label">Khoảng giá</label>
              <Input
                value={foodForm.priceRange}
                onChange={(event) =>
                  setFoodForm((current) => ({ ...current, priceRange: event.target.value }))
                }
              />
            </div>
          </div>

          <div>
            <label className="field-label">Mô tả</label>
            <Textarea
              value={foodForm.description}
              onChange={(event) =>
                setFoodForm((current) => ({ ...current, description: event.target.value }))
              }
            />
          </div>

          <div className="grid gap-5 md:grid-cols-2">
            <div>
              <ImageSourceField
                label="Ảnh món ăn"
                value={foodForm.imageUrl}
                onChange={(value) =>
                  setFoodForm((current) => ({ ...current, imageUrl: value }))
                }
                onUpload={async (file) => (await adminApi.uploadFile(file, "images/food-items")).url}
              />
            </div>
            <div>
              <label className="field-label">Độ cay</label>
              <Select
                value={foodForm.spicyLevel}
                onChange={(event) =>
                  setFoodForm((current) => ({
                    ...current,
                    spicyLevel: event.target.value as FoodItem["spicyLevel"],
                  }))
                }
              >
                <option value="mild">Mild</option>
                <option value="medium">Medium</option>
                <option value="hot">Hot</option>
              </Select>
            </div>
          </div>

          {formError ? (
            <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div>
          ) : null}

          <div className="flex justify-end gap-3 border-t border-sand-100 pt-5">
            <Button variant="ghost" onClick={() => setFoodModalOpen(false)}>
              Hủy
            </Button>
            <Button type="submit" disabled={isSaving}>
              {isSaving ? "Đang lưu..." : "Lưu món ăn"}
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
};
