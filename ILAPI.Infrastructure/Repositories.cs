using System.Data;
using Dapper;
using ILAPI.Core;
using Npgsql;

namespace ILAPI.Infrastructure;

public interface IUserRepository
{
    /// <summary>Finds a user by username or email.</summary>
    Task<UserRecord?> FindByLoginAsync(string login);
    Task<UserRecord?> FindByIdAsync(Guid id);
    Task<string[]> GetPermissionsAsync(Guid userId);
    Task<UserRecord?> CreateAsync(string email, string passwordHash, string fullName, string? phone);
}

public interface ITokenRepository
{
    Task StoreRefreshAsync(Guid userId, string tokenHash, DateTime expiresAt);
    /// <summary>Atomically revokes a valid refresh token and returns its user id.</summary>
    Task<Guid?> ConsumeRefreshAsync(string tokenHash);
    Task StoreHandoffAsync(Guid userId, string tokenHash, DateTime expiresAt);
    /// <summary>Atomically marks a valid handoff token used (one-time) and returns its user id.</summary>
    Task<Guid?> ConsumeHandoffAsync(string tokenHash);
}

public interface IProductRepository
{
    Task<IEnumerable<ProductDto>> GetActiveAsync();
    Task<decimal?> GetRateAsync(string code);
    Task<decimal?> GetCoverageLoadingAsync(string productCode, string coverageCode);
    Task<decimal?> GetCountryRiskAsync(string countryCode);
}

public interface IQuoteRepository
{
    Task<QuoteSummary> CreateAsync(Guid userId, string productCode, PremiumResult premium,
        Func<IDbConnection, IDbTransaction, Guid, Task> insertDetail);
    Task<IEnumerable<QuoteSummary>> ListForUserAsync(Guid userId);
    Task<IEnumerable<QuoteSummary>> ListAllAsync(string? status = null);
    /// <summary>Returns the quote if it belongs to the user, or to anyone when <paramref name="anyOwner"/> is true.</summary>
    Task<QuoteSummary?> GetAsync(Guid userId, Guid quoteId, bool anyOwner);
}

public class UserRepository(NpgsqlDataSource db) : IUserRepository
{
    private const string Select =
        "SELECT u.id, u.email::text AS email, u.username::text AS username, u.password_hash, u.full_name, u.is_active, " +
        "COALESCE((SELECT r.code FROM auth.user_roles ur JOIN auth.roles r ON r.id = ur.role_id WHERE ur.user_id = u.id ORDER BY r.id LIMIT 1), 'CUSTOMER') AS role " +
        "FROM auth.users u";

    public async Task<UserRecord?> FindByLoginAsync(string login)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QueryFirstOrDefaultAsync<UserRecord>($"{Select} WHERE u.username = @login OR u.email = @login ORDER BY (u.username = @login) DESC", new { login });
    }

    public async Task<UserRecord?> FindByIdAsync(Guid id)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QuerySingleOrDefaultAsync<UserRecord>($"{Select} WHERE u.id = @id", new { id });
    }

    public async Task<string[]> GetPermissionsAsync(Guid userId)
    {
        await using var c = await db.OpenConnectionAsync();
        return (await c.QueryAsync<string>(
            "SELECT DISTINCT p.code FROM auth.user_roles ur JOIN auth.role_permissions rp ON rp.role_id = ur.role_id " +
            "JOIN auth.permissions p ON p.id = rp.permission_id WHERE ur.user_id = @userId ORDER BY p.code", new { userId })).ToArray();
    }

    public async Task<UserRecord?> CreateAsync(string email, string passwordHash, string fullName, string? phone)
    {
        await using var c = await db.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        var id = await c.ExecuteScalarAsync<Guid?>(
            "INSERT INTO auth.users(email, username, password_hash, full_name) VALUES (@email, @email, @passwordHash, @fullName) " +
            "ON CONFLICT DO NOTHING RETURNING id", new { email, passwordHash, fullName }, tx);
        if (id is null) return null;
        await c.ExecuteAsync("INSERT INTO auth.user_roles(user_id, role_id) SELECT @id, id FROM auth.roles WHERE code = 'CUSTOMER'", new { id }, tx);
        var user = await c.QuerySingleAsync<UserRecord>($"{Select} WHERE u.id = @id", new { id }, tx);
        await c.ExecuteAsync(
            "INSERT INTO customer.customers(user_id, full_name, email, phone) VALUES (@id, @fullName, @email, @phone)",
            new { user.Id, fullName, email, phone }, tx);
        await tx.CommitAsync();
        return user;
    }
}

