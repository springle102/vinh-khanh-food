using System.Net;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class GoogleTranslateTtsProxyService(HttpClient httpClient, ILogger<GoogleTranslateTtsProxyService> logger)
{
    public async Task<(byte[] Content, string ContentType)> FetchAudioAsync(
        string text,
        string languageCode,
        int? segmentIndex,
        int? totalSegments,
        CancellationToken cancellationToken)
    {
        var normalizedText = text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new InvalidOperationException("Text lÃ  báº¯t buá»™c Ä‘á»ƒ táº¡o audio TTS.");
        }

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        var requestUri =
            "https://translate.google.com/translate_tts?" +
            string.Join(
                "&",
                new Dictionary<string, string>
                {
                    ["ie"] = "UTF-8",
                    ["client"] = "tw-ob",
                    ["tl"] = normalizedLanguageCode,
                    ["q"] = normalizedText,
                    ["ttsspeed"] = "1",
                }.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"));

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseSnippet = await SafeReadBodyAsync(response, cancellationToken);
            logger.LogWarning(
                "Google TTS proxy failed. Status={StatusCode}; language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}; response={ResponseSnippet}",
                response.StatusCode,
                normalizedLanguageCode,
                segmentIndex,
                totalSegments,
                responseSnippet);
            throw new InvalidOperationException("KhÃ´ng thá»ƒ táº¡o audio tá»« Google Translate TTS.");
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (content.Length == 0)
        {
            logger.LogWarning(
                "Google TTS proxy returned an empty response. language={LanguageCode}; segment={SegmentIndex}/{TotalSegments}",
                normalizedLanguageCode,
                segmentIndex,
                totalSegments);
            throw new InvalidOperationException("Google Translate TTS khÃ´ng tráº£ vá» dá»¯ liá»‡u audio.");
        }

        return (content, string.IsNullOrWhiteSpace(contentType) ? "audio/mpeg" : contentType);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var decoded = WebUtility.HtmlDecode(content).Trim();
            return decoded.Length <= 200 ? decoded : decoded[..200];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeLanguageCode(string? languageCode)
        => languageCode?.Trim() switch
        {
            "zh" => "zh-CN",
            "fr-FR" => "fr",
            "en-US" => "en",
            "ja-JP" => "ja",
            "ko-KR" => "ko",
            _ => string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim()
        };
}
