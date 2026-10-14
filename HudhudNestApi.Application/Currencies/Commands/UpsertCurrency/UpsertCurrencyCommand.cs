using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Currencies.DTOs;
using HudhudNestApi.Application.Currencies.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Application.Currencies.Commands.UpsertCurrency;

/// <summary>
/// Creates a currency or updates the rate (and descriptive fields) of an existing one. The rate
/// is the administrator's to set: nothing in this product guesses an exchange rate, and the values
/// the database was first seeded with are approximations to be replaced.
/// </summary>
public sealed record UpsertCurrencyCommand(
    string Code,
    string NameEn,
    string NameAr,
    string Symbol,
    int DecimalPlaces,
    decimal ExchangeRateToUsd,
    bool IsActive,
    Guid PerformedByUserId,
    string? IpAddress) : IRequest<CurrencyDto>;

public sealed class UpsertCurrencyCommandValidator : AbstractValidator<UpsertCurrencyCommand>
{
    public UpsertCurrencyCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$").WithMessage("Code must be a 3-letter ISO 4217 code.");
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(10);
        RuleFor(x => x.DecimalPlaces).InclusiveBetween(0, 4);
        RuleFor(x => x.ExchangeRateToUsd).GreaterThan(0m).LessThanOrEqualTo(100_000_000m);
    }
}

public sealed class UpsertCurrencyCommandHandler : IRequestHandler<UpsertCurrencyCommand, CurrencyDto>
{
    private readonly ICurrencyRepository _currencies;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public UpsertCurrencyCommandHandler(ICurrencyRepository currencies, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _currencies = currencies;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task<CurrencyDto> Handle(UpsertCurrencyCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var existing = await _currencies.GetByCodeAsync(code, ct);
        var oldRate = existing?.ExchangeRateToUSD;

        Currency currency;
        if (existing is null)
        {
            currency = Currency.Create(
                code, request.NameEn.Trim(), request.NameAr.Trim(), request.Symbol.Trim(),
                request.ExchangeRateToUsd, isBaseCurrency: false, request.DecimalPlaces);
            if (!request.IsActive)
                currency.UpdateDetails(currency.NameEn, currency.NameAr, currency.Symbol, currency.DecimalPlaces, false);
            _currencies.Add(currency);
        }
        else
        {
            currency = existing;
            currency.UpdateDetails(request.NameEn.Trim(), request.NameAr.Trim(), request.Symbol.Trim(), request.DecimalPlaces, request.IsActive);
            currency.UpdateExchangeRate(request.ExchangeRateToUsd);
        }

        await _uow.SaveChangesAsync(ct);

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.CurrencyRateUpdatedByAdmin,
            request.IpAddress,
            oldValue: oldRate is null ? null : $"{{\"code\":\"{code}\",\"rate\":{oldRate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}",
            newValue: $"{{\"code\":\"{code}\",\"rate\":{request.ExchangeRateToUsd.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"active\":{request.IsActive.ToString().ToLowerInvariant()}}}",
            ct);

        return new CurrencyDto(
            currency.Code.Trim(), currency.NameEn, currency.NameAr, currency.Symbol, currency.DecimalPlaces,
            currency.ExchangeRateToUSD, currency.ExchangeRateUpdatedAt, currency.IsBaseCurrency);
    }
}
