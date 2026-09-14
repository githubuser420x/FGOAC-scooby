using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class ServerSettingsView : UserControl, IComponentConnector
{
	private bool loaded;

	public event EventHandler? ConfigureRequested;

	public ServerSettingsView()
	{
		InitializeComponent();
	}

	public static int[] ConfiguredPorts()
	{
		string path = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		JsonNode jsonNode = ((!File.Exists(path)) ? null : JsonNode.Parse(File.ReadAllText(path))?["serverPorts"]);
		return new int[4]
		{
			jsonNode?["http"]?.GetValue<int>() ?? 80,
			jsonNode?["billing"]?.GetValue<int>() ?? 8443,
			jsonNode?["aime"]?.GetValue<int>() ?? 22345,
			jsonNode?["database"]?.GetValue<int>() ?? 3307
		};
	}

	public string[] ApplyArguments()
	{
		string[] array = new string[4] { "http", "billing", "aime", "database" };
		int[] array2 = new string[4] { HttpBox.Text, BillingBox.Text, AimeBox.Text, DatabaseBox.Text }.Select(delegate(string value)
		{
			if (!int.TryParse(value, out var result) || result < 1 || result > 65535)
			{
				throw new ArgumentException("Each port must be a whole number from 1 to 65535.");
			}
			return result;
		}).ToArray();
		if (array2.Distinct().Count() != 4)
		{
			throw new ArgumentException("The four services cannot share a port.");
		}
		List<string> list = new List<string>
		{
			"apply",
			"--host",
			HostBox.Text.Trim()
		};
		for (int num = 0; num < 4; num++)
		{
			list.Add("--" + array[num]);
			list.Add(array2[num].ToString());
		}
		return list.ToArray();
	}

	public static async Task<JsonNode> RunTool(params string[] args)
	{
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server"));
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(fullPath, "python", "python.exe"))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = fullPath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		processStartInfo.ArgumentList.Add(Path.Combine(fullPath, "tools", "fgo_server_config.py"));
		foreach (string item in args)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Could not run the server configuration tool.");
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		await ProcessCompletion.WaitAsync(process, TimeSpan.FromSeconds(15.0));
		await Task.WhenAll<string>(output, error).WaitAsync(TimeSpan.FromSeconds(2.0));
		string text = await output;
		string text2 = await error;
		JsonNode jsonNode = (string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
		if (process.ExitCode != 0)
		{
			throw new IOException(jsonNode?["error"]?.GetValue<string>() ?? text2);
		}
		return jsonNode ?? throw new IOException("No configuration came back from the server tool.");
	}

	public async Task ReloadAsync()
	{
		_ = 1;
		try
		{
			JsonNode config = await RunTool("show");
			HostBox.Text = config["host"].ToString();
			HttpBox.Text = config["http"].ToString();
			BillingBox.Text = config["billing"].ToString();
			AimeBox.Text = config["aime"].ToString();
			DatabaseBox.Text = config["database"].ToString();
			List<string> lines = new List<string>
			{
				$"Server address: {config["address"]}",
				"Program: Server/python/python.exe",
				"Save file: Server/state/fgo-players.json",
				"Database: Server/data/mariadb",
				"Log directory: logs",
				""
			};
			Dictionary<string, string> labels = new Dictionary<string, string>
			{
				["http"] = "Game / ALL.Net",
				["billing"] = "Billing HTTPS",
				["aime"] = "Aime card reader",
				["database"] = "Local database"
			};
			string[] array = new string[4] { "http", "billing", "aime", "database" };
			foreach (string key in array)
			{
				int port = config[key].GetValue<int>();
				bool open = false;
				using TcpClient socket = new TcpClient();
				using CancellationTokenSource timeout = new CancellationTokenSource(500);
				_ = 1;
				try
				{
					await socket.ConnectAsync("127.0.0.1", port, timeout.Token);
					open = true;
				}
				catch
				{
				}
				lines.Add($"{labels[key]} · {port}: {(open ? "listening" : "not running")}");
			}
			InfoText.Text = string.Join(Environment.NewLine, lines);
		}
		catch (Exception ex)
		{
			InfoText.Text = ex.Message;
		}
	}

	private void Configure_OnClick(object sender, RoutedEventArgs e)
	{
		this.ConfigureRequested?.Invoke(this, EventArgs.Empty);
	}

	private void Defaults_OnClick(object sender, RoutedEventArgs e)
	{
		HostBox.Text = "auto";
		HttpBox.Text = "80";
		BillingBox.Text = "8443";
		AimeBox.Text = "22345";
		DatabaseBox.Text = "3307";
	}

	private async void Refresh_OnClick(object sender, RoutedEventArgs e)
	{
		await ReloadAsync();
	}

	private async void View_OnLoaded(object sender, RoutedEventArgs e)
	{
		if (!loaded)
		{
			loaded = true;
			await ReloadAsync();
		}
	}
}
