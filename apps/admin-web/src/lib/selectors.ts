import type { AdminDataState, LanguageCode, Poi } from "../data/types";
import { normalizeSearchText } from "./utils";

export const getPoiTranslation = (
  state: AdminDataState,
  poiId: string,
  preferredLanguage?: LanguageCode,
) => {
  if (!state.pois.some((item) => item.id === poiId)) {
    return null;
  }

  const languages = [
    preferredLanguage,
    state.settings.defaultLanguage,
    state.settings.fallbackLanguage,
  ].filter(Boolean) as LanguageCode[];

  for (const language of languages) {
    const matched = state.translations.find(
      (item) =>
        item.entityType === "poi" && item.entityId === poiId && item.languageCode === language,
    );
    if (matched) {
      return matched;
    }
  }

  return (
    state.translations.find((item) => item.entityType === "poi" && item.entityId === poiId) ??
    null
  );
};

export const getPoiTitle = (
  state: AdminDataState,
  poiId: string,
  preferredLanguage?: LanguageCode,
) => getPoiTranslation(state, poiId, preferredLanguage)?.title ?? "Chưa có tiêu đề";

export const getCategoryName = (state: AdminDataState, categoryId: string) =>
  state.categories.find((item) => item.id === categoryId)?.name ?? "Chưa phân loại";

export const getPoiById = (state: AdminDataState, poiId: string) =>
  state.pois.find((item) => item.id === poiId);

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

export const searchPois = (pois: Poi[], state: AdminDataState, keyword: string) => {
  const normalizedKeyword = normalizeSearchText(keyword);
  if (!normalizedKeyword) {
    return pois;
  }

  return pois.filter((poi) => {
    const translation = getPoiTranslation(state, poi.id);
    const haystack = normalizeSearchText(
      [translation?.title, poi.address, poi.slug, poi.tags.join(" ")].join(" "),
    );

    return haystack.includes(normalizedKeyword);
  });
};
