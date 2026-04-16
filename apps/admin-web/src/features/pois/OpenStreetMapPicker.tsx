import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type MutableRefObject } from "react";
import L from "leaflet";
import {
  Circle,
  MapContainer,
  Marker,
  Popup,
  TileLayer,
  useMap,
  useMapEvents,
} from "react-leaflet";
import { useGeocoding, type GeocodingResult } from "./useGeocoding";

const DEFAULT_CENTER = {
  lat: 10.7578,
  lng: 106.7033,
};

const DEFAULT_EDITABLE_ZOOM = 16;
const DEFAULT_BROWSE_ZOOM = 15;
const SELECTION_RADIUS_METERS = 180;
const DEFAULT_TRIGGER_RADIUS_METERS = 20;
const EARTH_RADIUS_METERS = 6_371_000;
const BASE_TILE_SOURCE: {
  url: string;
  maxZoom: number;
  attribution: string;
} = {
  url: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
  maxZoom: 19,
  attribution:
    '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
};

const isValidLatitude = (value: number) => Number.isFinite(value) && value >= -90 && value <= 90;
const isValidLongitude = (value: number) => Number.isFinite(value) && value >= -180 && value <= 180;

const normalizeAddress = (value: string) => value.trim().replace(/\s+/g, " ");

const createEditableMarkerIcon = () =>
  L.divIcon({
    className: "vinh-khanh-map-marker",
    html: `
      <div style="position: relative; width: 28px; height: 38px;">
        <div style="
          position: absolute;
          left: 50%;
          top: 18px;
          width: 16px;
          height: 16px;
          border-radius: 9999px;
          background: rgba(249, 115, 22, 0.22);
          transform: translateX(-50%);
          box-shadow: 0 0 0 8px rgba(249, 115, 22, 0.10);
        "></div>
        <div style="
          position: absolute;
          left: 50%;
          top: 0;
          width: 24px;
          height: 24px;
          border-radius: 9999px 9999px 9999px 0;
          background: #f97316;
          border: 3px solid #ffffff;
          box-shadow: 0 10px 24px rgba(124, 45, 18, 0.30);
          transform: translateX(-50%) rotate(-45deg);
        "></div>
        <div style="
          position: absolute;
          left: 50%;
          top: 7px;
          width: 8px;
          height: 8px;
          border-radius: 9999px;
          background: #ffffff;
          transform: translateX(-50%);
        "></div>
      </div>
    `,
    iconSize: [28, 38],
    iconAnchor: [14, 34],
  });

const poiMarkerIconCache = new Map<string, L.DivIcon>();

const createPoiMarkerIcon = (selected: boolean) => {
  const cacheKey = selected ? "selected" : "default";
  const cached = poiMarkerIconCache.get(cacheKey);
  if (cached) {
    return cached;
  }

  const color = selected ? "#0f766e" : "#1f2937";
  const halo = selected ? "rgba(15, 118, 110, 0.18)" : "rgba(31, 41, 55, 0.12)";

  const nextIcon = L.divIcon({
    className: "vinh-khanh-poi-marker",
    html: `
      <div style="position: relative; width: 22px; height: 22px;">
        <span style="
          position: absolute;
          inset: 0;
          border-radius: 9999px;
          background: ${halo};
          transform: scale(1.7);
        "></span>
        <span style="
          position: absolute;
          inset: 0;
          border-radius: 9999px;
          background: ${color};
          border: 3px solid #ffffff;
          box-shadow: 0 10px 18px rgba(15, 23, 42, 0.20);
        "></span>
      </div>
    `,
    iconSize: [22, 22],
    iconAnchor: [11, 11],
  });

  poiMarkerIconCache.set(cacheKey, nextIcon);
  return nextIcon;
};

const editableMarkerIcon = createEditableMarkerIcon();

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

type MapPosition = {
  lat: number;
  lng: number;
};

