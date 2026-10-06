namespace ILAPI.Core;

public record RegisterRequest(string Email, string Password, string FullName, string? Phone);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record HandoffExchangeRequest(string Token);
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserInfo User);
public record UserInfo(Guid Id, string Email, string FullName, string Role);

public record ProductDto(int Id, string Code, string Name, string? Description, decimal BaseRate);

public record MotorQuoteRequest(string Make, string Model, int Year, decimal VehicleValue, int DriverAge, int ClaimsLast5Years, string CoverageCode);
public record HomeQuoteRequest(string PropertyType, string Postcode, int YearBuilt, decimal RebuildValue, decimal ContentsValue);
public record TravelQuoteRequest(string DestinationCode, DateOnly StartDate, DateOnly EndDate, int Travellers, decimal TripCost);
public record LiabilityQuoteRequest(string BusinessType, decimal AnnualTurnover, int Employees, decimal LimitAmount);

public record PremiumResult(decimal BasePremium, decimal Tax, decimal Total);
public record QuoteSummary(Guid Id, string QuoteNumber, string Status, string ProductCode, string ProductName,
    decimal BasePremium, decimal Tax, decimal TotalPremium, DateTime CreatedAt, DateTime ExpiresAt);

public class UserRecord
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "customer";
    public bool IsActive { get; set; }
}
