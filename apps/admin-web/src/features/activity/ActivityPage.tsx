import { Card } from "../../components/ui/Card";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import { formatDateTime, roleLabels } from "../../lib/utils";

export const ActivityPage = () => {
  const { state } = useAdminData();

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">Audit trail</p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Nhật ký hoạt động quản trị</h1>
      </Card>

      <Card>
        <div className="space-y-4">
          {state.auditLogs.map((log) => (
            <div
              key={log.id}
              className="grid gap-3 rounded-3xl border border-sand-100 bg-white p-4 md:grid-cols-[220px_minmax(0,1fr)_140px]"
            >
              <div>
                <p className="font-semibold text-ink-900">{log.actorName}</p>
                <p className="mt-1 text-sm text-ink-500">{roleLabels[log.actorRole]}</p>
              </div>
              <div>
                <p className="text-sm font-semibold text-ink-900">{log.action}</p>
                <p className="mt-1 break-all text-sm text-ink-500">Target: {log.target}</p>
              </div>
              <div className="flex flex-col items-start gap-2 md:items-end">
                <StatusBadge status="active" label="Ghi nhận" />
                <p className="text-sm text-ink-500">{formatDateTime(log.createdAt)}</p>
              </div>
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
};
