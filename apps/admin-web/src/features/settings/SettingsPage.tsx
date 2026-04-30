import { useEffect, useState, type FormEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Input, Textarea } from "../../components/ui/Input";
import { Select } from "../../components/ui/Select";
import { useAdminData } from "../../data/store";
import type { LanguageCode, SystemSetting } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { preventImplicitFormSubmit } from "../../lib/forms";
import { useAuth } from "../auth/AuthContext";

type SectionKey = "languages" | "contact";

const languageOptions: Array<{ code: LanguageCode; label: string }> = [
  { code: "vi", label: "Tiếng Việt" },
  { code: "en", label: "Tiếng Anh" },
  { code: "zh-CN", label: "Tiếng Trung" },
  { code: "ko", label: "Tiếng Hàn" },
  { code: "ja", label: "Tiếng Nhật" },
];

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const countPhoneDigits = (value: string) => value.replace(/\D/g, "").length;

export const SettingsPage = () => {
  const { isBootstrapping, refreshData, state } = useAdminData();
  const { user } = useAuth();
  const [form, setForm] = useState<SystemSetting>(state.settings);
  const [feedback, setFeedback] = useState<{ tone: "success" | "error"; message: string } | null>(null);
  const [savingSection, setSavingSection] = useState<SectionKey | null>(null);
  const [isRefreshing, setRefreshing] = useState(false);
  const canManageSettings = user?.role === "SUPER_ADMIN";

  useEffect(() => {
    setForm(state.settings);
  }, [state.settings]);

  const enabledDefaultOptions = languageOptions.filter((item) => form.supportedLanguages.includes(item.code));

  const reloadSettings = async () => {
    const nextState = await refreshData();
    setForm(nextState.settings);
  };

  const toggleSupportedLanguage = (language: LanguageCode) => {
    setForm((current) => {
      const nextSupportedLanguages = current.supportedLanguages.includes(language)
        ? current.supportedLanguages.filter((item) => item !== language)
        : [...current.supportedLanguages, language];
      const nextDefaultLanguage = nextSupportedLanguages.includes(current.defaultLanguage)
        ? current.defaultLanguage
        : nextSupportedLanguages.includes("vi")
          ? "vi"
          : nextSupportedLanguages[0] ?? current.defaultLanguage;

      return {
        ...current,
        defaultLanguage: nextDefaultLanguage,
        fallbackLanguage: nextDefaultLanguage,
        supportedLanguages: nextSupportedLanguages,
      };
    });
  };

  const validateLanguageSettings = () => {
    if (form.supportedLanguages.length === 0) {
      return "Phải bật ít nhất 1 ngôn ngữ.";
    }

    if (!form.supportedLanguages.includes(form.defaultLanguage)) {
      return "Ngôn ngữ mặc định phải nằm trong danh sách đang bật.";
    }

    return null;
  };

  const validateContactSettings = () => {
    if (!form.appName.trim()) {
      return "Tên đơn vị / hệ thống là bắt buộc.";
    }

    if (countPhoneDigits(form.supportPhone) < 8) {
      return "Số điện thoại hỗ trợ phải có ít nhất 8 chữ số.";
    }

    if (form.supportEmail.trim() && !emailPattern.test(form.supportEmail.trim())) {
      return "Email hỗ trợ không đúng định dạng.";
    }

    if (!form.supportInstructions.trim()) {
      return "Nội dung hướng dẫn khiếu nại / hỗ trợ là bắt buộc.";
    }

    return null;
  };

  const saveLanguages = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || !canManageSettings) {
      return;
    }

    const validationError = validateLanguageSettings();
    if (validationError) {
      setFeedback({ tone: "error", message: validationError });
      return;
    }

    setSavingSection("languages");
    setFeedback(null);
    try {
      await adminApi.saveLanguageSettings({
        defaultLanguage: form.defaultLanguage,
        enabledLanguages: form.supportedLanguages,
        actorName: user.name,
        actorRole: user.role,
      });
      await reloadSettings();
      setFeedback({ tone: "success", message: "Đã lưu cấu hình ngôn ngữ ứng dụng." });
    } catch (error) {
      setFeedback({ tone: "error", message: getErrorMessage(error) });
    } finally {
      setSavingSection(null);
    }
  };

  const saveContact = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!user || !canManageSettings) {
      return;
    }

    const validationError = validateContactSettings();
    if (validationError) {
      setFeedback({ tone: "error", message: validationError });
      return;
    }

    setSavingSection("contact");
    setFeedback(null);
    try {
      await adminApi.saveContactSettings({
        appName: form.appName.trim(),
        supportPhone: form.supportPhone.trim(),
        supportEmail: form.supportEmail.trim(),
        contactAddress: form.contactAddress.trim(),
        supportInstructions: form.supportInstructions.trim(),
        supportHours: form.supportHours.trim(),
        actorName: user.name,
        actorRole: user.role,
      });
      await reloadSettings();
      setFeedback({ tone: "success", message: "Đã lưu thông tin liên hệ / khiếu nại." });
    } catch (error) {
      setFeedback({ tone: "error", message: getErrorMessage(error) });
    } finally {
      setSavingSection(null);
    }
  };

  const handleRefresh = async () => {
    setRefreshing(true);
    setFeedback(null);
    try {
      await reloadSettings();
      setFeedback({ tone: "success", message: "Đã nạp dữ liệu mới nhất từ backend." });
    } catch (error) {
      setFeedback({ tone: "error", message: getErrorMessage(error) });
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-semibold uppercase tracking-[0.2em] text-primary-600">Cài đặt hệ thống</p>
          <h1 className="mt-2 text-2xl font-bold text-ink-900">Cấu hình ứng dụng mobile</h1>
        </div>
        <Button
          type="button"
          variant="secondary"
          onClick={() => {
            void handleRefresh();
          }}
          disabled={isRefreshing || isBootstrapping}
        >
          {isRefreshing ? "Đang tải..." : "Tải lại"}
        </Button>
      </div>

      {!canManageSettings ? (
        <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          Tài khoản hiện tại chỉ có quyền xem cấu hình.
        </div>
      ) : null}

      {isBootstrapping ? (
        <Card>
          <p className="text-sm text-ink-500">Đang tải cấu hình từ backend...</p>
        </Card>
      ) : null}

      {feedback ? (
        <div
          className={
            feedback.tone === "success"
              ? "rounded-lg bg-emerald-50 px-4 py-3 text-sm text-emerald-700"
              : "rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700"
          }
        >
          {feedback.message}
        </div>
      ) : null}

      <div className={isBootstrapping ? "hidden" : "grid gap-6"}>
        <Card>
          <form className="space-y-5" onSubmit={saveLanguages} onKeyDown={preventImplicitFormSubmit}>
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <h2 className="section-heading">Ngôn ngữ ứng dụng</h2>
                <p className="mt-1 text-sm text-ink-500">App chỉ hiển thị các ngôn ngữ đang bật.</p>
              </div>
              <Button type="submit" disabled={!canManageSettings || savingSection !== null}>
                {savingSection === "languages" ? "Đang lưu..." : "Lưu ngôn ngữ"}
              </Button>
            </div>

            <div className="grid gap-5 lg:grid-cols-[minmax(0,0.7fr)_minmax(0,1.3fr)]">
              <div>
                <label className="field-label">Ngôn ngữ mặc định</label>
                <Select
                  value={form.defaultLanguage}
                  onChange={(event) =>
                    setForm((current) => ({
                      ...current,
                      defaultLanguage: event.target.value as LanguageCode,
                      fallbackLanguage: event.target.value as LanguageCode,
                    }))
                  }
                  disabled={!canManageSettings || enabledDefaultOptions.length === 0}
                >
                  {(enabledDefaultOptions.length > 0 ? enabledDefaultOptions : languageOptions).map((language) => (
                    <option key={language.code} value={language.code}>
                      {language.label}
                    </option>
                  ))}
                </Select>
              </div>

              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {languageOptions.map((language) => (
                  <label
                    key={language.code}
                    className="flex min-h-[52px] items-center gap-3 rounded-lg border border-sand-200 bg-white px-4 py-3 text-sm text-ink-700"
                  >
                    <input
                      type="checkbox"
                      checked={form.supportedLanguages.includes(language.code)}
                      disabled={!canManageSettings}
                      onChange={() => toggleSupportedLanguage(language.code)}
                    />
                    <span className="font-medium">{language.label}</span>
                  </label>
                ))}
              </div>
            </div>
          </form>
        </Card>

        <Card>
          <form className="space-y-5" onSubmit={saveContact} onKeyDown={preventImplicitFormSubmit}>
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <h2 className="section-heading">Thông tin liên hệ / Khiếu nại</h2>
                <p className="mt-1 text-sm text-ink-500">Thông tin này được app mobile đọc từ backend.</p>
              </div>
              <Button type="submit" disabled={!canManageSettings || savingSection !== null}>
                {savingSection === "contact" ? "Đang lưu..." : "Lưu liên hệ"}
              </Button>
            </div>

            <div className="grid gap-5 md:grid-cols-2">
              <div>
                <label className="field-label">Tên đơn vị / hệ thống</label>
                <Input
                  value={form.appName}
                  onChange={(event) => setForm((current) => ({ ...current, appName: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
              <div>
                <label className="field-label">Số điện thoại hỗ trợ</label>
                <Input
                  value={form.supportPhone}
                  onChange={(event) => setForm((current) => ({ ...current, supportPhone: event.target.value }))}
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
                <label className="field-label">Địa chỉ liên hệ</label>
                <Input
                  value={form.contactAddress}
                  onChange={(event) => setForm((current) => ({ ...current, contactAddress: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
              <div className="md:col-span-2">
                <label className="field-label">Nội dung hướng dẫn khiếu nại / hỗ trợ</label>
                <Textarea
                  value={form.supportInstructions}
                  onChange={(event) => setForm((current) => ({ ...current, supportInstructions: event.target.value }))}
                  disabled={!canManageSettings}
                />
              </div>
            </div>
          </form>
        </Card>

      </div>
    </div>
  );
};
