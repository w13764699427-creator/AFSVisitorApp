using Microsoft.AspNetCore.Components.WebView;

namespace VisitorApp;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	/// <summary>
	/// WebView 初始化完成后订阅权限请求：自助机场景对摄像头 / 麦克风直接放行，
	/// 避免页面每次调用 getUserMedia 都弹“是否允许使用摄像头”的确认框。
	/// </summary>
	private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if WINDOWS
		e.WebView.CoreWebView2.PermissionRequested += (_, args) =>
		{
			if (args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera ||
			    args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone)
			{
				args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
				args.Handled = true;
			}
		};
#endif
	}
}
