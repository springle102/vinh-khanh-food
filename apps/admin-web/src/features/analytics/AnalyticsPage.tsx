import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { Card } from "../../components/ui/Card";
import { useAdminData } from "../../data/store";
import { formatNumber, languageLabels } from "../../lib/utils";
import { getPoiTitle } from "../../lib/selectors";

export const AnalyticsPage = () => {
  const { state } = useAdminData();

  const deviceBreakdown = ["web", "android", "ios"].map((deviceType) => ({
    label: deviceType.toUpperCase(),
    value: state.viewLogs.filter((item) => item.deviceType === deviceType).length,
  }));

  const languageBreakdown = Object.entries(languageLabels).map(([code, label]) => ({
    label,
    value: state.audioListenLogs.filter((item) => item.languageCode === code).length,
  }));

  const poiBreakdown = state.pois
    .map((poi) => ({
      name: getPoiTitle(state, poi.id),
      value: state.viewLogs.filter((item) => item.poiId === poi.id).length,
    }))
    .sort((left, right) => right.value - left.value)
    .slice(0, 6);

  const averageListenDuration = Math.round(
    state.audioListenLogs.reduce((sum, item) => sum + item.durationInSeconds, 0) /
    Math.max(state.audioListenLogs.length, 1),
  );

  return (
    <div className="space-y-6">


      <section className="grid gap-4 md:grid-cols-3">
        {[
          ["Tổng lượt scan/view", formatNumber(state.viewLogs.length)],
          ["Tổng lượt nghe audio", formatNumber(state.audioListenLogs.length)],
          ["Thời lượng nghe trung bình", `${averageListenDuration}s`],
        ].map(([label, value]) => (
          <Card key={label}>
            <p className="text-sm text-ink-500">{label}</p>
            <p className="mt-3 text-3xl font-bold text-ink-900">{value}</p>
          </Card>
        ))}
      </section>

      <section className="grid gap-6 xl:grid-cols-2">
        <Card>
          <h2 className="section-heading">Thiết bị truy cập</h2>
          <div className="mt-6 h-72">
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
          <h2 className="section-heading">Ngôn ngữ nghe audio</h2>
          <div className="mt-6 h-72">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={languageBreakdown} layout="vertical" margin={{ left: 30 }}>
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
        <h2 className="section-heading">Top POI theo lượt xem</h2>
        <div className="mt-6 h-80">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={poiBreakdown}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} />
              <XAxis dataKey="name" hide />
              <YAxis />
              <Tooltip />
              <Bar dataKey="value" fill="#d9a845" radius={[12, 12, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
        <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {poiBreakdown.map((item) => (
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
