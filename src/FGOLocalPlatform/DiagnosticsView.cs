using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class DiagnosticsView : UserControl, IComponentConnector
{
	public DiagnosticsView()
	{
		InitializeComponent();
		ErrorGuideText.Text = "4102：游戏程序异常停止（游戏原始资源：Unexpected Game Program Failure）。检查游戏退出码、崩溃转储、模块缺失和音频初始化。旧文件钩子在中文用户目录下存在路径转换缺陷，本版已修复；请完整应用更新，无需为此更改电脑账户名或固定安装到 C 盘。若仍出现 4102，需提供同次启动日志确认原因。\n\n4104 / 4105：AMDaemon 内部错误（Unexpected Error Occurred）。需查看同次启动的 amdaemon 日志；出现 Invalid app config / credit.max_credit 表示配置校验失败，Cannot open / Access denied 表示文件路径或权限问题。它们不是网络错误的专用编号。\n\n音频无声：本版将游戏四声道混合为双声道，通过 Windows 共享模式输出。先检查 Windows 默认输出与音量混合器是否静音；日志中的 WASAPI Initialize、endpoint mix 和 endpoint recovery 可区分格式与设备问题。\n\n0x88890004 / 0x88890026：音频设备或流失效；新版尝试重建共享流，并跟随默认输出设备切换。0x88890010：Windows 音频服务未运行。\n\n0xC0000005：程序访问冲突。请提供同次启动的日志和 ago-crash 转储。\n\n" + string.Join("\n\n", new int[11]
		{
			2, 3, 4, 5, 10, 11, 12, 13, 14, 15,
			22
		}.Select((int code) => $"启动器 {code}：{StartupDiagnostics.Explain(code)}"));
	}

	private async void EnvironmentCheck_OnClick(object sender, RoutedEventArgs e)
	{
		EnvironmentCheckButton.IsEnabled = false;
		EnvironmentCheckText.Text = "正在检查运行环境…";
		try
		{
			TextBlock environmentCheckText = EnvironmentCheckText;
			environmentCheckText.Text = await RuntimeDiagnostics.CheckAsync();
		}
		catch (Exception ex)
		{
			EnvironmentCheckText.Text = ex.Message;
		}
		finally
		{
			EnvironmentCheckButton.IsEnabled = true;
		}
	}
}