public class TokenRepository(NpgsqlDataSource db) : ITokenRepository
{
    public async Task StoreRefreshAsync(Guid userId, string tokenHash, DateTime expiresAt)
    {
        await using var c = await db.OpenConnectionAsync();
        await c.ExecuteAsync("INSERT INTO auth.refresh_tokens(user_id, token_hash, expires_at) VALUES (@userId, @tokenHash, @expiresAt)",
            new { userId, tokenHash, expiresAt });
    }

    public async Task<Guid?> ConsumeRefreshAsync(string tokenHash)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<Guid?>(
            "UPDATE auth.refresh_tokens SET revoked_at = now() WHERE token_hash = @tokenHash AND revoked_at IS NULL AND expires_at > now() RETURNING user_id",
            new { tokenHash });
    }

    public async Task StoreHandoffAsync(Guid userId, string tokenHash, DateTime expiresAt)
    {
        await using var c = await db.OpenConnectionAsync();
        await c.ExecuteAsync("INSERT INTO auth.handoff_tokens(user_id, token_hash, expires_at) VALUES (@userId, @tokenHash, @expiresAt)",
            new { userId, tokenHash, expiresAt });
    }

    public async Task<Guid?> ConsumeHandoffAsync(string tokenHash)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<Guid?>(
            "UPDATE auth.handoff_tokens SET used_at = now() WHERE token_hash = @tokenHash AND used_at IS NULL AND expires_at > now() RETURNING user_id",
            new { tokenHash });
    }
}

public class ProductRepository(NpgsqlDataSource db) : IProductRepository
{
    public async Task<IEnumerable<ProductDto>> GetActiveAsync()
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QueryAsync<ProductDto>("SELECT id, code, name, description, base_rate FROM product.fn_active_products()");
    }

    public async Task<decimal?> GetRateAsync(string code)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<decimal?>("SELECT base_rate FROM product.products WHERE code = @code AND is_active", new { code });
    }

    public async Task<decimal?> GetCoverageLoadingAsync(string productCode, string coverageCode)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<decimal?>(
            "SELECT cv.loading_pct FROM product.coverages cv JOIN product.products p ON p.id = cv.product_id WHERE p.code = @productCode AND cv.code = @coverageCode",
            new { productCode, coverageCode });
    }

    public async Task<decimal?> GetCountryRiskAsync(string countryCode)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<decimal?>("SELECT travel_risk_factor FROM common.countries WHERE code = @countryCode",
            new { countryCode = countryCode.ToUpperInvariant() });
    }
}

public class QuoteRepository(NpgsqlDataSource db) : IQuoteRepository
{
    private const string SummarySql = "SELECT id, quote_number, status, product_code, product_name, base_premium, tax, total_premium, created_at, expires_at FROM quote.v_quote_summary";

