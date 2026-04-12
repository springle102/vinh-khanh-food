import { useMemo, useState } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Input, Textarea } from "../../components/ui/Input";
import { Modal } from "../../components/ui/Modal";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { AdminUser } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { approvalStatusLabels, formatDateTime } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

export const OwnerRegistrationsPage = () => {
  const { state, isRefreshing, refreshData } = useAdminData();
  const { user } = useAuth();
  const [selectedRegistrationId, setSelectedRegistrationId] = useState<string | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState("");
  const [decisionError, setDecisionError] = useState("");
  const [submittingAction, setSubmittingAction] = useState<"approve" | "reject" | null>(null);

  const registrations = useMemo(
    () =>
      state.users
        .filter((account) => account.role === "PLACE_OWNER")
        .sort((left, right) =>
          (right.registrationSubmittedAt ?? right.createdAt).localeCompare(
            left.registrationSubmittedAt ?? left.createdAt,
          ),
        ),
    [state.users],
  );

  const selectedRegistration = useMemo(
    () => registrations.find((account) => account.id === selectedRegistrationId) ?? null,
    [registrations, selectedRegistrationId],
  );

  const pendingCount = registrations.filter((account) => account.approvalStatus === "pending").length;
  const approvedCount = registrations.filter((account) => account.approvalStatus === "approved").length;
  const rejectedCount = registrations.filter((account) => account.approvalStatus === "rejected").length;

  const openDetail = (registration: AdminUser) => {
    setSelectedRegistrationId(registration.id);
    setRejectReason(registration.rejectionReason ?? "");
    setDecisionError("");
    setDetailOpen(true);
  };

  const closeDetail = () => {
    setDetailOpen(false);
    setSelectedRegistrationId(null);
    setRejectReason("");
    setDecisionError("");
    setSubmittingAction(null);
  };

  const syncAfterAction = async (registrationId: string) => {
    const nextState = await refreshData();
    const stillExists = nextState.users.some((account) => account.id === registrationId);
    if (!stillExists) {
      closeDetail();
      return;
    }

    setSelectedRegistrationId(registrationId);
  };

  const handleApprove = async () => {
    if (!selectedRegistration) {
      return;
    }

    setDecisionError("");
    setSubmittingAction("approve");

    try {
      await adminApi.approvePlaceOwnerRegistration(selectedRegistration.id);
      await syncAfterAction(selectedRegistration.id);
    } catch (error) {
      setDecisionError(getErrorMessage(error));
    } finally {
      setSubmittingAction(null);
    }
  };

  const handleReject = async () => {
    if (!selectedRegistration) {
      return;
    }

    if (!rejectReason.trim()) {
      setDecisionError("Hãy nhập lý do từ chối trước khi lưu.");
      return;
    }

    setDecisionError("");
    setSubmittingAction("reject");

    try {
      await adminApi.rejectPlaceOwnerRegistration(selectedRegistration.id, rejectReason.trim());
      await syncAfterAction(selectedRegistration.id);
    } catch (error) {
      setDecisionError(getErrorMessage(error));
    } finally {
      setSubmittingAction(null);
    }
  };

  const columns: DataColumn<AdminUser>[] = [
    {
      key: "user",
      header: "Người đăng ký",
      render: (registration) => (
        <div>
          <p className="font-semibold text-ink-900">{registration.name}</p>
          <p className="mt-1 text-sm text-ink-500">{registration.email}</p>
        </div>
      ),
    },
    {
      key: "phone",
      header: "Liên hệ",
      render: (registration) => (
        <div>
          <p className="text-sm font-medium text-ink-700">{registration.phone}</p>
          <p className="mt-1 text-xs text-ink-500">
            Nộp lúc {formatDateTime(registration.registrationSubmittedAt ?? registration.createdAt)}
          </p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      render: (registration) => (
        <StatusBadge
          status={registration.approvalStatus}
          label={approvalStatusLabels[registration.approvalStatus]}
        />
      ),
    },
    {
      key: "reviewed",
      header: "Xét duyệt",
      render: (registration) => (
        <div>
          <p className="text-sm text-ink-700">{formatDateTime(registration.registrationReviewedAt)}</p>
          <p className="mt-1 text-xs text-ink-500">
            {registration.rejectionReason
              ? registration.rejectionReason
              : registration.approvalStatus === "approved"
                ? "Đã kích hoạt tài khoản"
                : registration.approvalStatus === "pending"
                  ? "Chờ admin xử lý"
                  : "Đã lưu lý do từ chối"}
          </p>
        </div>
      ),
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (registration) => (
        <Button variant="secondary" onClick={() => openDetail(registration)}>
          Xem chi tiết
        </Button>
      ),
    },
  ];

  if (user?.role !== "SUPER_ADMIN") {
    return (
      <Card className="border border-amber-100 bg-amber-50">
        <p className="font-semibold text-amber-800">Chỉ Super Admin mới được quản lý hồ sơ đăng ký chủ quán.</p>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold tracking-[0.12em] text-primary-600">Đăng ký chủ quán</p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Duyệt hồ sơ đăng ký tài khoản chủ quán</h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-ink-500">
          Khu vực này dùng để xem hồ sơ đăng ký, kiểm tra trạng thái chờ duyệt và xử lý duyệt hoặc từ
          chối. Khi từ chối, admin phải nhập lý do để chủ quán có thể xem, chỉnh sửa và gửi lại hồ sơ.
        </p>
      </Card>

      <section className="grid gap-4 md:grid-cols-3">
        <Card>
          <p className="text-sm text-ink-500">Chờ duyệt</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">{pendingCount}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Đã duyệt</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">{approvedCount}</p>
        </Card>
        <Card>
          <p className="text-sm text-ink-500">Từ chối</p>
          <p className="mt-2 text-3xl font-bold text-ink-900">{rejectedCount}</p>
        </Card>
      </section>

      <Card>
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-xl font-semibold text-ink-900">Danh sách hồ sơ</h2>
            <p className="mt-1 text-sm text-ink-500">
              Tổng cộng {registrations.length} hồ sơ chủ quán trong hệ thống.
            </p>
          </div>
          <Button variant="secondary" onClick={() => void refreshData()} disabled={isRefreshing}>
            {isRefreshing ? "Đang tải..." : "Làm mới"}
          </Button>
        </div>

        <DataTable data={registrations} columns={columns} rowKey={(row) => row.id} pageSize={8} />
      </Card>

      <Modal
        open={detailOpen && !!selectedRegistration}
        onClose={closeDetail}
        title="Chi tiết hồ sơ đăng ký chủ quán"
        description="Kiểm tra thông tin cá nhân, trạng thái xét duyệt và xử lý phê duyệt hoặc từ chối."
        maxWidthClassName="max-w-3xl"
      >
        {selectedRegistration ? (
          <div className="space-y-6">
            <div className="grid gap-4 md:grid-cols-2">
              <Card>
                <p className="text-sm text-ink-500">Họ tên</p>
                <p className="mt-2 text-lg font-semibold text-ink-900">{selectedRegistration.name}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Email</p>
                <p className="mt-2 text-lg font-semibold text-ink-900">{selectedRegistration.email}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Số điện thoại</p>
                <p className="mt-2 text-lg font-semibold text-ink-900">{selectedRegistration.phone}</p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Trạng thái hồ sơ</p>
                <div className="mt-2">
                  <StatusBadge
                    status={selectedRegistration.approvalStatus}
                    label={approvalStatusLabels[selectedRegistration.approvalStatus]}
                  />
                </div>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Thời gian gửi</p>
                <p className="mt-2 text-lg font-semibold text-ink-900">
                  {formatDateTime(selectedRegistration.registrationSubmittedAt ?? selectedRegistration.createdAt)}
                </p>
              </Card>
              <Card>
                <p className="text-sm text-ink-500">Thời gian xét duyệt</p>
                <p className="mt-2 text-lg font-semibold text-ink-900">
                  {formatDateTime(selectedRegistration.registrationReviewedAt)}
                </p>
              </Card>
            </div>

            {selectedRegistration.rejectionReason ? (
              <div className="rounded-3xl border border-rose-100 bg-rose-50 px-5 py-4">
                <p className="text-sm font-semibold tracking-[0.12em] text-rose-600">Lý do từ chối</p>
                <p className="mt-2 text-sm leading-6 text-rose-700">{selectedRegistration.rejectionReason}</p>
              </div>
            ) : null}

            {selectedRegistration.approvalStatus === "pending" ? (
              <div className="rounded-3xl border border-sand-100 bg-sand-50 p-5">
                <label className="field-label">Lý do từ chối</label>
                <Textarea
                  value={rejectReason}
                  onChange={(event) => setRejectReason(event.target.value)}
                  placeholder="Nhập lý do nếu cần từ chối hồ sơ này"
                />
                <p className="mt-2 text-xs text-ink-500">
                  Trường này bắt buộc khi từ chối. Nội dung sẽ hiển thị cho chủ quán để họ chỉnh sửa và
                  gửi lại.
                </p>
              </div>
            ) : (
              <div className="rounded-3xl border border-sand-100 bg-sand-50 px-5 py-4 text-sm text-ink-600">
                {selectedRegistration.approvalStatus === "approved"
                  ? "Hồ sơ này đã được duyệt và tài khoản đã có thể đăng nhập với quyền chủ quán."
                  : "Hồ sơ này đã bị từ chối. Chủ quán có thể mở lại phần đăng ký của mình để chỉnh sửa và gửi lại."}
              </div>
            )}

            {decisionError ? (
              <div className="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{decisionError}</div>
            ) : null}

            <div className="flex flex-wrap justify-end gap-3 border-t border-sand-100 pt-5">
              <Button variant="ghost" onClick={closeDetail}>
                Đóng
              </Button>
              {selectedRegistration.approvalStatus === "pending" ? (
                <>
                  <Button
                    variant="danger"
                    onClick={() => void handleReject()}
                    disabled={submittingAction !== null}
                  >
                    {submittingAction === "reject" ? "Đang từ chối..." : "Từ chối"}
                  </Button>
                  <Button onClick={() => void handleApprove()} disabled={submittingAction !== null}>
                    {submittingAction === "approve" ? "Đang duyệt..." : "Duyệt"}
                  </Button>
                </>
              ) : null}
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
};
