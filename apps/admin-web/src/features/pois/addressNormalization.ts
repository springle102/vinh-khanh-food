import type { GeocodingLocation } from "../../data/types";

export type NominatimPayload = {
  display_name?: string;
  name?: string;
  lat?: string | number;
  lon?: string | number;
  address?: Record<string, unknown>;
};

export type NormalizedPoiLocation = GeocodingLocation & {
  city: string;
  sourceDistrict: string;
  sourceWard: string;
  hasAdministrativeOverride: boolean;
  overrideReason: string | null;
};

const HO_CHI_MINH_CITY = "TP.HCM";
const DISTRICT_FOUR = "Quận 4";
const VIETNAM = "Việt Nam";

const hoChiMinhAliases = new Set([
  "tp hcm",
  "tphcm",
  "thanh pho ho chi minh",
  "ho chi minh city",
  "hcmc",
  "sai gon",
  "saigon",
]);

const districtFourAliases = new Set(["quan 4", "district 4"]);
const vietnamAliases = new Set(["viet nam", "vietnam"]);
const districtFourWardOverrides = new Set([
  "phuong khanh hoi",
  "khanh hoi",
  "khanh hoi ward",
  "phuong vinh hoi",
  "vinh hoi",
  "vinh hoi ward",
]);

const normalizeText = (value: unknown) =>
  typeof value === "string" ? value.trim().replace(/\s+/g, " ") : "";

const removeDiacritics = (value: string) =>
  value.normalize("NFD").replace(/\p{Diacritic}+/gu, "").normalize("NFC");

const normalizeLookupKey = (value: unknown) =>
  removeDiacritics(normalizeText(value).toLowerCase())
    .replace(/[^a-z0-9]+/g, " ")
    .trim();

const splitAddressSegments = (value: unknown) =>
  normalizeText(value)
    .split(",")
    .map((segment) => normalizeText(segment))
    .filter(Boolean);

const isPostalCode = (value: string) => /^\d{5,6}$/.test(value);

const isSameLookup = (left: unknown, right: unknown) =>
  normalizeLookupKey(left) === normalizeLookupKey(right);

const addDistinct = (parts: string[], value: unknown) => {
  const normalized = normalizeText(value);
  if (!normalized) {
    return;
  }

  if (parts.some((part) => isSameLookup(part, normalized))) {
    return;
  }

  parts.push(normalized);
};

const firstNonEmpty = (...values: unknown[]) =>
  values.map(normalizeText).find(Boolean) ?? "";

const joinNonEmpty = (...values: unknown[]) =>
  values.map(normalizeText).filter(Boolean).join(" ");

const containsAlias = (value: unknown, aliases: Set<string>) => {
  const lookup = normalizeLookupKey(value);
  if (aliases.has(lookup)) {
    return true;
  }

  return splitAddressSegments(value).some((segment) => aliases.has(normalizeLookupKey(segment)));
};

const matchesDistrictFourWardOverride = (value: unknown) =>
  districtFourWardOverrides.has(normalizeLookupKey(value));

const isThuDucCityAlias = (value: unknown) => {
  const lookup = normalizeLookupKey(value);
  return lookup === "thanh pho thu duc" || lookup === "thu duc city";
};

const isLikelyDistrictFourZone = (lat: number, lng: number) =>
  lat >= 10.758 && lat <= 10.7648 && lng >= 106.699 && lng <= 106.7068;

const isLikelyVinhKhanhStreet = (value: unknown) =>
  normalizeLookupKey(value).includes("vinh khanh");

