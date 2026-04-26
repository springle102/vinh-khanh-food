using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;

namespace VinhKhanh.MobileApp.Services;

public interface IAppLanguageService
{
    string CurrentLanguage { get; }
    CultureInfo CurrentCulture { get; }
    bool HasSavedLanguageSelection { get; }
    IReadOnlyList<AppLanguageDefinition> SupportedLanguages { get; }
    event EventHandler? LanguageChanged;
    Task InitializeAsync();
    // ✅ FIX: Return the actually-applied language code
    Task<string> SetLanguageAsync(string languageCode);
    string GetText(string key);
    AppLanguageDefinition GetLanguageDefinition(string? languageCode);
    // ✅ NEW: Method to validate if current selection is still allowed (for premium checking)
    Task<bool> ValidateCurrentLanguageAsync();
    // ✅ NEW: Method to restore to default if current language becomes unavailable
    Task<string> RestoreToAllowedLanguageAsync();
}

public sealed class AppLanguageService : IAppLanguageService
{
    private const string PreferenceKey = "vkfood.language.code";

    // ✅ FIX: Proper UTF-8 encoding for CJK (Trung, Hàn, Nhật)
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ✅ NEW: Cache ALL language files in memory for instant switching (thread-safe)
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> LanguageFileCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> SeedEnglishTexts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_title"] = "Vinh Khanh Food Street",
            ["brand_title"] = "Vinh Khanh\nFood Street",
            ["language_selection_title"] = "Choose your language",
            ["language_selection_subtitle"] = "Select a language before signing in",
            ["language_status_ready"] = "Ready to use",
            ["language_status_premium"] = "Included in Premium",
            ["language_status_locked"] = "Upgrade to Premium to use this language",
            ["login_portal_subtitle"] = "Login / Sign Up",
            ["login_tab"] = "Login",
            ["signup_tab"] = "Sign Up",
            ["login_identifier_placeholder"] = "Username / Email / Phone number",
            ["signup_name_placeholder"] = "Full name",
            ["signup_username_placeholder"] = "Username",
            ["signup_email_placeholder"] = "Email",
            ["signup_phone_placeholder"] = "Phone number",
            ["login_password_placeholder"] = "Password",
            ["signup_confirm_password_placeholder"] = "Confirm password",
            ["login_forgot_password"] = "Forgot password?",
            ["login_identifier_not_found"] = "We couldn't find a customer profile matching that identifier.",
            ["login_validation_required"] = "Please enter your username, email, or phone number and password.",
            ["login_invalid_credentials"] = "The username, email, phone number, or password is incorrect.",
            ["login_backend_unavailable"] = "The app could not reach the backend to verify this customer profile. On a real phone APK, do not use localhost; point ApiBaseUrl to the computer running the backend on the same network.",
            ["login_button"] = "Login",
            ["signup_button"] = "Sign Up",
            ["signup_success_message"] = "Your account has been created successfully.",
            ["signup_validation_name"] = "Please enter your full name.",
            ["signup_validation_username"] = "Username must be at least 3 characters and use only letters, numbers, dots, hyphens, or underscores.",
            ["signup_validation_email"] = "Please enter a valid email address.",
            ["signup_validation_phone"] = "Please enter a valid phone number.",
            ["signup_validation_password"] = "Password must contain at least 6 characters.",
            ["signup_validation_confirm_password"] = "Password confirmation does not match. Please re-enter it.",
            ["login_google"] = "Continue with Google",
            ["login_facebook"] = "Facebook",
            ["login_apple"] = "Apple",
            ["login_create_account"] = "Create a new account",
            ["home_search_placeholder"] = "Search restaurants or dishes...",
            ["home_poi_chip"] = "POI",
            ["home_layer"] = "Layer",
            ["tour_map_entry_action"] = "Tour routes",
            ["home_default_title"] = "Vinh Khanh Food Street",
            ["home_default_description"] = "Tap a marker or use the search bar to explore popular food spots.",
            ["home_default_address"] = "Khanh Hoi Ward, District 4, Ho Chi Minh City",
            ["user_location_title"] = "User location",
            ["user_location_coordinates_label"] = "Coordinates",
            ["user_location_status_label"] = "Status",
            ["user_location_status_idle"] = "Not inside the 30 m range of any POI",
            ["user_location_status_near_poi"] = "Near {0}",
            ["user_location_nearest_poi_label"] = "Nearest POI",
            ["user_location_nearest_distance_label"] = "Nearest distance",
            ["user_location_no_nearest_poi"] = "No nearby POI",
            ["user_location_distance_unknown"] = "Unknown",
            ["user_location_source_label"] = "Source",
            ["user_location_source_gps"] = "GPS",
            ["user_location_source_mock"] = "Test mode",
            ["user_location_source_manual"] = "Manual simulate",
            ["user_location_source_auto"] = "Auto simulate",
            ["virtual_location_title"] = "Virtual user",
            ["virtual_location_coordinates_label"] = "Coordinates",
            ["virtual_location_status_label"] = "Status",
            ["virtual_location_status_idle"] = "Not inside the 10 m range of any POI",
            ["virtual_location_status_near_poi"] = "Near {0}",
            ["virtual_location_nearest_poi_label"] = "Nearest POI",
            ["virtual_location_nearest_distance_label"] = "Nearest distance",
            ["virtual_location_no_nearest_poi"] = "No nearby POI",
            ["virtual_location_distance_unknown"] = "Unknown",
            ["tour_title"] = "Tour itinerary",
            ["tour_create"] = "Create New Tour",
            ["tour_checkpoints"] = "Visited checkpoints",
            ["tour_discover_title"] = "Explore tours",
            ["tour_discover_subtitle"] = "Published routes from the admin team appear here without locking your map.",
            ["tour_discover_subtitle_resume"] = "You still have an active tour saved. You can preview another route or come back any time.",
            ["tour_discover_page_title"] = "Explore tours",
            ["tour_discover_page_subtitle"] = "Browse published tours, review the route details, and send any tour back to the map when you are ready.",
            ["tour_discover_active_title"] = "Saved progress",
            ["tour_discover_active_subtitle"] = "Your current tour is still saved. You can continue it on the map or open My Tour for checkpoint details.",
            ["tour_discover_section_title"] = "Available tours",
            ["tour_discover_highlights"] = "Route highlights",
            ["tour_discover_empty_title"] = "No tours available yet",
            ["tour_discover_empty_subtitle"] = "Published tours from the Super Admin will appear here as soon as they are ready.",
            ["tour_discover_action_view_map"] = "View on map",
            ["tour_discover_action_continue"] = "Continue tour",
            ["tour_banner_preview"] = "Previewing tour {0}",
            ["tour_banner_active"] = "Following tour {0}",
            ["tour_action_view_all_places"] = "View all places",
            ["tour_action_change_tour"] = "Change tour",
            ["tour_action_start"] = "Start tour",
            ["tour_action_pause"] = "Pause tour",
            ["tour_action_exit_mode"] = "Exit tour mode",
            ["tour_action_open_my_tour"] = "My Tour",
            ["tour_action_resume_on_map"] = "Resume on map",
            ["tour_action_continue_short"] = "Continue",
            ["tour_status_preview"] = "Preview",
            ["tour_status_active_visible"] = "Active now",
            ["tour_status_active_saved"] = "Saved progress",
            ["tour_meta_stops"] = "{0} stops",
            ["tour_mode_preview_short"] = "View",
            ["tour_mode_active_short"] = "Go",
            ["tour_mode_free_short"] = "Map",
            ["settings_title"] = "Settings",
            ["settings_account"] = "Account",
            ["settings_language_title"] = "Language",
            ["settings_auto_narration_title"] = "Auto-play narration near a POI",
            ["settings_auto_narration_description"] = "When a route simulation is active on the map, the app auto-plays narration for POIs on the route and within 30 meters.",
            ["settings_public_mode_description"] = "The app opens straight to the POI map, with no sign-in and no customer account required.",
            ["settings_more_title"] = "More information",
            ["settings_full_name"] = "Full name",
            ["settings_user_name"] = "User name",
            ["settings_contact"] = "Email / Phone number",
            ["settings_profile_edit"] = "Edit profile",
            ["settings_profile_save"] = "Save changes",
            ["settings_profile_cancel"] = "Cancel",
            ["settings_profile_saved"] = "Your profile has been updated.",
            ["settings_profile_name_placeholder"] = "Enter your full name",
            ["settings_profile_username_placeholder"] = "Enter your username",
            ["settings_profile_email_placeholder"] = "Enter your email",
            ["settings_profile_phone_placeholder"] = "Enter your phone number",
            ["settings_premium_title"] = "Premium",
            ["settings_premium_status_active"] = "Premium active",
            ["settings_premium_status_free"] = "Free plan",
            ["settings_premium_description_free"] = "Unlock Chinese, Korean, and Japanese with Premium.",
            ["settings_premium_description_active"] = "Your account can already use all supported languages.",
            ["settings_premium_language_list_title"] = "Premium languages",
            ["settings_premium_price_label"] = "Current price",
            ["settings_premium_price_value"] = "{0} USD",
            ["settings_premium_buy_button"] = "Buy Premium for {0} USD",
            ["settings_logout"] = "Log out",
            ["settings_notifications"] = "Notifications",
            ["settings_cards"] = "Card Management",
            ["settings_privacy"] = "Privacy",
            ["settings_support"] = "Support",
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
            ["poi_detail_address"] = "Address",
            ["poi_detail_price_range"] = "Price range",
            ["poi_detail_food_items"] = "Dishes",
            ["poi_detail_promotions"] = "Promotions",
            ["poi_detail_opening_hours"] = "Opening hours",
            ["poi_detail_tags"] = "Tags",
            ["poi_detail_no_food_items"] = "No dishes are available for this place yet.",
            ["poi_detail_no_promotions"] = "No promotions are available right now.",
            ["auto_narration_dev_title"] = "Location test mode",
            ["auto_narration_dev_description"] = "Use this on the emulator or in development to simulate that the user is standing near the selected POI.",
            ["auto_narration_dev_action"] = "Simulate nearby",
            ["simulation_panel_title"] = "Route simulation",
            ["simulation_mode_manual"] = "Drag manually",
            ["simulation_mode_auto"] = "Auto run",
            ["simulation_action_start"] = "Start",
            ["simulation_action_pause"] = "Pause",
            ["simulation_action_stop"] = "Stop",
            ["simulation_action_reset"] = "Reset position",
            ["simulation_destination_none"] = "Choose a POI destination",
            ["simulation_destination_format"] = "Destination: {0}",
            ["simulation_status_idle"] = "Waiting for a destination",
            ["simulation_status_ready"] = "Route ready",
            ["simulation_status_running"] = "Simulating movement",
            ["simulation_status_paused"] = "Simulation paused",
            ["simulation_status_completed"] = "Arrived at the destination",
            ["simulation_route_loading"] = "Calculating the route...",
            ["simulation_select_destination_hint"] = "Pick a POI and tap Directions to draw a route from the blue marker.",
            ["simulation_build_route_hint"] = "Tap Directions to calculate the route from the blue marker to {0}.",
            ["simulation_valid_pois_count"] = "{0} valid POIs",
            ["simulation_route_summary"] = "{0} • {1} • {2}",
            ["simulation_duration_minutes"] = "{0} min",
            ["simulation_duration_hours"] = "{0} hr {1} min",
            ["poi_simulation_title"] = "Navigate from the blue marker",
            ["poi_simulation_manual_hint"] = "Drag or tap the blue marker anywhere on the map to place the user.",
            ["poi_simulation_override_hint"] = "A route to {0} is active. Tap Directions to switch to {1}.",
            ["poi_detail_status_active"] = "Active",
            ["poi_detail_status_upcoming"] = "Upcoming",
            ["poi_detail_status_expired"] = "Expired",
            ["poi_detail_status_info"] = "Info",
            ["poi_detail_period_from"] = "From",
            ["poi_detail_period_until"] = "Until",
            ["common_ok"] = "OK",
            ["common_cancel"] = "Cancel",
            ["common_continue"] = "Continue",
            ["premium_badge"] = "Premium",
            ["premium_upgrade_required_title"] = "Premium required",
            ["premium_upgrade_required_message"] = "{0} requires Premium. Upgrade now for {1} USD?",
            ["premium_purchase_title"] = "Premium purchase",
            ["premium_purchase_confirm"] = "Confirm demo payment of {0} USD to activate Premium for this account?",
            ["premium_purchase_success"] = "Premium is now active. Charged {0} USD.",
            ["premium_login_required"] = "Please sign in to a customer account before purchasing Premium.",
            ["premium_purchase_error"] = "We could not activate Premium. Please try again.",
            ["premium_checkout_title"] = "Premium checkout",
            ["premium_checkout_subtitle"] = "Complete payment before Premium is activated.",
            ["premium_checkout_summary_title"] = "Order summary",
            ["premium_checkout_methods_title"] = "Payment method",
            ["premium_checkout_note"] = "Demo payment only. No real money will be charged in this environment.",
            ["premium_checkout_card_section"] = "Card information",
            ["premium_checkout_cardholder"] = "Cardholder name",
            ["premium_checkout_card_number"] = "Card number",
            ["premium_checkout_card_expiry"] = "Expiry date",
            ["premium_checkout_card_cvv"] = "CVV",
            ["premium_checkout_wallet_section"] = "E-wallet information",
            ["premium_checkout_wallet_provider"] = "Wallet provider",
            ["premium_checkout_wallet_account"] = "Wallet account",
            ["premium_checkout_wallet_pin"] = "Verification PIN",
            ["premium_checkout_submit_button"] = "Pay {0} USD",
            ["premium_checkout_cardholder_placeholder"] = "e.g. Nguyen Bao Vy",
            ["premium_checkout_card_number_placeholder"] = "4242 4242 4242 4242",
            ["premium_checkout_expiry_month_placeholder"] = "MM",
            ["premium_checkout_expiry_year_placeholder"] = "YYYY",
            ["premium_checkout_card_cvv_placeholder"] = "123",
            ["premium_checkout_wallet_account_placeholder"] = "Phone number or email",
            ["premium_checkout_wallet_pin_placeholder"] = "4-6 digits",
            ["premium_checkout_wallet_momo"] = "MoMo",
            ["premium_checkout_wallet_zalopay"] = "ZaloPay",
            ["premium_checkout_method_card"] = "Bank card",
            ["premium_checkout_method_card_description"] = "Pay with a local or international bank card.",
            ["premium_checkout_method_wallet"] = "E-wallet",
            ["premium_checkout_method_wallet_description"] = "Pay with MoMo or ZaloPay in the demo flow.",
            ["premium_checkout_success_message"] = "Premium upgrade successful",
            ["premium_checkout_already_active"] = "This account is already using Premium.",
            ["premium_checkout_validation_cardholder"] = "Please enter the cardholder name.",
            ["premium_checkout_validation_card_number"] = "Please enter a valid card number.",
            ["premium_checkout_validation_expiry"] = "Please enter a valid expiry date.",
            ["premium_checkout_validation_cvv"] = "Please enter a valid CVV.",
            ["premium_checkout_validation_wallet_account"] = "Please enter the wallet account.",
            ["premium_checkout_validation_wallet_pin"] = "Please enter a valid wallet PIN."
        };

    private static readonly IReadOnlyDictionary<string, string> EmptySeedTexts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SupplementalTextsByLanguage =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = CreateSeedTextMap(
                ("settings_public_mode_description", "Ứng dụng mở trực tiếp vào bản đồ POI, không cần đăng nhập và không lưu tài khoản khách hàng."),
                ("settings_more_title", "Thông tin thêm")),
            ["ko"] = CreateSeedTextMap(
                ("settings_public_mode_description", "앱은 바로 POI 지도로 열리며 로그인이나 고객 계정이 필요하지 않습니다."),
                ("settings_more_title", "추가 정보"),
                ("tour_map_entry_action", "투어 경로"),
                ("settings_auto_narration_title", "POI 근처에서 자동 오디오 안내 재생"),
                ("settings_auto_narration_description", "지도에서 경로 시뮬레이션이 활성화되면 앱이 경로 위에 있고 30m 이내인 POI의 안내를 자동으로 재생합니다."),
                ("user_location_title", "사용자 위치"),
                ("user_location_coordinates_label", "좌표"),
                ("user_location_status_label", "상태"),
                ("user_location_status_idle", "어느 POI의 30m 범위 안에도 있지 않습니다"),
                ("user_location_status_near_poi", "{0} 근처"),
                ("user_location_nearest_poi_label", "가장 가까운 POI"),
                ("user_location_nearest_distance_label", "가장 가까운 거리"),
                ("user_location_no_nearest_poi", "근처 POI 없음"),
                ("user_location_distance_unknown", "알 수 없음"),
                ("user_location_source_label", "위치 소스"),
                ("user_location_source_manual", "수동 시뮬레이션"),
                ("user_location_source_auto", "자동 시뮬레이션"),
                ("auto_narration_dev_title", "위치 테스트 모드"),
                ("auto_narration_dev_description", "에뮬레이터나 개발 환경에서 사용자가 선택한 POI 근처에 있는 상황을 시뮬레이션합니다."),
                ("auto_narration_dev_action", "근처로 시뮬레이션"),
                ("simulation_panel_title", "경로 시뮬레이션"),
                ("simulation_mode_manual", "수동 드래그"),
                ("simulation_mode_auto", "자동 실행"),
                ("simulation_action_start", "시작"),
                ("simulation_action_pause", "일시 정지"),
                ("simulation_action_stop", "중지"),
                ("simulation_action_reset", "위치 초기화"),
                ("simulation_destination_none", "목적지 POI를 선택하세요"),
                ("simulation_destination_format", "목적지: {0}"),
                ("simulation_status_idle", "목적지를 기다리는 중"),
                ("simulation_status_ready", "경로 준비 완료"),
                ("simulation_status_running", "이동 시뮬레이션 중"),
                ("simulation_status_paused", "시뮬레이션 일시 정지됨"),
                ("simulation_status_completed", "목적지에 도착했습니다"),
                ("simulation_route_loading", "경로를 계산하는 중..."),
                ("simulation_select_destination_hint", "POI를 선택하고 길찾기를 눌러 파란 마커에서 경로를 그리세요."),
                ("simulation_build_route_hint", "길찾기를 눌러 파란 마커에서 {0}(으)로 가는 경로를 계산하세요."),
                ("simulation_valid_pois_count", "{0}개의 유효한 POI"),
                ("simulation_route_summary", "{0} • {1} • {2}"),
                ("simulation_duration_minutes", "{0}분"),
                ("simulation_duration_hours", "{0}시간 {1}분"),
                ("poi_simulation_title", "파란 마커에서 이동"),
                ("poi_simulation_manual_hint", "지도의 파란 마커를 드래그하거나 탭해 사용자 위치를 놓으세요."),
                ("poi_simulation_override_hint", "{0}까지의 경로가 활성화되어 있습니다. 길찾기를 눌러 {1}(으)로 전환하세요."),
                ("tour_discover_title", "투어 탐색"),
                ("tour_discover_subtitle", "관리팀이 게시한 경로가 지도를 잠그지 않고 여기에 표시됩니다."),
                ("tour_discover_subtitle_resume", "저장된 활성 투어가 아직 있습니다. 다른 경로를 미리 보거나 언제든 다시 돌아올 수 있습니다."),
                ("tour_discover_page_title", "투어 탐색"),
                ("tour_discover_page_subtitle", "게시된 투어를 살펴보고 경로 세부 정보를 확인한 뒤 준비되면 지도로 다시 보낼 수 있습니다."),
                ("tour_discover_active_title", "저장된 진행 상황"),
                ("tour_discover_active_subtitle", "현재 투어가 그대로 저장되어 있습니다. 지도에서 계속하거나 My Tour에서 체크포인트를 확인할 수 있습니다."),
                ("tour_discover_section_title", "이용 가능한 투어"),
                ("tour_discover_highlights", "경로 하이라이트"),
                ("tour_discover_empty_title", "아직 이용 가능한 투어가 없습니다"),
                ("tour_discover_empty_subtitle", "Super Admin이 투어를 게시하면 여기에 표시됩니다."),
                ("tour_discover_action_view_map", "지도에서 보기"),
                ("tour_discover_action_continue", "투어 계속"),
                ("tour_banner_preview", "투어 {0} 미리보기 중"),
                ("tour_banner_active", "투어 {0} 진행 중"),
                ("tour_action_view_all_places", "모든 장소 보기"),
                ("tour_action_change_tour", "투어 변경"),
                ("tour_action_start", "투어 시작"),
                ("tour_action_pause", "투어 일시 정지"),
                ("tour_action_exit_mode", "투어 모드 종료"),
                ("tour_action_open_my_tour", "내 투어"),
                ("tour_action_resume_on_map", "지도에서 계속"),
                ("tour_action_continue_short", "계속"),
                ("tour_status_preview", "미리보기"),
                ("tour_status_active_visible", "현재 진행 중"),
                ("tour_status_active_saved", "진행 상황 저장됨"),
                ("tour_meta_stops", "{0}개 정류장"),
                ("tour_mode_preview_short", "보기"),
                ("tour_mode_active_short", "이동"),
                ("tour_mode_free_short", "지도")),
            ["zh-CN"] = CreateSeedTextMap(
                ("settings_public_mode_description", "应用会直接打开 POI 地图，无需登录，也不需要客户账号。"),
                ("settings_more_title", "更多信息"),
                ("tour_map_entry_action", "游览路线"),
                ("settings_auto_narration_title", "靠近 POI 时自动播放讲解"),
                ("settings_auto_narration_description", "当地图上的路线模拟处于活动状态时，应用会自动播放位于路线中且 30 米范围内的 POI 讲解。"),
                ("user_location_title", "用户位置"),
                ("user_location_coordinates_label", "坐标"),
                ("user_location_status_label", "状态"),
                ("user_location_status_idle", "当前不在任何 POI 的 30 米范围内"),
                ("user_location_status_near_poi", "靠近 {0}"),
                ("user_location_nearest_poi_label", "最近的 POI"),
                ("user_location_nearest_distance_label", "最近距离"),
                ("user_location_no_nearest_poi", "附近暂无 POI"),
                ("user_location_distance_unknown", "未知"),
                ("user_location_source_label", "来源"),
                ("user_location_source_manual", "手动模拟"),
                ("user_location_source_auto", "自动模拟"),
                ("auto_narration_dev_title", "位置测试模式"),
                ("auto_narration_dev_description", "在模拟器或开发环境中，可用它模拟用户位于所选 POI 附近。"),
                ("auto_narration_dev_action", "模拟靠近"),
                ("simulation_panel_title", "路线模拟"),
                ("simulation_mode_manual", "手动拖动"),
                ("simulation_mode_auto", "自动运行"),
                ("simulation_action_start", "开始"),
                ("simulation_action_pause", "暂停"),
                ("simulation_action_stop", "停止"),
                ("simulation_action_reset", "重置位置"),
                ("simulation_destination_none", "请选择一个 POI 作为目的地"),
                ("simulation_destination_format", "目的地：{0}"),
                ("simulation_status_idle", "等待选择目的地"),
                ("simulation_status_ready", "路线已准备好"),
                ("simulation_status_running", "正在模拟移动"),
                ("simulation_status_paused", "模拟已暂停"),
                ("simulation_status_completed", "已到达目的地"),
                ("simulation_route_loading", "正在计算路线..."),
                ("simulation_select_destination_hint", "选择一个 POI，然后点击路线，从蓝色标记绘制路线。"),
                ("simulation_build_route_hint", "点击路线，计算从蓝色标记到 {0} 的路线。"),
                ("simulation_valid_pois_count", "{0} 个有效 POI"),
                ("simulation_route_summary", "{0} • {1} • {2}"),
                ("simulation_duration_minutes", "{0} 分钟"),
                ("simulation_duration_hours", "{0} 小时 {1} 分钟"),
                ("poi_simulation_title", "从蓝色标记开始导航"),
                ("poi_simulation_manual_hint", "拖动或点击地图上的蓝色标记来放置用户位置。"),
                ("poi_simulation_override_hint", "当前已有前往 {0} 的路线。点击路线可切换到 {1}。"),
                ("tour_discover_title", "探索游览路线"),
                ("tour_discover_subtitle", "管理团队发布的路线会显示在这里，而不会锁定您的地图。"),
                ("tour_discover_subtitle_resume", "您仍有一个已保存的进行中路线。您可以预览其他路线，或随时返回继续。"),
                ("tour_discover_page_title", "探索游览路线"),
                ("tour_discover_page_subtitle", "浏览已发布的路线，查看路线详情，并在准备好时将路线重新带回地图。"),
                ("tour_discover_active_title", "已保存的进度"),
                ("tour_discover_active_subtitle", "您当前的路线仍然保留。您可以在地图上继续，或前往 My Tour 查看检查点详情。"),
                ("tour_discover_section_title", "可用路线"),
                ("tour_discover_highlights", "路线亮点"),
                ("tour_discover_empty_title", "目前还没有可用路线"),
                ("tour_discover_empty_subtitle", "当 Super Admin 发布路线后，它们会显示在这里。"),
                ("tour_discover_action_view_map", "在地图上查看"),
                ("tour_discover_action_continue", "继续路线"),
                ("tour_banner_preview", "正在预览路线 {0}"),
                ("tour_banner_active", "正在跟随路线 {0}"),
                ("tour_action_view_all_places", "查看全部地点"),
                ("tour_action_change_tour", "更换路线"),
                ("tour_action_start", "开始路线"),
                ("tour_action_pause", "暂停路线"),
                ("tour_action_exit_mode", "退出路线模式"),
                ("tour_action_open_my_tour", "我的路线"),
                ("tour_action_resume_on_map", "在地图上继续"),
                ("tour_action_continue_short", "继续"),
                ("tour_status_preview", "预览"),
                ("tour_status_active_visible", "进行中"),
                ("tour_status_active_saved", "进度已保存"),
                ("tour_meta_stops", "{0} 个站点"),
                ("tour_mode_preview_short", "看"),
                ("tour_mode_active_short", "走"),
                ("tour_mode_free_short", "地图")),
            ["ja"] = CreateSeedTextMap(
                ("settings_public_mode_description", "アプリはそのまま POI マップを開き、ログインや利用者アカウントは不要です。"),
                ("settings_more_title", "追加情報"),
                ("tour_map_entry_action", "ツアールート"),
                ("settings_auto_narration_title", "POI の近くで自動音声ガイドを再生"),
                ("settings_auto_narration_description", "地図でルートシミュレーションが有効なとき、ルート上かつ 30m 以内にある POI の音声ガイドを自動再生します。"),
                ("user_location_title", "ユーザー位置"),
                ("user_location_coordinates_label", "座標"),
                ("user_location_status_label", "状態"),
                ("user_location_status_idle", "どの POI の 30m 範囲内にも入っていません"),
                ("user_location_status_near_poi", "{0} の近く"),
                ("user_location_nearest_poi_label", "最寄りの POI"),
                ("user_location_nearest_distance_label", "最短距離"),
                ("user_location_no_nearest_poi", "近くに POI がありません"),
                ("user_location_distance_unknown", "不明"),
                ("user_location_source_label", "位置ソース"),
                ("user_location_source_manual", "手動シミュレーション"),
                ("user_location_source_auto", "自動シミュレーション"),
                ("auto_narration_dev_title", "位置テストモード"),
                ("auto_narration_dev_description", "エミュレーターや開発環境で、ユーザーが選択した POI の近くにいる状態をシミュレーションします。"),
                ("auto_narration_dev_action", "近くをシミュレート"),
                ("simulation_panel_title", "ルートシミュレーション"),
                ("simulation_mode_manual", "手動操作"),
                ("simulation_mode_auto", "自動実行"),
                ("simulation_action_start", "開始"),
                ("simulation_action_pause", "一時停止"),
                ("simulation_action_stop", "停止"),
                ("simulation_action_reset", "位置をリセット"),
                ("simulation_destination_none", "目的地の POI を選択してください"),
                ("simulation_destination_format", "目的地: {0}"),
                ("simulation_status_idle", "目的地を待機中"),
                ("simulation_status_ready", "ルートの準備ができました"),
                ("simulation_status_running", "移動をシミュレーション中"),
                ("simulation_status_paused", "シミュレーションを一時停止しました"),
                ("simulation_status_completed", "目的地に到着しました"),
                ("simulation_route_loading", "ルートを計算しています..."),
                ("simulation_select_destination_hint", "POI を選び、ルートを押して青いマーカーから経路を描きます。"),
                ("simulation_build_route_hint", "ルートを押して、青いマーカーから {0} までの経路を計算します。"),
                ("simulation_valid_pois_count", "{0} 件の有効な POI"),
                ("simulation_route_summary", "{0} • {1} • {2}"),
                ("simulation_duration_minutes", "{0} 分"),
                ("simulation_duration_hours", "{0} 時間 {1} 分"),
                ("poi_simulation_title", "青いマーカーから移動"),
                ("poi_simulation_manual_hint", "地図上の青いマーカーをドラッグまたはタップして、ユーザー位置を置いてください。"),
                ("poi_simulation_override_hint", "{0} へのルートが有効です。ルートを押して {1} に切り替えてください。"),
                ("tour_discover_title", "ツアーを探す"),
                ("tour_discover_subtitle", "管理チームが公開したルートが、地図をロックせずにここに表示されます。"),
                ("tour_discover_subtitle_resume", "保存された進行中のツアーがあります。別のルートをプレビューすることも、いつでも戻ることもできます。"),
                ("tour_discover_page_title", "ツアーを探す"),
                ("tour_discover_page_subtitle", "公開されたツアーを見てルート詳細を確認し、準備ができたら地図へ戻せます。"),
                ("tour_discover_active_title", "保存された進行状況"),
                ("tour_discover_active_subtitle", "現在のツアーは保存されたままです。地図で続行するか、My Tour でチェックポイントを確認できます。"),
                ("tour_discover_section_title", "利用可能なツアー"),
                ("tour_discover_highlights", "ルートの見どころ"),
                ("tour_discover_empty_title", "利用可能なツアーはまだありません"),
                ("tour_discover_empty_subtitle", "Super Admin がツアーを公開すると、ここに表示されます。"),
                ("tour_discover_action_view_map", "地図で見る"),
                ("tour_discover_action_continue", "ツアーを続ける"),
                ("tour_banner_preview", "ツアー {0} をプレビュー中"),
                ("tour_banner_active", "ツアー {0} を進行中"),
                ("tour_action_view_all_places", "すべての場所を見る"),
                ("tour_action_change_tour", "ツアーを変更"),
                ("tour_action_start", "ツアーを開始"),
                ("tour_action_pause", "ツアーを一時停止"),
                ("tour_action_exit_mode", "ツアーモードを終了"),
                ("tour_action_open_my_tour", "マイツアー"),
                ("tour_action_resume_on_map", "地図で続ける"),
                ("tour_action_continue_short", "続ける"),
                ("tour_status_preview", "プレビュー"),
                ("tour_status_active_visible", "進行中"),
                ("tour_status_active_saved", "進行状況を保存済み"),
                ("tour_meta_stops", "{0} か所"),
                ("tour_mode_preview_short", "見る"),
                ("tour_mode_active_short", "移動"),
                ("tour_mode_free_short", "地図"))
        };

    private readonly WeakEventManager _eventManager = new();
    private readonly ILogger<AppLanguageService>? _logger;
    private readonly HashSet<string> _missingKeyLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fallbackKeyLogs = new(StringComparer.OrdinalIgnoreCase);
    // ✅ FIX: Add locking mechanism to prevent race conditions
    private readonly SemaphoreSlim _languageChangeLock = new(1, 1);
    private Dictionary<string, string> _fallbackTexts = new(SeedEnglishTexts, StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _currentTexts = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public AppLanguageService(ILogger<AppLanguageService>? logger = null)
    {
        _logger = logger;
        // ✅ FIX: Initialize from preferences, not from hardcoded default
        var savedLanguage = Preferences.Default.Get(PreferenceKey, AppLanguage.DefaultLanguage);
        CurrentLanguage = AppLanguage.NormalizeCode(savedLanguage);
        CurrentCulture = AppLanguage.CreateCulture(CurrentLanguage);
        HasSavedLanguageSelection = Preferences.Default.ContainsKey(PreferenceKey);
        _logger?.LogInformation(
            "[Language] Loaded saved language preference. saved={SavedLanguage}; normalized={NormalizedLanguage}; hasSavedSelection={HasSavedSelection}",
            savedLanguage,
            CurrentLanguage,
            HasSavedLanguageSelection);
    }

    public string CurrentLanguage { get; private set; }

    public CultureInfo CurrentCulture { get; private set; }

    public bool HasSavedLanguageSelection { get; private set; }

    public IReadOnlyList<AppLanguageDefinition> SupportedLanguages => AppLanguage.SupportedLanguages;

    public event EventHandler? LanguageChanged
    {
        add => _eventManager.AddEventHandler(value);
        remove => _eventManager.RemoveEventHandler(value);
    }

    // ✅ FIX: Add source of truth tracking to prevent concurrent language changes
    private string _languageChangeToken = Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        _logger?.LogInformation(
            "[Language] App language initialization started. current={CurrentLanguage}; hasSavedSelection={HasSavedSelection}",
            CurrentLanguage,
            HasSavedLanguageSelection);
        try
        {
            await ApplyLanguageAsync(CurrentLanguage, persistSelection: false);
        }
        finally
        {
            _initialized = true;
        }
    }

    // ✅ FIX: Ensure only one language change happens at a time and return the actual language set
    public async Task<string> SetLanguageAsync(string languageCode)
    {
        var normalizedCode = AppLanguage.NormalizeCode(languageCode);
        _logger?.LogInformation(
            "[Language] Language change requested. requested={RequestedLanguage}; normalized={NormalizedLanguage}; current={CurrentLanguage}",
            languageCode,
            normalizedCode,
            CurrentLanguage);
        await ApplyLanguageAsync(normalizedCode, persistSelection: true);
        return normalizedCode;
    }

    public string GetText(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_currentTexts.TryGetValue(key, out var currentValue) && !string.IsNullOrWhiteSpace(currentValue))
        {
            // ✅ FIX: Don't normalize! Return as-is (already correct from JSON)
            return TextEncodingHelper.NormalizeDisplayText(currentValue);
        }

        if (_fallbackTexts.TryGetValue(key, out var fallbackValue) && !string.IsNullOrWhiteSpace(fallbackValue))
        {
            LogFallbackKey(key);
            // ✅ FIX: Don't normalize! Return as-is
            return TextEncodingHelper.NormalizeDisplayText(fallbackValue);
        }

        LogMissingKey(key);
        return key;
    }

    public AppLanguageDefinition GetLanguageDefinition(string? languageCode)
        => AppLanguage.GetDefinition(languageCode);

    // ✅ NEW: Validate if current language is still available
    public async Task<bool> ValidateCurrentLanguageAsync()
    {
        // This method is called to check if the current language is still valid
        // (e.g., after premium status changes). Returns true if valid.
        // For now, we assume all supported languages are valid as long as they're defined
        var definition = AppLanguage.SupportedLanguages.FirstOrDefault(x =>
            string.Equals(x.Code, CurrentLanguage, StringComparison.OrdinalIgnoreCase));
        return definition != null;
    }

    // ✅ NEW: Restore to an allowed language if current becomes unavailable
    public async Task<string> RestoreToAllowedLanguageAsync()
    {
        // Check if current language is still available
        var isValid = await ValidateCurrentLanguageAsync();
        if (isValid)
        {
            return CurrentLanguage;
        }

        // Fallback order: Check if current language exists in supported list
        // If not, try fallback language, then default language
        var supportedLanguages = AppLanguage.SupportedLanguages;

        var fallbackCandidate = supportedLanguages.FirstOrDefault(x =>
            string.Equals(x.Code, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase));

        var defaultCandidate = supportedLanguages.FirstOrDefault(x =>
            string.Equals(x.Code, AppLanguage.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            ?? supportedLanguages.FirstOrDefault();

        var restoredLanguage = fallbackCandidate ?? defaultCandidate;
        if (restoredLanguage != null)
        {
            _logger?.LogWarning("Current language '{CurrentLanguage}' is no longer available. Restoring to '{RestoredLanguage}'.",
                CurrentLanguage, restoredLanguage.Code);
            await SetLanguageAsync(restoredLanguage.Code);
            return restoredLanguage.Code;
        }

        // Last resort
        _logger?.LogError("No available language found. Using default '{DefaultLanguage}'.", AppLanguage.DefaultLanguage);
        await SetLanguageAsync(AppLanguage.DefaultLanguage);
        return AppLanguage.DefaultLanguage;
    }

    private async Task ApplyLanguageAsync(string languageCode, bool persistSelection)
    {
        // ✅ FIX: Prevent concurrent language changes with semaphore lock
        await _languageChangeLock.WaitAsync();
        try
        {
            var normalizedCode = AppLanguage.NormalizeCode(languageCode);
            var previousLanguage = CurrentLanguage;

            // Skip if same language and already initialized (avoid redundant reloads)
            if (string.Equals(normalizedCode, CurrentLanguage, StringComparison.OrdinalIgnoreCase) && _initialized)
            {
                _logger?.LogDebug(
                    "[Language] Skipping language apply because the requested language is already active. language={LanguageCode}",
                    normalizedCode);
                return;
            }

            // ✅ FIX: Generate new token to invalidate old requests
            var newToken = Guid.NewGuid().ToString();
            _languageChangeToken = newToken;

            // ✅ OPTIMIZATION: Load en + current language PARALLEL for speed
            var fallbackTask = LoadFromFileAsync(AppLanguage.FallbackLanguage);
            var currentTask = !string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase)
                ? LoadFromFileAsync(normalizedCode)
                : Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            await Task.WhenAll(fallbackTask, currentTask);

            var nextFallbackTexts = new Dictionary<string, string>(SeedEnglishTexts, StringComparer.OrdinalIgnoreCase);
            MergeTexts(nextFallbackTexts, GetSupplementalTexts(AppLanguage.FallbackLanguage));
            MergeTexts(nextFallbackTexts, await fallbackTask);

            var nextCurrentTexts = string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>(nextFallbackTexts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase))
            {
                MergeTexts(nextCurrentTexts, GetSupplementalTexts(normalizedCode));
                MergeTexts(nextCurrentTexts, await currentTask);
            }

            if (nextCurrentTexts.Count == 0 && !string.Equals(normalizedCode, AppLanguage.FallbackLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogWarning("No translation file found for language '{LanguageCode}'. UI will use English fallback.", normalizedCode);
            }

            _fallbackTexts = nextFallbackTexts;
            _currentTexts = nextCurrentTexts;
            _missingKeyLogs.Clear();
            _fallbackKeyLogs.Clear();

            CurrentLanguage = normalizedCode;
            CurrentCulture = AppLanguage.CreateCulture(normalizedCode);
            ApplyCurrentCulture(CurrentCulture);

            if (persistSelection)
            {
                // ✅ FIX: Always persist to UserSettings via Preferences
                Preferences.Default.Set(PreferenceKey, normalizedCode);
                HasSavedLanguageSelection = true;
                _logger?.LogInformation(
                    "[Language] Language applied and persisted. previous={PreviousLanguage}; current={LanguageCode}",
                    previousLanguage,
                    normalizedCode);
            }
            else
            {
                HasSavedLanguageSelection = Preferences.Default.ContainsKey(PreferenceKey);
                _logger?.LogInformation(
                    "[Language] Language initialized from startup/bootstrap. current={LanguageCode}; hasSavedSelection={HasSavedSelection}",
                    normalizedCode,
                    HasSavedLanguageSelection);
            }

            // ✅ FIX: Only raise event if token hasn't changed (prevents old requests from firing events)
            if (newToken == _languageChangeToken)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    _eventManager.HandleEvent(this, EventArgs.Empty, nameof(LanguageChanged)));
            }
        }
        finally
        {
            _languageChangeLock.Release();
        }
    }

    private void LogMissingKey(string key)
    {
        var token = $"{CurrentLanguage}:{key}";
        if (_missingKeyLogs.Add(token))
        {
            _logger?.LogWarning("Missing translation key '{Key}' for language '{LanguageCode}'.", key, CurrentLanguage);
        }
    }

    private void LogFallbackKey(string key)
    {
        if (!_initialized)
        {
            return;
        }

        var token = $"{CurrentLanguage}:{key}";
        if (_fallbackKeyLogs.Add(token))
        {
            _logger?.LogInformation(
                "Using English fallback for translation key '{Key}' in language '{LanguageCode}'.",
                key,
                CurrentLanguage);
        }
    }

    private static async Task<Dictionary<string, string>> LoadFromFileAsync(string languageCode)
    {
        // ✅ FIX: Check cache first for instant loading
        if (LanguageFileCache.TryGetValue(languageCode, out var cached))
        {
            return new Dictionary<string, string>(cached, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"Localization/{languageCode}.json");
            // ✅ FIX: Force UTF-8 encoding for CJK characters
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync();

            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ✅ FIX: Store in cache - don't normalize, JSON is already correct!
            var result = values.ToDictionary(
                pair => pair.Key,
                pair => TextEncodingHelper.NormalizeDisplayText(pair.Value),
                StringComparer.OrdinalIgnoreCase);

            // ✅ NEW: Cache for instant future access (thread-safe with ConcurrentDictionary)
            LanguageFileCache.TryAdd(languageCode, result);

            return result;
        }
        catch
        {
            // Silently return empty dict if file not found (e.g., language not yet available)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void MergeTexts(Dictionary<string, string> destination, IReadOnlyDictionary<string, string> source)
    {
        if (source == null)
        {
            return;
        }

        foreach (var pair in source)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                // ✅ FIX: Don't normalize! Text is already correct from source
                destination[pair.Key] = TextEncodingHelper.NormalizeDisplayText(pair.Value);
            }
        }
    }

    private static void ApplyCurrentCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    // ✅ Helper: Create a readonly dictionary from tuples for seed texts
    private static IReadOnlyDictionary<string, string> CreateSeedTextMap(params (string key, string value)[] items)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in items)
        {
            dict[key] = value;
        }
        return dict;
    }

    // ✅ Helper: Get supplemental language-specific texts
    private static IReadOnlyDictionary<string, string> GetSupplementalTexts(string languageCode)
    {
        if (SupplementalTextsByLanguage.TryGetValue(languageCode, out var texts))
        {
            return texts;
        }
        return EmptySeedTexts;
    }
}

