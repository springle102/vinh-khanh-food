import { useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Icon } from "../../components/ui/Icons";
import { Input } from "../../components/ui/Input";
import { StatusBadge } from "../../components/ui/StatusBadge";
import type { PlaceOwnerRegistrationRecord } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { approvalStatusLabels, formatDateTime } from "../../lib/utils";

type RegistrationFormState = {
  name: string;
  email: string;
  password: string;
  confirmPassword: string;
  phone: string;
};

type RegistrationFormErrors = Partial<Record<keyof RegistrationFormState, string>>;

const EMPTY_FORM: RegistrationFormState = {
  name: "",
  email: "",
  password: "",
  confirmPassword: "",
  phone: "",
};

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const getStatusPanelClasses = (status: PlaceOwnerRegistrationRecord["approvalStatus"]) => {
  if (status === "approved") {
    return "border-emerald-100 bg-emerald-50 text-emerald-800";
  }

  if (status === "rejected") {
    return "border-rose-100 bg-rose-50 text-rose-800";
  }

  return "border-amber-100 bg-amber-50 text-amber-800";
};

const buildFormErrors = (form: RegistrationFormState) => {
  const errors: RegistrationFormErrors = {};

  if (!form.name.trim()) {
    errors.name = "Họ tên là bắt buộc.";
  }

  if (!form.email.trim()) {
    errors.email = "Email là bắt buộc.";
  } else if (!EMAIL_PATTERN.test(form.email.trim())) {
    errors.email = "Email không đúng định dạng.";
  }

  if (!form.phone.trim()) {
    errors.phone = "Số điện thoại là bắt buộc.";
  }

  if (!form.password) {
    errors.password = "Mật khẩu là bắt buộc.";
  }

  if (!form.confirmPassword) {
    errors.confirmPassword = "Xác nhận mật khẩu là bắt buộc.";
  } else if (form.password !== form.confirmPassword) {
    errors.confirmPassword = "Mật khẩu xác nhận chưa khớp.";
  }

  return errors;
};

const mapRegistrationToForm = (
  registration: PlaceOwnerRegistrationRecord,
  password: string,
): RegistrationFormState => ({
  name: registration.name,
  email: registration.email,
  password,
  confirmPassword: password,
  phone: registration.phone,
});

