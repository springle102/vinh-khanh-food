import { useEffect, useMemo, useState, type FormEvent } from "react";
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
const fixedFreeLanguages: LanguageCode[] = ["vi", "en"];
const fixedPremiumLanguages: LanguageCode[] = ["zh-CN", "ko", "ja"];

export const SettingsPage = () => {
  const { isBootstrapping, refreshData, saveSettings, state } = useAdminData();
  const { user } = useAuth();
  const [form, setForm] = useState<SystemSetting>(state.settings);
  const [premiumPriceInput, setPremiumPriceInput] = useState(String(state.settings.premiumUnlockPriceUsd));
  const [feedback, setFeedback] = useState("");
  const [feedbackTone, setFeedbackTone] = useState<"success" | "error">("success");
  const [isSaving, setSaving] = useState(false);
  const [isRefreshing, setRefreshing] = useState(false);
  const canManageSettings = user?.role === "SUPER_ADMIN";

  useEffect(() => {
    setForm(state.settings);
    setPremiumPriceInput(String(state.settings.premiumUnlockPriceUsd));
  }, [state.settings]);

  const premiumPriceError = useMemo(() => {
    const parsed = Number(premiumPriceInput);
    if (!Number.isFinite(parsed) || parsed <= 0) {
      return "Premium price must be greater than 0.";
    }

    return "";
  }, [premiumPriceInput]);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || !canManageSettings) {
      return;
    }

    if (premiumPriceError) {
      setFeedbackTone("error");
      setFeedback(premiumPriceError);
      return;
    }

    setSaving(true);
    setFeedback("");

    try {
      await saveSettings(
        {
          ...form,
          freeLanguages: fixedFreeLanguages,
          premiumLanguages: fixedPremiumLanguages,
          premiumUnlockPriceUsd: Math.round(Number(premiumPriceInput)),
        },
        user,
      );
      setFeedbackTone("success");
      setFeedback("Premium settings were saved successfully.");
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
      setFeedback("The latest settings were loaded from the backend.");
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
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Operations, pricing, and customer access rules</h1>
      </Card>

      {!canManageSettings ? (
        <Card className="border border-amber-100 bg-amber-50">
          <p className="font-semibold text-amber-800">Your account can only view settings.</p>
          <p className="mt-2 text-sm text-amber-700">
            Sign in as Super Admin to update Premium pricing and runtime options.
          </p>
        </Card>
      ) : null}

      {isBootstrapping ? (
        <Card>
          <p className="text-sm text-ink-500">Đang tải cấu hình từ backend...</p>
        </Card>
      ) : null}

      <form className={isBootstrapping ? "hidden" : "space-y-6"} onSubmit={handleSubmit} onKeyDown={preventImplicitFormSubmit} autoComplete="off">
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
            <h2 className="section-heading">Premium package</h2>
            <div className="mt-5 space-y-5">
              <div>
                <label className="field-label">Premium price (USD)</label>
                <Input
                  type="number"
                  min={1}
                  step={1}
                  value={premiumPriceInput}
                  onChange={(event) => {
                    setPremiumPriceInput(event.target.value);
                    setForm((current) => ({
                      ...current,
                      premiumUnlockPriceUsd: Number(event.target.value),
                    }));
                  }}
                  disabled={!canManageSettings}
                />
                <p className="mt-2 text-xs leading-5 text-ink-500">
                  The mobile app reads this price from backend settings. Any change here is reflected in the latest
                  Premium purchase CTA.
                </p>
                {premiumPriceError ? <p className="mt-2 text-sm text-rose-600">{premiumPriceError}</p> : null}
              </div>

              <div className="rounded-3xl border border-sand-200 bg-sand-50 p-4">
                <p className="text-sm font-semibold text-ink-800">Fixed package policy</p>
                <p className="mt-2 text-sm text-ink-600">
                  Language access is intentionally fixed in code and backend validation to avoid drift between admin,
                  API, and mobile rules.
                </p>

                <div className="mt-4 grid gap-4 md:grid-cols-2">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-ink-500">Free</p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      {fixedFreeLanguages.map((language) => (
                        <span
                          key={`free-${language}`}
                          className="rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700"
                        >
                          {languageLabels[language]}
                        </span>
                      ))}
                    </div>
                  </div>

                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-ink-500">Premium</p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      {fixedPremiumLanguages.map((language) => (
                        <span
                          key={`premium-${language}`}
                          className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-semibold text-amber-700"
                        >
                          {languageLabels[language]}
                        </span>
                      ))}
                    </div>
                  </div>
                </div>
              </div>
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
                <label className="field-label">Fallback TTS</label>
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
          <Button type="submit" disabled={isSaving || !canManageSettings || Boolean(premiumPriceError)}>
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
