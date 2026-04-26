import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { type ReactNode, useEffect, useState } from "react";
import { Button } from "../../components/ui/Button";
import { Card } from "../../components/ui/Card";
import { Icon } from "../../components/ui/Icons";
import { useAdminData } from "../../data/store";
import type { DashboardSummary, LanguageCode } from "../../data/types";
import { adminApi, getErrorMessage } from "../../lib/api";
import { formatNumber, languageLabels } from "../../lib/utils";

const EMPTY_SUMMARY: DashboardSummary = {
  totalPoiViews: 0,
  totalAudioPlays: 0,
  totalQrScans: 0,
  totalOfferViews: 0,
  totalPois: 0,
  totalTours: 0,
  totalOffers: 0,
  onlineUsers: 0,
  audioPlaysByLanguage: [],
  poiViewsByPoi: [],
};

const resolveLanguageLabel = (languageCode: string) =>
  languageLabels[languageCode as LanguageCode] ?? languageCode.toUpperCase();

const StatTable = ({
  headers,
  rows,
  emptyMessage,
}: {
  headers: string[];
  rows: ReactNode[][];
  emptyMessage: string;
}) => (
  <div className="overflow-hidden rounded-3xl border border-sand-100 bg-white">
    <div className="max-h-[22rem] overflow-auto">
      <table className="min-w-full border-collapse">
        <thead className="sticky top-0 bg-sand-50">
          <tr>
            {headers.map((header) => (
              <th
                key={header}
                className="border-b border-sand-100 px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.14em] text-ink-500"
              >
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td
                colSpan={headers.length}
                className="px-4 py-5 text-sm text-ink-500"
              >
                {emptyMessage}
              </td>
            </tr>
          ) : (
            rows.map((row, rowIndex) => (
              <tr key={rowIndex} className="border-b border-sand-100 last:border-b-0">
                {row.map((cell, cellIndex) => (
                  <td key={cellIndex} className="px-4 py-3 align-top text-sm text-ink-700">
                    {cell}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  </div>
);

export const DashboardPage = () => {
  const { state } = useAdminData();
  const [summary, setSummary] = useState<DashboardSummary>(EMPTY_SUMMARY);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [manualRefreshNonce, setManualRefreshNonce] = useState(0);

  useEffect(() => {
    let isDisposed = false;
    let pollTimer: number | null = null;

    const loadSummary = async (showLoading: boolean) => {
      if (showLoading) {
        setIsLoading(true);
      }

      setError(null);

      try {
        const nextSummary = await adminApi.getDashboardSummary();
        if (!isDisposed) {
          setSummary({
            totalPoiViews: nextSummary.totalPoiViews ?? 0,
            totalAudioPlays: nextSummary.totalAudioPlays ?? 0,
            totalQrScans: nextSummary.totalQrScans ?? 0,
            totalOfferViews: nextSummary.totalOfferViews ?? 0,
            totalPois: nextSummary.totalPois ?? 0,
            totalTours: nextSummary.totalTours ?? 0,
            totalOffers: nextSummary.totalOffers ?? 0,
            onlineUsers: nextSummary.onlineUsers ?? 0,
            audioPlaysByLanguage: nextSummary.audioPlaysByLanguage ?? [],
            poiViewsByPoi: nextSummary.poiViewsByPoi ?? [],
          });
        }
      } catch (nextError) {
        if (!isDisposed) {
          setError(getErrorMessage(nextError));
          if (showLoading) {
            setSummary(EMPTY_SUMMARY);
          }
        }
      } finally {
        if (!isDisposed && showLoading) {
          setIsLoading(false);
        }
      }
    };

    const reloadVisibleSummary = () => {
      if (typeof document !== "undefined" && document.visibilityState === "hidden") {
        return;
      }

      void loadSummary(false);
    };

    void loadSummary(true);

    if (typeof window !== "undefined") {
      pollTimer = window.setInterval(() => {
        void loadSummary(false);
      }, 10000);
      window.addEventListener("focus", reloadVisibleSummary);
    }

    if (typeof document !== "undefined") {
      document.addEventListener("visibilitychange", reloadVisibleSummary);
    }

    return () => {
      isDisposed = true;

      if (pollTimer !== null && typeof window !== "undefined") {
        window.clearInterval(pollTimer);
      }

      if (typeof window !== "undefined") {
        window.removeEventListener("focus", reloadVisibleSummary);
      }

      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", reloadVisibleSummary);
      }
    };
  }, [manualRefreshNonce, state.syncState?.version]);

  const summaryCards = [
    {
      label: "Người dùng online",
      value: formatNumber(summary.onlineUsers),
      icon: "users" as const,
    },
    {
      label: "Tổng lượt xem POI",
      value: formatNumber(summary.totalPoiViews),
      icon: "content" as const,
    },
    {
      label: "Tổng lượt nghe audio",
      value: formatNumber(summary.totalAudioPlays),
      icon: "audio" as const,
    },
    {
      label: "Tổng lượt quét QR",
      value: formatNumber(summary.totalQrScans),
      icon: "chart" as const,
    },
    {
      label: "Lượt ưu đãi",
      value: formatNumber(summary.totalOfferViews),
      icon: "gift" as const,
    },
    {
      label: "Tổng POI",
      value: formatNumber(summary.totalPois),
      icon: "content" as const,
    },
    {
      label: "Tổng tour",
      value: formatNumber(summary.totalTours),
      icon: "route" as const,
    },
    {
      label: "Tổng ưu đãi",
      value: formatNumber(summary.totalOffers),
      icon: "gift" as const,
    },
  ];

  const audioRows = summary.audioPlaysByLanguage.map((item, index) => [
    <span className="font-semibold text-ink-900">{index + 1}</span>,
    <span className="font-medium text-ink-900">{resolveLanguageLabel(item.languageCode)}</span>,
    <span className="font-semibold text-ink-900">{formatNumber(item.totalAudioPlays)}</span>,
  ]);

  const poiRows = summary.poiViewsByPoi.map((item, index) => [
    <span className="font-semibold text-ink-900">{index + 1}</span>,
    <div className="min-w-0">
      <p className="truncate font-semibold text-ink-900">{item.poiTitle}</p>
      <p className="mt-1 text-xs text-ink-500">{item.poiId}</p>
    </div>,
    <span className="font-semibold text-ink-900">{formatNumber(item.totalPoiViews)}</span>,
  ]);

  return (
    <div className="space-y-6">
      <section className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="section-heading">Thống kê tổng quan</h1>
          <p className="mt-2 text-sm text-ink-500">
            Dữ liệu được tải lại trực tiếp từ cơ sở dữ liệu mỗi lần và không dùng cache cũ.
          </p>
        </div>
        <Button
          variant="secondary"
          disabled={isLoading}
          onClick={() => setManualRefreshNonce((value) => value + 1)}
        >
          {isLoading ? "Đang lấy số liệu..." : "Làm mới dữ liệu"}
        </Button>
      </section>

      <section className="grid gap-4 md:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-8">
        {summaryCards.map((item) => (
          <Card key={item.label}>
            <div className="flex min-h-[176px] flex-col items-center justify-center text-center">
              <div className="rounded-2xl bg-primary-50 p-3 text-primary-600">
                <Icon name={item.icon} className="h-6 w-6" />
              </div>
              <p className="mt-5 text-base font-medium text-ink-500">{item.label}</p>
              <p className="mt-3 text-4xl font-bold text-ink-900">{item.value}</p>
            </div>
          </Card>
        ))}
      </section>

      {error ? (
        <Card>
          <p className="text-sm text-rose-600">{error}</p>
        </Card>
      ) : null}

      <section className="grid gap-6 xl:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
        <Card>
          <div className="mb-6 flex items-start justify-between gap-4">
            <div>
              <h2 className="section-heading">Lượt nghe theo ngôn ngữ</h2>
              <p className="mt-2 text-sm text-ink-500">
                Bảng thống kê số lượt phát audio theo từng ngôn ngữ thực tế.
              </p>
            </div>
            <div className="rounded-2xl bg-sand-50 px-4 py-3 text-right">
              <p className="text-xs uppercase tracking-[0.14em] text-ink-400">Ngôn ngữ</p>
              <p className="mt-1 text-lg font-bold text-ink-900">
                {formatNumber(summary.audioPlaysByLanguage.length)}
              </p>
            </div>
          </div>
          {isLoading ? (
            <p className="text-sm text-ink-500">Đang tải thống kê...</p>
          ) : (
            <div className="space-y-5">
              <div className="h-72 rounded-3xl border border-sand-100 bg-sand-50/45 p-3">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart
                    data={summary.audioPlaysByLanguage}
                    layout="vertical"
                    margin={{ top: 4, right: 12, left: 24, bottom: 4 }}
                  >
                    <CartesianGrid strokeDasharray="3 3" horizontal={false} />
                    <XAxis type="number" allowDecimals={false} />
                    <YAxis
                      type="category"
                      dataKey="languageCode"
                      width={92}
                      tickFormatter={resolveLanguageLabel}
                    />
                    <Tooltip
                      formatter={(value: number) => formatNumber(value)}
                      labelFormatter={(label) => resolveLanguageLabel(label)}
                    />
                    <Bar dataKey="totalAudioPlays" fill="#f97316" radius={[0, 12, 12, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
              <StatTable
                headers={["STT", "Ngôn ngữ", "Lượt nghe"]}
                rows={audioRows}
                emptyMessage="Chưa có dữ liệu lượt nghe theo ngôn ngữ."
              />
            </div>
          )}
        </Card>

        <Card>
          <div className="mb-6 flex items-start justify-between gap-4">
            <div>
              <h2 className="section-heading">Lượt xem từng POI</h2>
              <p className="mt-2 text-sm text-ink-500">
                Bảng thống kê số lượt người dùng mở chi tiết từng POI.
              </p>
            </div>
            <div className="rounded-2xl bg-sand-50 px-4 py-3 text-right">
              <p className="text-xs uppercase tracking-[0.14em] text-ink-400">POI</p>
              <p className="mt-1 text-lg font-bold text-ink-900">
                {formatNumber(summary.poiViewsByPoi.length)}
              </p>
            </div>
          </div>
          {isLoading ? (
            <p className="text-sm text-ink-500">Đang tải thống kê...</p>
          ) : (
            <StatTable
              headers={["STT", "POI", "Lượt xem"]}
              rows={poiRows}
              emptyMessage="Chưa có dữ liệu lượt xem POI."
            />
          )}
        </Card>
      </section>
    </div>
  );
};