const isHoChiMinhCityContext = ({
  addressLike,
  districtLike,
  cityLike,
  stateLike,
  countryCode,
  iso3166Level4,
  wardLike,
}: {
  addressLike?: unknown;
  districtLike?: unknown;
  cityLike?: unknown;
  stateLike?: unknown;
  countryCode?: unknown;
  iso3166Level4?: unknown;
  wardLike?: unknown;
}) => {
  const hasVietnamContext =
    normalizeText(iso3166Level4).toUpperCase() === "VN-SG" ||
    normalizeText(countryCode).toLowerCase() === "vn";

  if (hasVietnamContext) {
    if (
      containsAlias(addressLike, hoChiMinhAliases) ||
      containsAlias(cityLike, hoChiMinhAliases) ||
      containsAlias(stateLike, hoChiMinhAliases) ||
      containsAlias(districtLike, districtFourAliases) ||
      matchesDistrictFourWardOverride(wardLike)
    ) {
      return true;
    }
  }

  return (
    containsAlias(addressLike, hoChiMinhAliases) ||
    containsAlias(cityLike, hoChiMinhAliases) ||
    containsAlias(stateLike, hoChiMinhAliases) ||
    containsAlias(districtLike, districtFourAliases) ||
    matchesDistrictFourWardOverride(wardLike)
  );
};

const resolveCity = ({
  addressLike,
  districtLike,
  cityLike,
  stateLike,
  countryCode,
  iso3166Level4,
  wardLike,
}: {
  addressLike?: unknown;
  districtLike?: unknown;
  cityLike?: unknown;
  stateLike?: unknown;
  countryCode?: unknown;
  iso3166Level4?: unknown;
  wardLike?: unknown;
}) => {
  if (
    isHoChiMinhCityContext({
      addressLike,
      districtLike,
      cityLike,
      stateLike,
      countryCode,
      iso3166Level4,
      wardLike,
    })
  ) {
    return HO_CHI_MINH_CITY;
  }

  return normalizeText(stateLike);
};

const resolveDistrict = ({
  city,
  sourceDistrict,
  ward,
  road,
  addressLike,
  lat,
  lng,
}: {
  city: string;
  sourceDistrict: string;
  ward: string;
  road?: unknown;
  addressLike?: unknown;
  lat: number;
  lng: number;
}) => {
  if (matchesDistrictFourWardOverride(ward) && containsAlias(city, hoChiMinhAliases)) {
    return {
      district: DISTRICT_FOUR,
      overrideReason: isThuDucCityAlias(sourceDistrict)
        ? "hcmc-khanh-hoi-was-mapped-to-thu-duc"
        : sourceDistrict
          ? "hcmc-khanh-hoi-normalized-district"
          : "hcmc-khanh-hoi-filled-missing-district",
    };
  }

  if (
    containsAlias(city, hoChiMinhAliases) &&
    isThuDucCityAlias(sourceDistrict) &&
    (
      matchesDistrictFourWardOverride(ward) ||
      isLikelyVinhKhanhStreet(road) ||
      isLikelyDistrictFourZone(lat, lng) ||
      containsAlias(addressLike, districtFourAliases)
    )
  ) {
    return {
      district: DISTRICT_FOUR,
      overrideReason: "hcmc-vinh-khanh-normalized-from-thu-duc",
    };
  }

  if (containsAlias(city, hoChiMinhAliases) && containsAlias(sourceDistrict, hoChiMinhAliases)) {
    return {
      district: "",
      overrideReason: null,
    };
  }

  return {
    district: sourceDistrict,
    overrideReason: null,
  };
};

const resolveCountry = (country: unknown, countryCode: unknown) =>
  normalizeText(countryCode).toLowerCase() === "vn" || containsAlias(country, vietnamAliases)
    ? VIETNAM
    : normalizeText(country);

const resolveCountryFromAddress = (address: unknown) =>
  containsAlias(address, vietnamAliases) ? VIETNAM : "";

const resolveBaseSegments = ({
  addressLike,
  venue,
  street,
  ward,
  sourceDistrict,
  district,
  city,
  country,
}: {
  addressLike?: unknown;
  venue?: unknown;
  street?: unknown;
  ward: string;
  sourceDistrict: string;
  district: string;
  city: string;
  country: string;
}) => {
  const parts: string[] = [];
  addDistinct(parts, venue);
  addDistinct(parts, street);

  for (const segment of splitAddressSegments(addressLike)) {
    if (
      isPostalCode(segment) ||
      isSameLookup(segment, ward) ||
      isSameLookup(segment, sourceDistrict) ||
      isSameLookup(segment, district) ||
      isSameLookup(segment, city) ||
      (containsAlias(city, hoChiMinhAliases) && containsAlias(segment, hoChiMinhAliases)) ||
      containsAlias(segment, vietnamAliases) ||
      (country && isSameLookup(segment, country))
    ) {
      continue;
    }

    addDistinct(parts, segment);
  }

  return parts;
};

