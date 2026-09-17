using SqlSugar;
using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 本地 SQLite 持久化（SqlSugar 驱动）：负责保存访客登记记录，便于"签退 / 我的来访"页面查询。
/// 使用 SqlSugarScope —— 官方推荐的单例 / 多线程场景客户端，内部保证连接与线程安全。
/// 敏感字段（身份证号 / 住址 / 证件照 / 人脸照）以 DPAPI 密文落库，读取时自动解密；
/// 历史明文数据按明文兼容读取，首次改写后即转为密文。
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

    /// <summary>生成未被占用的访客码：生成后查库校验，撞码自动重试（避免重码导致签退命中错误访客）。</summary>
    public async Task<string> GenerateUniqueVisitCodeAsync(int maxAttempts = 8)
    {
        var db = await GetDbAsync();
        for (var i = 0; i < maxAttempts; i++)
        {
            var code = VisitorFormState.GenerateVisitCode();
            if (!await db.Queryable<Visitor>().AnyAsync(v => v.VisitCode == code))
                return code;
        }
        // 极端兜底：随机段撞满时追加时间片，保证当次生成的码唯一。
        return VisitorFormState.GenerateVisitCode() + DateTime.Now.ToString("fff");
    }

    public async Task<int> SaveAsync(Visitor visitor)
    {
        var db = await GetDbAsync();
        // 敏感字段加密落库；finally 还原调用方对象为明文（成功页等展示不受影响）。
        var (idNumber, address, idCardPhoto, facePhoto) =
            (visitor.IdNumber, visitor.Address, visitor.IdCardPhoto, visitor.FacePhoto);
        try
        {
            visitor.IdNumber = PiiProtector.Protect(idNumber) ?? idNumber;
            visitor.Address = PiiProtector.Protect(address) ?? address;
            visitor.IdCardPhoto = PiiProtector.Protect(idCardPhoto) ?? idCardPhoto;
            visitor.FacePhoto = PiiProtector.Protect(facePhoto) ?? facePhoto;

            if (visitor.Id == 0)
            {
                return await db.Insertable(visitor).ExecuteCommandAsync();
            }
            return await db.Updateable(visitor).ExecuteCommandAsync();
        }
        finally
        {
            visitor.IdNumber = idNumber;
            visitor.Address = address;
            visitor.IdCardPhoto = idCardPhoto;
            visitor.FacePhoto = facePhoto;
        }
    }

    public async Task<Visitor?> GetAsync(int id)
    {
        var db = await GetDbAsync();
        var visitor = await db.Queryable<Visitor>().InSingleAsync(id);
        Decrypt(visitor);
        return visitor;
    }

    public async Task<Visitor?> GetByCodeAsync(string code)
    {
        var db = await GetDbAsync();
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        var visitor = await db.Queryable<Visitor>()
            .Where(v => v.VisitCode == trimmed)
            .FirstAsync();
        Decrypt(visitor);
        return visitor;
    }

    public async Task<List<Visitor>> GetActiveByPhoneAsync(string phone)
    {
        var db = await GetDbAsync();
        if (string.IsNullOrWhiteSpace(phone)) return new List<Visitor>();
        var trimmed = phone.Trim();
        var list = await db.Queryable<Visitor>()
            .Where(v => v.Phone == trimmed && v.Status == VisitStatus.CheckedIn)
            .OrderByDescending(v => v.CheckInTime)
            .ToListAsync();
        foreach (var v in list) Decrypt(v);
        return list;
    }

    public async Task<List<Visitor>> GetRecentAsync(int max = 30)
    {
        var db = await GetDbAsync();
        var list = await db.Queryable<Visitor>()
            .OrderByDescending(v => v.CheckInTime)
            .Take(max)
            .ToListAsync();
        foreach (var v in list) Decrypt(v);
        return list;
    }

    public async Task<int> CheckOutAsync(int id)
    {
        var db = await GetDbAsync();
        var now = DateTime.Now;
        // 原子条件更新：WHERE 带状态校验，单条 UPDATE 完成"在场才签退"，
        // 不再"先查后改"两次往返，天然防止并发重复签退。
        return await db.Updateable<Visitor>()
            .SetColumns(v => new Visitor { Status = VisitStatus.CheckedOut, CheckOutTime = now })
            .Where(v => v.Id == id && v.Status == VisitStatus.CheckedIn)
            .ExecuteCommandAsync();
    }

    /// <summary>读取后解密敏感字段；历史明文数据（无 dpapi: 前缀）按原样返回。</summary>
    private static void Decrypt(Visitor? visitor)
    {
        if (visitor is null) return;
        visitor.IdNumber = PiiProtector.ReadFlexible(visitor.IdNumber);
        visitor.Address = PiiProtector.ReadFlexible(visitor.Address);
        visitor.IdCardPhoto = PiiProtector.ReadFlexible(visitor.IdCardPhoto);
        visitor.FacePhoto = PiiProtector.ReadFlexible(visitor.FacePhoto);
    }
}
