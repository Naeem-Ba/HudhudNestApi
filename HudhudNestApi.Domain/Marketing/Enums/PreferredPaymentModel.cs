namespace HudhudNestApi.Domain.Marketing.Enums;

/// <summary>Which pricing model the respondent says they'd prefer — pure market-research
/// signal. Does not imply any of these models is actually offered
/// (FRONTEND_BACKEND_CONTRACT.md §11.5: no billing system exists yet).</summary>
public enum PreferredPaymentModel
{
    MonthlySubscription = 1,
    YearlySubscription = 2,
    PerListingFee = 3,
    CommissionOnSale = 4,
    Mixed = 5
}
