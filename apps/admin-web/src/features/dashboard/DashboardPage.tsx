import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { Card } from "../../components/ui/Card";
import { Icon } from "../../components/ui/Icons";
import { StatusBadge } from "../../components/ui/StatusBadge";
import { useAdminData } from "../../data/store";
import { getCategoryName, getPoiTitle, getPoiTranslation } from "../../lib/selectors";
import { formatDateTime, formatNumber, languageLabels } from "../../lib/utils";

const chartPalette = ["#f97316", "#de6245", "#d9a845", "#9a3412", "#475569"];

const buildDateKey = (date: Date) => date.toISOString().slice(0, 10);

export const DashboardPage = () => {
  const { state } = useAdminData();

  const totalViews = state.viewLogs.length;
  const totalListens = state.audioListenLogs.length;
  const totalPublishedPois = state.pois.filter((item) => item.status === "published").length;
  const averageListenDuration = Math.round(
    state.audioListenLogs.reduce((sum, item) => sum + item.durationInSeconds, 0) /
      Math.max(state.audioListenLogs.length, 1),
  );

  const lastSevenDays = Array.from({ length: 7 }, (_, index) => {
    const date = new Date();
    date.setDate(date.getDate() - (6 - index));
    const key = buildDateKey(date);
    const label = new Intl.DateTimeFormat("vi-VN", { weekday: "short" }).format(date);

    return {
      label,
      views: state.viewLogs.filter((item) => item.viewedAt.startsWith(key)).length,
      listens: state.audioListenLogs.filter((item) => item.listenedAt.startsWith(key)).length,
    };
  });

  const languageDistribution = Object.entries(languageLabels)
    .map(([languageCode, label]) => ({
      name: label,
      value: state.viewLogs.filter((item) => item.languageCode === languageCode).length,
    }))
    .filter((item) => item.value > 0);

  const topPois = state.pois
    .map((poi) => ({
      id: poi.id,
      title: getPoiTitle(state, poi.id),
      category: getCategoryName(state, poi.categoryId),
      views: state.viewLogs.filter((item) => item.poiId === poi.id).length,
      listens: state.audioListenLogs.filter((item) => item.poiId === poi.id).length,
      status: poi.status,
      updatedAt: poi.updatedAt,
    }))
    .sort((left, right) => right.views + right.listens - (left.views + left.listens))
    .slice(0, 5);

  const latestPublished = state.pois
    .filter((item) => item.status === "published")
    .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))
    .slice(0, 4);

  const deviceBreakdown = ["web", "android", "ios"].map((deviceType) => ({
    label: deviceType.toUpperCase(),
    value: state.viewLogs.filter((item) => item.deviceType === deviceType).length,
  }));

  const audioLanguageBreakdown = Object.entries(languageLabels).map(([code, label]) => ({
    label,
    value: state.audioListenLogs.filter((item) => item.languageCode === code).length,
  }));

  const topViewedPois = state.pois
    .map((poi) => ({
      name: getPoiTitle(state, poi.id),
      value: state.viewLogs.filter((item) => item.poiId === poi.id).length,
    }))
    .sort((left, right) => right.value - left.value)
    .slice(0, 6);

  const summaryCards = [
    {
      label: "Tổng lượt xem",
      value: formatNumber(totalViews),
      icon: "content" as const,
    },
    {
      label: "Lượt nghe audio",
      value: formatNumber(totalListens),
      icon: "audio" as const,
    },
    {
      label: "Điểm đã xuất bản",
      value: formatNumber(totalPublishedPois),
      icon: "map" as const,
    },
    {
      label: "Nghe audio trung bình",
      value: `${formatNumber(averageListenDuration)}s`,
      icon: "audio" as const,
    },
  ];

  return (
    <div className="space-y-6">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
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

      <section className="grid gap-6 xl:grid-cols-[minmax(0,1.35fr)_minmax(0,0.95fr)]">
        <Card>
          <div className="mb-6 flex items-center justify-between">
            <div>
              <h2 className="section-heading">Lượt truy cập 7 ngày gần nhất</h2>
            </div>
            <StatusBadge status="active" label="Dữ liệu demo" />
          </div>
          <div className="h-80">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={lastSevenDays}>
                <XAxis dataKey="label" stroke="#8f91a0" />
                <YAxis stroke="#8f91a0" />
                <Tooltip />
                <Line type="monotone" dataKey="views" stroke="#f97316" strokeWidth={3} dot={false} />
                <Line type="monotone" dataKey="listens" stroke="#de6245" strokeWidth={3} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card>
          <div className="mb-6">
            <h2 className="section-heading">Ngôn ngữ được chọn nhiều nhất</h2>
          </div>
          <div className="grid gap-6 md:grid-cols-[220px_minmax(0,1fr)] xl:grid-cols-1">
            <div className="mx-auto h-56 w-full max-w-[220px]">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={languageDistribution} innerRadius={54} outerRadius={88} dataKey="value" paddingAngle={2}>
                    {languageDistribution.map((item, index) => (
                      <Cell key={item.name} fill={chartPalette[index % chartPalette.length]} />
                    ))}
                  </Pie>
                  <Tooltip />
                </PieChart>
              </ResponsiveContainer>
            </div>
            <div className="space-y-3">
              {languageDistribution.map((item, index) => (
                <div
                  key={item.name}
                  className="flex items-center justify-between rounded-2xl bg-sand-50 px-4 py-3"
                >
                  <div className="flex items-center gap-3">
                    <span
                      className="h-3 w-3 rounded-full"
                      style={{ backgroundColor: chartPalette[index % chartPalette.length] }}
                    />
                    <span className="font-medium text-ink-700">{item.name}</span>
                  </div>
                  <span className="font-semibold text-ink-900">{formatNumber(item.value)}</span>
                </div>
              ))}
            </div>
          </div>
        </Card>
      </section>

      <section className="grid gap-6 xl:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
        <Card>
          <div className="mb-6">
            <h2 className="section-heading">Top POI được tương tác nhiều nhất</h2>
          </div>
          <div className="space-y-4">
            {topPois.map((poi, index) => (
              <div
                key={poi.id}
                className="grid gap-3 rounded-3xl border border-sand-100 bg-white px-4 py-4 md:grid-cols-[56px_minmax(0,1fr)_120px]"
              >
                <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary-50 text-xl font-bold text-primary-600">
                  {index + 1}
                </div>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-3">
                    <h3 className="truncate text-base font-semibold text-ink-900">{poi.title}</h3>
                    <StatusBadge status={poi.status} />
                  </div>
                  <p className="mt-1 text-sm text-ink-500">{poi.category}</p>
                  <p className="mt-2 text-xs text-ink-400">Cập nhật {formatDateTime(poi.updatedAt)}</p>
                </div>
                <div className="grid grid-cols-2 gap-3 rounded-2xl bg-sand-50 p-3 text-center">
                  <div>
                    <p className="text-xs uppercase tracking-wide text-ink-400">Views</p>
                    <p className="mt-1 font-bold text-ink-900">{formatNumber(poi.views)}</p>
                  </div>
                  <div>
                    <p className="text-xs uppercase tracking-wide text-ink-400">Audio</p>
                    <p className="mt-1 font-bold text-ink-900">{formatNumber(poi.listens)}</p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </Card>

        <Card>
          <div className="mb-6">
            <h2 className="section-heading">Điểm vừa cập nhật</h2>
          </div>
          <div className="space-y-4">
            {latestPublished.map((poi) => {
              const translation = getPoiTranslation(state, poi.id);

              return (
                <div key={poi.id} className="rounded-3xl bg-sand-50 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <h3 className="font-semibold text-ink-900">{translation?.title ?? poi.slug}</h3>
                    <StatusBadge status={poi.status} />
                  </div>
                  <p className="mt-2 text-sm leading-6 text-ink-500">
                    {translation?.shortText ?? "Chưa có mô tả ngắn."}
                  </p>
                  <div className="mt-3 flex flex-wrap items-center gap-3 text-xs text-ink-400">
                    <span>{poi.address}</span>
                    <span>|</span>
                    <span>{getCategoryName(state, poi.categoryId)}</span>
                    <span>|</span>
                    <span>{formatDateTime(poi.updatedAt)}</span>
                  </div>
                </div>
              );
            })}
          </div>
        </Card>
      </section>

      <section className="grid gap-6 xl:grid-cols-2">
        <Card>
          <div className="mb-6">
            <h2 className="section-heading">Thiết bị truy cập</h2>
          </div>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={deviceBreakdown}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} />
                <XAxis dataKey="label" />
                <YAxis />
                <Tooltip />
                <Bar dataKey="value" fill="#f97316" radius={[12, 12, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card>
          <div className="mb-6">
            <h2 className="section-heading">Ngôn ngữ nghe audio</h2>
          </div>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={audioLanguageBreakdown} layout="vertical" margin={{ left: 30 }}>
                <CartesianGrid strokeDasharray="3 3" horizontal={false} />
                <XAxis type="number" />
                <YAxis type="category" dataKey="label" width={88} />
                <Tooltip />
                <Bar dataKey="value" fill="#de6245" radius={[0, 12, 12, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>
      </section>

      <Card>
        <div className="mb-6">
          <h2 className="section-heading">Top POI theo lượt xem</h2>
        </div>
        <div className="h-80">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={topViewedPois}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} />
              <XAxis dataKey="name" hide />
              <YAxis />
              <Tooltip />
              <Bar dataKey="value" fill="#d9a845" radius={[12, 12, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
        <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {topViewedPois.map((item) => (
            <div key={item.name} className="rounded-2xl bg-sand-50 px-4 py-3">
              <p className="font-semibold text-ink-900">{item.name}</p>
              <p className="mt-1 text-sm text-ink-500">{formatNumber(item.value)} lượt xem</p>
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
};
