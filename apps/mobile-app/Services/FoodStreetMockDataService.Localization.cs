using System.Globalization;
using System.Text;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetMockDataService
{
    private IReadOnlyList<PoiLocation> BuildLocalizedFallbackPois()
        => FallbackPois.Select(LocalizePoiLocation).ToList();

    private PoiLocation LocalizePoiLocation(PoiLocation poi)
    {
        return new PoiLocation
        {
            Id = poi.Id,
            Title = GetFallbackPoiTitle(poi.Id, poi.Title),
            ShortDescription = GetFallbackPoiSummary(poi.Id, poi.ShortDescription),
            Address = LocalizeAddress(poi.Address, poi.Id),
            Category = LocalizeCategory(poi.Category),
            PriceRange = poi.PriceRange,
            ThumbnailUrl = poi.ThumbnailUrl,
            Latitude = poi.Latitude,
            Longitude = poi.Longitude,
            IsFeatured = poi.IsFeatured,
            HeatIntensity = poi.HeatIntensity,
            DistanceText = LocalizeDistanceText(poi.DistanceText)
        };
    }

    private string GetFallbackPoiTitle(string poiId, string fallbackTitle)
        => SelectLocalizedText(GetFallbackPoiNameTranslations(poiId, fallbackTitle));

    private string GetFallbackPoiSummary(string poiId, string fallbackSummary)
        => SelectLocalizedText(GetFallbackPoiSummaryTranslations(poiId, fallbackSummary));

    private IReadOnlyDictionary<string, string> GetFallbackPoiNameTranslations(string poiId, string fallbackTitle)
    {
        return poiId switch
        {
            "poi-snail-signature" => CreateLocalizedMap(
                "Quán Ốc Vĩnh Khánh Signature",
                "Vinh Khanh Signature Snail House",
                "永庆招牌螺店",
                "빈카인 시그니처 달팽이집",
                "ヴィンカイン名物貝料理店",
                "Maison Signature des Escargots Vinh Khanh"),
            "poi-bbq-night" => CreateLocalizedMap(
                "Quảng trường Ẩm thực BBQ Night",
                "BBQ Night Food Square",
                "BBQ Night 美食广场",
                "BBQ Night 푸드 스퀘어",
                "BBQ Night フードスクエア",
                "Place Gourmande BBQ Night"),
            "poi-sweet-lane" => CreateLocalizedMap(
                "Hẻm Chè Vĩnh Khánh",
                "Vinh Khanh Dessert Alley",
                "永庆甜品巷",
                "빈카인 디저트 골목",
                "ヴィンカインスイーツ路地",
                "Ruelle des Desserts Vinh Khanh"),
            _ => CreateFallbackTranslationMap(fallbackTitle)
        };
    }

    private IReadOnlyDictionary<string, string> GetFallbackPoiSummaryTranslations(string poiId, string fallbackSummary)
    {
        return poiId switch
        {
            "poi-snail-signature" => CreateLocalizedMap(
                "Quán ốc đặc trưng với thực đơn đa dạng, phù hợp khách lần đầu đến khu phố.",
                "A signature snail restaurant with a varied menu that is easy for first-time visitors to enjoy.",
                "这家招牌螺食店菜单丰富，很适合第一次来这条美食街的游客。",
                "메뉴가 다양해 이 거리를 처음 찾는 방문자도 편하게 즐길 수 있는 대표 달팽이 맛집입니다.",
                "メニューが豊富で、この通りを初めて訪れる人でも楽しみやすい名物の貝料理店です。",
                "Une adresse signature aux coquillages avec une carte variée, idéale pour une première découverte de la rue."),
            "poi-bbq-night" => CreateLocalizedMap(
                "Điểm tụ họp sôi động với món nướng hải sản và không khí phố đêm náo nhiệt.",
                "A lively grilled seafood stop with the high-energy feel of Vinh Khanh at night.",
                "这里以烧烤海鲜和热闹夜市氛围闻名，是永庆街最热闹的聚点之一。",
                "해산물 구이와 활기찬 밤거리 분위기로 유명한 빈카인의 대표 모임 장소입니다.",
                "海鮮焼きとにぎやかな夜の雰囲気で知られる、ヴィンカイン通りでも特に活気のある立ち寄り先です。",
                "Une halte très animée, connue pour ses fruits de mer grillés et l'énergie nocturne de Vinh Khanh."),
            "poi-sweet-lane" => CreateLocalizedMap(
                "Điểm tráng miệng và món ngọt giúp cân bằng hành trình ăn uống.",
                "A dessert stop that balances the whole food journey with sweet local treats.",
                "这是一处用本地甜品为整段美食之旅收尾的轻松站点。",
                "달콤한 현지 디저트로 전체 미식 코스를 부드럽게 마무리해 주는 곳입니다.",
                "地元スイーツで食べ歩きの流れをやさしく整えてくれる甘味スポットです。",
                "Une étape dessert qui équilibre le parcours gourmand avec des douceurs locales."),
            _ => CreateFallbackTranslationMap(fallbackSummary)
        };
    }

    private IReadOnlyDictionary<string, string> GetFallbackPoiDescriptionTranslations(
        string poiId,
        string localizedAddress,
        string localizedCategory,
        string fallbackSummary)
    {
        return poiId switch
        {
            "poi-snail-signature" => CreateLocalizedMap(
                "Quán ốc signature phù hợp để mở đầu hành trình ẩm thực, nổi bật với món ốc xào, ốc nướng và các đĩa hải sản để chia sẻ.",
                "This signature snail stop opens the food journey well, with stir-fried snails, grilled shellfish, and shareable seafood platters.",
                "这里很适合作为美食之旅的开场站点，以炒螺、烤贝类和适合分享的海鲜拼盘而受欢迎。",
                "이곳은 미식 코스의 시작점으로 잘 어울리며, 볶은 달팽이와 구운 조개, 함께 나눠 먹기 좋은 해산물 플래터로 유명합니다.",
                "ストリートフード巡りの最初の一軒にぴったりで、炒めた貝料理や焼き貝、シェアしやすい海鮮プレートで知られています。",
                "Cette adresse signature ouvre parfaitement le parcours gourmand avec ses escargots sautés, ses coquillages grillés et ses plateaux de fruits de mer à partager."),
            "poi-bbq-night" => CreateLocalizedMap(
                "Khu BBQ nhộn nhịp vào buổi tối, hợp với nhóm bạn muốn trải nghiệm không khí phố ẩm thực và các món hải sản nướng nóng hổi.",
                "This BBQ stop comes alive at night and suits groups who want the bright, energetic seafood street atmosphere.",
                "这处海鲜烧烤据点在夜晚尤其热闹，很适合想感受永庆街灯火与烟火气的游客。",
                "이 해산물 BBQ 명소는 밤이 되면 특히 활기를 띠어, 빈카인 거리의 불빛과 에너지를 느끼고 싶은 일행에게 잘 어울립니다.",
                "この海鮮BBQスポットは夜になると特ににぎわい、ヴィンカイン通りらしい灯りと活気を味わいたい人に向いています。",
                "Ce spot BBQ s'anime surtout le soir et convient parfaitement à ceux qui veulent retrouver l'ambiance lumineuse et énergique de Vinh Khanh."),
            "poi-sweet-lane" => CreateLocalizedMap(
                "Hẻm chè và món ngọt là điểm dừng để kết tour, thường được chọn để nghỉ chân sau các món mặn và hải sản.",
                "This dessert lane works well as the final stop, especially after savory seafood dishes and street-food tastings.",
                "这条甜品小巷很适合作为最后一站，在海鲜和咸食之后用本地甜点收束整段行程。",
                "이 디저트 골목은 마지막 코스로 잘 어울리며, 해산물과 짭짤한 요리 뒤에 현지 달콤한 메뉴로 마무리하기 좋습니다.",
                "この甘味の路地は最後の立ち寄り先にぴったりで、海鮮や塩気のある料理のあとを地元スイーツで締めくくれます。",
                "Cette ruelle sucrée fait une excellente dernière étape pour conclure le parcours sur une note dessert après les fruits de mer et les plats salés."),
            _ => CreateLocalizedMap(
                fallbackSummary,
                $"{ToLatinText(fallbackSummary)} Address: {localizedAddress}. Category: {localizedCategory}.",
                $"{ToLatinText(fallbackSummary)} 地址：{localizedAddress}。类别：{localizedCategory}。",
                $"{ToLatinText(fallbackSummary)} 주소: {localizedAddress}. 분류: {localizedCategory}.",
                $"{ToLatinText(fallbackSummary)} 住所: {localizedAddress}。カテゴリ: {localizedCategory}。",
                $"{ToLatinText(fallbackSummary)} Adresse : {localizedAddress}. Catégorie : {localizedCategory}.")
        };
    }

    private string GetTourThemeText()
        => SelectLocalizedText(CreateLocalizedMap(
            "Hành Trình Ăn Vặt",
            "Street Snack Journey",
            "小吃之旅",
            "길거리 간식 코스",
            "ストリートスナック巡り",
            "Parcours street food"));

    private string GetTourDescriptionText()
        => SelectLocalizedText(CreateLocalizedMap(
            "Tour ngắn ưu tiên các POI đang được quản lý trong admin và fallback mock khi backend chưa bật.",
            "A short route built from admin-managed POIs, with mock fallback content when the backend is unavailable.",
            "这条短路线优先展示后台管理中的 POI；当后端未开启时会改用内置示例数据。",
            "이 짧은 코스는 관리자에서 관리 중인 POI를 우선 사용하며, 백엔드가 꺼져 있으면 내장 목업 데이터로 대체됩니다.",
            "この短いコースでは管理画面で運用中の POI を優先し、バックエンドが使えない場合は内蔵モックデータに切り替えます。",
            "Ce parcours court privilégie les POI gérés dans l'admin et bascule sur des données fictives intégrées lorsque le backend est indisponible."));

    private string GetTourSummaryText()
        => SelectLocalizedText(CreateLocalizedMap(
            "Lộ trình nhẹ, nhiều món signature và kết thúc bằng món ngọt.",
            "A lighter route with signature dishes and a sweet finish.",
            "这是一条较轻松的路线，主打招牌菜，并以甜品收尾。",
            "대표 메뉴를 맛본 뒤 디저트로 마무리하는 가벼운 코스입니다.",
            "名物料理を楽しみ、最後はスイーツで締めくくる軽めのコースです。",
            "Un itinéraire plus léger, rythmé par des plats signature et une fin en douceur."));

    private string FormatTourProgressText(int completedCount, int totalCount)
        => SelectLocalizedText(CreateLocalizedMap(
            $"{completedCount} / {totalCount} điểm đã đi",
            $"{completedCount} / {totalCount} stops visited",
            $"{completedCount} / {totalCount} 个站点已到访",
            $"{completedCount} / {totalCount}곳 방문 완료",
            $"{completedCount} / {totalCount}スポット訪問済み",
            $"{completedCount} / {totalCount} étapes visitées"));

    private string GetTourStopDescription(string distanceText)
        => SelectLocalizedText(CreateLocalizedMap(
            $"Tạo Tour - {distanceText}",
            $"Planned stop - {distanceText}",
            $"行程站点 - {distanceText}",
            $"코스 정차지 - {distanceText}",
            $"立ち寄り予定 - {distanceText}",
            $"Étape prévue - {distanceText}"));

    private string GetFallbackStopTitle()
        => SelectLocalizedText(CreateLocalizedMap(
            "Điểm dừng",
            "Stop",
            "站点",
            "정차지",
            "立ち寄り先",
            "Étape"));

    private string FormatVisitDuration(int durationInMinutes)
    {
        var safeDuration = Math.Max(1, durationInMinutes);
        return CurrentLanguageCode switch
        {
            "zh-CN" => $"{safeDuration} 分钟",
            "ko" => $"{safeDuration}분",
            "ja" => $"{safeDuration}分",
            "fr" => $"{safeDuration} min",
            "en" => $"{safeDuration} min",
            _ => $"{safeDuration} phút"
        };
    }

    private string LocalizeDistanceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var raw = value.Trim();
        var normalized = ToLatinText(raw);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return raw;
        }

        var number = parts[0];
        var unit = parts[1].ToLowerInvariant();

        if (unit is "phut" or "min")
        {
            var digits = new string(number.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var minutes) ? FormatVisitDuration(minutes) : raw;
        }

        return FormatMeasurement(number, unit);
    }

    private string FormatMeasurement(string number, string unit)
    {
        var normalizedNumber = CurrentLanguageCode == "fr"
            ? number.Replace('.', ',')
            : number.Replace(',', '.');

        return (CurrentLanguageCode, unit) switch
        {
            ("zh-CN", "km") => $"{normalizedNumber} 公里",
            ("zh-CN", "m") => $"{normalizedNumber} 米",
            ("ko", "km") => $"{normalizedNumber}km",
            ("ko", "m") => $"{normalizedNumber}m",
            ("ja", "km") => $"{normalizedNumber}km",
            ("ja", "m") => $"{normalizedNumber}m",
            ("fr", "km") => $"{normalizedNumber} km",
            ("fr", "m") => $"{normalizedNumber} m",
            ("en", "km") => $"{normalizedNumber} km",
            ("en", "m") => $"{normalizedNumber} m",
            (_, "km") => $"{number.Replace('.', ',')} km",
            (_, "m") => $"{number.Replace('.', ',')} m",
            _ => $"{normalizedNumber} {unit}"
        };
    }

    private string LocalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return SelectLocalizedText(CreateLocalizedMap(
                "Địa điểm ẩm thực",
                "Food spot",
                "美食地点",
                "맛집",
                "グルメスポット",
                "Spot gourmand"));
        }

        return NormalizeLookupKey(category) switch
        {
            "oc-dac-san" => SelectLocalizedText(CreateLocalizedMap(
                "Ốc đặc sản",
                "Signature snails",
                "招牌螺贝",
                "시그니처 달팽이 요리",
                "名物の貝料理",
                "Escargots signature")),
            "hai-san-nuong" => SelectLocalizedText(CreateLocalizedMap(
                "Hải sản nướng",
                "Grilled seafood",
                "烧烤海鲜",
                "해산물 구이",
                "海鮮焼き",
                "Fruits de mer grillés")),
            "mon-ngot" => SelectLocalizedText(CreateLocalizedMap(
                "Món ngọt",
                "Desserts",
                "甜品",
                "디저트",
                "スイーツ",
                "Desserts")),
            _ => CurrentLanguageCode == "vi" ? category.Trim() : ToLatinText(category)
        };
    }

    private string LocalizeAddress(string? address, string? poiId = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        if (CurrentLanguageCode == "vi")
        {
            return address.Trim();
        }

        var text = address.Trim();
        var normalizedKey = NormalizeLookupKey(text);

        return normalizedKey switch
        {
            "42-vinh-khanh-phuong-khanh-hoi-tp-hcm" => SelectLocalizedText(CreateLocalizedMap(
                "42 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
                "42 Vinh Khanh, Khanh Hoi Ward, Ho Chi Minh City",
                "42 永庆街, 庆会坊, 胡志明市",
                "호찌민시 카인호이동 빈카인 42",
                "ホーチミン市カインホイ街区ヴィンカイン42",
                "42 Vinh Khanh, quartier Khanh Hoi, Hô-Chi-Minh-Ville")),
            "126-vinh-khanh-phuong-khanh-hoi-tp-hcm" => SelectLocalizedText(CreateLocalizedMap(
                "126 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
                "126 Vinh Khanh, Khanh Hoi Ward, Ho Chi Minh City",
                "126 永庆街, 庆会坊, 胡志明市",
                "호찌민시 카인호이동 빈카인 126",
                "ホーチミン市カインホイ街区ヴィンカイン126",
                "126 Vinh Khanh, quartier Khanh Hoi, Hô-Chi-Minh-Ville")),
            "88-4-vinh-khanh-phuong-vinh-hoi-tp-hcm" => SelectLocalizedText(CreateLocalizedMap(
                "88/4 Vĩnh Khánh, Phường Vĩnh Hội, TP.HCM",
                "88/4 Vinh Khanh, Vinh Hoi Ward, Ho Chi Minh City",
                "胡志明市永会坊永庆街 88/4 号",
                "호찌민시 빈호이동 빈카인 88/4",
                "ホーチミン市ヴィンホイ街区ヴィンカイン88/4",
                "88/4 Vinh Khanh, quartier Vinh Hoi, Hô-Chi-Minh-Ville")),
            _ => ApplyAddressLocalization(text)
        };
    }

    private string ApplyAddressLocalization(string address)
    {
        var replacements = CurrentLanguageCode switch
        {
            "zh-CN" => new[]
            {
                ("Phường Khánh Hội", "庆会坊"),
                ("Phường Vĩnh Hội", "永会坊"),
                ("Quận 4", "第四郡"),
                ("TP.HCM", "胡志明市"),
                ("TP. HCM", "胡志明市"),
                ("Thành phố Hồ Chí Minh", "胡志明市"),
                ("Vĩnh Khánh", "永庆"),
                ("Đường", "街")
            },
            "ko" => new[]
            {
                ("Phường Khánh Hội", "카인호이동"),
                ("Phường Vĩnh Hội", "빈호이동"),
                ("Quận 4", "4군"),
                ("TP.HCM", "호찌민시"),
                ("TP. HCM", "호찌민시"),
                ("Thành phố Hồ Chí Minh", "호찌민시"),
                ("Vĩnh Khánh", "빈카인"),
                ("Đường", "도로")
            },
            "ja" => new[]
            {
                ("Phường Khánh Hội", "カインホイ街区"),
                ("Phường Vĩnh Hội", "ヴィンホイ街区"),
                ("Quận 4", "4区"),
                ("TP.HCM", "ホーチミン市"),
                ("TP. HCM", "ホーチミン市"),
                ("Thành phố Hồ Chí Minh", "ホーチミン市"),
                ("Vĩnh Khánh", "ヴィンカイン"),
                ("Đường", "通り")
            },
            "fr" => new[]
            {
                ("Phường Khánh Hội", "quartier Khanh Hoi"),
                ("Phường Vĩnh Hội", "quartier Vinh Hoi"),
                ("Quận 4", "4e arrondissement"),
                ("TP.HCM", "Hô-Chi-Minh-Ville"),
                ("TP. HCM", "Hô-Chi-Minh-Ville"),
                ("Thành phố Hồ Chí Minh", "Hô-Chi-Minh-Ville"),
                ("Vĩnh Khánh", "Vinh Khanh"),
                ("Đường", "rue")
            },
            _ => new[]
            {
                ("Phường Khánh Hội", "Khanh Hoi Ward"),
                ("Phường Vĩnh Hội", "Vinh Hoi Ward"),
                ("Quận 4", "District 4"),
                ("TP.HCM", "Ho Chi Minh City"),
                ("TP. HCM", "Ho Chi Minh City"),
                ("Thành phố Hồ Chí Minh", "Ho Chi Minh City"),
                ("Vĩnh Khánh", "Vinh Khanh"),
                ("Đường", "Street")
            }
        };

        var localized = ApplyReplacements(address, replacements);
        return ToLatinText(localized);
    }

    private string SelectLocalizedText(IReadOnlyDictionary<string, string> values)
        => LocalizedTextHelper.GetLocalizedText(values, CurrentLanguageCode);

    private string CurrentLanguageCode => AppLanguage.NormalizeCode(_languageService.CurrentLanguage);

    private static IReadOnlyDictionary<string, string> CreateLocalizedMap(
        string vietnameseText,
        string englishText,
        string simplifiedChineseText,
        string koreanText,
        string japaneseText,
        string frenchText)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = vietnameseText,
            ["en"] = englishText,
            ["zh-CN"] = simplifiedChineseText,
            ["ko"] = koreanText,
            ["ja"] = japaneseText,
            ["fr"] = frenchText
        };

    private static IReadOnlyDictionary<string, string> CreateFallbackTranslationMap(string fallbackText)
    {
        var latinText = ToLatinText(fallbackText);
        return CreateLocalizedMap(
            fallbackText,
            latinText,
            latinText,
            latinText,
            latinText,
            latinText);
    }

    private static string ApplyReplacements(string source, IEnumerable<(string From, string To)> replacements)
    {
        var result = source;
        foreach (var (from, to) in replacements)
        {
            result = result.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string NormalizeLookupKey(string value)
    {
        var normalized = ToLatinText(value).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ToLatinText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character switch
            {
                'Đ' => 'D',
                'đ' => 'd',
                _ => character
            });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
