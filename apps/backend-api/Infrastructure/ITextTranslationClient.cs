using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public interface ITextTranslationClient
{
    Task<TextTranslationResponse> TranslateAsync(
        TextTranslationRequest request,
        CancellationToken cancellationToken);
}
