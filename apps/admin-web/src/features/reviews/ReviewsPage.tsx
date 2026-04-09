import { Card } from "../../components/ui/Card";
import { DataTable, type DataColumn } from "../../components/ui/DataTable";
import { Button } from "../../components/ui/Button";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import type { Review } from "../../data/types";
import { canModerateReviews } from "../../lib/rbac";
import { useAuth } from "../auth/AuthContext";
import { formatDateTime, languageLabels } from "../../lib/utils";
import { getPoiTitle } from "../../lib/selectors";

export const ReviewsPage = () => {
  const { state, saveReviewStatus } = useAdminData();
  const { user } = useAuth();
  const canManageReviewModeration = canModerateReviews(user?.role);

  const columns: DataColumn<Review>[] = [
    {
      key: "review",
      header: "Đánh giá",
      render: (item) => (
        <div>
          <div className="flex items-center gap-3">
            <p className="font-semibold text-ink-900">{item.userName}</p>
            <StatusBadge status="draft" label={`${item.rating}/5`} />
          </div>
          <p className="mt-2 text-sm leading-6 text-ink-500">{item.comment}</p>
        </div>
      ),
    },
    {
      key: "poi",
      header: "POI",
      render: (item) => (
        <div>
          <p className="font-medium text-ink-800">{getPoiTitle(state, item.poiId)}</p>
          <p className="mt-1 text-xs text-ink-500">{languageLabels[item.languageCode]}</p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Kiểm duyệt",
      render: (item) => <StatusBadge status={item.status} />,
    },
    {
      key: "time",
      header: "Thời gian",
      render: (item) => <p className="text-sm text-ink-600">{formatDateTime(item.createdAt)}</p>,
    },
    {
      key: "actions",
      header: "Thao tác",
      render: (item) => (
        canManageReviewModeration ? (
          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              onClick={() => user && saveReviewStatus(item.id, "approved", user)}
            >
              Duyệt
            </Button>
            <Button variant="ghost" onClick={() => user && saveReviewStatus(item.id, "hidden", user)}>
              Ẩn
            </Button>
          </div>
        ) : (
          <p className="text-sm text-ink-500">Chỉ xem</p>
        )
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <p className="text-sm font-semibold uppercase tracking-[0.25em] text-primary-600">Feedback</p>
        <h1 className="mt-3 text-3xl font-bold text-ink-900">Đánh giá, phản hồi và kiểm duyệt trải nghiệm khách</h1>
      </Card>

      <Card>
        <DataTable data={state.reviews} columns={columns} rowKey={(row) => row.id} />
      </Card>
    </div>
  );
};
