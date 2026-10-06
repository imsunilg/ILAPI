namespace ILAPI.Core;

/// <summary>Permission codes. Each one is also an authorization policy name and a JWT "permission" claim.</summary>
public static class Permissions
{
    public const string Dashboard = "dashboard.view";
    public const string MotorQuote = "motor.quote";
    public const string HomeQuote = "home.quote";
    public const string TravelQuote = "travel.quote";
    public const string LiabilityQuote = "liability.quote";
    public const string CustomersView = "customers.view";
    public const string QuotesView = "quotes.view";
    public const string QuotesOwn = "quotes.own";
    public const string Underwriting = "underwriting.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string ReportsView = "reports.view";
    public const string ProfileView = "profile.view";
    public const string PoliciesView = "policies.view";

    public static readonly string[] All =
    [
        Dashboard, MotorQuote, HomeQuote, TravelQuote, LiabilityQuote, CustomersView, QuotesView, QuotesOwn,
        Underwriting, UsersManage, RolesManage, ReportsView, ProfileView, PoliciesView,
    ];
}
