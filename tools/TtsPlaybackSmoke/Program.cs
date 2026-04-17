using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VinhKhanh.BackendApi.Infrastructure;

var failures = new List<string>();

void AssertEqual<T>(string name, T actual, T expected)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        failures.Add($"{name}: expected '{expected}' but got '{actual}'");
    }
}

void AssertContains(string name, string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
    {
        failures.Add($"{name}: expected content to contain '{expected}'");
    }
}

void AssertDoesNotContain(string name, string value, string forbidden)
{
    if (value.Contains(forbidden, StringComparison.Ordinal))
    {
        failures.Add($"{name}: content must not contain '{forbidden}'");
    }
}

var handler = new FakeElevenLabsHandler();
using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://api.elevenlabs.io/")
};
using var cache = new MemoryCache(new MemoryCacheOptions());
var service = new ElevenLabsTextToSpeechService(
    httpClient,
    Options.Create(new TextToSpeechOptions
    {
        ApiKey = "test-api-key",
        DefaultVoiceId = "voice-test",
        ModelId = TextToSpeechOptions.DefaultModelIdValue,
        OutputFormat = TextToSpeechOptions.DefaultOutputFormatValue,
        CacheDurationMinutes = 5
    }),
    cache,
    NullLogger<ElevenLabsTextToSpeechService>.Instance);

var first = await service.GenerateAudioAsync(
    new TextToSpeechRequest("Hello from Japan", "ja-JP"),
    CancellationToken.None);
var second = await service.GenerateAudioAsync(
    new TextToSpeechRequest("Hello from Japan", "ja-JP"),
    CancellationToken.None);
var callCountAfterSecondRequest = handler.CallCount;
var third = await service.GenerateAudioAsync(
    new TextToSpeechRequest("Hello from Korea", "ko"),
    CancellationToken.None);
var callCountAfterThirdRequest = handler.CallCount;
var longText = string.Join(
    ' ',
    Enumerable.Repeat(
        "This is a long multilingual narration block designed to force the backend service to split the request into multiple ElevenLabs segments without losing any words.",
        18));
var callCountBeforeLongRequest = handler.CallCount;
var longResult = await service.GenerateAudioAsync(
    new TextToSpeechRequest(longText, "en-US"),
    CancellationToken.None);
var callCountAfterLongRequest = handler.CallCount;

AssertEqual("first call returns audio content type", first.ContentType, "audio/mpeg");
AssertEqual("second identical call uses backend TTS cache", callCountAfterSecondRequest, 1);
AssertEqual("different language/text bypasses backend TTS cache", callCountAfterThirdRequest, 2);
AssertEqual("cached response keeps content", Convert.ToHexString(second.Content), Convert.ToHexString(first.Content));
AssertEqual("new response has different test bytes", Convert.ToHexString(third.Content) == Convert.ToHexString(first.Content), false);
AssertEqual("long request is split into multiple provider calls", callCountAfterLongRequest > callCountBeforeLongRequest, true);

var expectedLongBytes = Enumerable
    .Range(callCountBeforeLongRequest + 1, callCountAfterLongRequest - callCountBeforeLongRequest)
    .SelectMany(index => new byte[] { 1, 2, 3, (byte)index })
    .ToArray();
AssertEqual("long request concatenates every audio chunk in order", Convert.ToHexString(longResult.Content), Convert.ToHexString(expectedLongBytes));

foreach (var payloadJson in handler.Payloads.Skip(callCountBeforeLongRequest))
{
    using var payload = JsonDocument.Parse(payloadJson);
    var payloadText = payload.RootElement.GetProperty("text").GetString() ?? string.Empty;
    AssertEqual("long request chunk text length stays under provider limit", payloadText.Length <= 900, true);
}

using (var payload = JsonDocument.Parse(handler.Payloads[0]))
{
    AssertEqual(
        "low latency TTS model is sent to ElevenLabs",
        payload.RootElement.GetProperty("model_id").GetString(),
        TextToSpeechOptions.DefaultModelIdValue);
    AssertEqual(
        "Japanese locale is normalized for ElevenLabs",
        payload.RootElement.GetProperty("language_code").GetString(),
        "ja");
}

var poiPlaybackSource = File.ReadAllText(FindRepoFile(Path.Combine(
    "apps",
    "admin-web",
    "src",
    "features",
    "pois",
    "usePoiNarrationPlayback.ts")));
var mediaPreviewSource = File.ReadAllText(FindRepoFile(Path.Combine(
    "apps",
    "admin-web",
    "src",
    "features",
    "media",
    "useNarrationPreview.ts")));
var adminNarrationSource = File.ReadAllText(FindRepoFile(Path.Combine(
    "apps",
    "admin-web",
    "src",
    "lib",
    "narration.ts")));
var mobileNarrationSource = File.ReadAllText(FindRepoFile(Path.Combine(
    "apps",
    "mobile-app",
    "Services",
    "PoiExperienceServices.cs")));

AssertDoesNotContain(
    "POI playback must not prefetch every TTS chunk before playing",
    poiPlaybackSource,
    "fetchTtsPlaybackUrls(");
AssertDoesNotContain(
    "media preview must not prefetch every TTS chunk before playing",
    mediaPreviewSource,
    "fetchTtsPlaybackUrls(");
AssertContains(
    "POI playback prepares TTS chunks through the blob playback queue",
    poiPlaybackSource,
    "createTtsPlaybackQueue");
AssertContains(
    "media preview prepares TTS chunks through the blob playback queue",
    mediaPreviewSource,
    "createTtsPlaybackQueue");
AssertContains(
    "POI playback falls back to browser speech when web audio cannot play",
    poiPlaybackSource,
    "playBrowserSpeechFallback");
AssertContains(
    "media preview falls back to browser speech when web audio cannot play",
    mediaPreviewSource,
    "playBrowserSpeechPreview");
AssertContains(
    "admin TTS URLs request the low latency model",
    adminNarrationSource,
    "model_id: TTS_PROXY_MODEL_ID");
AssertContains(
    "mobile narration requests backend TTS through POST",
    mobileNarrationSource,
    "new HttpRequestMessage(HttpMethod.Post, requestUri)");
AssertContains(
    "mobile backend TTS has a bounded wait before falling back",
    mobileNarrationSource,
    "TextToSpeechProxyRequestTimeout");
AssertDoesNotContain(
    "mobile narration must not split TTS text on the client anymore",
    mobileNarrationSource,
    "SplitNarrationIntoChunks(");
AssertDoesNotContain(
    "mobile narration must not override the backend model_id anymore",
    mobileNarrationSource,
    "[\"model_id\"] = TextToSpeechProxyModelId");

if (failures.Count > 0)
{
    Console.Error.WriteLine("TtsPlaybackSmoke failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.Exit(1);
}

Console.WriteLine("TtsPlaybackSmoke passed.");

string FindRepoFile(string relativePath)
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        current = current.Parent;
    }

    throw new FileNotFoundException($"Could not find repo file '{relativePath}'.");
}

sealed class FakeElevenLabsHandler : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public List<string> Payloads { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount += 1;
        Payloads.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, (byte)CallCount])
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        return response;
    }
}
