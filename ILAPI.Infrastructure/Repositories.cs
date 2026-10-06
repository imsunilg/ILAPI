using System.Data;
using Dapper;
using ILAPI.Core;
using Npgsql;

namespace ILAPI.Infrastructure;

public interface IUserRepository
{
    Task<UserRecord?> FindByEmailAsync(string email);
    Task<UserRecord?> FindByIdAsync(Guid id);
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
    Task<QuoteSummary?> GetForUserAsync(Guid userId, Guid quoteId);
}

public class UserRepository(NpgsqlDataSource db) : IUserRepository
{
    private const string Cols = "id, email::text AS email, password_hash, full_name, role, is_active";

    public async Task<UserRecord?> FindByEmailAsync(string email)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QuerySingleOrDefaultAsync<UserRecord>($"SELECT {Cols} FROM auth.users WHERE email = @email", new { email });
    }

    public async Task<UserRecord?> FindByIdAsync(Guid id)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QuerySingleOrDefaultAsync<UserRecord>($"SELECT {Cols} FROM auth.users WHERE id = @id", new { id });
    }

    public async Task<UserRecord?> CreateAsync(string email, string passwordHash, string fullName, string? phone)
    {
        await using var c = await db.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        var user = await c.QuerySingleOrDefaultAsync<UserRecord>(
            $"INSERT INTO auth.users(email, password_hash, full_name) VALUES (@email, @passwordHash, @fullName) " +
            $"ON CONFLICT (email) DO NOTHING RETURNING {Cols}", new { email, passwordHash, fullName }, tx);
        if (user is null) return null;
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

    public async Task<QuoteSummary?> GetForUserAsync(Guid userId, Guid quoteId)
    {
        await using var c = await db.OpenConnectionAsync();
        return await c.QuerySingleOrDefaultAsync<QuoteSummary>(
            $"{SummarySql} WHERE id = @quoteId AND customer_id IN (SELECT id FROM customer.customers WHERE user_id = @userId)",
            new { quoteId, userId });
    }
}
