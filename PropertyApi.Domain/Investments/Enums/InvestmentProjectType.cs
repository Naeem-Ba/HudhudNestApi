namespace PropertyApi.Domain.Investments.Enums;

/// <summary>Broad category of the underlying real-estate investment project.</summary>
public enum InvestmentProjectType
{
    Residential = 0,
    Commercial = 1,
    Mixed = 2,
    Industrial = 3,
    Land = 4,

    /// <summary>Buy-renovate-sell/lease style project on an existing building.</summary>
    Renovation = 5,

    /// <summary>Ground-up new construction.</summary>
    NewConstruction = 6,
}