export const PlaceOwnerRegistrationPage = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialEmail = searchParams.get("email")?.trim() ?? "";

  const [form, setForm] = useState<RegistrationFormState>({ ...EMPTY_FORM, email: initialEmail });
  const [formErrors, setFormErrors] = useState<RegistrationFormErrors>({});
  const [formError, setFormError] = useState("");
  const [formSuccess, setFormSuccess] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const [lookupEmail, setLookupEmail] = useState(initialEmail);
  const [lookupPassword, setLookupPassword] = useState("");
  const [lookupError, setLookupError] = useState("");
  const [loadingExisting, setLoadingExisting] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [registration, setRegistration] = useState<PlaceOwnerRegistrationRecord | null>(null);

  const canEditForm = !registration || registration.approvalStatus === "rejected";

  const statusMessage = useMemo(() => {
    if (!registration) {
      return null;
    }

    if (registration.approvalStatus === "approved") {
      return "Hồ sơ đã được duyệt. Bạn có thể quay lại trang đăng nhập để vào admin-web với quyền chủ quán.";
    }

    if (registration.approvalStatus === "rejected") {
      return "Hồ sơ đã bị từ chối. Bạn có thể chỉnh sửa thông tin bên dưới rồi gửi lại để admin duyệt lại.";
    }

    return "Hồ sơ của bạn đang chờ admin phê duyệt. Trong thời gian này bạn chưa thể đăng nhập admin-web.";
  }, [registration]);

  const updateForm = <T extends keyof RegistrationFormState>(key: T, value: RegistrationFormState[T]) => {
    setForm((current) => ({ ...current, [key]: value }));
    setFormErrors((current) => ({ ...current, [key]: undefined }));
    setFormError("");
    setFormSuccess("");
  };

  const handleLoadExisting = async () => {
    setLookupError("");
    setFormError("");
    setFormSuccess("");

    if (!lookupEmail.trim() || !lookupPassword) {
      setLookupError("Hãy nhập email và mật khẩu đã dùng khi đăng ký trước đó.");
      return;
    }

    setLoadingExisting(true);

    try {
      const existingRegistration = await adminApi.accessPlaceOwnerRegistration({
        email: lookupEmail.trim(),
        password: lookupPassword,
      });

      setRegistration(existingRegistration);
      setCurrentPassword(lookupPassword);
      setForm(mapRegistrationToForm(existingRegistration, lookupPassword));
      setFormErrors({});
      setFormSuccess("Đã tải hồ sơ đăng ký của bạn.");
    } catch (error) {
      setLookupError(getErrorMessage(error));
    } finally {
      setLoadingExisting(false);
    }
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setFormSuccess("");
    setFormError("");

    const nextErrors = buildFormErrors(form);
    setFormErrors(nextErrors);
    if (Object.values(nextErrors).some(Boolean)) {
      setFormError("Vui lòng kiểm tra lại các trường bắt buộc.");
      return;
    }

    setSubmitting(true);

    try {
      const payload = {
        name: form.name.trim(),
        email: form.email.trim(),
        password: form.password,
        confirmPassword: form.confirmPassword,
        phone: form.phone.trim(),
      };

      const nextRegistration =
        registration && registration.approvalStatus === "rejected"
          ? await adminApi.resubmitPlaceOwnerRegistration(registration.id, {
              ...payload,
              currentPassword,
            })
          : await adminApi.createPlaceOwnerRegistration(payload);

      setRegistration(nextRegistration);
      setCurrentPassword(form.password);
      setLookupEmail(nextRegistration.email);
      setLookupPassword(form.password);
      setForm(mapRegistrationToForm(nextRegistration, form.password));
      setFormSuccess(
        nextRegistration.approvalStatus === "pending"
          ? "Đã gửi hồ sơ thành công. Tài khoản của bạn đang chờ admin phê duyệt."
          : "Đã cập nhật hồ sơ thành công.",
      );
    } catch (error) {
      setFormError(getErrorMessage(error));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-sand-50 bg-hero-warm px-4 py-10 text-ink-900">
      <div className="w-full max-w-6xl">
        <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-4">
            <div className="flex h-14 w-14 items-center justify-center rounded-3xl bg-primary-500 text-white">
              <Icon name="users" className="h-7 w-7" />
            </div>
            <div>
              <p className="text-sm font-semibold tracking-[0.12em] text-primary-600">Đăng ký chủ quán</p>
              <h1 className="text-2xl font-bold">Gửi hồ sơ tài khoản chờ admin duyệt</h1>
            </div>
          </div>
          <Link
            to="/login"
            className="text-sm font-medium text-primary-700 transition hover:text-primary-800"
          >
            Quay lại đăng nhập
          </Link>
        </div>

        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
          <Card className="p-8 sm:p-10">
            <div className="max-w-2xl">
              <p className="text-sm text-ink-500">
                Chủ quán chỉ cần đăng ký thông tin cá nhân. Sau khi được duyệt và đăng nhập vào admin-web,
                bạn sẽ tự tạo POI và nhập thông tin quán trong chức năng quản lý hiện có.
              </p>
            </div>

            {registration && statusMessage ? (
              <div
                className={`mt-6 rounded-3xl border px-5 py-4 ${getStatusPanelClasses(registration.approvalStatus)}`}
              >
                <div className="flex flex-wrap items-center gap-3">
                  <StatusBadge
                    status={registration.approvalStatus}
                    label={approvalStatusLabels[registration.approvalStatus]}
                  />
                  <span className="text-sm font-medium">
                    Gửi lúc {formatDateTime(registration.registrationSubmittedAt ?? registration.createdAt)}
                  </span>
                </div>
                <p className="mt-3 text-sm leading-6">{statusMessage}</p>
                {registration.rejectionReason ? (
                  <div className="mt-4 rounded-2xl bg-white/70 px-4 py-3 text-sm text-rose-700">
                    <span className="font-semibold">Lý do từ chối:</span> {registration.rejectionReason}
                  </div>
                ) : null}
                {registration.registrationReviewedAt ? (
                  <p className="mt-3 text-xs opacity-80">
                    Xét duyệt lúc {formatDateTime(registration.registrationReviewedAt)}
                  </p>
                ) : null}
                {registration.approvalStatus === "approved" ? (
                  <div className="mt-4">
                    <Button onClick={() => navigate("/login")}>Đến trang đăng nhập</Button>
                  </div>
                ) : null}
              </div>
            ) : null}

            <form className="mt-6 space-y-5" onSubmit={(event) => void handleSubmit(event)} autoComplete="off">
              <div className="grid gap-5 md:grid-cols-2">
                <div className="md:col-span-2">
                  <label className="field-label">Họ tên</label>
                  <Input
                    value={form.name}
                    onChange={(event) => updateForm("name", event.target.value)}
                    placeholder="Nhập họ tên"
                    disabled={!canEditForm}
                  />
                  {formErrors.name ? <p className="mt-2 text-sm text-rose-600">{formErrors.name}</p> : null}
                </div>
                <div>
                  <label className="field-label">Email</label>
                  <Input
                    type="email"
                    value={form.email}
                    onChange={(event) => updateForm("email", event.target.value)}
                    placeholder="Nhập email"
                    disabled={!canEditForm}
                  />
                  {formErrors.email ? <p className="mt-2 text-sm text-rose-600">{formErrors.email}</p> : null}
                </div>
                <div>
                  <label className="field-label">Số điện thoại</label>
                  <Input
                    value={form.phone}
                    onChange={(event) => updateForm("phone", event.target.value)}
                    placeholder="Nhập số điện thoại"
                    disabled={!canEditForm}
                  />
                  {formErrors.phone ? <p className="mt-2 text-sm text-rose-600">{formErrors.phone}</p> : null}
                </div>
                <div>
                  <label className="field-label">Mật khẩu</label>
                  <Input
                    type="password"
                    value={form.password}
                    onChange={(event) => updateForm("password", event.target.value)}
                    placeholder="Nhập mật khẩu"
                    disabled={!canEditForm}
                  />
                  {formErrors.password ? (
                    <p className="mt-2 text-sm text-rose-600">{formErrors.password}</p>
                  ) : null}
                </div>
                <div>
                  <label className="field-label">Xác nhận mật khẩu</label>
                  <Input
                    type="password"
                    value={form.confirmPassword}
                    onChange={(event) => updateForm("confirmPassword", event.target.value)}
                    placeholder="Nhập lại mật khẩu"
                    disabled={!canEditForm}
                  />
                  {formErrors.confirmPassword ? (
                    <p className="mt-2 text-sm text-rose-600">{formErrors.confirmPassword}</p>
                  ) : null}
                </div>
              </div>

              {formError ? (
                <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{formError}</div>
              ) : null}

              {formSuccess ? (
                <div className="rounded-2xl bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
                  {formSuccess}
                </div>
              ) : null}

              <div className="flex flex-wrap items-center justify-between gap-3 pt-2">
                <Link
                  to="/login"
                  className="text-sm font-medium text-primary-700 transition hover:text-primary-800"
                >
                  Đã có tài khoản? Quay lại đăng nhập
                </Link>
                {canEditForm ? (
                  <Button type="submit" disabled={submitting}>
                    {submitting
                      ? registration?.approvalStatus === "rejected"
                        ? "Đang gửi lại..."
                        : "Đang gửi đăng ký..."
                      : registration?.approvalStatus === "rejected"
                        ? "Cập nhật và gửi lại"
                        : "Gửi đăng ký"}
                  </Button>
                ) : null}
              </div>
            </form>
          </Card>

          <div className="space-y-6">
            <Card className="p-6">
              <p className="text-sm font-semibold tracking-[0.12em] text-primary-600">Hồ sơ đã gửi</p>
              <h2 className="mt-3 text-xl font-bold text-ink-900">Mở lại hồ sơ đăng ký của bạn</h2>
              <p className="mt-3 text-sm leading-6 text-ink-500">
                Dùng email và mật khẩu đã đăng ký trước đó để xem trạng thái hiện tại hoặc chỉnh sửa hồ
                sơ bị từ chối.
              </p>

              <div className="mt-5 space-y-4">
                <div>
                  <label className="field-label">Email đã đăng ký</label>
                  <Input
                    type="email"
                    value={lookupEmail}
                    onChange={(event) => {
                      setLookupEmail(event.target.value);
                      setLookupError("");
                    }}
                    placeholder="Nhập email đã đăng ký"
                  />
                </div>
                <div>
                  <label className="field-label">Mật khẩu đã đăng ký</label>
                  <Input
                    type="password"
                    value={lookupPassword}
                    onChange={(event) => {
                      setLookupPassword(event.target.value);
                      setLookupError("");
                    }}
                    placeholder="Nhập mật khẩu"
                  />
                </div>

                {lookupError ? (
                  <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{lookupError}</div>
                ) : null}

                <Button className="w-full" variant="secondary" onClick={() => void handleLoadExisting()}>
                  {loadingExisting ? "Đang tải hồ sơ..." : "Xem hồ sơ của tôi"}
                </Button>
              </div>
            </Card>

            <Card className="p-6">
              <p className="text-sm font-semibold tracking-[0.12em] text-primary-600">Lưu ý</p>
              <ul className="mt-3 space-y-3 text-sm leading-6 text-ink-500">
                <li>Hồ sơ mới luôn được tạo ở trạng thái chờ duyệt.</li>
                <li>Khi hồ sơ bị từ chối, hệ thống sẽ lưu lại lý do để bạn chỉnh sửa và gửi lại.</li>
                <li>Sau khi được duyệt, tài khoản chỉ có quyền của chủ quán và không thấy chức năng super admin.</li>
              </ul>
            </Card>
          </div>
        </div>
      </div>
    </main>
  );
};
