import type { AdminDataState, EntityType, LanguageCode, Poi, Translation } from "../data/types";
import { normalizeSearchText } from "./utils";

type TranslationFallbackSettings = Pick<AdminDataState["settings"], "defaultLanguage" | "fallbackLanguage">;

const isPoiEntityType = (entityType: string) =>
  entityType === "poi" || entityType === "place";

const addLanguageCandidate = (languages: LanguageCode[], language?: LanguageCode | null) => {
  if (language && !languages.includes(language)) {
    languages.push(language);
  }
};

export const getTranslationLanguageOrder = (
  state: { settings: TranslationFallbackSettings },
  preferredLanguage?: LanguageCode,
) => {
  const languages: LanguageCode[] = [];
  addLanguageCandidate(languages, preferredLanguage);
  addLanguageCandidate(languages, state.settings.defaultLanguage);
  addLanguageCandidate(languages, state.settings.fallbackLanguage);
  return languages;
};

export const getEntityTranslationFromList = (
  translations: Translation[],
  state: { settings: TranslationFallbackSettings },
  preferredLanguage?: LanguageCode,
) => {
  const languages = getTranslationLanguageOrder(state, preferredLanguage);

  for (const language of languages) {
    const matched = translations.find((item) => item.languageCode === language);
    if (matched) {
      return matched;
    }
  }

  return null;
};

export const getEntityTranslation = (
  state: AdminDataState,
  entityType: EntityType,
  entityId: string,
  preferredLanguage?: LanguageCode,
) => {
  const translations = state.translations.filter(
    (item) =>
      item.entityId === entityId &&
      (entityType === "poi" ? isPoiEntityType(item.entityType) : item.entityType === entityType),
  );

  return getEntityTranslationFromList(translations, state, preferredLanguage);
};

export const getPoiTranslation = (
  state: AdminDataState,
  poiId: string,
  preferredLanguage?: LanguageCode,
) => {
  if (!state.pois.some((item) => item.id === poiId)) {
    return null;
  }

  return getEntityTranslation(state, "poi", poiId, preferredLanguage);
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
