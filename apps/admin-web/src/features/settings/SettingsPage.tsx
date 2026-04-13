import { useEffect, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Input } from "../../components/ui/Input";
import { Select } from "../../components/ui/Select";
import { useAdminData } from "../../data/store";
import type { LanguageCode, SystemSetting } from "../../data/types";
import { getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { languageLabels } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

const allLanguages: LanguageCode[] = ["vi", "en", "zh-CN", "ko", "ja"];

export const SettingsPage = () => {
  const { isBootstrapping, refreshData, saveSettings, state } = useAdminData();
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

  const toggleSupportedLanguage = (language: LanguageCode) => {
    setForm((current) => {
      const nextSupportedLanguages = current.supportedLanguages.includes(language)
        ? current.supportedLanguages.filter((item) => item !== language)
        : [...current.supportedLanguages, language];

      return {
        ...current,
        supportedLanguages: nextSupportedLanguages.length > 0 ? nextSupportedLanguages : [current.defaultLanguage],
      };
    });
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || !canManageSettings) {
      return;
    }

    if (form.supportedLanguages.length === 0) {
      setFeedbackTone("error");
      setFeedback("Cần chọn ít nhất một ngôn ngữ hỗ trợ.");
      return;
    }

    setSaving(true);
    setFeedback("");

    try {
      await saveSettings(
        {
          ...form,
          supportedLanguages: [...form.supportedLanguages],
        },
        user,
      );
      setFeedbackTone("success");
      setFeedback("Đã lưu cấu hình ngôn ngữ và thông số hệ thống.");
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
      setFeedback("Đã nạp cấu hình mới nhất từ backend.");
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
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Ngôn ngữ công khai và thông số vận hành</h1>
      </Card>

      {!canManageSettings ? (
        <Card className="border border-amber-100 bg-amber-50">
          <p className="font-semibold text-amber-800">Tài khoản này chỉ có quyền xem.</p>
          <p className="mt-2 text-sm text-amber-700">
            Đăng nhập bằng Super Admin để cập nhật cấu hình ngôn ngữ và runtime.
          </p>
        </Card>
      ) : null}

      {isBootstrapping ? (
        <Card>
          <p className="text-sm text-ink-500">Đang tải cấu hình từ backend...</p>
        </Card>
      ) : null}

      <form
        className={isBootstrapping ? "hidden" : "space-y-6"}
        onSubmit={handleSubmit}
        onKeyDown={preventImplicitFormSubmit}
        autoComplete="off"
      >
        <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <Card>
            <h2 className="section-heading">Core information</h2>
            <div className="mt-5 grid gap-5 md:grid-cols-2">
              <div>
                <label className="field-label">System name</label>
                <Input
                  value={form.appName}
                  onChange={(event) => setForm((current) => ({ ...current, appName: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
              <div>
                <label className="field-label">Support email</label>
                <Input
                  type="email"
                  value={form.supportEmail}
                  onChange={(event) => setForm((current) => ({ ...current, supportEmail: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
              <div>
                <label className="field-label">Default language</label>
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
                <label className="field-label">Fallback language</label>
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
            <h2 className="section-heading">Ngôn ngữ công khai</h2>
            <p className="mt-4 text-sm text-ink-500">
              App Android công khai chỉ dùng tập ngôn ngữ hỗ trợ này. Không còn khóa premium theo tài khoản.
            </p>
            <div className="mt-5 grid gap-3">
              {allLanguages.map((language) => (
                <label
                  key={language}
                  className="flex items-center gap-3 rounded-2xl border border-sand-200 bg-sand-50 px-4 py-3 text-sm text-ink-700"
                >
                  <input
                    type="checkbox"
                    checked={form.supportedLanguages.includes(language)}
                    disabled={!canManageSettings}
                    onChange={() => toggleSupportedLanguage(language)}
                  />
                  <span className="font-medium">{languageLabels[language]}</span>
                </label>
              ))}
            </div>
          </Card>
        </section>

        <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <Card>
            <h2 className="section-heading">Providers</h2>
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
                  <option value="s3">S3 compatible</option>
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
                  <option value="elevenlabs">ElevenLabs TTS</option>
                </Select>
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
                <label className="field-label">Analytics retention (days)</label>
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
                Allow anonymous guest reviews
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
            {canManageSettings ? (isSaving ? "Saving..." : "Save settings") : "No permission"}
          </Button>
          <Button
            type="button"
            variant="secondary"
            onClick={() => {
              void handleRefresh();
            }}
            disabled={isRefreshing}
          >
            {isRefreshing ? "Refreshing..." : "Refresh from backend"}
          </Button>
        </section>
      </form>
    </div>
  );
};
