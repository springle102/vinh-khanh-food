import { useMemo, useRef, useState, type ChangeEvent } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { QRCodeRecord } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { getPlaceTitle } from "../../lib/selectors";
import { formatNumber } from "../../lib/utils";
import { useAuth } from "../auth/AuthContext";

const QrImageUploadButton = ({
  disabled,
  hasImage,
  onUpload,
}: {
  disabled?: boolean;
  hasImage: boolean;
  onUpload: (file: File) => Promise<void>;
}) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");

  const handleFileChange = async (event: ChangeEvent<HTMLInputElement>) => {
    const nextFile = event.target.files?.[0];
    event.target.value = "";

    if (!nextFile) {
      return;
    }

    setIsUploading(true);
    setUploadError("");

    try {
      await onUpload(nextFile);
    } catch (error) {
      setUploadError(getErrorMessage(error));
    } finally {
      setIsUploading(false);
    }
  };

  return (
    <div className="space-y-2">
      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/jpeg,image/webp,image/svg+xml"
        className="hidden"
        onChange={(event) => {
          void handleFileChange(event);
        }}
      />
      <Button
        variant="secondary"
        disabled={disabled || isUploading}
        onClick={() => inputRef.current?.click()}
      >
        {isUploading ? "Đang tải..." : hasImage ? "Đổi ảnh QR" : "Thêm ảnh QR"}
      </Button>
      {uploadError ? (
        <div className="rounded-2xl bg-rose-50 px-3 py-2 text-xs text-rose-700">{uploadError}</div>
      ) : null}
    </div>
  );
};

export const QrRoutesPage = () => {
  const { state, saveQrCodeImage, saveRouteQrState } = useAdminData();
  const { user } = useAuth();
  const [pageError, setPageError] = useState("");

  const scanCountsByEntityId = useMemo(() => {
    const counts = new Map<string, number>();

    for (const log of state.viewLogs) {
      counts.set(log.placeId, (counts.get(log.placeId) ?? 0) + 1);
    }

    return counts;
  }, [state.viewLogs]);

  const handleUploadQr = async (item: QRCodeRecord, file: File) => {
    if (!user) {
      return;
    }

    setPageError("");
    const uploaded = await adminApi.uploadFile(file, "images/qr-codes");
    await saveQrCodeImage(item.id, uploaded.url, user);
  };

  const handleToggleQr = async (item: QRCodeRecord) => {
    if (!user) {
      return;
    }

    setPageError("");

    try {
      await saveRouteQrState(item.id, !item.isActive, user);
    } catch (error) {
      setPageError(getErrorMessage(error));
    }
  };

  const qrColumns: DataColumn<QRCodeRecord>[] = [
    {
      key: "entity",
      header: "Mã QR",
      render: (item) => (
        <div className="flex items-center gap-3">
          {item.qrImageUrl ? (
            <img
              src={item.qrImageUrl}
              alt={getPlaceTitle(state, item.entityId)}
              className="h-16 w-16 rounded-2xl border border-sand-100 bg-white p-2"
            />
          ) : (
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl border border-dashed border-sand-200 bg-sand-50 text-xs text-ink-400">
              QR
            </div>
          )}
          <div className="min-w-0">
            <p className="font-semibold text-ink-900">{getPlaceTitle(state, item.entityId)}</p>
          </div>
        </div>
      ),
    },
    {
      key: "scanCount",
      header: "Lượt quét",
      render: (item) => (
        <p className="text-sm font-semibold text-ink-800">
          {formatNumber(scanCountsByEntityId.get(item.entityId) ?? 0)} lượt
        </p>
      ),
    },
    {
      key: "status",
      header: "Trạng thái",
      render: (item) => (
        <StatusBadge
          status={item.isActive ? "active" : "archived"}
          label={item.isActive ? "Active" : "Inactive"}
        />
      ),
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (item) => (
        <div className="flex flex-wrap gap-2">
          <QrImageUploadButton
            disabled={!user}
            hasImage={Boolean(item.qrImageUrl)}
            onUpload={(file) => handleUploadQr(item, file)}
          />
          <Button
            variant="ghost"
            disabled={!user}
            onClick={() => {
              void handleToggleQr(item);
            }}
          >
            {item.isActive ? "Tắt QR" : "Bật QR"}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">QR</p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Quản lý mã QR</h1>
      </Card>

      <Card>
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h2 className="section-heading">Danh sách QR codes</h2>
          </div>
        </div>
        {pageError ? (
          <div className="mt-5 rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{pageError}</div>
        ) : null}
        <div className="mt-6">
          <DataTable data={state.qrCodes} columns={qrColumns} rowKey={(row) => row.id} />
        </div>
      </Card>
    </div>
  );
};
