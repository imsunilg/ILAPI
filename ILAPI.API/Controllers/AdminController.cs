using ILAPI.Core;
using ILAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ILAPI.API.Controllers;

/// <summary>Role-gated read endpoints. Authorization is enforced by permission policies, not by the client menus.</summary>
[ApiController]
[Authorize]
[Route("api")]
public class AdminController(IAdminRepository admin, IQuoteRepository quotes) : ControllerBase
{
    [Authorize(Policy = Permissions.CustomersView)]
    [HttpGet("customers")]
    public async Task<IEnumerable<dynamic>> Customers() => await admin.CustomersAsync();

    [Authorize(Policy = Permissions.UsersManage)]
    [HttpGet("users")]
    public async Task<IEnumerable<dynamic>> Users() => await admin.UsersAsync();

    [Authorize(Policy = Permissions.RolesManage)]
    [HttpGet("roles")]
    public async Task<IEnumerable<dynamic>> Roles() => await admin.RolesAsync();

    [Authorize(Policy = Permissions.ReportsView)]
    [HttpGet("reports/summary")]
    public async Task<IEnumerable<dynamic>> Report() => await admin.ReportSummaryAsync();

    [Authorize(Policy = Permissions.Underwriting)]
    [HttpGet("underwriting/queue")]
    public async Task<IEnumerable<QuoteSummary>> Queue() => await quotes.ListAllAsync("quoted");
}
