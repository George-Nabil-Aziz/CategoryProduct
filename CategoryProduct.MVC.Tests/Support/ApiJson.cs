namespace CategoryProduct.MVC.Tests;

/// <summary>
/// Builders for the JSON bodies the API returns (what the MVC deserializes). Each returns an anonymous object
/// meant for <c>FakeResponse.Json(status, ApiJson.X(...))</c>; serialization is camelCase.
/// </summary>
public static class ApiJson
{
    private static int _loginSeq;

    /// <summary>LoginResult. Token and refresh default to unique "access-N"/"refresh-N" values.</summary>
    public static object Login(string role = "User", int expiresInSeconds = 900, string? token = null, string? refresh = null)
    {
        var n = Interlocked.Increment(ref _loginSeq);
        var now = DateTime.UtcNow;
        return new
        {
            token = token ?? $"access-{n}",
            expiresAtUtc = now.AddSeconds(expiresInSeconds),
            role,
            refreshToken = refresh ?? $"refresh-{n}",
            refreshTokenExpiresAtUtc = now.AddDays(7)
        };
    }

    /// <summary>PagedResult. Totals default from the item count (one page).</summary>
    public static object Paged(IEnumerable<object> items, int page = 1, int pageSize = 5, int? totalCount = null, int? totalPages = null)
    {
        var list = items.ToList();
        var count = totalCount ?? list.Count;
        return new
        {
            items = list,
            page,
            pageSize,
            totalCount = count,
            totalPages = totalPages ?? Math.Max(1, (int)Math.Ceiling(count / (double)pageSize))
        };
    }

    public static object Category(int id, string name) => new { id, name };

    public static object Product(int id, string name, decimal price = 10m, int quantity = 5, int categoryId = 1,
        string? imageUrl = null, bool isLowStock = false) =>
        new { id, name, price, quantity, categoryId, imageUrl, isLowStock };

    public static object StockMovement(int id, int productId, int quantityChange, int quantityAfter,
        string reason = "Restock", string? note = null, int? userId = 1, string? userName = "root", DateTime? createdAtUtc = null) =>
        new { id, productId, quantityChange, quantityAfter, reason, note, userId, userName, createdAtUtc = createdAtUtc ?? DateTime.UtcNow };

    public static object CartLine(int productId, string productName, decimal unitPrice, int quantity, int availableQuantity = 100) =>
        new { productId, productName, unitPrice, quantity, lineTotal = unitPrice * quantity, availableQuantity };

    /// <summary>Cart; <paramref name="lines"/> come from <see cref="CartLine"/>. Empty cart: call with no arguments.</summary>
    public static object Cart(params object[] lines)
    {
        decimal total = 0; var count = 0;
        foreach (var l in lines)
        {
            var t = l.GetType();
            total += (decimal)t.GetProperty("lineTotal")!.GetValue(l)!;
            count += (int)t.GetProperty("quantity")!.GetValue(l)!;
        }
        return new { items = lines, itemCount = count, total };
    }

    public static object OrderLine(int id, int? productId, string productName, decimal unitPrice, int quantity) =>
        new { id, productId, productName, unitPrice, quantity, lineTotal = unitPrice * quantity };

    public static object Order(int id, string status = "Pending", int userId = 1, string userName = "alice", params object[] lines)
    {
        decimal total = 0;
        foreach (var l in lines) total += (decimal)l.GetType().GetProperty("lineTotal")!.GetValue(l)!;
        var now = DateTime.UtcNow;
        return new { id, userId, userName, status, total, createdAtUtc = now, updatedAtUtc = now, items = lines };
    }

    public static object OrderSummary(int id, string status = "Pending", int userId = 1, string userName = "alice",
        decimal total = 10m, int itemCount = 1)
    {
        var now = DateTime.UtcNow;
        return new { id, userId, userName, status, total, itemCount, createdAtUtc = now, updatedAtUtc = now };
    }

    public static object User(int id, string userName, string role = "User") => new { id, userName, role };

    /// <summary>400 problem body: <c>errors = { field: [messages] }</c>. Several calls' worth via the dictionary overload.</summary>
    public static object ValidationProblem(string field, params string[] messages) =>
        ValidationProblem(new Dictionary<string, string[]> { [field] = messages });

    public static object ValidationProblem(IDictionary<string, string[]> errors) =>
        new { title = "One or more validation errors occurred.", status = 400, errors };

    /// <summary>409 problem body; the MVC surfaces <paramref name="detail"/>.</summary>
    public static object ConflictProblem(string detail) => new { title = "Conflict", status = 409, detail };
}
