import { useEffect, useMemo, useRef, useState } from "react";
import L from "leaflet";

const DEFAULT_CENTER = {
  lat: 10.7578,
  lng: 106.7033,
};

const SELECTION_RADIUS_METERS = 180;

const isValidCoordinate = (value: number) => Number.isFinite(value);

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

const createPoiMarkerIcon = (selected: boolean, featured: boolean) => {
  const color = selected ? "#0f766e" : featured ? "#f97316" : "#1f2937";
  const halo = selected ? "rgba(15, 118, 110, 0.18)" : "rgba(31, 41, 55, 0.12)";

  return L.divIcon({
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
};

const escapeHtml = (value: string) =>
  value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");

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

type NominatimReverseResult = {
  display_name?: string;
  name?: string;
  address?: Partial<Record<string, string>>;
};

const formatReverseAddress = (result: NominatimReverseResult) => {
  const address = result.address ?? {};
  const roadLine = [address.house_number, address.road].filter(Boolean).join(" ");
  const parts = [
    result.name,
    roadLine,
    address.neighbourhood,
    address.suburb,
    address.quarter,
    address.city_district,
    address.borough,
    address.county,
    address.city,
    address.state,
    address.country,
  ]
    .map((part) => normalizeAddress(part ?? ""))
    .filter(Boolean);

  const dedupedParts = parts.filter(
    (part, index) =>
      parts.findIndex((candidate) => candidate.toLowerCase() === part.toLowerCase()) === index,
  );

  return normalizeAddress(dedupedParts.join(", ") || result.display_name || "");
};

export type PoiMapItem = {
  id: string;
  title: string;
  address: string;
  category: string;
  status: string;
  featured: boolean;
  lat: number;
  lng: number;
};

type OpenStreetMapPickerProps = {
  address?: string;
  lat: number;
  lng: number;
  onChange?: (lat: number, lng: number) => void;
  onAddressResolved?: (address: string) => void;
  editable?: boolean;
  pois?: PoiMapItem[];
  selectedPoiId?: string | null;
  onPoiSelect?: (poiId: string) => void;
};

export const OpenStreetMapPicker = ({
  address = "",
  lat,
  lng,
  onChange,
  onAddressResolved,
  editable = true,
  pois = [],
  selectedPoiId = null,
  onPoiSelect,
}: OpenStreetMapPickerProps) => {
  const mapElementRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markerRef = useRef<L.Marker | null>(null);
  const markerHaloRef = useRef<L.CircleMarker | null>(null);
  const poiLayerRef = useRef<L.LayerGroup | null>(null);
  const onChangeRef = useRef(onChange);
  const onAddressResolvedRef = useRef(onAddressResolved);
  const onPoiSelectRef = useRef(onPoiSelect);
  const lastGeocodedAddressRef = useRef("");
  const skipNextAddressLookupRef = useRef(false);
  const reverseGeocodeAbortRef = useRef<AbortController | null>(null);
  const forwardGeocodeAbortRef = useRef<AbortController | null>(null);
  const forwardGeocodeTimerRef = useRef<number | null>(null);
  const reverseLookupTokenRef = useRef(0);
  const forwardLookupTokenRef = useRef(0);
  const [geocodeState, setGeocodeState] = useState<
    "idle" | "searching" | "resolved" | "not-found" | "error"
  >("idle");
  const [geocodeMessage, setGeocodeMessage] = useState(
    "Nhập địa chỉ chi tiết để bản đồ tự định vị.",
  );

  const selectedPosition = useMemo(
    () =>
      isValidCoordinate(lat) && isValidCoordinate(lng)
        ? { lat, lng }
        : DEFAULT_CENTER,
    [lat, lng],
  );

  const selectablePois = useMemo(
    () => pois.filter((item) => isValidCoordinate(item.lat) && isValidCoordinate(item.lng)),
    [pois],
  );

  useEffect(() => {
    onChangeRef.current = onChange;
  }, [onChange]);

  useEffect(() => {
    onAddressResolvedRef.current = onAddressResolved;
  }, [onAddressResolved]);

  useEffect(() => {
    onPoiSelectRef.current = onPoiSelect;
  }, [onPoiSelect]);

  const cancelPendingForwardGeocode = () => {
    forwardGeocodeAbortRef.current?.abort();
    forwardGeocodeAbortRef.current = null;

    if (forwardGeocodeTimerRef.current !== null) {
      window.clearTimeout(forwardGeocodeTimerRef.current);
      forwardGeocodeTimerRef.current = null;
    }
  };

  const reverseGeocodePosition = async (nextLat: number, nextLng: number) => {
    if (!editable) {
      return;
    }

    cancelPendingForwardGeocode();
    reverseGeocodeAbortRef.current?.abort();

    const controller = new AbortController();
    reverseGeocodeAbortRef.current = controller;
    const lookupToken = ++reverseLookupTokenRef.current;

    try {
      setGeocodeState("searching");
      setGeocodeMessage("Đang cập nhật địa chỉ theo vị trí đã chọn...");

      const response = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=18&addressdetails=1&namedetails=1&accept-language=vi&lat=${nextLat}&lon=${nextLng}`,
        {
          signal: controller.signal,
          headers: {
            Accept: "application/json",
          },
        },
      );

      if (!response.ok) {
        throw new Error(`Nominatim reverse request failed with status ${response.status}`);
      }

      const result = (await response.json()) as NominatimReverseResult;
      if (lookupToken !== reverseLookupTokenRef.current) {
        return;
      }

      const resolvedAddress = formatReverseAddress(result);
      if (!resolvedAddress) {
        setGeocodeState("not-found");
        setGeocodeMessage(
          "Không lấy được địa chỉ từ vị trí này. Bạn có thể nhập thủ công.",
        );
        return;
      }

      skipNextAddressLookupRef.current = true;
      lastGeocodedAddressRef.current = resolvedAddress;
      onAddressResolvedRef.current?.(resolvedAddress);
      setGeocodeState("resolved");
      setGeocodeMessage("Đã cập nhật địa chỉ theo điểm bạn vừa chọn.");
    } catch {
      if (controller.signal.aborted) {
        return;
      }

      setGeocodeState("error");
      setGeocodeMessage(
        "Không thể lấy địa chỉ từ vị trí đã chọn lúc này. Bạn có thể nhập thủ công.",
      );
    }
  };

  useEffect(() => {
    if (!editable) {
      return;
    }

    const normalizedAddress = normalizeAddress(address);

    if (skipNextAddressLookupRef.current) {
      skipNextAddressLookupRef.current = false;
      return;
    }

    if (normalizedAddress.length < 6) {
      lastGeocodedAddressRef.current = "";
      setGeocodeState("idle");
      setGeocodeMessage("Nhập địa chỉ chi tiết hơn để bản đồ tự định vị.");
      return;
    }

    if (normalizedAddress === lastGeocodedAddressRef.current) {
      return;
    }

    const controller = new AbortController();
    forwardGeocodeAbortRef.current = controller;
    const lookupToken = ++forwardLookupTokenRef.current;
    const timer = window.setTimeout(async () => {
      try {
        setGeocodeState("searching");
        setGeocodeMessage("Đang định vị theo địa chỉ...");

        const hasHoChiMinhHint = /ho chi minh|hcm|hồ chí minh|tp\.?\s*hcm/i.test(
          normalizedAddress,
        );
        const query = hasHoChiMinhHint
          ? normalizedAddress
          : `${normalizedAddress}, Quan 4, Ho Chi Minh City, Viet Nam`;

        const response = await fetch(
          `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=vn&accept-language=vi&q=${encodeURIComponent(
            query,
          )}`,
          {
            signal: controller.signal,
            headers: {
              Accept: "application/json",
            },
          },
        );

        if (!response.ok) {
          throw new Error(`Nominatim request failed with status ${response.status}`);
        }

        const results = (await response.json()) as Array<{ lat: string; lon: string }>;
        if (lookupToken !== forwardLookupTokenRef.current) {
          return;
        }

        if (!results.length) {
          setGeocodeState("not-found");
          setGeocodeMessage("Không tìm thấy vị trí phù hợp. Bạn có thể chọn trực tiếp trên bản đồ.");
          return;
        }

        const nextLat = Number(results[0].lat);
        const nextLng = Number(results[0].lon);
        if (!Number.isFinite(nextLat) || !Number.isFinite(nextLng)) {
          throw new Error("Nominatim returned an invalid coordinate.");
        }

        lastGeocodedAddressRef.current = normalizedAddress;
        setGeocodeState("resolved");
        setGeocodeMessage("Đã định vị theo địa chỉ. Bạn vẫn có thể kéo marker để chỉnh.");
        onChangeRef.current?.(nextLat, nextLng);
      } catch {
        if (controller.signal.aborted) {
          return;
        }

        setGeocodeState("error");
        setGeocodeMessage("Không thể định vị từ địa chỉ lúc này. Bạn có thể chọn thủ công trên bản đồ.");
      }
    }, 700);

    forwardGeocodeTimerRef.current = timer;

    return () => {
      controller.abort();
      window.clearTimeout(timer);
      if (forwardGeocodeAbortRef.current === controller) {
        forwardGeocodeAbortRef.current = null;
      }
      if (forwardGeocodeTimerRef.current === timer) {
        forwardGeocodeTimerRef.current = null;
      }
    };
  }, [address, editable]);

  useEffect(() => {
    if (!mapElementRef.current || mapRef.current) {
      return;
    }

    const map = L.map(mapElementRef.current, {
      center: selectedPosition,
      zoom: editable ? 16 : 15,
      zoomControl: true,
      attributionControl: true,
    });

    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    if (editable) {
      const marker = L.marker(selectedPosition, {
        draggable: true,
        icon: createEditableMarkerIcon(),
      }).addTo(map);

      const markerHalo = L.circleMarker(selectedPosition, {
        radius: 16,
        color: "#f97316",
        weight: 2,
        fillColor: "#fb923c",
        fillOpacity: 0.18,
        interactive: false,
      }).addTo(map);

      marker.bindPopup("Vị trí POI đang chọn", {
        autoClose: false,
        closeButton: false,
        className: "vinh-khanh-map-popup",
        offset: [0, -18],
      });
      marker.openPopup();

      map.on("click", (event: L.LeafletMouseEvent) => {
        const nextPosition = event.latlng;
        marker.setLatLng(nextPosition);
        markerHalo.setLatLng(nextPosition);
        map.panTo(nextPosition);
        marker.openPopup();
        onChangeRef.current?.(nextPosition.lat, nextPosition.lng);
        void reverseGeocodePosition(nextPosition.lat, nextPosition.lng);
      });

      marker.on("dragend", () => {
        const nextPosition = marker.getLatLng();
        markerHalo.setLatLng(nextPosition);
        marker.openPopup();
        onChangeRef.current?.(nextPosition.lat, nextPosition.lng);
        void reverseGeocodePosition(nextPosition.lat, nextPosition.lng);
      });

      markerRef.current = marker;
      markerHaloRef.current = markerHalo;
    } else {
      const poiLayer = L.layerGroup().addTo(map);
      poiLayerRef.current = poiLayer;

      map.on("click", (event: L.LeafletMouseEvent) => {
        const nearestPoi = selectablePois
          .map((poi) => ({
            poi,
            distance: haversineDistanceInMeters(event.latlng.lat, event.latlng.lng, poi.lat, poi.lng),
          }))
          .sort((left, right) => left.distance - right.distance)[0];

        if (nearestPoi && nearestPoi.distance <= SELECTION_RADIUS_METERS) {
          onPoiSelectRef.current?.(nearestPoi.poi.id);
        }
      });
    }

    mapRef.current = map;

    requestAnimationFrame(() => {
      map.invalidateSize();
    });

    return () => {
      cancelPendingForwardGeocode();
      reverseGeocodeAbortRef.current?.abort();
      markerHaloRef.current?.remove();
      markerRef.current?.remove();
      poiLayerRef.current?.remove();
      map.remove();
      markerHaloRef.current = null;
      markerRef.current = null;
      poiLayerRef.current = null;
      mapRef.current = null;
    };
  }, [editable, selectablePois, selectedPosition]);

  useEffect(() => {
    if (!editable || !mapRef.current || !markerRef.current || !markerHaloRef.current) {
      return;
    }

    markerRef.current.setLatLng(selectedPosition);
    markerHaloRef.current.setLatLng(selectedPosition);
    mapRef.current.setView(selectedPosition, mapRef.current.getZoom(), {
      animate: false,
    });
    markerRef.current.openPopup();

    requestAnimationFrame(() => {
      mapRef.current?.invalidateSize();
    });
  }, [editable, selectedPosition]);

  useEffect(() => {
    if (editable || !mapRef.current || !poiLayerRef.current) {
      return;
    }

    poiLayerRef.current.clearLayers();

    selectablePois.forEach((poi) => {
      const marker = L.marker([poi.lat, poi.lng], {
        icon: createPoiMarkerIcon(poi.id === selectedPoiId, poi.featured),
      }).addTo(poiLayerRef.current!);

      marker.bindPopup(
        `
          <div style="min-width: 180px;">
            <div style="font-weight: 700; color: #111827;">${escapeHtml(poi.title)}</div>
            <div style="margin-top: 6px; font-size: 12px; color: #475569;">${escapeHtml(
              poi.category,
            )}</div>
            <div style="margin-top: 6px; font-size: 12px; color: #64748b;">${escapeHtml(
              poi.address,
            )}</div>
          </div>
        `,
        {
          closeButton: false,
          offset: [0, -10],
        },
      );

      marker.on("click", () => {
        onPoiSelectRef.current?.(poi.id);
      });

      if (poi.id === selectedPoiId) {
        marker.openPopup();
      }
    });

    if (selectedPoiId) {
      const selectedPoi = selectablePois.find((item) => item.id === selectedPoiId);
      if (selectedPoi) {
        mapRef.current.setView([selectedPoi.lat, selectedPoi.lng], Math.max(mapRef.current.getZoom(), 16), {
          animate: false,
        });
        return;
      }
    }

    if (selectablePois.length === 1) {
      mapRef.current.setView([selectablePois[0].lat, selectablePois[0].lng], 16, {
        animate: false,
      });
      return;
    }

    if (selectablePois.length > 1) {
      const bounds = L.latLngBounds(selectablePois.map((poi) => [poi.lat, poi.lng] as [number, number]));
      mapRef.current.fitBounds(bounds.pad(0.12), {
        maxZoom: 16,
        animate: false,
      });
    }
  }, [editable, selectablePois, selectedPoiId]);

  const browseMessage = selectedPoiId
    ? "Đã chọn POI từ bản đồ. Chạm điểm khác để xem nhanh thông tin POI gần nhất."
    : selectablePois.length
      ? "Chạm vào marker hoặc bất kỳ điểm nào gần POI trên bản đồ để xem toàn bộ thông tin."
      : "Chưa có POI nào để hiển thị trên bản đồ.";

  const message = editable ? geocodeMessage : browseMessage;
  const messageTone = editable ? geocodeState : selectedPoiId ? "resolved" : "idle";

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-ink-900">
            {editable ? "Chọn vị trí POI trên OpenStreetMap" : "Bản đồ POI"}
          </p>
          <p className="mt-1 text-xs text-ink-500">
            {editable
              ? "Nhập địa chỉ để tự định vị hoặc click, kéo marker để chỉnh vị trí chính xác."
              : "Hệ thống sẽ tự bật toàn bộ thông tin của POI được chọn từ marker hoặc từ điểm gần nhất bạn chạm."}
          </p>
        </div>
        <div className="rounded-2xl bg-sand-50 px-4 py-2 text-xs font-medium text-ink-600">
          {selectedPosition.lat.toFixed(6)}, {selectedPosition.lng.toFixed(6)}
        </div>
      </div>

      <div
        className={`rounded-2xl px-4 py-3 text-sm ${
          messageTone === "error"
            ? "bg-rose-50 text-rose-700"
            : messageTone === "resolved"
              ? "bg-emerald-50 text-emerald-700"
              : "bg-sand-50 text-ink-600"
        }`}
      >
        {message}
      </div>

      <div className="overflow-hidden rounded-3xl border border-sand-200 bg-sand-50">
        <div ref={mapElementRef} className="h-[360px] w-full" />
      </div>
    </div>
  );
};