const resolvePoiTriggerRadiusMeters = (triggerRadius?: number) => {
  const normalizedValue = Number(triggerRadius);
  return Number.isFinite(normalizedValue) && normalizedValue >= DEFAULT_TRIGGER_RADIUS_METERS
    ? normalizedValue
    : DEFAULT_TRIGGER_RADIUS_METERS;
};

const toDegrees = (value: number) => (value * 180) / Math.PI;

const clampLatitude = (value: number) => Math.max(-90, Math.min(90, value));
const clampLongitude = (value: number) => Math.max(-180, Math.min(180, value));

const buildPoiRadiusBounds = (lat: number, lng: number, radiusMeters: number) => {
  const angularDistance = radiusMeters / EARTH_RADIUS_METERS;
  const latOffset = toDegrees(angularDistance);
  const safeCosLatitude = Math.max(Math.abs(Math.cos((lat * Math.PI) / 180)), 0.000001);
  const lngOffset = toDegrees(angularDistance / safeCosLatitude);

  return L.latLngBounds(
    [clampLatitude(lat - latOffset), clampLongitude(lng - lngOffset)],
    [clampLatitude(lat + latOffset), clampLongitude(lng + lngOffset)],
  );
};

const buildPoiBounds = (pois: PoiMapItem[]) => {
  let bounds: L.LatLngBounds | null = null;

  pois.forEach((poi) => {
    if (!isValidLatitude(poi.lat) || !isValidLongitude(poi.lng)) {
      return;
    }

    const nextBounds = buildPoiRadiusBounds(
      poi.lat,
      poi.lng,
      resolvePoiTriggerRadiusMeters(poi.triggerRadius),
    );

    bounds = bounds ? bounds.extend(nextBounds) : nextBounds;
  });

  return bounds;
};

export type PoiMapItem = {
  id: string;
  title: string;
  address: string;
  category: string;
  status: string;
  lat: number;
  lng: number;
  triggerRadius?: number;
};

type OpenStreetMapPickerProps = {
  address?: string;
  lat: number;
  lng: number;
  selectedTriggerRadius?: number;
  isVisible?: boolean;
  onChange?: (lat: number, lng: number) => void;
  onLocationResolved?: (location: GeocodingResult) => void;
  addressSearchVersion?: number;
  editable?: boolean;
  pois?: PoiMapItem[];
  selectedPoiId?: string | null;
  onPoiSelect?: (poiId: string) => void;
  onPoiHover?: (poiId: string) => void;
  onVisiblePoiIdsChange?: (poiIds: string[]) => void;
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

    if (!isVisible) {
      return () => {
        if (mapRef.current === map) {
          mapRef.current = null;
        }
      };
    }

    map.whenReady(() => {
      invalidateSize();
    });
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

const EditableMapClickHandler = ({
  onSelectPosition,
}: {
  onSelectPosition: (position: MapPosition) => void;
}) => {
  useMapEvents({
    click: (event) => {
      onSelectPosition({
        lat: event.latlng.lat,
        lng: event.latlng.lng,
      });
    },
  });

  return null;
};

const BrowseMapClickHandler = ({
  pois,
  onPoiSelect,
}: {
  pois: PoiMapItem[];
  onPoiSelect?: (poiId: string) => void;
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
        onPoiSelect?.(nearestPoi.poi.id);
      }
    },
  });

  return null;
};

