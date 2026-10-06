using ILAPI.Core;
using Xunit;

namespace ILAPI.Tests;

public class PremiumCalculatorTests
{
    [Fact]
    public void Motor_StandardDriver_AppliesRateAndTax()
    {
        var r = PremiumCalculator.Motor(new("Toyota", "Corolla", 2020, 20000m, 35, 0, "COMPREHENSIVE"), 0.035m, 0);
        Assert.Equal(700m, r.BasePremium);
        Assert.Equal(70m, r.Tax);
        Assert.Equal(770m, r.Total);
    }

    [Fact]
    public void Motor_YoungDriverWithClaims_CostsMore()
    {
        var std = PremiumCalculator.Motor(new("A", "B", 2020, 20000m, 35, 0, "COMPREHENSIVE"), 0.035m, 0);
        var risky = PremiumCalculator.Motor(new("A", "B", 2020, 20000m, 21, 2, "COMPREHENSIVE"), 0.035m, 0);
        Assert.True(risky.BasePremium > std.BasePremium);
    }

    [Fact]
    public void Motor_ThirdPartyLoadingReducesPremium()
    {
        var comp = PremiumCalculator.Motor(new("A", "B", 2020, 20000m, 35, 0, "COMPREHENSIVE"), 0.035m, 0);
        var tp = PremiumCalculator.Motor(new("A", "B", 2020, 20000m, 35, 0, "THIRD_PARTY"), 0.035m, -30);
        Assert.Equal(comp.BasePremium * 0.7m, tp.BasePremium);
    }

    [Fact]
    public void Motor_InvalidAge_Throws() =>
        Assert.Throws<ArgumentException>(() => PremiumCalculator.Motor(new("A", "B", 2020, 1000m, 10, 0, "X"), 0.035m, 0));

    [Fact]
    public void Home_OldPropertyHasLoading()
    {
        var newer = PremiumCalculator.Home(new("House", "AB1", 2010, 300000m, 0m), 0.0012m, 2026);
        var older = PremiumCalculator.Home(new("House", "AB1", 1950, 300000m, 0m), 0.0012m, 2026);
        Assert.Equal(360m, newer.BasePremium);
        Assert.Equal(432m, older.BasePremium);
    }

    [Fact]
    public void Home_FutureYearBuilt_Throws() =>
        Assert.Throws<ArgumentException>(() => PremiumCalculator.Home(new("House", "AB1", 2090, 1m, 0m), 0.0012m, 2026));

    [Fact]
    public void Travel_UsesDaysTravellersAndCountryFactor()
    {
        var r = PremiumCalculator.Travel(new("US", new(2026, 12, 1), new(2026, 12, 10), 2, 4000m), 0.04m, 2.5m);
        // (4000*0.04 + 10*2*1.5) * 2.5 = 475
        Assert.Equal(475m, r.BasePremium);
        Assert.Equal(522.5m, r.Total);
    }

    [Fact]
    public void Travel_EndBeforeStart_Throws() =>
        Assert.Throws<ArgumentException>(() => PremiumCalculator.Travel(new("GB", new(2026, 12, 10), new(2026, 12, 1), 1, 100m), 0.04m, 1m));

    [Fact]
    public void Liability_ScalesWithTurnoverAndEmployees()
    {
        var r = PremiumCalculator.Liability(new("Retail", 400000m, 5, 1_000_000m), 0.0025m);
        Assert.Equal(1060m, r.BasePremium); // 400000*0.0025 + 5*12
    }

    [Fact]
    public void MinimumPremiumApplies()
    {
        var r = PremiumCalculator.Home(new("Flat", "AB1", 2020, 1000m, 0m), 0.0012m, 2026);
        Assert.Equal(25m, r.BasePremium);
    }
}