const buildAddress = ({
  baseSegments,
  ward,
  district,
  city,
  country,
}: {
  baseSegments: string[];
  ward: string;
  district: string;
  city: string;
  country: string;
}) => {
  const parts: string[] = [];
  baseSegments.forEach((segment) => addDistinct(parts, segment));
  addDistinct(parts, ward);
  addDistinct(parts, district);
  addDistinct(parts, city);
  addDistinct(parts, country);
  return parts.join(", ");
};

export const normalizeNominatimPayload = (payload: NominatimPayload): NormalizedPoiLocation => {
  const addressDetails = payload.address ?? {};
  const lat = Number(payload.lat ?? 0);
  const lng = Number(payload.lon ?? 0);
  const sourceWard = firstNonEmpty(
    addressDetails.suburb,
    addressDetails.neighbourhood,
    addressDetails.quarter,
    addressDetails.city_block,
    addressDetails.hamlet,
  );
  const ward = normalizeText(sourceWard);
  const sourceDistrict = firstNonEmpty(
    addressDetails.city_district,
    addressDetails.state_district,
    addressDetails.county,
    addressDetails.city,
  );
  const city = resolveCity({
    addressLike: payload.display_name,
    districtLike: sourceDistrict,
    cityLike: addressDetails.city,
    stateLike: addressDetails.state,
    countryCode: addressDetails.country_code,
    iso3166Level4: addressDetails["ISO3166-2-lvl4"],
    wardLike: ward,
  });
  const districtDecision = resolveDistrict({
    city,
    sourceDistrict,
    ward,
    road: addressDetails.road,
    addressLike: payload.display_name,
    lat,
    lng,
  });
  const country = resolveCountry(addressDetails.country, addressDetails.country_code);
  const street = joinNonEmpty(addressDetails.house_number, addressDetails.road);
  const baseSegments = resolveBaseSegments({
    addressLike: payload.display_name,
    venue: payload.name,
    street,
    ward,
    sourceDistrict,
    district: districtDecision.district,
    city,
    country,
  });

  return {
    address: buildAddress({
      baseSegments,
      ward,
      district: districtDecision.district,
      city,
      country,
    }),
    district: districtDecision.district,
    ward,
    lat,
    lng,
    city,
    sourceDistrict: normalizeText(sourceDistrict),
    sourceWard,
    hasAdministrativeOverride: !!districtDecision.overrideReason,
    overrideReason: districtDecision.overrideReason,
  };
};

export const normalizePersistedPoiLocation = ({
  address,
  district,
  ward,
  lat,
  lng,
}: {
  address: string;
  district: string;
  ward: string;
  lat: number;
  lng: number;
}): NormalizedPoiLocation => {
  const normalizedWard = normalizeText(ward);
  const sourceDistrict = normalizeText(district);
  const city = resolveCity({
    addressLike: address,
    districtLike: sourceDistrict,
    wardLike: normalizedWard,
  });
  const districtDecision = resolveDistrict({
    city,
    sourceDistrict,
    ward: normalizedWard,
    addressLike: address,
    lat,
    lng,
  });
  const country = resolveCountryFromAddress(address);
  const baseSegments = resolveBaseSegments({
    addressLike: address,
    ward: normalizedWard,
    sourceDistrict,
    district: districtDecision.district,
    city,
    country,
  });

  return {
    address: buildAddress({
      baseSegments,
      ward: normalizedWard,
      district: districtDecision.district,
      city,
      country,
    }),
    district: districtDecision.district,
    ward: normalizedWard,
    lat,
    lng,
    city,
    sourceDistrict,
    sourceWard: normalizedWard,
    hasAdministrativeOverride: !!districtDecision.overrideReason,
    overrideReason: districtDecision.overrideReason,
  };
};