    public async Task<QuoteSummary> CreateAsync(Guid userId, string productCode, PremiumResult premium,
        Func<IDbConnection, IDbTransaction, Guid, Task> insertDetail)
    {
        await using var c = await db.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        // staff users have no customer profile by default: create one on first quote
        await c.ExecuteAsync("INSERT INTO customer.customers(user_id, full_name, email) SELECT id, full_name, email FROM auth.users WHERE id = @userId ON CONFLICT (user_id) DO NOTHING", new { userId }, tx);
        var customerId = await c.ExecuteScalarAsync<Guid>("SELECT id FROM customer.customers WHERE user_id = @userId", new { userId }, tx);
        var id = await c.ExecuteScalarAsync<Guid>(
            "INSERT INTO quote.quotes(quote_number, customer_id, product_id, base_premium, tax, total_premium) " +
            "SELECT quote.fn_next_quote_number(), @customerId, p.id, @b, @t, @total FROM product.products p WHERE p.code = @productCode RETURNING id",
            new { customerId, productCode, b = premium.BasePremium, t = premium.Tax, total = premium.Total }, tx);
        await insertDetail(c, tx, id);
        await tx.CommitAsync();
        return await c.QuerySingleAsync<QuoteSummary>($"{SummarySql} WHERE id = @id", new { id });
    }

    public async Task<IEnumerable<QuoteSummary>> ListForUserAsync(Guid userId)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QueryAsync<QuoteSummary>(
            $"{SummarySql} WHERE customer_id IN (SELECT id FROM customer.customers WHERE user_id = @userId) ORDER BY created_at DESC LIMIT 200",
            new { userId });
    }

    public async Task<IEnumerable<QuoteSummary>> ListAllAsync(string? status = null)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QueryAsync<QuoteSummary>(
            $"{SummarySql} WHERE (@status IS NULL OR status = @status) ORDER BY created_at DESC LIMIT 200", new { status });
    }

    public async Task<QuoteSummary?> GetAsync(Guid userId, Guid quoteId, bool anyOwner)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QuerySingleOrDefaultAsync<QuoteSummary>(
            $"{SummarySql} WHERE id = @quoteId AND (@anyOwner OR customer_id IN (SELECT id FROM customer.customers WHERE user_id = @userId))",
            new { quoteId, userId, anyOwner });
    }
}

public interface IAdminRepository
{
    Task<IEnumerable<dynamic>> CustomersAsync();
    Task<IEnumerable<dynamic>> UsersAsync();
    Task<IEnumerable<dynamic>> RolesAsync();
    Task<IEnumerable<dynamic>> ReportSummaryAsync();
}

public class AdminRepository(NpgsqlDataSource db) : IAdminRepository
{
    private async Task<IEnumerable<dynamic>> Q(string sql)
    {
        await using var c = await db.OpenConnectionAsync();
        return (await c.QueryAsync(sql)).ToList();
    }

    public Task<IEnumerable<dynamic>> CustomersAsync() => Q(
        "SELECT id, full_name, email::text AS email, phone, created_at FROM customer.customers ORDER BY created_at DESC LIMIT 200");

    public Task<IEnumerable<dynamic>> UsersAsync() => Q(
        "SELECT u.id, u.username::text AS username, u.email::text AS email, u.full_name, u.is_active, " +
        "COALESCE(string_agg(r.code, ', '), '') AS roles FROM auth.users u " +
        "LEFT JOIN auth.user_roles ur ON ur.user_id = u.id LEFT JOIN auth.roles r ON r.id = ur.role_id " +
        "GROUP BY u.id ORDER BY u.created_at LIMIT 200");

    public Task<IEnumerable<dynamic>> RolesAsync() => Q(
        "SELECT r.code, r.name, COALESCE(string_agg(p.code, ', ' ORDER BY p.code), '') AS permissions FROM auth.roles r " +
        "LEFT JOIN auth.role_permissions rp ON rp.role_id = r.id LEFT JOIN auth.permissions p ON p.id = rp.permission_id " +
        "GROUP BY r.id ORDER BY r.id");

    public Task<IEnumerable<dynamic>> ReportSummaryAsync() => Q(
        "SELECT product_name, status, count(*)::int AS quotes, COALESCE(sum(total_premium), 0) AS total_premium " +
        "FROM quote.v_quote_summary GROUP BY product_name, status ORDER BY product_name, status");
}
