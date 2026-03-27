import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Input } from "../../components/ui/Input";
import { Select } from "../../components/ui/Select";
import { useAdminData } from "../../data/store";
import type { LanguageCode, SystemSetting } from "../../data/types";
import { getErrorMessage } from "../../lib/api";
import { languageLabels } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

const allLanguages: LanguageCode[] = ["vi", "en", "zh-CN", "ko", "ja"];

export const SettingsPage = () => {
  const { refreshData, saveSettings, state } = useAdminData();
  const { user } = useAuth();
  const [form, setForm] = useState<SystemSetting>(state.settings);
  const [feedback, setFeedback] = useState("");
  const [feedbackTone, setFeedbackTone] = useState<"success" | "error">("success");
  const [isSaving, setSaving] = useState(false);
  const [isRefreshing, setRefreshing] = useState(false);
  const canManageSettings = user?.role === "SUPER_ADMIN";

  useEffect(() => {
    setForm(state.settings);
  }, [state.settings]);

  const premiumCandidates = useMemo(
    () => allLanguages.filter((language) => !form.freeLanguages.includes(language)),
    [form.freeLanguages],
  );

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || !canManageSettings) {
      return;
    }

    setSaving(true);
    setFeedback("");

    try {
      await saveSettings(form, user);
      setFeedbackTone("success");
      setFeedback("Đã lưu cấu hình hệ thống từ backend.");
    } catch (error) {
      setFeedbackTone("error");
      setFeedback(getErrorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const handleRefresh = async () => {
    setRefreshing(true);
    setFeedback("");

    try {
      await refreshData();
      setFeedbackTone("success");
      setFeedback("Đã tải lại dữ liệu cấu hình từ backend.");
    } catch (error) {
      setFeedbackTone("error");
      setFeedback(getErrorMessage(error));
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">System settings</p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Cấu hình hệ thống và quy tắc vận hành</h1>
      </Card>

      {!canManageSettings ? (
        <Card className="border border-amber-100 bg-amber-50">
          <p className="font-semibold text-amber-800">Tài khoản hiện tại chỉ có quyền xem cấu hình.</p>
          <p className="mt-2 text-sm text-amber-700">
            Đăng nhập bằng Super Admin để cập nhật cài đặt hệ thống.
          </p>
        </Card>
      ) : null}

      <form className="space-y-6" onSubmit={handleSubmit}>
        <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <Card>
            <h2 className="section-heading">Thông tin nền tảng</h2>
            <div className="mt-5 grid gap-5 md:grid-cols-2">
              <div>
                <label className="field-label">Tên hệ thống</label>
                <Input
                  value={form.appName}
                  onChange={(event) => setForm((current) => ({ ...current, appName: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
              <div>
                <label className="field-label">Email hỗ trợ</label>
                <Input
                  type="email"
                  value={form.supportEmail}
                  onChange={(event) => setForm((current) => ({ ...current, supportEmail: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
              <div>
                <label className="field-label">Ngôn ngữ mặc định</label>
                <Select
                  value={form.defaultLanguage}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, defaultLanguage: event.target.value as LanguageCode }))
                  }
                  disabled={!canManageSettings}
                >
                  {allLanguages.map((language) => (
                    <option key={language} value={language}>
                      {languageLabels[language]}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="field-label">Ngôn ngữ fallback</label>
                <Select
                  value={form.fallbackLanguage}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, fallbackLanguage: event.target.value as LanguageCode }))
                  }
                  disabled={!canManageSettings}
                >
                  {allLanguages.map((language) => (
                    <option key={language} value={language}>
                      {languageLabels[language]}
                    </option>
                  ))}
                </Select>
              </div>
            </div>
          </Card>

          <Card>
            <h2 className="section-heading">Provider tích hợp</h2>
            <div className="mt-5 space-y-5">
              <div>
                <label className="field-label">Map provider</label>
                <Select
                  value={form.mapProvider}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      mapProvider: event.target.value as SystemSetting["mapProvider"],
                    }))
                  }
                  disabled={!canManageSettings}
                >
                  <option value="openstreetmap">OpenStreetMap</option>
                  <option value="google">Google Maps</option>
                  <option value="mapbox">Mapbox</option>
                </Select>
              </div>
              <div>
                <label className="field-label">Storage provider</label>
                <Select
                  value={form.storageProvider}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      storageProvider: event.target.value as SystemSetting["storageProvider"],
                    }))
                  }
                  disabled={!canManageSettings}
                >
                  <option value="cloudinary">Cloudinary</option>
                  <option value="s3">S3 Compatible</option>
                </Select>
              </div>
              <div>
                <label className="field-label">TTS provider</label>
                <Select
                  value={form.ttsProvider}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      ttsProvider: event.target.value as SystemSetting["ttsProvider"],
                    }))
                  }
                  disabled={!canManageSettings}
                >
                  <option value="azure">Azure Cognitive Services</option>
                  <option value="native">Native device TTS</option>
                </Select>
              </div>
            </div>
          </Card>
        </section>

        <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <Card>
            <h2 className="section-heading">Free và premium languages</h2>
            <div className="mt-5 grid gap-6 md:grid-cols-2">
              <div>
                <p className="mb-3 text-sm font-semibold text-ink-700">Free languages</p>
                <div className="space-y-3">
                  {allLanguages.map((language) => (
                    <label
                      key={`free-${language}`}
                      className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-700"
                    >
                      <input
                        type="checkbox"
                        checked={form.freeLanguages.includes(language)}
                        disabled={!canManageSettings}
                        onChange={(event) =>
                          setForm((current) => ({
                            ...current,
                            freeLanguages: event.target.checked
                              ? [...current.freeLanguages, language]
                              : current.freeLanguages.filter((item) => item !== language),
                          }))
                        }
                      />
                      {languageLabels[language]}
                    </label>
                  ))}
                </div>
              </div>
              <div>
                <p className="mb-3 text-sm font-semibold text-ink-700">Premium languages</p>
                <div className="space-y-3">
                  {premiumCandidates.map((language) => (
                    <label
                      key={`premium-${language}`}
                      className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-700"
                    >
                      <input
                        type="checkbox"
                        checked={form.premiumLanguages.includes(language)}
                        disabled={!canManageSettings}
                        onChange={(event) =>
                          setForm((current) => ({
                            ...current,
                            premiumLanguages: event.target.checked
                              ? [...current.premiumLanguages, language]
                              : current.premiumLanguages.filter((item) => item !== language),
                          }))
                        }
                      />
                      {languageLabels[language]}
                    </label>
                  ))}
                </div>
                <div className="mt-4">
                  <label className="field-label">Giá mở khóa premium (USD)</label>
                  <Input
                    type="number"
                    value={form.premiumUnlockPriceUsd}
                    onChange={(event) =>
                      setForm((current) => ({
                        ...current,
                        premiumUnlockPriceUsd: Number(event.target.value),
                      }))
                    }
                    disabled={!canManageSettings}
                  />
                </div>
              </div>
            </div>
          </Card>

          <Card>
            <h2 className="section-heading">Runtime options</h2>
            <div className="mt-5 space-y-5">
              <div>
                <label className="field-label">Geofence radius (m)</label>
                <Input
                  type="number"
                  value={form.geofenceRadiusMeters}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      geofenceRadiusMeters: Number(event.target.value),
                    }))
                  }
                  disabled={!canManageSettings}
                />
              </div>
              <div>
                <label className="field-label">Retention analytics (ngày)</label>
                <Input
                  type="number"
                  value={form.analyticsRetentionDays}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      analyticsRetentionDays: Number(event.target.value),
                    }))
                  }
                  disabled={!canManageSettings}
                />
              </div>
              <label className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-700">
                <input
                  type="checkbox"
                  checked={form.guestReviewEnabled}
                  disabled={!canManageSettings}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, guestReviewEnabled: event.target.checked }))
                  }
                />
                Cho phép gửi review ẩn danh
              </label>
            </div>
          </Card>
        </section>

        {feedback ? (
          <div
            className={
              feedbackTone === "success"
                ? "rounded-2xl bg-emerald-50 px-4 py-3 text-sm text-emerald-700"
                : "rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700"
            }
          >
            {feedback}
          </div>
        ) : null}

        <section className="flex flex-wrap justify-between gap-4">
          <Button type="submit" disabled={isSaving || !canManageSettings}>
            {canManageSettings ? (isSaving ? "Đang lưu..." : "Lưu cấu hình hệ thống") : "Không có quyền cập nhật"}
          </Button>
          <Button
            type="button"
            variant="secondary"
            onClick={() => {
              void handleRefresh();
            }}
            disabled={isRefreshing}
          >
            {isRefreshing ? "Đang tải lại..." : "Tải lại từ backend"}
          </Button>
        </section>
      </form>
    </div>
  );
};
