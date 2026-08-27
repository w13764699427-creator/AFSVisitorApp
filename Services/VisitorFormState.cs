using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// Scoped 状态：在 Consent → Register → Success 之间保留访客填写的内容，
/// 避免页面刷新或路由跳转丢失。Success 完成后调用 ResetCurrent。
/// </summary>
public class VisitorFormState
{
    public VisitorForm Current { get; private set; } = new();

    /// <summary>当前流程最终生成的访客记录（成功页用于展示凭证）。</summary>
    public Visitor? LastSubmitted { get; set; }

    public void ResetCurrent()
    {
        Current.Reset();
    }

    public void ResetAll()
    {
        Current = new VisitorForm();
        LastSubmitted = null;
    }

    public static string GenerateVisitCode()
    {
        // 6 位访客流水号：日期前缀 + 随机数字，便于前台口播 / 手填。
        var rand = Random.Shared.Next(1000, 9999);
        var datePart = DateTime.Now.ToString("MMdd");
        return $"V{datePart}{rand}";
    }
}
