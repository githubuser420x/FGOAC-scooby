using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class StartupDiagnostics
{
	public const string GameErrors = "4102：游戏程序异常停止（游戏原始资源：Unexpected Game Program Failure）。检查游戏退出码、崩溃转储、模块缺失和音频初始化。旧文件钩子在中文用户目录下存在路径转换缺陷，本版已修复；请完整应用更新，无需为此更改电脑账户名或固定安装到 C 盘。若仍出现 4102，需提供同次启动日志确认原因。\n\n4104 / 4105：AMDaemon 内部错误（Unexpected Error Occurred）。需查看同次启动的 amdaemon 日志；出现 Invalid app config / credit.max_credit 表示配置校验失败，Cannot open / Access denied 表示文件路径或权限问题。它们不是网络错误的专用编号。\n\n音频无声：本版将游戏四声道混合为双声道，通过 Windows 共享模式输出。先检查 Windows 默认输出与音量混合器是否静音；日志中的 WASAPI Initialize、endpoint mix 和 endpoint recovery 可区分格式与设备问题。\n\n0x88890004 / 0x88890026：音频设备或流失效；新版尝试重建共享流，并跟随默认输出设备切换。0x88890010：Windows 音频服务未运行。\n\n0xC0000005：程序访问冲突。请提供同次启动的日志和 ago-crash 转储。";

	public static string Explain(int code)
	{
		return code switch
		{
			2 => "运行文件缺失。请将完整更新包覆盖到安装根目录，保留 App、Server、AMFS、DEVICE 的目录结构；检查安全软件隔离记录。", 
			3 => "旧启动脚本找不到实体网卡。新版 auto 模式使用离线虚拟网络，请确认已同时更新 App/FGO_Launcher.ps1 和 fgohook.dll。", 
			4 => "目录不可写。管理员启动后检查提示的具体路径、磁盘空间、只读或写保护，以及安全软件的文件夹访问限制。", 
			5 => "没有可用的音频输出。先在 Windows 中启用扬声器或耳机并设为默认输出，再启动游戏。", 
			10 => "本地服务器启动失败。检查下方原始错误；常见情况是端口被占用、数据库启动失败、旧服务器仍在运行或目录不可写。日志位于 logs/server-control.log、artemis-stderr.log 和 mariadb.log。", 
			11 => "所需服务端口无法连接。单机请将服务器地址设为 auto，并启动本地服务器；远程模式检查所填地址及端口。auto 模式不需要联网。", 
			12 => "额外音频模块缺失。11.00 使用内置共享音频钩子，无需旧版 FGOAudio.dll；取消实验音频参数。", 
			13 => "配置文件 JSON 或游戏版本无效。检查 fgo-launcher.json、config.json 的语法，11.00 的版本值应为 11.00。", 
			14 => "显示器或分辨率配置无效。在前端重新选择当前显示器及有效宽高，再保存。", 
			15 => "运行环境检查未通过。根据下方缺失项补齐基础库、启用音频服务或恢复内置服务器文件；可在诊断与帮助页面点击“运行环境检查”查看完整报告。", 
			22 => "游戏异常退出或过早结束。查看日志中的最后一个失败项、异常码和崩溃转储；4102 只是游戏已停止的结果，不是网络故障的专用编号。", 
			_ => "启动脚本未正常完成。请查看下方原始错误和 logs/fgo-last-launch.log；不要仅凭退出编号判断原因。", 
		};
	}

	public static void CheckLayout()
	{
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		string text = Path.Combine(GamePaths.GameRoot, "FGO_StartupChecks.ps1");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException(Explain(2), text);
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(PowerShellHost.Executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = fullPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		string[] array = new string[7] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; . $env:FGO_CHECK_SCRIPT; Test-FgoWritableLayout -InstallRoot $env:FGO_CHECK_ROOT" };
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		processStartInfo.Environment["FGO_CHECK_SCRIPT"] = text;
		processStartInfo.Environment["FGO_CHECK_ROOT"] = fullPath;
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("无法运行目录检查。");
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		Task<string> task2 = process.StandardError.ReadToEndAsync();
		ProcessCompletion.WaitAsync(process, TimeSpan.FromSeconds(20.0)).GetAwaiter().GetResult();
		Task.WhenAll<string>(task, task2).WaitAsync(TimeSpan.FromSeconds(2.0)).GetAwaiter()
			.GetResult();
		if (process.ExitCode != 0)
		{
			throw new IOException(Explain(4) + "\n\n" + task2.GetAwaiter().GetResult() + task.GetAwaiter().GetResult());
		}
	}
}
