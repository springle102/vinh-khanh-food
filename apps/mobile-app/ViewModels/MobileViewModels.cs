using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Microsoft.Maui.Devices.Sensors;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Interfaces;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.ViewModels;

public sealed class SplashViewModel(
    IGuideApiService guideApiService,
    IAppSettingsService settingsService,
    ILocalizationService localizationService) : BaseViewModel
{
    public async Task<string> InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await settingsService.GetAsync();
            await localizationService.InitializeAsync();
            var mobileSettings = await guideApiService.GetMobileSettingsAsync();

            settings.ApiBaseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
                ? "https://vinh-khanh-food-tour-f2ddbxcgbabmfehr.eastasia-01.azurewebsites.net"
                : settings.ApiBaseUrl;
            settings.GeofenceRadiusMeters = mobileSettings.GeofenceRadiusMeters > 0 ? mobileSettings.GeofenceRadiusMeters : settings.GeofenceRadiusMeters;
            await settingsService.SaveAsync(settings);

            return string.IsNullOrWhiteSpace(settings.SelectedLanguage)
                ? AppRoutes.Root(AppRoutes.LanguageSelection)
                : AppRoutes.Root(AppRoutes.Home);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class LanguageSelectionViewModel(
    IGuideApiService guideApiService,
    IAppSettingsService settingsService,
    ILocalizationService localizationService) : BaseViewModel
{
    public ObservableCollection<AppLanguage> Languages { get; } = [];
    public ICommand SelectLanguageCommand => new AsyncCommand<AppLanguage>(SelectLanguageAsync);

    public async Task LoadAsync()
    {
        var settings = await settingsService.GetAsync();
        var mobileSettings = await guideApiService.GetMobileSettingsAsync();
        Languages.ReplaceRange(mobileSettings.SupportedLanguages.Select(language => new AppLanguage
        {
            Code = language.Code,
            DisplayName = language.DisplayName,
            IsPremium = language.IsPremium,
            IsSelected = string.Equals(language.Code, settings.SelectedLanguage, StringComparison.OrdinalIgnoreCase)
        }));
    }

    private async Task SelectLanguageAsync(AppLanguage? language)
    {
        if (language is null)
        {
            return;
        }

        await settingsService.SetLanguageAsync(language.Code);
        await localizationService.SetLanguageAsync(language.Code);
        await Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Home));
    }
}

public sealed class HomeViewModel(
    IGuideApiService guideApiService,
    ILocalizationService localizationService,
    IAppSettingsService settingsService) : BaseViewModel
{
    private string _searchText = string.Empty;
    private PoiSummaryModel? _heroPoi;

    public ObservableCollection<AppLanguage> Languages { get; } = [];
    public ObservableCollection<PoiSummaryModel> FeaturedPois { get; } = [];

    public PoiSummaryModel? HeroPoi
    {
        get => _heroPoi;
        set => SetProperty(ref _heroPoi, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public ICommand SelectLanguageCommand => new AsyncCommand<AppLanguage>(SelectLanguageAsync);
    public ICommand OpenMapCommand => new AsyncCommand(() => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Map)));
    public ICommand OpenListCommand => new AsyncCommand(OpenListAsync);
    public ICommand OpenSettingsCommand => new AsyncCommand(() => Shell.Current.GoToAsync(AppRoutes.Root(AppRoutes.Settings)));
    public ICommand OpenHeroCommand => new AsyncCommand(async () => await OpenPoiAsync(HeroPoi));
    public ICommand OpenFeaturedPoiCommand => new AsyncCommand<PoiSummaryModel>(OpenPoiAsync);

    public string WelcomeTitle => "WELCOME TO";
    public string WelcomeDescription => "Khám phá thiên đường ẩm thực Vĩnh Khánh với bản đồ và thuyết minh tự động.";
    public string MapLabel => "BẢN ĐỒ";
    public string ListLabel => "KHÁM PHÁ GẦN BẠN";
    public string SettingsLabel => "CÀI ĐẶT";
    public string SearchPlaceholder => "Tìm kiếm...";
    public string FeaturedSectionTitle => "Món ăn nổi bật";
    public string FeaturedActionText => "Tất cả";
    public string HeroTitle => HeroPoi?.Title ?? "Vĩnh Khánh Food Guide";
    public string HeroDescription => HeroPoi?.ShortDescription ?? "Khám phá thiên đường ốc Quận 4";

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await settingsService.GetAsync();
            await localizationService.SetLanguageAsync(settings.SelectedLanguage);

            var mobileSettings = await guideApiService.GetMobileSettingsAsync();
            Languages.ReplaceRange(mobileSettings.SupportedLanguages
                .Take(5)
                .Select(language => new AppLanguage
                {
                    Code = language.Code,
                    DisplayName = language.DisplayName,
                    IsPremium = language.IsPremium,
                    IsSelected = string.Equals(language.Code, settings.SelectedLanguage, StringComparison.OrdinalIgnoreCase)
                }));

            var pois = (await guideApiService.GetPoisAsync(settings.SelectedLanguage))
                .OrderByDescending(item => item.IsFeatured)
                .ThenBy(item => item.Title)
                .ToList();

            HeroPoi = pois.FirstOrDefault();
            FeaturedPois.ReplaceRange(pois.Take(8));
            RefreshAllBindings();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SelectLanguageAsync(AppLanguage? language)
    {
        if (language is null)
        {
            return;
        }

        await settingsService.SetLanguageAsync(language.Code);
        await localizationService.SetLanguageAsync(language.Code);
        await InitializeAsync();
    }

    private Task OpenListAsync()
    {
        var encodedSearch = Uri.EscapeDataString(SearchText ?? string.Empty);
        return Shell.Current.GoToAsync($"{AppRoutes.Root(AppRoutes.PoiList)}?search={encodedSearch}");
    }

    private static Task OpenPoiAsync(PoiSummaryModel? poi)
        => poi is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{nameof(PoiDetailPage)}?poiId={Uri.EscapeDataString(poi.Id)}");
}