const BrowseMapViewport = ({
  pois,
  selectedPoiId,
  onVisiblePoiIdsChange,
}: {
  pois: PoiMapItem[];
  selectedPoiId?: string | null;
  onVisiblePoiIdsChange?: (poiIds: string[]) => void;
}) => {
  const map = useMap();

  useEffect(() => {
    const selectedPoi = selectedPoiId
      ? pois.find((poi) => poi.id === selectedPoiId) ?? null
      : null;

    if (!pois.length) {
      map.setView(DEFAULT_CENTER, DEFAULT_BROWSE_ZOOM, { animate: false });
      return;
    }

    const poiBounds = buildPoiBounds(pois);

    if (selectedPoi) {
      if (poiBounds && pois.length === 1) {
        map.fitBounds(poiBounds.pad(0.5), {
          animate: false,
          maxZoom: 17,
        });
        return;
      }

      const selectedBounds = buildPoiBounds([selectedPoi]);
      if (selectedBounds) {
        map.fitBounds(selectedBounds.pad(0.8), {
          animate: false,
          maxZoom: 17,
        });
        return;
      }

      map.setView([selectedPoi.lat, selectedPoi.lng], Math.max(map.getZoom(), 16), {
        animate: false,
      });
      return;
    }

    if (pois.length === 1) {
      if (poiBounds) {
        map.fitBounds(poiBounds.pad(0.8), {
          animate: false,
          maxZoom: 17,
        });
        return;
      }

      map.setView([pois[0].lat, pois[0].lng], 16, { animate: false });
      return;
    }

    if (poiBounds) {
      map.fitBounds(poiBounds.pad(0.12), {
        animate: false,
        maxZoom: 16,
      });
    }
  }, [map, pois, selectedPoiId]);

  useEffect(() => {
    if (!onVisiblePoiIdsChange) {
      return;
    }

    const publishVisiblePois = () => {
      const bounds = map.getBounds();
      const visiblePoiIds = pois
        .filter((poi) => bounds.contains([poi.lat, poi.lng]))
        .map((poi) => poi.id);

      onVisiblePoiIdsChange(visiblePoiIds);
    };

    publishVisiblePois();
    map.on("moveend", publishVisiblePois);
    map.on("zoomend", publishVisiblePois);

    return () => {
      map.off("moveend", publishVisiblePois);
      map.off("zoomend", publishVisiblePois);
    };
  }, [map, onVisiblePoiIdsChange, pois]);

  return null;
};

