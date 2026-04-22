using System.Globalization;
using System.Text;
using VinhKhanh.Core.Pois;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
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
            TriggerRadius = poi.TriggerRadius,
            Priority = Math.Max(0, poi.Priority),
            PlaceTier = PoiPlaceTierCatalog.Normalize(poi.PlaceTier),
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
                "Nhà Hàng Sushi Ko",
                "Sushi Ko Seafood House",
                "寿司Ko海鲜馆",
                "스시 코 해산물 하우스",
                "寿司こう 海鮮ハウス",
                "Maison des Fruits de Mer Sushi Ko"),
            "poi-sweet-lane" => CreateLocalizedMap(
                "Hẻm Cà Phê Vĩnh Khánh",
                "Vinh Khanh Coffee & Tea Alley",
                "永庆咖啡茶巷",
                "빈카인 커피 & 티 골목",
                "ヴィンカイン コーヒー&ティー路地",
                "Ruelle Café & Thé Vinh Khanh"),
            _ => CreateFallbackTranslationMap(fallbackTitle, "Vinh Khanh food spot")
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
                "Điểm hải sản & đồ sống nổi bật với sushi, sashimi và nhịp sống phố đêm.",
                "A seafood and raw-dish stop known for sushi, sashimi, and Vinh Khanh's lively night energy.",
                "这里主打寿司、刺身与海鲜生食，也是感受永庆夜间节奏的热门一站。",
                "스시와 사시미, 신선한 해산물 요리로 빈카인 밤거리의 분위기를 느끼기 좋은 곳입니다.",
                "寿司や刺身、新鮮な海鮮料理でヴィンカインの夜の活気を感じられる立ち寄り先です。",
                "Une halte fruits de mer et plats crus, réputée pour ses sushis, ses sashimis et l'énergie nocturne de Vinh Khanh."),
            "poi-sweet-lane" => CreateLocalizedMap(
                "Điểm cà phê & trà phù hợp để nghỉ chân giữa hành trình ăn uống.",
                "A coffee and tea stop that gives the food journey a relaxed mid-route break.",
                "这是一处适合在美食行程中途停下来喝咖啡与茶的轻松站点。",
                "미식 여정 중간에 커피와 차로 잠시 쉬어 가기 좋은 곳입니다.",
                "食べ歩きの途中でコーヒーやお茶を楽しみながらひと休みできる場所です。",
                "Une pause café et thé idéale pour souffler au milieu du parcours gourmand."),
            _ => CreateFallbackTranslationMap(fallbackSummary, "A food stop in the Vinh Khanh area.")
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
                "Không gian hải sản & đồ sống này phù hợp với nhóm bạn muốn gọi sushi, sashimi và các phần ăn chia sẻ trong nhịp sống phố đêm Vĩnh Khánh.",
                "This seafood and raw-dish stop suits groups who want sushi, sashimi, and shareable plates in Vinh Khanh's lively night scene.",
                "这处海鲜与生食据点很适合结伴前来，点寿司、刺身与适合分享的菜品，感受永庆夜间氛围。",
                "이 해산물·생식 명소는 스시와 사시미, 함께 나눠 먹기 좋은 메뉴를 즐기며 빈카인 밤거리를 느끼고 싶은 일행에게 잘 어울립니다.",
                "この海鮮・生ものスポットは、寿司や刺身、シェアしやすい料理を楽しみながらヴィンカインの夜を味わいたいグループに向いています。",
                "Cette adresse fruits de mer et plats crus convient parfaitement aux groupes qui veulent partager sushis, sashimis et assiettes à plusieurs dans l'ambiance nocturne de Vinh Khanh."),
            "poi-sweet-lane" => CreateLocalizedMap(
                "Hẻm cà phê là điểm dừng để nghỉ chân, thường được chọn sau các món mặn và hải sản để cân bằng lại hành trình với cà phê sữa hoặc trà trái cây.",
                "This coffee lane works well as a pause stop after savory seafood dishes, especially for iced coffee or fruit tea.",
                "这条咖啡小巷适合作为海鲜与咸食之后的休息站，用冰咖啡或水果茶让整段行程更平衡。",
                "이 커피 골목은 해산물과 짭짤한 요리 뒤에 아이스커피나 과일차로 동선을 정리하기 좋은 휴식 코스입니다.",
                "このコーヒー路地は、海鮮や塩気のある料理のあとにアイスコーヒーやフルーツティーでひと息つくのにちょうど良い立ち寄り先です。",
                "Cette ruelle café est idéale pour faire une pause après les plats salés et les fruits de mer, autour d'un café glacé ou d'un thé fruité."),
            _ => CreateFallbackTranslationMap(fallbackSummary, "A food stop in the Vinh Khanh area.")
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

    private string LocalizeRouteTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return string.Empty;
        }

        return NormalizeLookupKey(theme) switch
        {
            "an-vat" => SelectLocalizedText(CreateLocalizedMap(
                "Ăn vặt",
                "Street snacks",
                "小吃",
                "길거리 간식",
                "ストリートスナック",
                "Street food")),
            "hai-san" => SelectLocalizedText(CreateLocalizedMap(
                "Hải sản",
                "Seafood",
                "海鲜",
                "해산물",
                "海鮮",
                "Fruits de mer")),
            "buoi-toi" => SelectLocalizedText(CreateLocalizedMap(
                "Buổi tối",
                "Evening route",
                "夜间路线",
                "저녁 코스",
                "夜のルート",
                "Parcours du soir")),
            "khach-quoc-te" => SelectLocalizedText(CreateLocalizedMap(
                "Khách quốc tế",
                "International visitors",
                "国际游客",
                "해외 방문객",
                "海外からの訪問者",
                "Visiteurs internationaux")),
            "gia-dinh" => SelectLocalizedText(CreateLocalizedMap(
                "Gia đình",
                "Family friendly",
                "适合家庭",
                "가족에게 좋음",
                "家族向け",
                "Adapté aux familles")),
            "tong-hop" => SelectLocalizedText(CreateLocalizedMap(
                "Tổng hợp",
                "Mixed highlights",
                "精选组合",
                "추천 모음",
                "ハイライトミックス",
                "Sélection variée")),
            _ => LocalizationFallbackPolicy.SourceTextForLanguage(theme, CurrentLanguageCode)
        };
    }

    private string GetTourDescriptionText()
        => SelectLocalizedText(CreateLocalizedMap(
            "Tour ngắn được tạo từ các POI đang được quản lý trong admin.",
            "A short route built from the POIs currently managed in admin.",
            "这条短路线由当前在管理后台中维护的 POI 组成。",
            "이 짧은 코스는 현재 admin에서 관리 중인 POI로 구성됩니다.",
            "この短いコースは現在 admin で管理されている POI から構成されています。",
            "Ce parcours court est construit à partir des POI actuellement gérés dans l'admin."));

    private string GetTourSummaryText()
        => SelectLocalizedText(CreateLocalizedMap(
            "Lộ trình nhẹ, nhiều món signature và kết thúc bằng cà phê hoặc trà.",
            "A lighter route with signature dishes and a coffee-or-tea finish.",
            "这是一条较轻松的路线，主打招牌菜，并以咖啡或茶收尾。",
            "대표 메뉴를 맛본 뒤 커피나 차로 마무리하는 가벼운 코스입니다.",
            "名物料理を楽しみ、最後はコーヒーやお茶で締めくくる軽めのコースです。",
            "Un itinéraire plus léger, rythmé par des plats signature et une fin autour d'un café ou d'un thé."));

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
            $"Điểm trong tour - {distanceText}",
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

        var raw = TextEncodingHelper.NormalizeDisplayText(value);
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
        var normalizedNumber = number.Replace(',', '.');

        return (CurrentLanguageCode, unit) switch
        {
            ("zh-CN", "km") => $"{normalizedNumber} 公里",
            ("zh-CN", "m") => $"{normalizedNumber} 米",
            ("ko", "km") => $"{normalizedNumber}km",
            ("ko", "m") => $"{normalizedNumber}m",
            ("ja", "km") => $"{normalizedNumber}km",
            ("ja", "m") => $"{normalizedNumber}m",
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

        var normalizedCategory = TextEncodingHelper.NormalizeDisplayText(category);

        return NormalizeLookupKey(normalizedCategory) switch
        {
            "nuong-beer" => SelectLocalizedText(CreateLocalizedMap(
                "Nướng & Beer",
                "Grill & beer",
                "烧烤与啤酒",
                "구이와 맥주",
                "焼き物とビール",
                "Grillades & bière")),
            "oc-hai-san" => SelectLocalizedText(CreateLocalizedMap(
                "Ốc & Hải sản",
                "Snails & seafood",
                "螺类与海鲜",
                "달팽이와 해산물",
                "貝料理と海鮮",
                "Escargots & fruits de mer")),
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
            "hai-san-do-song" => SelectLocalizedText(CreateLocalizedMap(
                "Hải sản & đồ sống",
                "Seafood & raw dishes",
                "海鲜与生食",
                "해산물 & 생식",
                "海鮮・生もの",
                "Fruits de mer & plats crus")),
            "ca-phe-tra" => SelectLocalizedText(CreateLocalizedMap(
                "Cà phê & trà",
                "Coffee & tea",
                "咖啡与茶",
                "커피 & 차",
                "コーヒー&ティー",
                "Café & thé")),
            "mon-ngot" => SelectLocalizedText(CreateLocalizedMap(
                "Cà phê & trà",
                "Coffee & tea",
                "咖啡与茶",
                "커피 & 차",
                "コーヒー&ティー",
                "Café & thé")),
            _ => LocalizationFallbackPolicy.CanUseSourceLanguageText(CurrentLanguageCode)
                ? normalizedCategory
                : SelectLocalizedText(CreateLocalizedMap(
                    "Địa điểm ẩm thực",
                    "Food spot",
                    "美食地点",
                    "맛집",
                    "グルメスポット",
                    "Spot gourmand"))
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
            return TextEncodingHelper.NormalizeDisplayText(address);
        }

        var text = TextEncodingHelper.NormalizeDisplayText(address);
        var normalizedKey = NormalizeLookupKey(text);

        return normalizedKey switch
        {
            "42-vinh-khanh-phuong-khanh-hoi-tp-hcm" => SelectLocalizedText(CreateLocalizedMap(
                "42 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
                "42 Vinh Khanh, Khanh Hoi Ward, Ho Chi Minh City",
                "42 永庆街，庆会坊，胡志明市",
                "호찌민시 카인호이동 빈카인 42",
                "ホーチミン市カインホイ街区ヴィンカイン42",
                "42 Vinh Khanh, quartier Khanh Hoi, Hô-Chi-Minh-Ville")),
            "126-vinh-khanh-phuong-khanh-hoi-tp-hcm" => SelectLocalizedText(CreateLocalizedMap(
                "126 Vĩnh Khánh, Phường Khánh Hội, TP.HCM",
                "126 Vinh Khanh, Khanh Hoi Ward, Ho Chi Minh City",
                "126 永庆街，庆会坊，胡志明市",
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
                ("Đường", "街"),
                ("Hẻm", "巷"),
                ("Việt Nam", "越南"),
                ("Viet Nam", "越南"),
                ("Vietnam", "越南")
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
                ("Đường", "도로"),
                ("Hẻm", "골목"),
                ("Việt Nam", "베트남"),
                ("Viet Nam", "베트남"),
                ("Vietnam", "베트남")
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
                ("Đường", "通り"),
                ("Hẻm", "路地"),
                ("Việt Nam", "ベトナム"),
                ("Viet Nam", "ベトナム"),
                ("Vietnam", "ベトナム")
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
                ("Đường", "Street"),
                ("Hẻm", "Alley"),
                ("Việt Nam", "Vietnam"),
                ("Viet Nam", "Vietnam"),
                ("Vietnam", "Vietnam")
            }
        };

        return TextEncodingHelper.NormalizeDisplayText(ApplyReplacements(address, replacements));
    }

    private string SelectLocalizedText(IReadOnlyDictionary<string, string> values)
        => LocalizedTextHelper.GetLocalizedText(values, CurrentLanguageCode);

    private string CurrentLanguageCode
        => AppLanguage.NormalizeCode(
            string.IsNullOrWhiteSpace(_languageOverride.Value)
                ? SelectedLanguageCode
                : _languageOverride.Value);

    private static IReadOnlyDictionary<string, string> CreateLocalizedMap(
        string vietnameseText,
        string englishText,
        string simplifiedChineseText,
        string koreanText,
        string japaneseText,
        string unusedLegacyText)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = vietnameseText,
            ["en"] = englishText,
            ["zh-CN"] = simplifiedChineseText,
            ["ko"] = koreanText,
            ["ja"] = japaneseText
        };

    private static IReadOnlyDictionary<string, string> CreateFallbackTranslationMap(string fallbackText, string englishFallback)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = englishFallback
        };
        var sourceText = fallbackText?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            values["vi"] = sourceText;
        }

        return values;
    }

    private static string ApplyReplacements(string source, IEnumerable<(string From, string To)> replacements)
    {
        var result = source?.Trim() ?? string.Empty;
        foreach (var (from, to) in replacements)
        {
            result = result.Replace(
                from?.Trim() ?? string.Empty,
                to?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
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
        var source = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var normalized = source.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character == '\u0110')
            {
                builder.Append('D');
                continue;
            }

            if (character == '\u0111')
            {
                builder.Append('d');
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