public sealed class PoiListViewModel(
    IGuideApiService guideApiService,
    ILocalizationService localizationService,
    IAppSettingsService settingsService) : BaseViewModel
{
    private const string AllCategoryKey = "tat-ca";

    private readonly List<string> _availableCategories = [];
    private string _searchText = string.Empty;
    private string _selectedCategoryKey = AllCategoryKey;

    public ObservableCollection<PoiSummaryModel> Pois { get; } = [];
    public ObservableCollection<PoiSummaryModel> FilteredPois { get; } = [];
    public ObservableCollection<CategoryChipModel> Categories { get; } = [];
    public ICommand OpenPoiCommand => new AsyncCommand<PoiSummaryModel>(OpenPoiAsync);
    public ICommand SelectCategoryCommand => new AsyncCommand<CategoryChipModel>(SelectCategoryAsync);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SearchPlaceholder => localizationService.GetText("list_search");

    public async Task LoadAsync(string? initialSearch = null)
    {
        IsBusy = true;
        try
        {
            if (initialSearch is not null)
            {
                _searchText = Uri.UnescapeDataString(initialSearch);
                OnPropertyChanged(nameof(SearchText));
            }

            var settings = await settingsService.GetAsync();
            var pois = await guideApiService.GetPoisAsync(settings.SelectedLanguage, SearchText);
            Pois.ReplaceRange(pois.OrderByDescending(item => item.IsFeatured).ThenBy(item => item.Title));
            BuildCategories();
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<PoiSummaryModel> filtered = Pois;

        if (_selectedCategoryKey != AllCategoryKey)
        {
            filtered = filtered.Where(item => NormalizeKey(item.CategoryDisplay) == _selectedCategoryKey);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(item =>
                item.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.ShortDescription.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.HighlightedDishes.Any(dish => dish.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                item.CategoryDisplay.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        FilteredPois.ReplaceRange(filtered);
    }

    private void BuildCategories()
    {
        _availableCategories.Clear();
        _availableCategories.Add(AllCategoryKey);
        _availableCategories.AddRange(Pois
            .Select(item => NormalizeKey(item.CategoryDisplay))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct());

        if (!_availableCategories.Contains(_selectedCategoryKey))
        {
            _selectedCategoryKey = AllCategoryKey;
        }

        Categories.ReplaceRange(_availableCategories.Select(key => new CategoryChipModel
        {
            Key = key,
            Label = key == AllCategoryKey ? "Tất cả" : BuildCategoryLabel(key),
            IsSelected = key == _selectedCategoryKey
        }));
    }

    private Task SelectCategoryAsync(CategoryChipModel? category)
    {
        if (category is null)
        {
            return Task.CompletedTask;
        }

        _selectedCategoryKey = category.Key;
        BuildCategories();
        ApplyFilter();
        return Task.CompletedTask;
    }

    private static Task OpenPoiAsync(PoiSummaryModel? poi)
        => poi is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{nameof(PoiDetailPage)}?poiId={Uri.EscapeDataString(poi.Id)}");

    private static string BuildCategoryLabel(string key)
    {
        return key switch
        {
            "oc" => "Ốc",
            "do-nuong" => "Đồ nướng",
            "do-uong" => "Đồ uống",
            "ca-phe-tra" => "Cà phê & trà",
            "hai-san-do-song" => "Hải sản & đồ sống",
            "mon-ngot" => "Cà phê & trà",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(key.Replace('-', ' '))
        };
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}

public sealed class MapViewModel(
    IGuideApiService guideApiService,
    ILocalizationService localizationService,
    IAppSettingsService settingsService,
    ILocationTrackerService locationTrackerService,
    INarrationService narrationService) : BaseViewModel
{
    private PoiSummaryModel? _selectedPoi;
    private Location? _currentLocation;

    public ObservableCollection<PoiSummaryModel> Pois { get; } = [];
    public ObservableCollection<TourRouteModel> Routes { get; } = [];

    public PoiSummaryModel? SelectedPoi
    {
        get => _selectedPoi;
        set
        {
            if (SetProperty(ref _selectedPoi, value))
            {
                OnPropertyChanged(nameof(SelectedTitle));
                OnPropertyChanged(nameof(SelectedDescription));
            }
        }
    }

    public Location? CurrentLocation
    {
        get => _currentLocation;
        set => SetProperty(ref _currentLocation, value);
    }

    public string SelectedTitle => SelectedPoi?.Title ?? localizationService.GetText("map_select_poi");
    public string SelectedDescription => SelectedPoi?.ShortDescription ?? localizationService.GetText("map_hint");

    public ICommand OpenDetailCommand => new AsyncCommand(async () =>
    {
        if (SelectedPoi is not null)
        {
            await Shell.Current.GoToAsync($"{nameof(PoiDetailPage)}?poiId={Uri.EscapeDataString(SelectedPoi.Id)}");
        }
    });

    public ICommand OpenDirectionsCommand => new AsyncCommand(async () =>
    {
        if (SelectedPoi is null)
        {
            return;
        }

        await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(
            new Location(SelectedPoi.Latitude, SelectedPoi.Longitude),
            new Microsoft.Maui.ApplicationModel.MapLaunchOptions
            {
                Name = SelectedPoi.Title,
                NavigationMode = Microsoft.Maui.ApplicationModel.NavigationMode.Walking
            });
    });

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await settingsService.GetAsync();
            var pois = await guideApiService.GetPoisAsync(settings.SelectedLanguage);
            Pois.ReplaceRange(pois);
            Routes.ReplaceRange(await guideApiService.GetRoutesAsync(settings.SelectedLanguage));
            CurrentLocation = await locationTrackerService.GetCurrentLocationAsync();

            if (settings.AutoNarrationEnabled)
            {
                await locationTrackerService.StartAsync(Pois, settings, async poi =>
                {
                    SelectedPoi = poi;
                    var detail = await guideApiService.GetPoiByIdAsync(poi.Id, settings.SelectedLanguage);
                    if (detail is not null)
                    {
                        await narrationService.PlayAsync(detail, settings.SelectedLanguage);
                    }
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectPoiAsync(PoiSummaryModel poi)
    {
        SelectedPoi = poi;
        RefreshAllBindings();
        var settings = await settingsService.GetAsync();
        var detail = await guideApiService.GetPoiByIdAsync(poi.Id, settings.SelectedLanguage);
        if (detail is not null)
        {
            await narrationService.PlayAsync(detail, settings.SelectedLanguage);
        }
    }
}

public sealed class PoiDetailViewModel(
    IGuideApiService guideApiService,
    ILocalizationService localizationService,
    IAppSettingsService settingsService,
    INarrationService narrationService) : BaseViewModel
{
    private PoiDetailModel? _detail;

    public PoiDetailModel? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public ICommand PlayCommand => new AsyncCommand(PlayAsync);
    public ICommand PauseCommand => new AsyncCommand(narrationService.PauseAsync);
    public ICommand OpenDirectionsCommand => new AsyncCommand(async () =>
    {
        if (Detail is null)
        {
            return;
        }

        await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(
            new Location(Detail.Latitude, Detail.Longitude),
            new Microsoft.Maui.ApplicationModel.MapLaunchOptions
            {
                Name = Detail.Title,
                NavigationMode = Microsoft.Maui.ApplicationModel.NavigationMode.Walking
            });
    });

    public string PlayText => localizationService.GetText("detail_play");
    public string PauseText => localizationService.GetText("detail_pause");

    public async Task LoadByIdAsync(string? poiId, string? slug)
    {
        if (string.IsNullOrWhiteSpace(poiId) && string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var settings = await settingsService.GetAsync();
            Detail = !string.IsNullOrWhiteSpace(poiId)
                ? await guideApiService.GetPoiByIdAsync(poiId, settings.SelectedLanguage)
                : await guideApiService.GetPoiBySlugAsync(slug!, settings.SelectedLanguage);

            if (Detail is not null)
            {
                await guideApiService.TrackViewAsync(Detail.Id, new TrackViewRequest
                {
                    LanguageCode = settings.SelectedLanguage
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PlayAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var settings = await settingsService.GetAsync();
        await narrationService.PlayAsync(Detail, settings.SelectedLanguage);
    }
}

public sealed class SettingsViewModel(
    IGuideApiService guideApiService,
    IAppSettingsService settingsService,
    ILocalizationService localizationService) : BaseViewModel
{
    public ObservableCollection<AppLanguage> Languages { get; } = [];
    public ObservableCollection<VoiceOption> Voices { get; } = [];
    public UserSettings Settings { get; private set; } = new();
    public ICommand SelectLanguageCommand => new AsyncCommand<AppLanguage>(SelectLanguageAsync);

    public async Task LoadAsync()
    {
        Settings = await settingsService.GetAsync();
        var mobileSettings = await guideApiService.GetMobileSettingsAsync();
        Languages.ReplaceRange(mobileSettings.SupportedLanguages.Select(language => new AppLanguage
        {
            Code = language.Code,
            DisplayName = language.DisplayName,
            IsPremium = language.IsPremium,
            IsSelected = string.Equals(language.Code, Settings.SelectedLanguage, StringComparison.OrdinalIgnoreCase)
        }));
        Voices.ReplaceRange(BuildVoices(Settings.SelectedLanguage).Select(voice => new VoiceOption
        {
            Id = voice.Id,
            DisplayName = voice.DisplayName,
            Locale = voice.Locale,
            IsSelected = string.Equals(voice.Id, Settings.SelectedVoiceId, StringComparison.OrdinalIgnoreCase)
        }));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(SelectedVoiceName));
    }

    public async Task SaveAsync()
    {
        await settingsService.SaveAsync(Settings);
        await localizationService.SetLanguageAsync(Settings.SelectedLanguage);
    }

    public string SelectedVoiceName => Voices.FirstOrDefault(item => item.IsSelected)?.ShortName ?? "Mặc định";

    public async Task SelectVoiceAsync(VoiceOption? voiceOption)
    {
        if (voiceOption is null)
        {
            return;
        }

        Settings.SelectedVoiceId = voiceOption.Id;
        Settings.SelectedVoiceLocale = voiceOption.Locale;

        foreach (var voice in Voices)
        {
            voice.IsSelected = string.Equals(voice.Id, voiceOption.Id, StringComparison.OrdinalIgnoreCase);
        }

        await SaveAsync();
        OnPropertyChanged(nameof(SelectedVoiceName));
    }

    public async Task UpdateAutoNarrationAsync(bool isEnabled)
    {
        Settings.AutoNarrationEnabled = isEnabled;
        await SaveAsync();
    }

    public async Task UpdatePreparedAudioAsync(bool isEnabled)
    {
        Settings.PreferPreparedAudio = isEnabled;
        await SaveAsync();
    }

    private async Task SelectLanguageAsync(AppLanguage? language)
    {
        if (language is null)
        {
            return;
        }

        Settings.SelectedLanguage = language.Code;
        await SaveAsync();
        await LoadAsync();
    }

    public IReadOnlyList<VoiceOption> BuildVoices(string languageCode)
    {
        return languageCode switch
        {
            "en" => [new VoiceOption { Id = "en_default", DisplayName = "English - Default", Locale = "en-US" }],
            "zh-CN" => [new VoiceOption { Id = "zh_default", DisplayName = "中文 - 默认", Locale = "zh-CN" }],
            "ko" => [new VoiceOption { Id = "ko_default", DisplayName = "한국어 - 기본", Locale = "ko-KR" }],
            "ja" => [new VoiceOption { Id = "ja_default", DisplayName = "日本語 - 標準", Locale = "ja-JP" }],
            _ => [new VoiceOption { Id = "vi_default", DisplayName = "Tiếng Việt - Mặc định", Locale = "vi-VN" }]
        };
    }
}

