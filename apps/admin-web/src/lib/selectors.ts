import type { AdminDataState, LanguageCode, Place } from "../data/types";
import { normalizeSearchText } from "./utils";

export const getPlaceTranslation = (
  state: AdminDataState,
  placeId: string,
  preferredLanguage?: LanguageCode,
) => {
  const place = state.places.find((item) => item.id === placeId);
  if (!place) {
    return null;
  }

  const languages = [
    preferredLanguage,
    place.defaultLanguageCode,
    state.settings.defaultLanguage,
    state.settings.fallbackLanguage,
  ].filter(Boolean) as LanguageCode[];

  for (const language of languages) {
    const matched = state.translations.find(
      (item) =>
        item.entityType === "place" && item.entityId === placeId && item.languageCode === language,
    );
    if (matched) {
      return matched;
    }
  }

  return (
    state.translations.find((item) => item.entityType === "place" && item.entityId === placeId) ??
    null
  );
};

export const getPlaceTitle = (
  state: AdminDataState,
  placeId: string,
  preferredLanguage?: LanguageCode,
) => getPlaceTranslation(state, placeId, preferredLanguage)?.title ?? "Chưa có tiêu đề";

export const getCategoryName = (state: AdminDataState, categoryId: string) =>
  state.categories.find((item) => item.id === categoryId)?.name ?? "Chưa phân loại";

export const getPlaceById = (state: AdminDataState, placeId: string) =>
  state.places.find((item) => item.id === placeId);

export const getOwnerName = (state: AdminDataState, ownerUserId: string | null) => {
  if (!ownerUserId) {
    return "Chưa gán chủ quán";
  }

  return state.users.find((item) => item.id === ownerUserId)?.name ?? "Chưa gán chủ quán";
};

export const createDailyMetrics = (values: string[], getValue: (date: Date) => number) =>
  values.map((label, index, array) => {
    const date = new Date();
    date.setDate(date.getDate() - (array.length - 1 - index));
    return {
      label,
      value: getValue(date),
    };
  });

export const searchPlaces = (places: Place[], state: AdminDataState, keyword: string) => {
  const normalizedKeyword = normalizeSearchText(keyword);
  if (!normalizedKeyword) {
    return places;
  }

  return places.filter((place) => {
    const translation = getPlaceTranslation(state, place.id);
    const haystack = normalizeSearchText(
      [translation?.title, place.address, place.slug, place.tags.join(" ")].join(" "),
    );

    return haystack.includes(normalizedKeyword);
  });
};
