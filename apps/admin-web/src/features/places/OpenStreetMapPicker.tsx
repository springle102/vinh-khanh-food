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
      <div style="
        width: 18px;
        height: 18px;
        border-radius: 9999px;
        background: #f97316;
        border: 3px solid #ffffff;
        box-shadow: 0 6px 18px rgba(124, 45, 18, 0.35);
      "></div>
    `,
    iconSize: [18, 18],
    iconAnchor: [9, 9],
  });

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
  const onChangeRef = useRef(onChange);
  const onAddressResolvedRef = useRef(onAddressResolved);
  const lastGeocodedAddressRef = useRef("");
  const skipNextAddressLookupRef = useRef(false);
  const reverseGeocodeAbortRef = useRef<AbortController | null>(null);
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

  const reverseGeocodePosition = async (nextLat: number, nextLng: number) => {
    reverseGeocodeAbortRef.current?.abort();

    const controller = new AbortController();
    reverseGeocodeAbortRef.current = controller;

    try {
      setGeocodeState("searching");
      setGeocodeMessage("Đang cập nhật địa chỉ theo vị trí đã chọn...");

      const response = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=jsonv2&accept-language=vi&lat=${nextLat}&lon=${nextLng}`,
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

      const result = (await response.json()) as {
        display_name?: string;
      };
      const resolvedAddress = normalizeAddress(result.display_name ?? "");

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
        "Đã cập nhật địa chỉ theo vị trí trên bản đồ. Bạn vẫn có thể sửa lại nếu cần.",
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

    return () => {
      controller.abort();
      window.clearTimeout(timer);
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

    map.on("click", (event: L.LeafletMouseEvent) => {
      const nextPosition = event.latlng;
      marker.setLatLng(nextPosition);
      map.panTo(nextPosition);
      onChangeRef.current(nextPosition.lat, nextPosition.lng);
      void reverseGeocodePosition(nextPosition.lat, nextPosition.lng);
    });

    marker.on("dragend", () => {
      const nextPosition = marker.getLatLng();
      onChangeRef.current(nextPosition.lat, nextPosition.lng);
      void reverseGeocodePosition(nextPosition.lat, nextPosition.lng);
    });

    mapRef.current = map;
    markerRef.current = marker;

    requestAnimationFrame(() => {
      map.invalidateSize();
    });

    return () => {
      reverseGeocodeAbortRef.current?.abort();
      marker.remove();
      map.remove();
      markerRef.current = null;
      mapRef.current = null;
    };
  }, [selectedPosition]);

  useEffect(() => {
    if (!mapRef.current || !markerRef.current) {
      return;
    }

    markerRef.current.setLatLng(selectedPosition);
    mapRef.current.setView(selectedPosition, mapRef.current.getZoom(), {
      animate: false,
    });

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
