namespace VinhKhanh.BackendApi.Infrastructure;

public interface IPremiumPaymentProcessor
{
    PremiumPaymentResult Process(PremiumPaymentChargeRequest request);
}

public sealed class MockPremiumPaymentProcessor(
    ILogger<MockPremiumPaymentProcessor> logger) : IPremiumPaymentProcessor
{
    public PremiumPaymentResult Process(PremiumPaymentChargeRequest request)
    {
        var processedAt = DateTimeOffset.UtcNow;

        if (string.Equals(request.PaymentMethod, PremiumPaymentMethodCatalog.BankCard, StringComparison.OrdinalIgnoreCase))
        {
            var cardNumber = request.CardNumber ?? string.Empty;
            if (cardNumber.EndsWith("0002", StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "Mock premium payment declined. customerId={CustomerId}, method={PaymentMethod}, maskedAccount={MaskedAccount}, amountUsd={AmountUsd}, reason=insufficient-funds",
                    request.CustomerId,
                    request.PaymentMethod,
                    request.MaskedAccount,
                    request.AmountUsd);
                return PremiumPaymentResult.Fail("Thanh toan bi tu choi. The hien khong du han muc.", processedAt);
            }

            if (cardNumber.EndsWith("0003", StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "Mock premium payment gateway error. customerId={CustomerId}, method={PaymentMethod}, maskedAccount={MaskedAccount}, amountUsd={AmountUsd}, reason=gateway-timeout",
                    request.CustomerId,
                    request.PaymentMethod,
                    request.MaskedAccount,
                    request.AmountUsd);
                return PremiumPaymentResult.Fail("Cong thanh toan tam thoi gian doan. Vui long thu lai sau.", processedAt);
            }
        }
        else if (string.Equals(request.PaymentMethod, PremiumPaymentMethodCatalog.EWallet, StringComparison.OrdinalIgnoreCase))
        {
            if ((request.WalletAccount ?? string.Empty).Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.WalletPin, "000000", StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "Mock premium e-wallet payment failed. customerId={CustomerId}, method={PaymentMethod}, maskedAccount={MaskedAccount}, amountUsd={AmountUsd}",
                    request.CustomerId,
                    request.PaymentMethod,
                    request.MaskedAccount,
                    request.AmountUsd);
                return PremiumPaymentResult.Fail("Vi dien tu tu choi giao dich. Vui long kiem tra tai khoan va thu lai.", processedAt);
            }
        }

        var providerTransactionId = $"mockpay-{Guid.NewGuid():N}";
        logger.LogInformation(
            "Mock premium payment succeeded. customerId={CustomerId}, method={PaymentMethod}, maskedAccount={MaskedAccount}, amountUsd={AmountUsd}, providerTransactionId={ProviderTransactionId}",
            request.CustomerId,
            request.PaymentMethod,
            request.MaskedAccount,
            request.AmountUsd,
            providerTransactionId);

        return PremiumPaymentResult.Success(providerTransactionId, processedAt);
    }
}

public static class PremiumPaymentMethodCatalog
{
    public const string BankCard = "bank_card";
    public const string EWallet = "e_wallet";

    public static bool IsSupported(string? value)
        => string.Equals(value, BankCard, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, EWallet, StringComparison.OrdinalIgnoreCase);

    public static bool RequiresCardDetails(string? value)
        => string.Equals(value, BankCard, StringComparison.OrdinalIgnoreCase);

    public static bool RequiresWalletDetails(string? value)
        => string.Equals(value, EWallet, StringComparison.OrdinalIgnoreCase);
}

public sealed record PremiumPaymentChargeRequest(
    string CustomerId,
    string PaymentProvider,
    string PaymentMethod,
    int AmountUsd,
    string CurrencyCode,
    string MaskedAccount,
    string? CardholderName,
    string? CardNumber,
    string? ExpiryMonth,
    string? ExpiryYear,
    string? Cvv,
    string? WalletProvider,
    string? WalletAccount,
    string? WalletPin);

public sealed record PremiumPaymentResult(
    bool IsSuccessful,
    string? ProviderTransactionId,
    string? FailureMessage,
    DateTimeOffset ProcessedAt)
{
    public static PremiumPaymentResult Success(string providerTransactionId, DateTimeOffset processedAt)
        => new(true, providerTransactionId, null, processedAt);

    public static PremiumPaymentResult Fail(string failureMessage, DateTimeOffset processedAt)
        => new(false, null, failureMessage, processedAt);
}
