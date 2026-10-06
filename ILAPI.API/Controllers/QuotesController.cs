using Dapper;
using ILAPI.API.Services;
using ILAPI.Core;
using ILAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ILAPI.API.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(IProductRepository products) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ProductDto>> Get() => await products.GetActiveAsync();
}

[ApiController]
[Authorize]
[Route("api/quotes")]
public class QuotesController(IProductRepository products, IQuoteRepository quotes) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<QuoteSummary>> List() => await quotes.ListForUserAsync(User.UserId());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuoteSummary>> Get(Guid id)
    {
        var q = await quotes.GetForUserAsync(User.UserId(), id);
        return q is null ? NotFound() : Ok(q);
    }

    [HttpPost("motor")]
    public Task<ActionResult<QuoteSummary>> Motor(MotorQuoteRequest r) => Run("MOTOR", async rate =>
    {
        var loading = await products.GetCoverageLoadingAsync("MOTOR", r.CoverageCode)
            ?? throw new ArgumentException("Unknown coverage code");
        return PremiumCalculator.Motor(r, rate, loading);
    }, (c, tx, id) => c.ExecuteAsync(
        "INSERT INTO motor.vehicle_quotes(quote_id, make, model, year, vehicle_value, driver_age, claims_last_5_years, coverage_code) " +
        "VALUES (@id, @Make, @Model, @Year, @VehicleValue, @DriverAge, @ClaimsLast5Years, @CoverageCode)",
        new { id, r.Make, r.Model, r.Year, r.VehicleValue, r.DriverAge, r.ClaimsLast5Years, r.CoverageCode }, tx));

    [HttpPost("home")]
    public Task<ActionResult<QuoteSummary>> Home(HomeQuoteRequest r) => Run("HOME",
        rate => Task.FromResult(PremiumCalculator.Home(r, rate, DateTime.UtcNow.Year)),
        (c, tx, id) => c.ExecuteAsync(
            "INSERT INTO home.property_quotes(quote_id, property_type, postcode, year_built, rebuild_value, contents_value) " +
            "VALUES (@id, @PropertyType, @Postcode, @YearBuilt, @RebuildValue, @ContentsValue)",
            new { id, r.PropertyType, r.Postcode, r.YearBuilt, r.RebuildValue, r.ContentsValue }, tx));

    [HttpPost("travel")]
    public Task<ActionResult<QuoteSummary>> Travel(TravelQuoteRequest r) => Run("TRAVEL", async rate =>
    {
        var risk = await products.GetCountryRiskAsync(r.DestinationCode ?? "")
            ?? throw new ArgumentException("Unknown destination country");
        return PremiumCalculator.Travel(r, rate, risk);
    }, (c, tx, id) => c.ExecuteAsync(
        "INSERT INTO travel.trip_quotes(quote_id, destination_code, start_date, end_date, travellers, trip_cost) " +
        "VALUES (@id, @dest, @s, @e, @Travellers, @TripCost)",
        new { id, dest = r.DestinationCode!.ToUpperInvariant(), s = r.StartDate.ToDateTime(TimeOnly.MinValue),
              e = r.EndDate.ToDateTime(TimeOnly.MinValue), r.Travellers, r.TripCost }, tx));

    [HttpPost("liability")]
    public Task<ActionResult<QuoteSummary>> Liability(LiabilityQuoteRequest r) => Run("LIABILITY",
        rate => Task.FromResult(PremiumCalculator.Liability(r, rate)),
        (c, tx, id) => c.ExecuteAsync(
            "INSERT INTO liability.liability_quotes(quote_id, business_type, annual_turnover, employees, limit_amount) " +
            "VALUES (@id, @BusinessType, @AnnualTurnover, @Employees, @LimitAmount)",
            new { id, r.BusinessType, r.AnnualTurnover, r.Employees, r.LimitAmount }, tx));

    private async Task<ActionResult<QuoteSummary>> Run(string code, Func<decimal, Task<PremiumResult>> calc,
        Func<System.Data.IDbConnection, System.Data.IDbTransaction, Guid, Task> insert)
    {
        var rate = await products.GetRateAsync(code);
        if (rate is null) return NotFound(new { error = "Product not available" });
        PremiumResult premium;
        try { premium = await calc(rate.Value); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        var created = await quotes.CreateAsync(User.UserId(), code, premium, insert);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
