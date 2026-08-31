using SqlSugar;
using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 本地 SQLite 持久化（SqlSugar 驱动）：负责保存访客登记记录，便于"签退 / 我的来访"页面查询。
/// 使用 SqlSugarScope —— 官方推荐的单例 / 多线程场景客户端，内部保证连接与线程安全。
/// </summary>
public class VisitorDatabase
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SqlSugarScope? _db;

    private async Task<SqlSugarScope> GetDbAsync()
    {
        if (_db is not null)
            return _db;

        await _initLock.WaitAsync();
        try
        {
            if (_db is not null)
                return _db;

            // MAUI（尤其 Android / iOS）需要显式初始化 SQLitePCLRaw 原生库
            SQLitePCL.Batteries_V2.Init();

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "visitor-app.db3");

            var db = new SqlSugarScope(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"DataSource={dbPath}",
                IsAutoCloseConnection = true,
            });

            // CodeFirst：不存在则建表，已存在则按实体特性增量维护结构与索引
            db.CodeFirst.InitTables<Visitor>();

            _db = db;
            return _db;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<int> SaveAsync(Visitor visitor)
    {
        var db = await GetDbAsync();
        if (visitor.Id == 0)
        {
            return await db.Insertable(visitor).ExecuteCommandAsync();
        }
        return await db.Updateable(visitor).ExecuteCommandAsync();
    }

    public async Task<Visitor?> GetAsync(int id)
    {
        var db = await GetDbAsync();
        return await db.Queryable<Visitor>().InSingleAsync(id);
    }

    public async Task<Visitor?> GetByCodeAsync(string code)
    {
        var db = await GetDbAsync();
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return await db.Queryable<Visitor>()
            .Where(v => v.VisitCode == trimmed)
            .FirstAsync();
    }

    public async Task<List<Visitor>> GetActiveByPhoneAsync(string phone)
    {
        var db = await GetDbAsync();
        if (string.IsNullOrWhiteSpace(phone)) return new List<Visitor>();
        var trimmed = phone.Trim();
        return await db.Queryable<Visitor>()
            .Where(v => v.Phone == trimmed && v.Status == VisitStatus.CheckedIn)
            .OrderByDescending(v => v.CheckInTime)
            .ToListAsync();
    }

    public async Task<List<Visitor>> GetRecentAsync(int max = 30)
    {
        var db = await GetDbAsync();
        return await db.Queryable<Visitor>()
            .OrderByDescending(v => v.CheckInTime)
            .Take(max)
            .ToListAsync();
    }

    public async Task<int> CheckOutAsync(int id)
    {
        var db = await GetDbAsync();
        var visitor = await db.Queryable<Visitor>().InSingleAsync(id);
        if (visitor is null || visitor.Status != VisitStatus.CheckedIn)
        {
            return 0;
        }
        visitor.CheckOutTime = DateTime.Now;
        visitor.Status = VisitStatus.CheckedOut;
        return await db.Updateable(visitor).ExecuteCommandAsync();
    }
}
