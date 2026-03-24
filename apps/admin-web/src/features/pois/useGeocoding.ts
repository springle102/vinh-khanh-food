import { useCallback, useRef } from "react";
import { adminApi, ApiError } from "../../lib/api";
import type { GeocodingLocation } from "../../data/types";

export type GeocodingResult = GeocodingLocation;

const REVERSE_TIMEOUT_MS = 5_000;
const FORWARD_TIMEOUT_MS = 5_000;

const normalizeText = (value: string) => value.trim().replace(/\s+/g, " ");

const createCacheKeyFromCoordinate = (lat: number, lng: number) =>
  `${lat.toFixed(6)},${lng.toFixed(6)}`;

const normalizeOptionalText = (value: unknown) =>
  typeof value === "string" ? normalizeText(value) : "";

const shouldFallbackToDirectNominatim = (error: unknown) => {
  if (error instanceof Error && error.name === "AbortError") {
    return false;
  }

  if (error instanceof Error && error.message === "REQUEST_TIMEOUT") {
    return false;
  }

  if (error instanceof ApiError) {
    if (error.message === "ADDRESS_NOT_FOUND") {
      return false;
    }

    return error.status === 404 || error.status >= 500;
  }

  return true;
};

const mapNominatimPayloadToLocation = (payload: {
  display_name?: string;
  lat?: string | number;
  lon?: string | number;
  address?: Record<string, unknown>;
}) => {
  const addressDetails = payload.address ?? {};

  return {
    address: normalizeOptionalText(payload.display_name),
    district: normalizeOptionalText(
      addressDetails.city_district ??
        addressDetails.state_district ??
        addressDetails.county ??
        addressDetails.city,
    ),
    ward: normalizeOptionalText(
      addressDetails.suburb ??
        addressDetails.neighbourhood ??
        addressDetails.quarter ??
        addressDetails.city_block ??
        addressDetails.hamlet,
    ),
    lat: Number(payload.lat ?? 0),
    lng: Number(payload.lon ?? 0),
  } satisfies GeocodingResult;
};

const fetchDirectReverseGeocode = async (lat: number, lng: number, signal?: AbortSignal) => {
  const response = await fetch(
    `https://nominatim.openstreetmap.org/reverse?format=json&addressdetails=1&accept-language=vi&lat=${encodeURIComponent(lat)}&lon=${encodeURIComponent(lng)}`,
    {
      signal,
      headers: {
        Accept: "application/json",
      },
    },
  );

  if (!response.ok) {
    throw new Error(`REVERSE_GEOCODE_HTTP_${response.status}`);
  }

  const payload = (await response.json()) as {
    display_name?: string;
    lat?: string | number;
    lon?: string | number;
    address?: Record<string, unknown>;
  };

  return mapNominatimPayloadToLocation(payload);
};

const fetchDirectForwardGeocode = async (query: string, signal?: AbortSignal) => {
  const response = await fetch(
    `https://nominatim.openstreetmap.org/search?format=json&addressdetails=1&accept-language=vi&limit=1&q=${encodeURIComponent(query)}`,
    {
      signal,
      headers: {
        Accept: "application/json",
      },
    },
  );

  if (!response.ok) {
    throw new Error(`FORWARD_GEOCODE_HTTP_${response.status}`);
  }

  const payload = (await response.json()) as Array<{
    display_name?: string;
    lat?: string | number;
    lon?: string | number;
    address?: Record<string, unknown>;
  }>;

  if (!payload.length) {
    throw new Error("ADDRESS_NOT_FOUND");
  }

  return mapNominatimPayloadToLocation(payload[0]);
};

const withTimeout = async <T>(
  run: (controller: AbortController) => Promise<T>,
  timeoutMs: number,
  signal?: AbortSignal,
) => {
  const controller = new AbortController();
  let timeoutId: number | null = null;

  const abortFromSignal = () => controller.abort(signal?.reason);
  signal?.addEventListener("abort", abortFromSignal, { once: true });

  try {
    return await Promise.race([
      run(controller),
      new Promise<never>((_, reject) => {
        timeoutId = window.setTimeout(() => {
          controller.abort();
          reject(new Error("REQUEST_TIMEOUT"));
        }, timeoutMs);
      }),
    ]);
  } finally {
    if (timeoutId !== null) {
      window.clearTimeout(timeoutId);
    }
    signal?.removeEventListener("abort", abortFromSignal);
  }
};

export const useGeocoding = () => {
  const reverseCacheRef = useRef(new Map<string, GeocodingResult>());
  const forwardCacheRef = useRef(new Map<string, GeocodingResult>());

  const reverseGeocode = useCallback(
    async (lat: number, lng: number, signal?: AbortSignal) => {
      const cacheKey = createCacheKeyFromCoordinate(lat, lng);
      const cached = reverseCacheRef.current.get(cacheKey);
      if (cached) {
        return cached;
      }

      let result: GeocodingResult;
      try {
        result = await withTimeout(
          (controller) => adminApi.reverseGeocode(lat, lng, controller.signal),
          REVERSE_TIMEOUT_MS,
          signal,
        );
      } catch (error) {
        if (!shouldFallbackToDirectNominatim(error)) {
          throw error;
        }

        result = await withTimeout(
          (controller) => fetchDirectReverseGeocode(lat, lng, controller.signal),
          REVERSE_TIMEOUT_MS,
          signal,
        );
      }

      reverseCacheRef.current.set(cacheKey, result);
      return result;
    },
    [],
  );

  const forwardGeocode = useCallback(
    async (rawAddress: string, signal?: AbortSignal) => {
      const normalizedAddress = normalizeText(rawAddress);
      const cacheKey = normalizedAddress.toLowerCase();
      const cached = forwardCacheRef.current.get(cacheKey);
      if (cached) {
        return cached;
      }

      let result: GeocodingResult;
      try {
        result = await withTimeout(
          (controller) => adminApi.forwardGeocode(normalizedAddress, controller.signal),
          FORWARD_TIMEOUT_MS,
          signal,
        );
      } catch (error) {
        if (!shouldFallbackToDirectNominatim(error)) {
          throw error;
        }

        result = await withTimeout(
          (controller) => fetchDirectForwardGeocode(normalizedAddress, controller.signal),
          FORWARD_TIMEOUT_MS,
          signal,
        );
      }

      forwardCacheRef.current.set(cacheKey, result);
      return result;
    },
    [],
  );

  return {
    forwardGeocode,
    reverseGeocode,
  };
};
