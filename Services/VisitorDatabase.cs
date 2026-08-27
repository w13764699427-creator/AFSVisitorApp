using SQLite;
using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 本地 SQLite 持久化：负责保存访客登记记录，便于"签退 / 我的来访"页面查询。
/// </summary>
public class VisitorDatabase
{
    /// <summary>sqlite_master 查询映射行。</summary>
    private sealed class IndexInfoRow
    {
        public string name { get; set; } = string.Empty;
    }

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection _database;

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_database is not null)
            return _database;

        await _initLock.WaitAsync();
        try
        {
            if (_database is not null)
                return _database;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "visitor-app.db3");

            var connection = new SQLiteAsyncConnection(dbPath,
                SQLiteOpenFlags.ReadWrite |
                SQLiteOpenFlags.Create |
                SQLiteOpenFlags.SharedCache);

            await connection.CreateTableAsync<Visitor>();

            // 兼容旧库：手机号曾被建成 UNIQUE 索引，导致同一访客再次登记时插入失败；
            // 先清掉含 Phone 的旧索引，再重建为普通索引（同一手机号允许多次来访）。
            var phoneIndexes = await connection.QueryAsync<IndexInfoRow>(
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='Visitors' " +
                "AND sql LIKE '%Phone%' AND name NOT LIKE 'sqlite_%'");
            foreach (var idx in phoneIndexes)
            {
                await connection.ExecuteAsync($"DROP INDEX \"{idx.name}\"");
            }

            await connection.CreateIndexAsync<Visitor>(v => v.Phone, unique: false);
            await connection.CreateIndexAsync<Visitor>(v => v.Name);

            _database = connection;
            return _database;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<int> SaveAsync(Visitor visitor)
    {
        var database = await GetDatabaseAsync();
        if (visitor.Id == 0)
        {
            return await database.InsertAsync(visitor);
        }
        return await database.UpdateAsync(visitor);
    }

    public async Task<Visitor?> GetAsync(int id)
    {
        var database = await GetDatabaseAsync();
        return await database.FindAsync<Visitor>(id);
    }

    public async Task<Visitor?> GetByCodeAsync(string code)
    {
        var database = await GetDatabaseAsync();
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return await database.Table<Visitor>()
            .Where(v => v.VisitCode == trimmed)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Visitor>> GetActiveByPhoneAsync(string phone)
    {
        var database = await GetDatabaseAsync();
        if (string.IsNullOrWhiteSpace(phone)) return new List<Visitor>();
        var trimmed = phone.Trim();
        return await database.Table<Visitor>()
            .Where(v => v.Phone == trimmed && v.Status == VisitStatus.CheckedIn)
            .OrderByDescending(v => v.CheckInTime)
            .ToListAsync();
    }

    public async Task<List<Visitor>> GetRecentAsync(int max = 30)
    {
        var database = await GetDatabaseAsync();
        var list = await database.Table<Visitor>()
            .OrderByDescending(v => v.CheckInTime)
            .ToListAsync();
        return list.Take(max).ToList();
    }

    public async Task<int> CheckOutAsync(int id)
    {
        var database = await GetDatabaseAsync();
        var visitor = await database.FindAsync<Visitor>(id);
        if (visitor is null || visitor.Status != VisitStatus.CheckedIn)
        {
            return 0;
        }
        visitor.CheckOutTime = DateTime.Now;
        visitor.Status = VisitStatus.CheckedOut;
        return await database.UpdateAsync(visitor);
    }
}
