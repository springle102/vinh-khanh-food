import { useEffect, useMemo, useRef, useState, type MutableRefObject } from "react";
import L from "leaflet";
import {
  MapContainer,
  Marker,
  Popup,
  TileLayer,
  useMap,
  useMapEvents,
} from "react-leaflet";

const DEFAULT_CENTER: [number, number] = [10.7578, 106.7033];
const DEFAULT_ZOOM = 15;
const FOCUS_ZOOM = 17;
const SELECTION_RADIUS_METERS = 180;
const TRANSPARENT_TILE_URL =
  "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='256' height='256' viewBox='0 0 256 256'%3E%3C/svg%3E";
const TILE_SOURCES: {
  key: string;
  url: string;
  maxZoom: number;
  attribution: string;
}[] = [
  {
    key: "osm",
    url: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    maxZoom: 19,
    attribution:
      '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  },
  {
    key: "carto-light",
    url: "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png",
    maxZoom: 20,
    attribution:
      '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; CARTO',
  },
];

const markerIconCache = new Map<string, L.DivIcon>();

const isValidLatitude = (value: number) => Number.isFinite(value) && value >= -90 && value <= 90;
const isValidLongitude = (value: number) => Number.isFinite(value) && value >= -180 && value <= 180;

const haversineDistanceInMeters = (
  lat1: number,
  lng1: number,
  lat2: number,
  lng2: number,
) => {
  const toRadians = (value: number) => (value * Math.PI) / 180;
  const earthRadius = 6_371_000;
  const dLat = toRadians(lat2 - lat1);
  const dLng = toRadians(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos(toRadians(lat1)) *
      Math.cos(toRadians(lat2)) *
      Math.sin(dLng / 2) *
      Math.sin(dLng / 2);

  return 2 * earthRadius * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
};

const createPoiMarkerIcon = (order?: number) => {
  const cacheKey = typeof order === "number" ? `selected-${order}` : "default";
  const cached = markerIconCache.get(cacheKey);
  if (cached) {
    return cached;
  }

  const selected = typeof order === "number";
  const color = selected ? "#0f766e" : "#1f2937";
  const halo = selected ? "rgba(15, 118, 110, 0.22)" : "rgba(31, 41, 55, 0.12)";
  const label = selected ? String(order) : "";

  const nextIcon = L.divIcon({
    className: "vinh-khanh-tour-poi-marker",
    html: `
      <div style="position: relative; width: 34px; height: 34px;">
        <span style="
          position: absolute;
          inset: 0;
          border-radius: 9999px;
          background: ${halo};
          transform: scale(1.25);
        "></span>
        <span style="
          position: absolute;
          inset: 0;
          display: flex;
          align-items: center;
          justify-content: center;
          border-radius: 9999px;
          background: ${color};
          border: 3px solid #ffffff;
          box-shadow: 0 12px 24px rgba(15, 23, 42, 0.22);
          color: #ffffff;
          font-size: 12px;
          font-weight: 700;
          line-height: 1;
        ">${label}</span>
      </div>
    `,
    iconSize: [34, 34],
    iconAnchor: [17, 17],
    popupAnchor: [0, -18],
  });

  markerIconCache.set(cacheKey, nextIcon);
  return nextIcon;
};

const MapInstanceBinder = ({
  mapRef,
  isVisible,
}: {
  mapRef: MutableRefObject<L.Map | null>;
  isVisible: boolean;
}) => {
  const map = useMap();

  useEffect(() => {
    mapRef.current = map;
    const container = map.getContainer();
    if (!isVisible) {
      return () => {
        if (mapRef.current === map) {
          mapRef.current = null;
        }
      };
    }

    let frameId: number | null = null;
    const timeoutIds: number[] = [];

    const invalidateSize = () => {
      if (frameId !== null) {
        window.cancelAnimationFrame(frameId);
      }

      frameId = window.requestAnimationFrame(() => {
        map.invalidateSize({ pan: false });
        frameId = null;
      });
    };

    const scheduleInvalidate = (delayMs: number) => {
      const timeoutId = window.setTimeout(invalidateSize, delayMs);
      timeoutIds.push(timeoutId);
    };

    map.whenReady(invalidateSize);
    invalidateSize();
    [50, 150, 350, 700, 1200].forEach(scheduleInvalidate);
    const resizeObserver =
      typeof ResizeObserver === "undefined"
        ? null
        : new ResizeObserver(() => {
            invalidateSize();
          });
    resizeObserver?.observe(container);
    if (container.parentElement) {
      resizeObserver?.observe(container.parentElement);
    }
    window.addEventListener("resize", invalidateSize);

    return () => {
      timeoutIds.forEach((timeoutId) => window.clearTimeout(timeoutId));
      if (frameId !== null) {
        window.cancelAnimationFrame(frameId);
      }

      resizeObserver?.disconnect();
      window.removeEventListener("resize", invalidateSize);
      if (mapRef.current === map) {
        mapRef.current = null;
      }
    };
  }, [isVisible, map, mapRef]);

  return null;
};

const ResilientTileLayer = ({
  onStatusChange,
}: {
  onStatusChange: (message: string | null) => void;
}) => {
  const map = useMap();
  const [sourceIndex, setSourceIndex] = useState(0);
  const tileErrorCountRef = useRef(0);
  const source = TILE_SOURCES[sourceIndex] ?? TILE_SOURCES[0];

  useEffect(() => {
    tileErrorCountRef.current = 0;
    onStatusChange(null);
    console.debug("[admin-tour-map] tile-provider-active", {
      key: source.key,
      url: source.url,
    });
    map.invalidateSize({ pan: false });
  }, [map, onStatusChange, source.key, source.url]);

  return (
    <TileLayer
      key={source.key}
      attribution={source.attribution}
      errorTileUrl={TRANSPARENT_TILE_URL}
      eventHandlers={{
        load: () => {
          tileErrorCountRef.current = 0;
          onStatusChange(null);
        },
        tileerror: () => {
          tileErrorCountRef.current += 1;
          console.warn("[admin-tour-map] tile-load-error", {
            key: source.key,
            url: source.url,
            errors: tileErrorCountRef.current,
          });

          if (tileErrorCountRef.current < 4) {
            return;
          }

          if (sourceIndex < TILE_SOURCES.length - 1) {
            setSourceIndex((current) => Math.min(current + 1, TILE_SOURCES.length - 1));
            return;
          }

          onStatusChange(
            "Không tải được lớp bản đồ. Hệ thống vẫn giữ POI hợp lệ để bạn chọn tour.",
          );
        },
      }}
      keepBuffer={2}
      maxZoom={source.maxZoom}
      url={source.url}
      updateWhenIdle
    />
  );
};

const MapViewportController = ({
  pois,
  focusedPoiId,
}: {
  pois: TourMapPoi[];
  focusedPoiId?: string | null;
}) => {
  const map = useMap();

  useEffect(() => {
    const focusedPoi = focusedPoiId
      ? pois.find((poi) => poi.id === focusedPoiId) ?? null
      : null;

    if (focusedPoi) {
      map.setView([focusedPoi.lat, focusedPoi.lng], Math.max(map.getZoom(), FOCUS_ZOOM), {
        animate: false,
      });
      return;
    }

    if (!pois.length) {
      map.setView(DEFAULT_CENTER, DEFAULT_ZOOM, { animate: false });
      return;
    }

    if (pois.length === 1) {
      map.setView([pois[0].lat, pois[0].lng], FOCUS_ZOOM, { animate: false });
      return;
    }

    const bounds = L.latLngBounds(
      pois.map((poi) => [poi.lat, poi.lng] as [number, number]),
    );
    map.fitBounds(bounds.pad(0.12), {
      animate: false,
      maxZoom: DEFAULT_ZOOM + 1,
    });
  }, [focusedPoiId, map, pois]);

  return null;
};

const MapClickSelector = ({
  pois,
  onTogglePoi,
}: {
  pois: TourMapPoi[];
  onTogglePoi: (poiId: string) => void;
}) => {
  useMapEvents({
    click: (event) => {
      const nearestPoi = pois
        .map((poi) => ({
          poi,
          distance: haversineDistanceInMeters(event.latlng.lat, event.latlng.lng, poi.lat, poi.lng),
        }))
        .sort((left, right) => left.distance - right.distance)[0];

      if (nearestPoi && nearestPoi.distance <= SELECTION_RADIUS_METERS) {
        onTogglePoi(nearestPoi.poi.id);
      }
    },
  });

  return null;
};

export type TourMapPoi = {
  id: string;
  title: string;
  address: string;
  category: string;
  status: string;
  lat: number;
  lng: number;
};

type TourPoiMapProps = {
  pois: TourMapPoi[];
  selectedPoiIds: string[];
  focusedPoiId?: string | null;
  onTogglePoi: (poiId: string) => void;
  isVisible?: boolean;
};

export const TourPoiMap = ({
  pois,
  selectedPoiIds,
  focusedPoiId = null,
  onTogglePoi,
  isVisible = true,
}: TourPoiMapProps) => {
  const mapRef = useRef<L.Map | null>(null);
  const [tileStatusMessage, setTileStatusMessage] = useState<string | null>(null);
  const validPois = useMemo(() => {
    const validItems = pois.filter((poi) => isValidLatitude(poi.lat) && isValidLongitude(poi.lng));
    if (validItems.length !== pois.length) {
      console.warn("[admin-tour-map] rejected-invalid-poi-coordinates", {
        sourceCount: pois.length,
        validCount: validItems.length,
        rejected: pois
          .filter((poi) => !isValidLatitude(poi.lat) || !isValidLongitude(poi.lng))
          .map((poi) => ({
            id: poi.id,
            title: poi.title,
            lat: poi.lat,
            lng: poi.lng,
          })),
      });
    }

    console.debug("[admin-tour-map] poi-payload-ready", {
      sourceCount: pois.length,
      validCount: validItems.length,
      selectedCount: selectedPoiIds.length,
    });

    return validItems;
  }, [pois, selectedPoiIds.length]);
  const stopOrderByPoiId = useMemo(
    () =>
      new Map(
        selectedPoiIds.map((poiId, index) => [poiId, index + 1] as const),
      ),
    [selectedPoiIds],
  );

  return (
    <div className="space-y-3">
      <div className="rounded-2xl bg-sand-50 px-4 py-3 text-sm text-ink-600">
        {validPois.length
          ? "Chạm vào marker hoặc khu vực gần POI để thêm hoặc bỏ chọn trực tiếp trên bản đồ."
          : "Không có POI phù hợp với bộ lọc hiện tại để hiển thị trên bản đồ."}
      </div>

      {tileStatusMessage ? (
        <div className="rounded-2xl bg-amber-50 px-4 py-3 text-sm font-medium text-amber-800">
          {tileStatusMessage}
        </div>
      ) : null}

      <div className="vinh-khanh-poi-map overflow-hidden rounded-3xl border border-sand-200 bg-sand-50">
        <MapContainer
          center={DEFAULT_CENTER}
          zoom={DEFAULT_ZOOM}
          scrollWheelZoom
          className="h-[440px] w-full"
        >
          <MapInstanceBinder mapRef={mapRef} isVisible={isVisible} />
          <ResilientTileLayer onStatusChange={setTileStatusMessage} />
          <MapClickSelector pois={validPois} onTogglePoi={onTogglePoi} />
          <MapViewportController pois={validPois} focusedPoiId={focusedPoiId} />

          {validPois.map((poi) => {
            const stopOrder = stopOrderByPoiId.get(poi.id);

            return (
              <Marker
                key={poi.id}
                position={[poi.lat, poi.lng]}
                icon={createPoiMarkerIcon(stopOrder)}
                eventHandlers={{
                  click: () => {
                    onTogglePoi(poi.id);
                  },
                }}
              >
                <Popup closeButton={false}>
                  <div className="min-w-[220px]">
                    <p className="font-semibold text-ink-900">{poi.title}</p>
                    <p className="mt-1 text-xs font-medium uppercase tracking-[0.18em] text-ink-500">
                      {poi.category}
                    </p>
                    <p className="mt-2 text-sm text-ink-600">{poi.address}</p>
                    <p className="mt-3 text-xs font-medium text-ink-500">
                      Trạng thái: {poi.status}
                    </p>
                    <p className="mt-2 text-sm font-semibold text-primary-700">
                      {stopOrder ? `Đã chọn ở vị trí #${stopOrder}` : "Chưa nằm trong tour"}
                    </p>
                  </div>
                </Popup>
              </Marker>
            );
          })}
        </MapContainer>
      </div>
    </div>
  );
};
