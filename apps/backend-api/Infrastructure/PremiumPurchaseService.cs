using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class PremiumPurchaseService(
    AdminDataRepository repository,
    IPremiumPaymentProcessor paymentProcessor,
    ILogger<PremiumPurchaseService> logger)
{
    public PremiumPurchaseResponse Purchase(string customerId, PremiumPurchaseRequest request)
    {
        var customer = repository.GetCustomerUserById(customerId)
            ?? throw new InvalidOperationException("Khong tim thay khach hang de kich hoat Premium.");

        var normalizedRequest = ValidateAndNormalizeRequest(request);
        var settings = repository.GetSettings();
        var premiumPriceUsd = settings.PremiumUnlockPriceUsd > 0
            ? settings.PremiumUnlockPriceUsd
            : PremiumAccessCatalog.DefaultPremiumPriceUsd;

        if (normalizedRequest.ExpectedPriceUsd.HasValue &&
            normalizedRequest.ExpectedPriceUsd.Value > 0 &&
            normalizedRequest.ExpectedPriceUsd.Value != premiumPriceUsd)
        {
            throw new InvalidOperationException("Gia goi Premium da thay doi. Vui long tai lai va thu lai.");
        }

        var existingTransaction = repository.GetPremiumPurchaseTransactionByIdempotencyKey(
            customerId,
            normalizedRequest.ClientRequestId);
        if (existingTransaction is not null)
        {
            return ResolveExistingTransaction(customer, existingTransaction);
        }

        if (customer.IsPremium)
        {
            throw new InvalidOperationException("Tai khoan nay da la Premium.");
        }

        var pendingTransaction = CreatePendingTransaction(customerId, premiumPriceUsd, normalizedRequest);
        if (!string.Equals(pendingTransaction.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveExistingTransaction(customer, pendingTransaction);
        }

        var paymentResult = paymentProcessor.Process(new PremiumPaymentChargeRequest(
            customerId,
            normalizedRequest.PaymentProvider,
            normalizedRequest.PaymentMethod,
            premiumPriceUsd,
            "USD",
            normalizedRequest.MaskedAccount,
            normalizedRequest.CardholderName,
            normalizedRequest.CardNumber,
            normalizedRequest.ExpiryMonth,
            normalizedRequest.ExpiryYear,
            normalizedRequest.Cvv,
            normalizedRequest.WalletProvider,
            normalizedRequest.WalletAccount,
            normalizedRequest.WalletPin));

        if (!paymentResult.IsSuccessful)
        {
            var failureMessage = string.IsNullOrWhiteSpace(paymentResult.FailureMessage)
                ? "Thanh toan Premium that bai. Vui long thu lai."
                : paymentResult.FailureMessage;
            repository.MarkPremiumPurchaseFailed(
                pendingTransaction.Id,
                failureMessage,
                paymentResult.ProcessedAt);
            throw new InvalidOperationException(failureMessage);
        }

        var completed = repository.CompletePremiumPurchase(
            pendingTransaction.Id,
            customerId,
            customer.Name,
            "CUSTOMER",
            paymentResult.ProviderTransactionId ?? pendingTransaction.Id,
            paymentResult.ProcessedAt);

        logger.LogInformation(
            "Premium purchase completed. customerId={CustomerId}, paymentProvider={PaymentProvider}, paymentMethod={PaymentMethod}, amountUsd={AmountUsd}, transactionId={TransactionId}",
            completed.Customer.Id,
            completed.Transaction.PaymentProvider,
            completed.Transaction.PaymentMethod,
            premiumPriceUsd,
            completed.Transaction.Id);

        return new PremiumPurchaseResponse(
            completed.Customer,
            premiumPriceUsd,
            "USD",
            completed.Transaction.PaymentProvider,
            completed.Transaction.PaymentMethod,
            completed.Transaction.Id,
            completed.Transaction.ProcessedAt ?? completed.Transaction.CreatedAt);
    }

    private PremiumPurchaseResponse ResolveExistingTransaction(CustomerUser customer, PremiumPurchaseTransaction transaction)
    {
        if (string.Equals(transaction.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Giao dich Premium dang duoc xu ly. Vui long cho trong giay lat.");
        }

        if (string.Equals(transaction.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(transaction.FailureMessage)
                    ? "Thanh toan Premium that bai. Vui long thu lai."
                    : transaction.FailureMessage);
        }

        var refreshedCustomer = repository.GetCustomerUserById(customer.Id) ?? customer;
        return new PremiumPurchaseResponse(
            refreshedCustomer,
            transaction.AmountUsd,
            transaction.CurrencyCode,
            transaction.PaymentProvider,
            transaction.PaymentMethod,
            transaction.Id,
            transaction.ProcessedAt ?? transaction.CreatedAt);
    }

    private PremiumPurchaseTransaction CreatePendingTransaction(
        string customerId,
        int premiumPriceUsd,
        ValidatedPremiumPurchaseRequest request)
    {
        var transaction = new PremiumPurchaseTransaction
        {
            Id = CreateTransactionId(),
            CustomerUserId = customerId,
            AmountUsd = premiumPriceUsd,
            CurrencyCode = "USD",
            PaymentProvider = request.PaymentProvider,
            PaymentMethod = request.PaymentMethod,
            MaskedAccount = request.MaskedAccount,
            IdempotencyKey = request.ClientRequestId,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            return repository.CreatePendingPremiumPurchase(transaction);
        }
        catch (SqlException exception) when (IsUniqueConstraintViolation(exception))
        {
            var existingTransaction = repository.GetPremiumPurchaseTransactionByIdempotencyKey(customerId, request.ClientRequestId);
            if (existingTransaction is not null)
            {
                return existingTransaction;
            }

            var pendingTransaction = repository.GetLatestPendingPremiumPurchase(customerId);
            if (pendingTransaction is not null)
            {
                throw new InvalidOperationException("Dang co mot giao dich Premium duoc xu ly. Vui long doi giao dich hien tai hoan tat.");
            }

            throw;
        }
    }

    private static ValidatedPremiumPurchaseRequest ValidateAndNormalizeRequest(PremiumPurchaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var paymentProvider = string.IsNullOrWhiteSpace(request.PaymentProvider)
            ? "mock"
            : request.PaymentProvider.Trim().ToLowerInvariant();
        if (!string.Equals(paymentProvider, "mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("He thong hien tai chi ho tro mock payment cho goi Premium.");
        }

        var paymentMethod = NormalizeRequiredValue(request.PaymentMethod, "Phuong thuc thanh toan", 30)
            .ToLowerInvariant();
        if (!PremiumPaymentMethodCatalog.IsSupported(paymentMethod))
        {
            throw new InvalidOperationException("Phuong thuc thanh toan khong duoc ho tro.");
        }

        var clientRequestId = NormalizeRequiredValue(request.ClientRequestId, "Ma giao dich", 100);
        string maskedAccount;
        string? cardholderName = null;
        string? cardNumber = null;
        string? expiryMonth = null;
        string? expiryYear = null;
        string? cvv = null;
        string? walletProvider = null;
        string? walletAccount = null;
        string? walletPin = null;

        if (PremiumPaymentMethodCatalog.RequiresCardDetails(paymentMethod))
        {
            cardholderName = NormalizeRequiredValue(request.CardholderName, "Ten chu the", 120);
            cardNumber = NormalizeDigits(request.CardNumber, "So the");
            if (cardNumber.Length < 12 || cardNumber.Length > 19 || !PassesLuhnCheck(cardNumber))
            {
                throw new InvalidOperationException("So the thanh toan khong hop le.");
            }

            expiryMonth = NormalizeDigits(request.ExpiryMonth, "Thang het han");
            expiryYear = NormalizeDigits(request.ExpiryYear, "Nam het han");
            if (!int.TryParse(expiryMonth, out var expiryMonthValue) || expiryMonthValue is < 1 or > 12)
            {
                throw new InvalidOperationException("Thang het han khong hop le.");
            }

            if (!int.TryParse(expiryYear, out var expiryYearValue) || expiryYearValue < DateTimeOffset.UtcNow.Year)
            {
                throw new InvalidOperationException("Nam het han khong hop le.");
            }

            var cardExpiry = new DateTimeOffset(
                expiryYearValue,
                expiryMonthValue,
                DateTime.DaysInMonth(expiryYearValue, expiryMonthValue),
                23,
                59,
                59,
                TimeSpan.Zero);
            if (cardExpiry < DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("The thanh toan da het han.");
            }

            cvv = NormalizeDigits(request.Cvv, "CVV");
            if (cvv.Length is < 3 or > 4)
            {
                throw new InvalidOperationException("CVV khong hop le.");
            }

            maskedAccount = $"**** **** **** {cardNumber[^4..]}";
        }
        else
        {
            walletProvider = NormalizeRequiredValue(request.WalletProvider, "Nha cung cap vi", 50)
                .ToLowerInvariant();
            if (walletProvider is not ("momo" or "zalopay"))
            {
                throw new InvalidOperationException("Vi dien tu khong duoc ho tro.");
            }

            walletAccount = NormalizeRequiredValue(request.WalletAccount, "Tai khoan vi", 120);
            walletPin = NormalizeDigits(request.WalletPin, "Ma xac nhan vi");
            if (walletPin.Length is < 4 or > 6)
            {
                throw new InvalidOperationException("Ma xac nhan vi khong hop le.");
            }

            maskedAccount = MaskWalletAccount(walletAccount);
        }

        return new ValidatedPremiumPurchaseRequest(
            paymentProvider,
            paymentMethod,
            clientRequestId,
            request.ExpectedPriceUsd,
            maskedAccount,
            cardholderName,
            cardNumber,
            expiryMonth,
            expiryYear,
            cvv,
            walletProvider,
            walletAccount,
            walletPin);
    }

    private static string NormalizeRequiredValue(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} la bat buoc.");
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{fieldName} khong duoc vuot qua {maxLength} ky tu.");
        }

        return normalized;
    }

    private static string NormalizeDigits(string? value, string fieldName)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} la bat buoc.");
        }

        return normalized;
    }

    private static string MaskWalletAccount(string walletAccount)
    {
        if (walletAccount.Length <= 4)
        {
            return walletAccount;
        }

        return $"{new string('*', walletAccount.Length - 4)}{walletAccount[^4..]}";
    }

    private static bool PassesLuhnCheck(string digits)
    {
        var checksum = 0;
        var shouldDouble = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (shouldDouble)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            checksum += digit;
            shouldDouble = !shouldDouble;
        }

        return checksum > 0 && checksum % 10 == 0;
    }

    private static bool IsUniqueConstraintViolation(SqlException exception)
        => exception.Number is 2601 or 2627;

    private static string CreateTransactionId()
        => $"premium-{Guid.NewGuid():N}";

    private sealed record ValidatedPremiumPurchaseRequest(
        string PaymentProvider,
        string PaymentMethod,
        string ClientRequestId,
        int? ExpectedPriceUsd,
        string MaskedAccount,
        string? CardholderName,
        string? CardNumber,
        string? ExpiryMonth,
        string? ExpiryYear,
        string? Cvv,
        string? WalletProvider,
        string? WalletAccount,
        string? WalletPin);
}
