import { useEffect, useMemo, useRef, useState } from "react";
import L from "leaflet";

const DEFAULT_CENTER = {
  lat: 10.7578,
  lng: 106.7033,
};

const isValidCoordinate = (value: number) => Number.isFinite(value);

const normalizeAddress = (value: string) => value.trim().replace(/\s+/g, " ");

const createMarkerIcon = () =>
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
    (part, index) => parts.findIndex((candidate) => candidate.toLowerCase() === part.toLowerCase()) === index,
  );

  return normalizeAddress(dedupedParts.join(", ") || result.display_name || "");
};

type OpenStreetMapPickerProps = {
  address: string;
  lat: number;
  lng: number;
  onChange: (lat: number, lng: number) => void;
  onAddressResolved: (address: string) => void;
};

export const OpenStreetMapPicker = ({
  address,
  lat,
  lng,
  onChange,
  onAddressResolved,
}: OpenStreetMapPickerProps) => {
  const mapElementRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markerRef = useRef<L.Marker | null>(null);
  const markerHaloRef = useRef<L.CircleMarker | null>(null);
  const onChangeRef = useRef(onChange);
  const onAddressResolvedRef = useRef(onAddressResolved);
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

  useEffect(() => {
    onChangeRef.current = onChange;
  }, [onChange]);

  useEffect(() => {
    onAddressResolvedRef.current = onAddressResolved;
  }, [onAddressResolved]);

  const cancelPendingForwardGeocode = () => {
    forwardGeocodeAbortRef.current?.abort();
    forwardGeocodeAbortRef.current = null;

    if (forwardGeocodeTimerRef.current !== null) {
      window.clearTimeout(forwardGeocodeTimerRef.current);
      forwardGeocodeTimerRef.current = null;
    }
  };

  const reverseGeocodePosition = async (nextLat: number, nextLng: number) => {
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
          "Không lấy được địa chỉ từ vị trí này. Bạn có thể nhập địa chỉ thủ công.",
        );
        return;
      }

      skipNextAddressLookupRef.current = true;
      lastGeocodedAddressRef.current = resolvedAddress;
      onAddressResolvedRef.current(resolvedAddress);
      setGeocodeState("resolved");
      setGeocodeMessage(
        "Đã cập nhật địa chỉ theo điểm bạn vừa chọn trên bản đồ.",
      );
    } catch (error) {
      if (controller.signal.aborted) {
        return;
      }

      setGeocodeState("error");
      setGeocodeMessage(
        "Không thể lấy địa chỉ từ vị trí đã chọn lúc này. Bạn có thể nhập địa chỉ thủ công.",
      );
    }
  };

  useEffect(() => {
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

        const results = (await response.json()) as Array<{
          lat: string;
          lon: string;
        }>;

        if (lookupToken !== forwardLookupTokenRef.current) {
          return;
        }

        if (!results.length) {
          setGeocodeState("not-found");
          setGeocodeMessage(
            "Không tìm thấy vị trí phù hợp. Bạn có thể click trực tiếp lên bản đồ.",
          );
          return;
        }

        const nextLat = Number(results[0].lat);
        const nextLng = Number(results[0].lon);

        if (!Number.isFinite(nextLat) || !Number.isFinite(nextLng)) {
          throw new Error("Nominatim returned an invalid coordinate.");
        }

        lastGeocodedAddressRef.current = normalizedAddress;
        setGeocodeState("resolved");
        setGeocodeMessage(
          "Đã định vị theo địa chỉ. Bạn vẫn có thể kéo marker để tinh chỉnh.",
        );
        onChangeRef.current(nextLat, nextLng);
      } catch (error) {
        if (controller.signal.aborted) {
          return;
        }

        setGeocodeState("error");
        setGeocodeMessage(
          "Không thể định vị từ địa chỉ lúc này. Bạn có thể chọn thủ công trên bản đồ.",
        );
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
  }, [address]);

  useEffect(() => {
    if (!mapElementRef.current || mapRef.current) {
      return;
    }

    const map = L.map(mapElementRef.current, {
      center: selectedPosition,
      zoom: 16,
      zoomControl: true,
      attributionControl: true,
    });

    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    const marker = L.marker(selectedPosition, {
      draggable: true,
      icon: createMarkerIcon(),
    }).addTo(map);
    const markerHalo = L.circleMarker(selectedPosition, {
      radius: 16,
      color: "#f97316",
      weight: 2,
      fillColor: "#fb923c",
      fillOpacity: 0.18,
      interactive: false,
    }).addTo(map);

    marker.bindPopup("Vị trí đã chọn", {
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
      onChangeRef.current(nextPosition.lat, nextPosition.lng);
      void reverseGeocodePosition(nextPosition.lat, nextPosition.lng);
    });

    marker.on("dragend", () => {
      const nextPosition = marker.getLatLng();
      markerHalo.setLatLng(nextPosition);
      marker.openPopup();
      onChangeRef.current(nextPosition.lat, nextPosition.lng);
      void reverseGeocodePosition(nextPosition.lat, nextPosition.lng);
    });

    mapRef.current = map;
    markerRef.current = marker;
    markerHaloRef.current = markerHalo;

    requestAnimationFrame(() => {
      map.invalidateSize();
    });

    return () => {
      cancelPendingForwardGeocode();
      reverseGeocodeAbortRef.current?.abort();
      markerHalo.remove();
      marker.remove();
      map.remove();
      markerHaloRef.current = null;
      markerRef.current = null;
      mapRef.current = null;
    };
  }, [selectedPosition]);

  useEffect(() => {
    if (!mapRef.current || !markerRef.current || !markerHaloRef.current) {
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
  }, [selectedPosition]);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-ink-900">
            Chọn vị trí trên OpenStreetMap
          </p>
          <p className="mt-1 text-xs text-ink-500">
            Nhập địa chỉ để tự động định vị hoặc click lên bản đồ, kéo marker để chỉnh sửa.
          </p>
        </div>
        <div className="rounded-2xl bg-sand-50 px-4 py-2 text-xs font-medium text-ink-600">
          {selectedPosition.lat.toFixed(6)}, {selectedPosition.lng.toFixed(6)}
        </div>
      </div>

      <div
        className={`rounded-2xl px-4 py-3 text-sm ${geocodeState === "error"
          ? "bg-rose-50 text-rose-700"
          : geocodeState === "resolved"
            ? "bg-emerald-50 text-emerald-700"
            : "bg-sand-50 text-ink-600"
          }`}
      >
        {geocodeMessage}
      </div>

      <div className="overflow-hidden rounded-3xl border border-sand-200 bg-sand-50">
        <div ref={mapElementRef} className="h-[360px] w-full" />
      </div>
    </div>
  );
};
