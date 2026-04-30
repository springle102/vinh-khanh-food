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
            "ca-phe-che" => CreateLocalizedMap(
                "Cà phê Chè",
                "Tea Coffee",
                "茶咖啡",
                "티 커피",
                "ティーコーヒー",
                "Café Thé"),
            "oc-loan" => CreateLocalizedMap(
                "Nhà hàng Ốc Loan",
                "Oc Loan Restaurant",
                "Ốc Loan 餐厅",
                "옥 로안 레스토랑",
                "Ốc Loan レストラン",
                "Restaurant Oc Loan"),
            "oc-oanh-1" => CreateLocalizedMap(
                "Ốc Oanh 1",
                "Oc Oanh 1",
                "Ốc Oanh 1",
                "옥 오아인 1",
                "Ốc Oanh 1",
                "Oc Oanh 1"),
            "oc-phat" => CreateLocalizedMap(
                "Ốc Phát",
                "Oc Phat",
                "Ốc Phát",
                "옥 팟",
                "Ốc Phát",
                "Oc Phat"),
            "sushi-ko" => CreateLocalizedMap(
                "Nhà Hàng Sushi Ko",
                "Sushi Ko Restaurant",
                "Sushi Ko 餐厅",
                "스시 코 레스토랑",
                "寿司Ko レストラン",
                "Restaurant Sushi Ko"),
            _ => CreateFallbackTranslationMap(fallbackTitle, "Vinh Khanh food spot")
        };
    }

    private IReadOnlyDictionary<string, string> GetFallbackPoiSummaryTranslations(string poiId, string fallbackSummary)
    {
        return poiId switch
        {
            "ca-phe-che" => CreateLocalizedMap(
                "Quán tráng miệng và cà phê nhẹ nhàng, phù hợp nghỉ chân sau khi ăn hải sản.",
                "A gentle dessert and coffee stop, good for resting after seafood.",
                "一家轻松的甜品和咖啡店，适合吃完海鲜后休息。",
                "해산물 식사 후 쉬어 가기 좋은 디저트와 커피 장소입니다.",
                "海鮮料理のあとに休憩しやすい、穏やかなデザートとコーヒーの店です。",
                "Une halte douce pour dessert et café après les fruits de mer."),
            "oc-loan" => CreateLocalizedMap(
                "Hải sản tươi, ốc to và nêm nếm đậm đà theo khẩu vị miền Nam.",
                "Fresh seafood, plump snails, and bold southern-style seasoning.",
                "海鲜新鲜，螺类饱满，调味浓郁并带有南部风味。",
                "신선한 해산물과 통통한 달팽이 요리, 남부식 진한 양념이 특징입니다.",
                "新鮮な海鮮、大ぶりの貝、南部らしい濃い味付けが魅力です。",
                "Fruits de mer frais, coquillages généreux et assaisonnement du Sud bien marqué."),
            "oc-oanh-1" => CreateLocalizedMap(
                "Quán ốc quen thuộc trên tuyến Vĩnh Khánh, nổi bật với sò huyết rang me và sò điệp nướng mỡ hành.",
                "A familiar Vinh Khanh snail stop, known for tamarind blood cockles and scallops with scallion oil.",
                "永庆街上一家熟悉的螺类餐馆，以罗望子炒血蚶和葱油烤扇贝出名。",
                "빈카인 거리의 익숙한 달팽이 맛집으로, 타마린드 꼬막과 파기름 가리비가 유명합니다.",
                "ヴィンカイン通りの定番貝料理店で、タマリンド味の赤貝とネギ油焼きホタテが人気です。",
                "Une adresse de coquillages familière de Vinh Khanh, connue pour les coques au tamarin et les coquilles Saint-Jacques à l'huile d'oignon vert."),
            "oc-phat" => CreateLocalizedMap(
                "Quán ốc đầu phố với thực đơn hải sản đa dạng, phù hợp nhóm bạn và khách lần đầu đến Vĩnh Khánh.",
                "A street-entry snail restaurant with varied seafood, good for groups and first-time visitors.",
                "街口的螺类海鲜店，菜单多样，适合朋友聚会和第一次到访的游客。",
                "거리 초입의 달팽이·해산물 식당으로, 친구 모임과 첫 방문객에게 잘 어울립니다.",
                "通りの入口にある貝料理店で、海鮮メニューが幅広く、グループや初訪問に向いています。",
                "Une adresse de coquillages en début de rue, variée et pratique pour les groupes comme pour une première visite."),
            "sushi-ko" => CreateLocalizedMap(
                "Điểm dừng món Nhật gần phố ẩm thực Vĩnh Khánh, nổi bật với sushi và hải sản chế biến tươi.",
                "A Japanese food stop near Vinh Khanh food street, known for sushi and fresh seafood dishes.",
                "永庆美食街附近的日式餐厅，以寿司和新鲜海鲜料理为特色。",
                "빈카인 음식 거리 근처의 일본식 식당으로, 스시와 신선한 해산물 요리가 돋보입니다.",
                "ヴィンカインの美食街近くにある和食店で、寿司と新鮮な海鮮料理が魅力です。",
                "Une halte japonaise près de la rue gourmande Vinh Khanh, réputée pour les sushis et les fruits de mer frais."),
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
            "ca-phe-che" => CreateFallbackTranslationMap(
                "Chào mừng bạn đến với Cà phê Chè. Sau những món ốc và hải sản đậm vị, đây là nơi phù hợp để nghỉ chân bằng một ly cà phê hoặc chén chè mát. Không gian nhẹ nhàng, dễ ngồi trò chuyện và rất hợp để kết thúc hành trình ẩm thực Vĩnh Khánh bằng một vị ngọt dễ chịu.",
                "Welcome to Tea Coffee. After rich snail and seafood dishes, this is a calm place to rest with coffee or sweet soup."),
            "oc-loan" => CreateFallbackTranslationMap(
                "Điểm cộng lớn nhất của Ốc Loan là hải sản luôn tươi rói, các loại ốc to, mập mạp và được nêm nếm rất đậm đà, thiên một chút xíu về khẩu vị ngọt đặc trưng của miền Nam. Nếu có dịp ghé quán, bạn nhất định nên gọi thử những món quen thuộc nhưng cực kỳ \"bắt miệng\" như: sò huyết xào me chua ngọt sền sệt, ốc hương xào bơ tỏi thơm lừng chấm kèm bánh mì, hay một phần sụn gà chiên nước mắm giòn rụm để nhâm nhi.",
                "Oc Loan is known for fresh seafood, plump snails, and rich southern-style seasoning."),
            "oc-oanh-1" => CreateFallbackTranslationMap(
                "Bạn đang dừng chân tại Ốc Oanh 1, một tọa độ hải sản quen thuộc trên tuyến Vĩnh Khánh. Quầy ốc phía trước giúp bạn nhìn thấy nguyên liệu trước khi gọi món. Nếu thích vị chua ngọt, hãy chọn sò huyết rang me. Nếu thích mùi thơm béo, sò điệp nướng mỡ hành là lựa chọn rất dễ nhớ.",
                "Oc Oanh 1 is a familiar Vinh Khanh seafood stop where you can see the snails before ordering."),
            "oc-phat" => CreateFallbackTranslationMap(
                "Bạn đang ở Ốc Phát, một trong những điểm dừng dễ tiếp cận nhất khi bắt đầu khám phá phố ẩm thực Vĩnh Khánh. Hãy thử các món ốc hương rang muối, nghêu hấp Thái hoặc ốc móng tay xào bơ tỏi. Điểm hấp dẫn của quán là không khí tụ tập rộn ràng, món lên nhanh và hương hải sản lan rất rõ trong buổi tối Quận 4.",
                "Oc Phat is an easy first stop on Vinh Khanh food street, with lively seafood dishes for groups."),
            "sushi-ko" => CreateFallbackTranslationMap(
                "Chào mừng bạn đến với Nhà Hàng Sushi Ko. Đây là điểm dừng dành cho những ai yêu thích hương vị Nhật Bản giữa khu phố ẩm thực Vĩnh Khánh. Quán nổi bật với sushi, mì soba lạnh và các món cá nướng được chuẩn bị kỹ lưỡng. Hãy dành ít phút cảm nhận sự đối lập thú vị giữa nhịp phố sôi động bên ngoài và phong cách ẩm thực tinh tế bên trong.",
                "Sushi Ko is a Japanese food stop near Vinh Khanh, known for sushi, cold soba, and carefully prepared grilled fish."),
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
            "hai-san-mon-nhat" => SelectLocalizedText(CreateLocalizedMap(
                "Hải sản & món Nhật",
                "Seafood & Japanese food",
                "海鲜与日式料理",
                "해산물 & 일본 요리",
                "海鮮と和食",
                "Fruits de mer & cuisine japonaise")),
            "ca-phe-tra" => SelectLocalizedText(CreateLocalizedMap(
                "Cà phê & trà",
                "Coffee & tea",
                "咖啡与茶",
                "커피 & 차",
                "コーヒー&ティー",
                "Café & thé")),
            "ca-phe-trang-mieng" => SelectLocalizedText(CreateLocalizedMap(
                "Cà phê & tráng miệng",
                "Coffee & dessert",
                "咖啡与甜点",
                "커피 & 디저트",
                "コーヒー&デザート",
                "Café & dessert")),
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
        if (!string.IsNullOrWhiteSpace(poiId))
        {
            var localizedAddress = poiId switch
            {
                "ca-phe-che" => SelectLocalizedText(CreateLocalizedMap(
                    "Chung cư H1, Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM",
                    "H1 Apartment, Hoang Dieu, Khanh Hoi Ward, District 4, Ho Chi Minh City",
                    "胡志明市第四郡庆会坊黄耀街 H1 公寓",
                    "호찌민시 4군 카인호이동 호앙디에우 H1 아파트",
                    "ホーチミン市4区カインホイ街区ホアンジエウ H1アパート",
                    "Appartement H1, Hoang Dieu, quartier Khanh Hoi, district 4, Hô-Chi-Minh-Ville")),
                "oc-loan" => SelectLocalizedText(CreateLocalizedMap(
                    "Nhà Hàng Ốc Loan, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam",
                    "Oc Loan Restaurant, Vinh Khanh, Khanh Hoi Ward, District 4, Ho Chi Minh City, Vietnam",
                    "越南胡志明市第四郡庆会坊永庆街 Ốc Loan 餐厅",
                    "베트남 호찌민시 4군 카인호이동 빈카인 옥 로안 레스토랑",
                    "ベトナム・ホーチミン市4区カインホイ街区ヴィンカイン Ốc Loan レストラン",
                    "Restaurant Oc Loan, Vinh Khanh, quartier Khanh Hoi, district 4, Hô-Chi-Minh-Ville, Vietnam")),
                "oc-oanh-1" => SelectLocalizedText(CreateLocalizedMap(
                    "534 hẻm 42/237 Hoàng Diệu, Phường Khánh Hội, Quận 4, TP.HCM",
                    "534 alley 42/237 Hoang Dieu, Khanh Hoi Ward, District 4, Ho Chi Minh City",
                    "胡志明市第四郡庆会坊黄耀街 42/237 巷 534 号",
                    "호찌민시 4군 카인호이동 호앙디에우 42/237 골목 534",
                    "ホーチミン市4区カインホイ街区ホアンジエウ42/237路地534",
                    "534 ruelle 42/237 Hoang Dieu, quartier Khanh Hoi, district 4, Hô-Chi-Minh-Ville")),
                "oc-phat" => SelectLocalizedText(CreateLocalizedMap(
                    "Ốc Phát, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam",
                    "Oc Phat, Vinh Khanh, Khanh Hoi Ward, District 4, Ho Chi Minh City, Vietnam",
                    "越南胡志明市第四郡庆会坊永庆街 Ốc Phát",
                    "베트남 호찌민시 4군 카인호이동 빈카인 옥 팟",
                    "ベトナム・ホーチミン市4区カインホイ街区ヴィンカイン Ốc Phát",
                    "Oc Phat, Vinh Khanh, quartier Khanh Hoi, district 4, Hô-Chi-Minh-Ville, Vietnam")),
                "sushi-ko" => SelectLocalizedText(CreateLocalizedMap(
                    "122/37/15 Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM",
                    "122/37/15 Vinh Khanh, Khanh Hoi Ward, District 4, Ho Chi Minh City",
                    "胡志明市第四郡庆会坊永庆街 122/37/15",
                    "호찌민시 4군 카인호이동 빈카인 122/37/15",
                    "ホーチミン市4区カインホイ街区ヴィンカイン122/37/15",
                    "122/37/15 Vinh Khanh, quartier Khanh Hoi, district 4, Hô-Chi-Minh-Ville")),
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(localizedAddress))
            {
                return localizedAddress;
            }
        }

        return ApplyAddressLocalization(text);
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