export const OpenStreetMapPicker = ({
  address = "",
  lat,
  lng,
  selectedTriggerRadius = DEFAULT_TRIGGER_RADIUS_METERS,
  isVisible = true,
  onChange,
  onLocationResolved,
  addressSearchVersion = 0,
  editable = true,
  pois = [],
  selectedPoiId = null,
  onPoiSelect,
  onPoiHover,
  onVisiblePoiIdsChange,
}: OpenStreetMapPickerProps) => {
  const mapRef = useRef<L.Map | null>(null);
  const markerRef = useRef<L.Marker | null>(null);
  const onChangeRef = useRef(onChange);
  const onLocationResolvedRef = useRef(onLocationResolved);
  const onPoiSelectRef = useRef(onPoiSelect);
  const onPoiHoverRef = useRef(onPoiHover);
  const onVisiblePoiIdsChangeRef = useRef(onVisiblePoiIdsChange);
  const reverseAbortRef = useRef<AbortController | null>(null);
  const forwardAbortRef = useRef<AbortController | null>(null);
  const lastAddressSearchVersionRef = useRef(addressSearchVersion);
  const lastResolvedAddressRef = useRef("");
  const isDraggingMarkerRef = useRef(false);

  const selectedPosition = useMemo(
    () =>
      isValidLatitude(lat) && isValidLongitude(lng)
        ? { lat, lng }
        : DEFAULT_CENTER,
    [lat, lng],
  );
  const selectablePois = useMemo(
    () => pois.filter((item) => isValidLatitude(item.lat) && isValidLongitude(item.lng)),
    [pois],
  );
  const { reverseGeocode, forwardGeocode } = useGeocoding();

  const [position, setPosition] = useState<MapPosition>(selectedPosition);
  const [isLoading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [messageTone, setMessageTone] = useState<"idle" | "resolved" | "error">("idle");
  const [message, setMessage] = useState("Nhập địa chỉ chi tiết để bản đồ tự định vị.");

  useEffect(() => {
    onChangeRef.current = onChange;
  }, [onChange]);

  useEffect(() => {
    onLocationResolvedRef.current = onLocationResolved;
  }, [onLocationResolved]);

  useEffect(() => {
    onPoiSelectRef.current = onPoiSelect;
  }, [onPoiSelect]);

  useEffect(() => {
    onPoiHoverRef.current = onPoiHover;
  }, [onPoiHover]);

  useEffect(() => {
    onVisiblePoiIdsChangeRef.current = onVisiblePoiIdsChange;
  }, [onVisiblePoiIdsChange]);

  useEffect(() => {
    if (editable && !isDraggingMarkerRef.current) {
      setPosition(selectedPosition);
    }
  }, [editable, selectedPosition]);

  useEffect(() => {
    return () => {
      reverseAbortRef.current?.abort();
      forwardAbortRef.current?.abort();
    };
  }, []);

  const applyResolvedLocation = useCallback(
    (location: GeocodingResult, options?: { flyTo?: boolean }) => {
      const nextPosition = {
        lat: location.lat,
        lng: location.lng,
      };

      console.debug("[admin-poi] map-location-resolved", {
        location,
        flyTo: options?.flyTo ?? false,
      });

      setPosition(nextPosition);
      onChangeRef.current?.(nextPosition.lat, nextPosition.lng);
      onLocationResolvedRef.current?.(location);

      if (markerRef.current) {
        markerRef.current.setLatLng(nextPosition);
        markerRef.current.openPopup();
      }

      if (options?.flyTo && mapRef.current) {
        mapRef.current.flyTo(nextPosition, Math.max(mapRef.current.getZoom(), 17), {
          animate: true,
          duration: 0.35,
        });
      }
    },
    [],
  );

  const handleReverseGeocode = useCallback(
    async (nextPosition: MapPosition) => {
      reverseAbortRef.current?.abort();

      const controller = new AbortController();
      reverseAbortRef.current = controller;

      console.debug("[admin-poi] map-reverse-geocode-request", nextPosition);

      setLoading(true);
      setError(null);
      setMessageTone("idle");
      setMessage("Đang cập nhật địa chỉ theo vị trí đã chọn...");

      try {
        const result = await reverseGeocode(nextPosition.lat, nextPosition.lng, controller.signal);

        if (!result.address) {
          setError("Không lấy được địa chỉ.");
          setMessageTone("error");
          setMessage("Không lấy được địa chỉ. Dữ liệu cũ vẫn được giữ nguyên.");
          return;
        }

        lastResolvedAddressRef.current = normalizeAddress(result.address);
        applyResolvedLocation(result);
        setMessageTone("resolved");
        setMessage("Đã cập nhật địa chỉ theo điểm bạn vừa chọn.");
      } catch (caughtError) {
        if (controller.signal.aborted) {
          return;
        }

        setError(caughtError instanceof Error ? caughtError.message : "Không lấy được địa chỉ.");
        setMessageTone("error");
        setMessage("Không lấy được địa chỉ. Dữ liệu cũ vẫn được giữ nguyên.");
      } finally {
        if (reverseAbortRef.current === controller) {
          reverseAbortRef.current = null;
          setLoading(false);
        }
      }
    },
    [applyResolvedLocation, reverseGeocode],
  );

  const handleDrag = useCallback((nextPosition: MapPosition) => {
    setPosition(nextPosition);
    onChangeRef.current?.(nextPosition.lat, nextPosition.lng);
  }, []);

  const handleDragEnd = useCallback(
    async (nextPosition: MapPosition) => {
      handleDrag(nextPosition);
      await handleReverseGeocode(nextPosition);
    },
    [handleDrag, handleReverseGeocode],
  );

  const handleMapSelect = useCallback(
    (nextPosition: MapPosition) => {
      setPosition(nextPosition);
      onChangeRef.current?.(nextPosition.lat, nextPosition.lng);
      void handleReverseGeocode(nextPosition);
    },
    [handleReverseGeocode],
  );

  const handleVisiblePoiIdsChange = useCallback((poiIds: string[]) => {
    onVisiblePoiIdsChangeRef.current?.(poiIds);
  }, []);

  const handleAddressSearch = useCallback(async () => {
    if (!editable) {
      return;
    }

    const normalizedAddress = normalizeAddress(address);
    if (normalizedAddress.length < 6) {
      setError(null);
      setMessageTone("idle");
      setMessage("Nhập địa chỉ chi tiết để bản đồ tự định vị.");
      return;
    }

    if (normalizedAddress === lastResolvedAddressRef.current) {
      return;
    }

    forwardAbortRef.current?.abort();

    const controller = new AbortController();
    forwardAbortRef.current = controller;

    console.debug("[admin-poi] map-forward-geocode-request", {
      address: normalizedAddress,
    });

    setLoading(true);
    setError(null);
    setMessageTone("idle");
    setMessage("Đang định vị theo địa chỉ...");

    try {
      const result = await forwardGeocode(normalizedAddress, controller.signal);

      lastResolvedAddressRef.current = normalizeAddress(result.address || normalizedAddress);
      applyResolvedLocation(
        {
          ...result,
          address: result.address || normalizedAddress,
        },
        { flyTo: true },
      );
      setMessageTone("resolved");
      setMessage("Đã định vị theo địa chỉ. Bạn vẫn có thể kéo marker để chỉnh.");
    } catch (caughtError) {
      if (controller.signal.aborted) {
        return;
      }

      const nextError =
        caughtError instanceof Error && caughtError.message === "ADDRESS_NOT_FOUND"
          ? "Không tìm thấy vị trí phù hợp."
          : "Không thể định vị từ địa chỉ lúc này.";

      setError(nextError);
      setMessageTone("error");
      setMessage(`${nextError} Dữ liệu cũ vẫn được giữ nguyên.`);
    } finally {
      if (forwardAbortRef.current === controller) {
        forwardAbortRef.current = null;
        setLoading(false);
      }
    }
  }, [address, applyResolvedLocation, editable, forwardGeocode]);

  useEffect(() => {
    if (!editable) {
      return;
    }

    if (addressSearchVersion === lastAddressSearchVersionRef.current) {
      return;
    }

    lastAddressSearchVersionRef.current = addressSearchVersion;
    void handleAddressSearch();
  }, [addressSearchVersion, editable, handleAddressSearch]);

  const browseMessage = selectedPoiId
    ? "Đã chọn POI từ bản đồ. Chạm điểm khác để xem nhanh thông tin POI gần nhất."
    : selectablePois.length
      ? "Chạm vào marker hoặc bất kỳ điểm nào gần POI trên bản đồ để xem toàn bộ thông tin."
      : "Chưa có POI nào để hiển thị trên bản đồ.";

  const statusMessage = editable ? message : browseMessage;
  const statusTone = editable ? (error ? "error" : messageTone) : selectedPoiId ? "resolved" : "idle";

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-ink-900">
            {editable ? "Chọn vị trí POI trên OpenStreetMap" : "Bản đồ POI"}
          </p>
          <p className="mt-1 text-xs text-ink-500">
            {editable
              ? "Nhập địa chỉ rồi nhấn Enter hoặc rời ô nhập. Bạn cũng có thể kéo marker trên bản đồ để cập nhật vị trí chính xác."
              : "Hệ thống sẽ tự bật toàn bộ thông tin của POI được chọn từ marker hoặc từ điểm gần nhất bạn chạm."}
          </p>
        </div>
        <div className="rounded-2xl bg-sand-50 px-4 py-2 text-xs font-medium text-ink-600">
          {position.lat.toFixed(6)}, {position.lng.toFixed(6)}
        </div>
      </div>

      <div
        className={`rounded-2xl px-4 py-3 text-sm ${
          statusTone === "error"
            ? "bg-rose-50 text-rose-700"
            : statusTone === "resolved"
              ? "bg-emerald-50 text-emerald-700"
              : "bg-sand-50 text-ink-600"
        }`}
      >
        {isLoading ? `${statusMessage}` : statusMessage}
      </div>

      <div className="vinh-khanh-poi-map overflow-hidden rounded-3xl border border-sand-200 bg-sand-50">
        <MapContainer
          center={editable ? position : selectedPosition}
          zoom={editable ? DEFAULT_EDITABLE_ZOOM : DEFAULT_BROWSE_ZOOM}
          scrollWheelZoom
          className="h-[360px] w-full"
        >
          <MapInstanceBinder mapRef={mapRef} isVisible={isVisible} />
          <TileLayer
            attribution={BASE_TILE_SOURCE.attribution}
            maxZoom={BASE_TILE_SOURCE.maxZoom}
            url={BASE_TILE_SOURCE.url}
          />

          {editable ? (
            <>
              <EditableMapClickHandler onSelectPosition={handleMapSelect} />
              <Circle
                center={position}
                radius={resolvePoiTriggerRadiusMeters(selectedTriggerRadius)}
                color="#f97316"
                weight={2}
                fillColor="#fb923c"
                fillOpacity={0.12}
                opacity={0.46}
                interactive={false}
              />
              <Marker
                ref={markerRef}
                position={position}
                draggable
                icon={editableMarkerIcon}
                eventHandlers={{
                  dragstart: () => {
                    isDraggingMarkerRef.current = true;
                  },
                  drag: (event) => {
                    const nextPosition = event.target.getLatLng();
                    handleDrag({
                      lat: nextPosition.lat,
                      lng: nextPosition.lng,
                    });
                  },
                  dragend: async (event) => {
                    isDraggingMarkerRef.current = false;
                    const nextPosition = event.target.getLatLng();
                    await handleDragEnd({
                      lat: nextPosition.lat,
                      lng: nextPosition.lng,
                    });
                  },
                }}
              >
                <Popup autoClose={false} closeButton={false} offset={[0, -18]}>
                  <div style={{ minWidth: 220 }}>
                    <div style={{ fontWeight: 700, color: "#111827" }}>Vị trí POI đang chọn</div>
                    <div style={{ marginTop: 6, fontSize: 12, color: "#475569" }}>
                      {address || "Đang chờ địa chỉ chuẩn từ geocoding..."}
                    </div>
                  </div>
                </Popup>
              </Marker>
            </>
          ) : (
            <>
              <BrowseMapClickHandler pois={selectablePois} onPoiSelect={onPoiSelectRef.current} />
              <BrowseMapViewport
                pois={selectablePois}
                selectedPoiId={selectedPoiId}
                onVisiblePoiIdsChange={handleVisiblePoiIdsChange}
              />
              {selectablePois.map((poi) => {
                const isSelected = poi.id === selectedPoiId;
                const circleRadius = resolvePoiTriggerRadiusMeters(poi.triggerRadius);

                return (
                  <Fragment key={poi.id}>
                    <Circle
                      center={[poi.lat, poi.lng]}
                      radius={circleRadius}
                      color={isSelected ? "#0f766e" : "#64748b"}
                      weight={isSelected ? 2 : 1.5}
                      opacity={isSelected ? 0.56 : 0.34}
                      fillColor={isSelected ? "#14b8a6" : "#cbd5e1"}
                      fillOpacity={isSelected ? 0.14 : 0.08}
                      interactive={false}
                    />
                    <Marker
                      position={[poi.lat, poi.lng]}
                      icon={createPoiMarkerIcon(isSelected)}
                      eventHandlers={{
                        click: () => {
                          onPoiSelectRef.current?.(poi.id);
                        },
                        mouseover: () => {
                          onPoiHoverRef.current?.(poi.id);
                        },
                      }}
                    >
                      <Popup closeButton={false} offset={[0, -10]}>
                          <div style={{ minWidth: 180 }}>
                          <div style={{ fontWeight: 700, color: "#111827" }}>{poi.title}</div>
                          <div style={{ marginTop: 6, fontSize: 12, color: "#475569" }}>
                            {poi.category}
                          </div>
                          <div style={{ marginTop: 6, fontSize: 12, color: "#64748b" }}>
                            {poi.address}
                          </div>
                        </div>
                      </Popup>
                    </Marker>
                  </Fragment>
                );
              })}
            </>
          )}
        </MapContainer>
      </div>
    </div>
  );
};
