using System.Text.Json;
using Microsoft.Maui.Storage;

namespace VinhKhanh.MobileApp.Services;

public interface IAppLanguageService
{
    string CurrentLanguage { get; }
    event EventHandler? LanguageChanged;
    Task InitializeAsync();
    Task SetLanguageAsync(string languageCode);
    string GetText(string key);
}

public sealed class AppLanguageService : IAppLanguageService
{
    private const string PreferenceFileName = "vkfood.language.txt";
    private const string DefaultLanguage = "vi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuiltInTexts =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qr_success_title"] = "QUÉT QR THÀNH CÔNG!",
                ["qr_choose_language"] = "CHỌN NGÔN NGỮ CỦA BẠN",
                ["qr_continue"] = "TIẾP TỤC",
                ["login_portal_subtitle"] = "Đăng nhập / Đăng ký",
                ["login_tab"] = "ĐĂNG NHẬP",
                ["signup_tab"] = "ĐĂNG KÝ",
                ["login_identifier_placeholder"] = "Email / Số điện thoại",
                ["login_password_placeholder"] = "Mật khẩu",
                ["login_forgot_password"] = "Quên mật khẩu?",
                ["login_button"] = "ĐĂNG NHẬP",
                ["signup_button"] = "ĐĂNG KÝ",
                ["login_google"] = "Đăng nhập bằng Google",
                ["login_facebook"] = "Facebook",
                ["login_apple"] = "Apple",
                ["login_create_account"] = "Tạo tài khoản mới",
                ["home_search_placeholder"] = "Tìm quán ăn, món ăn...",
                ["home_poi_chip"] = "POI",
                ["home_layer"] = "Lớp",
                ["home_default_title"] = "Vinh Khanh Food Street",
                ["home_default_description"] = "Chạm marker hoặc dùng thanh tìm kiếm để khám phá các quán ăn nổi bật.",
                ["home_default_address"] = "Phường Khánh Hội, Quận 4, TP.HCM",
                ["tour_title"] = "LỊCH TRÌNH TOUR",
                ["tour_create"] = "Tạo Tour Mới",
                ["tour_checkpoints"] = "Checkpoint đã đi",
                ["settings_title"] = "CÀI ĐẶT",
                ["settings_account"] = "TÀI KHOẢN",
                ["settings_user_name"] = "Tên người dùng",
                ["settings_contact"] = "Email / Số điện thoại",
                ["settings_logout"] = "Đăng xuất",
                ["settings_notifications"] = "Thông báo",
                ["settings_cards"] = "Quản lý Thẻ",
                ["settings_privacy"] = "Quyền riêng tư",
                ["settings_support"] = "Hỗ trợ",
                ["bottom_qr"] = "Quét QR",
                ["bottom_settings"] = "Cài Đặt",
                ["bottom_poi"] = "Tìm POI",
                ["bottom_tour"] = "Tour Của Tôi",
                ["poi_detail_listen"] = "Nghe thuyết minh",
                ["poi_detail_directions"] = "Chỉ đường",
                ["poi_detail_save"] = "Lưu vào tour",
                ["poi_detail_saved"] = "Đã lưu",
                ["poi_detail_loading"] = "Đang tải thông tin...",
                ["poi_detail_featured"] = "Nổi bật",
                ["poi_detail_reviews"] = "đánh giá",
                ["poi_detail_no_selection"] = "Chọn một địa điểm trên bản đồ",
                ["poi_detail_address"] = "Địa chỉ"
            },
            ["en"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qr_success_title"] = "QR SCAN SUCCESSFUL!",
                ["qr_choose_language"] = "CHOOSE YOUR LANGUAGE",
                ["qr_continue"] = "CONTINUE",
                ["login_portal_subtitle"] = "Login / Sign Up",
                ["login_tab"] = "LOGIN",
                ["signup_tab"] = "SIGN UP",
                ["login_identifier_placeholder"] = "Email / Phone number",
                ["login_password_placeholder"] = "Password",
                ["login_forgot_password"] = "Forgot password?",
                ["login_button"] = "LOGIN",
                ["signup_button"] = "SIGN UP",
                ["login_google"] = "Continue with Google",
                ["login_facebook"] = "Facebook",
                ["login_apple"] = "Apple",
                ["login_create_account"] = "Create a new account",
                ["home_search_placeholder"] = "Search restaurants or dishes...",
                ["home_poi_chip"] = "POI",
                ["home_layer"] = "Layer",
                ["home_default_title"] = "Vinh Khanh Food Street",
                ["home_default_description"] = "Tap a marker or use the search bar to explore popular food spots.",
                ["home_default_address"] = "Khanh Hoi Ward, District 4, Ho Chi Minh City",
                ["tour_title"] = "TOUR ITINERARY",
                ["tour_create"] = "Create New Tour",
                ["tour_checkpoints"] = "Visited checkpoints",
                ["settings_title"] = "SETTINGS",
                ["settings_account"] = "ACCOUNT",
                ["settings_user_name"] = "User name",
                ["settings_contact"] = "Email / Phone number",
                ["settings_logout"] = "Log out",
                ["settings_notifications"] = "Notifications",
                ["settings_cards"] = "Card Management",
                ["settings_privacy"] = "Privacy",
                ["settings_support"] = "Support",
                ["bottom_qr"] = "Scan QR",
                ["bottom_settings"] = "Settings",
                ["bottom_poi"] = "Find POI",
                ["bottom_tour"] = "My Tour",
                ["poi_detail_listen"] = "Listen",
                ["poi_detail_directions"] = "Directions",
                ["poi_detail_save"] = "Save to tour",
                ["poi_detail_saved"] = "Saved",
                ["poi_detail_loading"] = "Loading details...",
                ["poi_detail_featured"] = "Featured",
                ["poi_detail_reviews"] = "reviews",
                ["poi_detail_no_selection"] = "Select a place on the map",
                ["poi_detail_address"] = "Address"
            },
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qr_success_title"] = "二维码扫描成功！",
                ["qr_choose_language"] = "选择您的语言",
                ["qr_continue"] = "继续",
                ["login_portal_subtitle"] = "登录 / 注册",
                ["login_tab"] = "登录",
                ["signup_tab"] = "注册",
                ["login_identifier_placeholder"] = "邮箱 / 电话号码",
                ["login_password_placeholder"] = "密码",
                ["login_forgot_password"] = "忘记密码？",
                ["login_button"] = "登录",
                ["signup_button"] = "注册",
                ["login_google"] = "使用 Google 登录",
                ["login_facebook"] = "Facebook",
                ["login_apple"] = "Apple",
                ["login_create_account"] = "创建新账号",
                ["home_search_placeholder"] = "搜索餐厅或菜品...",
                ["home_poi_chip"] = "POI",
                ["home_layer"] = "图层",
                ["home_default_title"] = "Vinh Khanh Food Street",
                ["home_default_description"] = "点击标记或使用搜索栏探索热门美食地点。",
                ["home_default_address"] = "胡志明市第四郡庆会坊",
                ["tour_title"] = "行程安排",
                ["tour_create"] = "创建新行程",
                ["tour_checkpoints"] = "已到访站点",
                ["settings_title"] = "设置",
                ["settings_account"] = "账户",
                ["settings_user_name"] = "用户名",
                ["settings_contact"] = "邮箱 / 电话号码",
                ["settings_logout"] = "退出登录",
                ["settings_notifications"] = "通知",
                ["settings_cards"] = "卡片管理",
                ["settings_privacy"] = "隐私",
                ["settings_support"] = "支持",
                ["bottom_qr"] = "扫码",
                ["bottom_settings"] = "设置",
                ["bottom_poi"] = "找 POI",
                ["bottom_tour"] = "我的行程",
                ["poi_detail_listen"] = "收听讲解",
                ["poi_detail_directions"] = "路线",
                ["poi_detail_save"] = "保存到行程",
                ["poi_detail_saved"] = "已保存",
                ["poi_detail_loading"] = "正在加载详情...",
                ["poi_detail_featured"] = "精选",
                ["poi_detail_reviews"] = "条评价",
                ["poi_detail_no_selection"] = "请在地图上选择地点",
                ["poi_detail_address"] = "地址"
            },
            ["ko"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qr_success_title"] = "QR 스캔 성공!",
                ["qr_choose_language"] = "언어를 선택하세요",
                ["qr_continue"] = "계속",
                ["login_portal_subtitle"] = "로그인 / 회원가입",
                ["login_tab"] = "로그인",
                ["signup_tab"] = "회원가입",
                ["login_identifier_placeholder"] = "이메일 / 전화번호",
                ["login_password_placeholder"] = "비밀번호",
                ["login_forgot_password"] = "비밀번호를 잊으셨나요?",
                ["login_button"] = "로그인",
                ["signup_button"] = "회원가입",
                ["login_google"] = "Google로 로그인",
                ["login_facebook"] = "Facebook",
                ["login_apple"] = "Apple",
                ["login_create_account"] = "새 계정 만들기",
                ["home_search_placeholder"] = "식당이나 음식을 검색하세요...",
                ["home_poi_chip"] = "POI",
                ["home_layer"] = "레이어",
                ["home_default_title"] = "Vinh Khanh Food Street",
                ["home_default_description"] = "마커를 누르거나 검색창으로 인기 맛집을 찾아보세요.",
                ["home_default_address"] = "호치민시 4군 칸호이동",
                ["tour_title"] = "투어 일정",
                ["tour_create"] = "새 투어 만들기",
                ["tour_checkpoints"] = "방문한 체크포인트",
                ["settings_title"] = "설정",
                ["settings_account"] = "계정",
                ["settings_user_name"] = "사용자 이름",
                ["settings_contact"] = "이메일 / 전화번호",
                ["settings_logout"] = "로그아웃",
                ["settings_notifications"] = "알림",
                ["settings_cards"] = "카드 관리",
                ["settings_privacy"] = "개인정보",
                ["settings_support"] = "지원",
                ["bottom_qr"] = "QR 스캔",
                ["bottom_settings"] = "설정",
                ["bottom_poi"] = "POI 찾기",
                ["bottom_tour"] = "내 투어",
                ["poi_detail_listen"] = "오디오 가이드",
                ["poi_detail_directions"] = "길찾기",
                ["poi_detail_save"] = "투어에 저장",
                ["poi_detail_saved"] = "저장됨",
                ["poi_detail_loading"] = "정보를 불러오는 중...",
                ["poi_detail_featured"] = "추천",
                ["poi_detail_reviews"] = "개 후기",
                ["poi_detail_no_selection"] = "지도에서 장소를 선택하세요",
                ["poi_detail_address"] = "주소"
            },
            ["ja"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qr_success_title"] = "QR読み取り成功！",
                ["qr_choose_language"] = "言語を選択してください",
                ["qr_continue"] = "続ける",
                ["login_portal_subtitle"] = "ログイン / 新規登録",
                ["login_tab"] = "ログイン",
                ["signup_tab"] = "登録",
                ["login_identifier_placeholder"] = "メール / 電話番号",
                ["login_password_placeholder"] = "パスワード",
                ["login_forgot_password"] = "パスワードをお忘れですか？",
                ["login_button"] = "ログイン",
                ["signup_button"] = "登録",
                ["login_google"] = "Googleでログイン",
                ["login_facebook"] = "Facebook",
                ["login_apple"] = "Apple",
                ["login_create_account"] = "新しいアカウントを作成",
                ["home_search_placeholder"] = "お店や料理を検索...",
                ["home_poi_chip"] = "POI",
                ["home_layer"] = "レイヤー",
                ["home_default_title"] = "Vinh Khanh Food Street",
                ["home_default_description"] = "マーカーや検索バーで人気の店を探せます。",
                ["home_default_address"] = "ホーチミン市4区カインホイ坊",
                ["tour_title"] = "ツアー日程",
                ["tour_create"] = "新しいツアーを作成",
                ["tour_checkpoints"] = "訪問済みチェックポイント",
                ["settings_title"] = "設定",
                ["settings_account"] = "アカウント",
                ["settings_user_name"] = "ユーザー名",
                ["settings_contact"] = "メール / 電話番号",
                ["settings_logout"] = "ログアウト",
                ["settings_notifications"] = "通知",
                ["settings_cards"] = "カード管理",
                ["settings_privacy"] = "プライバシー",
                ["settings_support"] = "サポート",
                ["bottom_qr"] = "QR",
                ["bottom_settings"] = "設定",
                ["bottom_poi"] = "POI検索",
                ["bottom_tour"] = "マイツアー",
                ["poi_detail_listen"] = "音声ガイド",
                ["poi_detail_directions"] = "経路",
                ["poi_detail_save"] = "ツアーに保存",
                ["poi_detail_saved"] = "保存済み",
                ["poi_detail_loading"] = "詳細を読み込み中...",
                ["poi_detail_featured"] = "注目",
                ["poi_detail_reviews"] = "件のレビュー",
                ["poi_detail_no_selection"] = "地図でスポットを選択してください",
                ["poi_detail_address"] = "住所"
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["qr_success_title"] = "SCAN QR RÉUSSI !",
                ["qr_choose_language"] = "CHOISISSEZ VOTRE LANGUE",
                ["qr_continue"] = "CONTINUER",
                ["login_portal_subtitle"] = "Connexion / Inscription",
                ["login_tab"] = "CONNEXION",
                ["signup_tab"] = "INSCRIPTION",
                ["login_identifier_placeholder"] = "E-mail / Téléphone",
                ["login_password_placeholder"] = "Mot de passe",
                ["login_forgot_password"] = "Mot de passe oublié ?",
                ["login_button"] = "CONNEXION",
                ["signup_button"] = "INSCRIPTION",
                ["login_google"] = "Continuer avec Google",
                ["login_facebook"] = "Facebook",
                ["login_apple"] = "Apple",
                ["login_create_account"] = "Créer un nouveau compte",
                ["home_search_placeholder"] = "Rechercher un restaurant ou un plat...",
                ["home_poi_chip"] = "POI",
                ["home_layer"] = "Calque",
                ["home_default_title"] = "Vinh Khanh Food Street",
                ["home_default_description"] = "Touchez un marqueur ou utilisez la barre de recherche pour explorer les lieux populaires.",
                ["home_default_address"] = "Quartier Khanh Hoi, District 4, Hô Chi Minh-Ville",
                ["tour_title"] = "ITINÉRAIRE DU TOUR",
                ["tour_create"] = "Créer un nouveau tour",
                ["tour_checkpoints"] = "Étapes visitées",
                ["settings_title"] = "PARAMÈTRES",
                ["settings_account"] = "COMPTE",
                ["settings_user_name"] = "Nom d'utilisateur",
                ["settings_contact"] = "E-mail / Téléphone",
                ["settings_logout"] = "Se déconnecter",
                ["settings_notifications"] = "Notifications",
                ["settings_cards"] = "Gestion des cartes",
                ["settings_privacy"] = "Confidentialité",
                ["settings_support"] = "Assistance",
                ["bottom_qr"] = "QR",
                ["bottom_settings"] = "Réglages",
                ["bottom_poi"] = "Trouver POI",
                ["bottom_tour"] = "Mon Tour",
                ["poi_detail_listen"] = "Écouter",
                ["poi_detail_directions"] = "Itinéraire",
                ["poi_detail_save"] = "Enregistrer",
                ["poi_detail_saved"] = "Enregistré",
                ["poi_detail_loading"] = "Chargement des détails...",
                ["poi_detail_featured"] = "À la une",
                ["poi_detail_reviews"] = "avis",
                ["poi_detail_no_selection"] = "Sélectionnez un lieu sur la carte",
                ["poi_detail_address"] = "Adresse"
            }
        };

    private readonly WeakEventManager _eventManager = new();
    private Dictionary<string, string> _texts = CreateBuiltInTextDictionary(DefaultLanguage);

    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    public AppLanguageService()
    {
        _texts = CreateBuiltInTextDictionary(DefaultLanguage);
    }

    public event EventHandler? LanguageChanged
    {
        add => _eventManager.AddEventHandler(value);
        remove => _eventManager.RemoveEventHandler(value);
    }

    public async Task InitializeAsync()
    {
        var languageCode = await LoadSavedLanguageAsync() ?? DefaultLanguage;
        await SetLanguageAsync(languageCode);
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        var normalizedCode = NormalizeLanguageCode(languageCode);
        var mergedTexts = CreateBuiltInTextDictionary(normalizedCode);

        var fileDictionary = await LoadFromFileAsync(normalizedCode);
        foreach (var pair in fileDictionary)
        {
            mergedTexts[pair.Key] = pair.Value;
        }

        if (mergedTexts.Count == 0 && !string.Equals(normalizedCode, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            await SetLanguageAsync(DefaultLanguage);
            return;
        }

        CurrentLanguage = normalizedCode;
        _texts = mergedTexts;
        await SaveLanguageAsync(normalizedCode);
        _eventManager.HandleEvent(this, EventArgs.Empty, nameof(LanguageChanged));
    }

    public string GetText(string key)
        => _texts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : key;

    private static async Task<Dictionary<string, string>> LoadFromFileAsync(string languageCode)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"Localization/{languageCode}.json");
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> CreateBuiltInTextDictionary(string languageCode)
    {
        var normalizedCode = NormalizeLanguageCode(languageCode);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!BuiltInTexts.TryGetValue(normalizedCode, out var builtIn))
        {
            return texts;
        }

        foreach (var pair in builtIn)
        {
            texts[pair.Key] = pair.Value;
        }

        return texts;
    }

    private static async Task<string?> LoadSavedLanguageAsync()
    {
        try
        {
            var path = GetPreferenceFilePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var value = (await File.ReadAllTextAsync(path)).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveLanguageAsync(string languageCode)
    {
        try
        {
            var path = GetPreferenceFilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, languageCode);
        }
        catch
        {
            // Best effort persistence only.
        }
    }

    private static string GetPreferenceFilePath()
        => Path.Combine(FileSystem.Current.AppDataDirectory, PreferenceFileName);
    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguage;
        }

        return languageCode.Trim() switch
        {
            "zh" => "zh-CN",
            "fr-FR" => "fr",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            _ => languageCode.Trim()
        };
    }
}
