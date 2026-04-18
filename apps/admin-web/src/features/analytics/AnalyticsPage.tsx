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
import { getPoiTitle } from "../../lib/selectors";
import { formatNumber, languageLabels } from "../../lib/utils";

export const AnalyticsPage = () => {
  const { state } = useAdminData();
  const viewEvents = state.usageEvents.filter((item) => item.eventType === "poi_view");
  const audioEvents = state.usageEvents.filter((item) => item.eventType === "audio_play");
  const qrEvents = state.usageEvents.filter((item) => item.eventType === "qr_scan");

  const deviceBreakdown = ["android", "web"].map((platform) => ({
    label: platform.toUpperCase(),
    value: state.usageEvents.filter((item) => item.platform === platform).length,
  }));

  const languageBreakdown = Object.entries(languageLabels).map(([code, label]) => ({
    label,
    value: state.usageEvents.filter((item) => item.languageCode === code).length,
  }));

  const poiBreakdown = state.pois
    .map((poi) => ({
      id: poi.id,
      name: getPoiTitle(state, poi.id),
      value: viewEvents.filter((item) => item.poiId === poi.id).length,
    }))
    .sort((left, right) => right.value - left.value)
    .slice(0, 6);

  const averageListenDuration = Math.round(
    audioEvents.reduce((sum, item) => sum + (item.durationInSeconds ?? 0), 0) /
      Math.max(audioEvents.length, 1),
  );

  return (
    <div className="space-y-6">
      <section className="grid gap-4 md:grid-cols-4">
        {[
          ["Tổng lượt xem POI", formatNumber(viewEvents.length)],
          ["Tổng lượt nghe audio", formatNumber(audioEvents.length)],
          ["Tổng lượt quét QR", formatNumber(qrEvents.length)],
          ["Thời lượng nghe TB", `${averageListenDuration}s`],
        ].map(([label, value]) => (
          <Card key={label}>
            <p className="text-sm text-ink-500">{label}</p>
            <p className="mt-3 text-3xl font-bold text-ink-900">{value}</p>
          </Card>
        ))}
      </section>

      <section className="grid gap-6 xl:grid-cols-2">
        <Card>
          <h2 className="section-heading">Nền tảng sử dụng</h2>
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
          <h2 className="section-heading">Ngôn ngữ sử dụng</h2>
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
      </Card>
    </div>
  );
};
