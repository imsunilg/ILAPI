namespace ILAPI.Core;

/// <summary>Pure premium rules. Rates come from product.products.base_rate.</summary>
public static class PremiumCalculator
{
    public const decimal TaxRate = 0.10m;
    private const decimal MinimumPremium = 25m;

    public static PremiumResult Motor(MotorQuoteRequest r, decimal rate, decimal coverageLoadingPct)
    {
        Validate(r.VehicleValue > 0, "Vehicle value must be positive");
        Validate(r.DriverAge is >= 17 and <= 100, "Driver age must be between 17 and 100");
        Validate(r.ClaimsLast5Years >= 0, "Claims cannot be negative");
        var ageFactor = r.DriverAge < 25 ? 1.6m : r.DriverAge >= 70 ? 1.3m : 1.0m;
        var claimsFactor = 1m + 0.15m * r.ClaimsLast5Years;
        var basePremium = r.VehicleValue * rate * ageFactor * claimsFactor * (1m + coverageLoadingPct / 100m);
        return Finish(basePremium, 150m);
    }

    public static PremiumResult Home(HomeQuoteRequest r, decimal rate, int currentYear)
    {
        Validate(r.RebuildValue > 0, "Rebuild value must be positive");
        Validate(r.ContentsValue >= 0, "Contents value cannot be negative");
        Validate(r.YearBuilt >= 1800 && r.YearBuilt <= currentYear, "Invalid year built");
        var ageFactor = currentYear - r.YearBuilt > 50 ? 1.2m : 1.0m;
        var basePremium = (r.RebuildValue * rate + r.ContentsValue * rate * 1.5m) * ageFactor;
        return Finish(basePremium, MinimumPremium);
    }

    public static PremiumResult Travel(TravelQuoteRequest r, decimal rate, decimal countryRiskFactor)
    {
        Validate(r.EndDate >= r.StartDate, "End date must not be before start date");
        Validate(r.Travellers > 0, "At least one traveller is required");
        Validate(r.TripCost >= 0, "Trip cost cannot be negative");
        var days = r.EndDate.DayNumber - r.StartDate.DayNumber + 1;
        var basePremium = (r.TripCost * rate + days * r.Travellers * 1.5m) * countryRiskFactor;
        return Finish(basePremium, MinimumPremium);
    }

    public static PremiumResult Liability(LiabilityQuoteRequest r, decimal rate)
    {
        Validate(r.AnnualTurnover > 0, "Annual turnover must be positive");
        Validate(r.LimitAmount > 0, "Limit must be positive");
        Validate(r.Employees >= 0, "Employees cannot be negative");
        var limitFactor = 1m + (r.LimitAmount / 1_000_000m - 1m) * 0.25m;
        if (limitFactor < 0.5m) limitFactor = 0.5m;
        var basePremium = r.AnnualTurnover * rate * limitFactor + r.Employees * 12m;
        return Finish(basePremium, 100m);
    }

    private static PremiumResult Finish(decimal basePremium, decimal minimum)
    {
        var b = Math.Round(Math.Max(basePremium, minimum), 2, MidpointRounding.AwayFromZero);
        var tax = Math.Round(b * TaxRate, 2, MidpointRounding.AwayFromZero);
        return new PremiumResult(b, tax, b + tax);
    }

    private static void Validate(bool ok, string message)
    {
        if (!ok) throw new ArgumentException(message);
    }
}
