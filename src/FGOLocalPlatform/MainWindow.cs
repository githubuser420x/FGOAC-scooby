using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using DeckReaderUI;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private sealed class CapturedProcess
	{
		public Process Process { get; }

		public Task StreamsCompleted { get; }

		public CapturedProcess(Process process, Task streamsCompleted)
		{
			Process = process;
			StreamsCompleted = streamsCompleted;
		}
	}

	private sealed class AccountEntry
	{
		public int AimeId { get; init; }

		public string MasterName { get; init; } = "";

		public string AccountMode { get; init; } = "";

		public string AccessCode { get; init; } = "";

		public bool IsCurrent { get; init; }

		public int ServantCount { get; init; }

		public int SummonResultCount { get; init; }

		public int OwnedCardCount { get; init; }

		public int OwnedCardCopyCount { get; init; }

		public int PendingPrintCount { get; init; }

		public HashSet<int> OwnedTradingCardIds { get; init; } = new HashSet<int>();

		public Dictionary<int, int> OwnedCardCounts { get; init; } = new Dictionary<int, int>();

		public List<SummonHistoryEntry> SummonHistory { get; init; } = new List<SummonHistoryEntry>();

		public int MasterLevel { get; init; } = 1;

		public int MasterExp { get; init; }

		public int MasterLevelExp { get; init; }

		public int MasterNextLevelExp { get; init; }

		public int MasterExpToNext { get; init; }

		public int QpAmount { get; init; }

		public int FriendPointAmount { get; init; }

		public int ManaPrismAmount { get; init; }

		public int SummonPointAmount { get; init; }

		public int ClearedQuestCount { get; init; }

		public int TrackedQuestCount { get; init; }

		public string ModeLabel
		{
			get
			{
				string accountMode = AccountMode;
				if (!(accountMode == "test_full"))
				{
					if (accountMode == "normal")
					{
						return "Normal";
					}
					return AccountMode;
				}
				return "All Servants (test)";
			}
		}

		public string DisplayText => $"{MasterName} (ID {AimeId}) - Lv.{MasterLevel} - {ModeLabel}{(IsCurrent ? " (current)" : "")}";

		public override string ToString()
		{
			return DisplayText;
		}
	}

	private sealed class SummonHistoryEntry
	{
		public string ConfirmedAt { get; init; } = "";

		public int LotteryType { get; init; }

		public int LineupId { get; init; }

		public int DrawIndex { get; init; }

		public int DrawCount { get; init; }

		public int TradingCardId { get; init; }

		public int ServantId { get; init; }

		public int CraftEssenceId { get; init; }

		public int CardTypeId { get; init; } = 1;

		public string DisplayName { get; init; } = "";

		public string Sid { get; init; } = "";

		public string CardTypeLabel
		{
			get
			{
				if (CardTypeId != 2)
				{
					return "Servant";
				}
				return "Craft Essence";
			}
		}

		public int EntityId
		{
			get
			{
				if (CardTypeId != 2)
				{
					return ServantId;
				}
				return CraftEssenceId;
			}
		}

		public string DrawPosition
		{
			get
			{
				if (DrawCount <= 1)
				{
					return "Single";
				}
				return $"{DrawIndex}/{DrawCount}";
			}
		}

		public string PoolLabel => LotteryType switch
		{
			2 => "Friend Point", 
			3 => "Battle", 
			5 => $"Pickup {LineupId}", 
			6 => "Summon Point x1", 
			7 => "Summon Point x10", 
			_ => (LineupId > 0) ? $"Pool {LineupId}" : "History", 
		};
	}

	private sealed class AccountToolResult
	{
		public bool Ok { get; init; }

		public string Error { get; init; } = "";

		public string Message { get; init; } = "";

		public string Stderr { get; init; } = "";

		public string RawOutput { get; init; } = "";

		public JsonObject? Root { get; init; }
	}

	private sealed class BenefitGrantEntry
	{
		public string Key { get; init; } = "";

		public string Category { get; init; } = "";

		public string Name { get; init; } = "";

		public string ItemId { get; init; } = "";

		public int Current { get; init; }

		public int MaxAmount { get; init; }

		public int GrantAmount { get; set; }

		public string GrantText { get; set; } = "0";
	}

	private sealed record CardIconRow(IReadOnlyList<CardStack> Cards);

	private const int MaxSelection = 30;

	private const int MaxLauncherPendingCharacters = 262144;

	private const int MaxLauncherVisibleCharacters = 524288;

	private const int TrimmedLauncherVisibleCharacters = 393216;

	private const int MaxLauncherUiBatchCharacters = 65536;

	private static readonly IReadOnlyDictionary<string, (int Width, int Height)[]> ResolutionPresets = new Dictionary<string, (int, int)[]>
	{
		["16:9"] = new(int, int)[7]
		{
			(1280, 720),
			(1600, 900),
			(1920, 1080),
			(2560, 1440),
			(3840, 2160),
			(5120, 2880),
			(7680, 4320)
		},
		["16:10"] = new(int, int)[7]
		{
			(1280, 800),
			(1440, 900),
			(1680, 1050),
			(1920, 1200),
			(2560, 1600),
			(3840, 2400),
			(5120, 3200)
		},
		["21:9"] = new(int, int)[7]
		{
			(1680, 720),
			(2560, 1080),
			(3360, 1440),
			(3440, 1440),
			(3840, 1600),
			(5040, 2160),
			(5120, 2160)
		},
		["32:9"] = new(int, int)[4]
		{
			(2560, 720),
			(3840, 1080),
			(5120, 1440),
			(7680, 2160)
		},
		["4:3"] = new(int, int)[6]
		{
			(1280, 960),
			(1600, 1200),
			(1920, 1440),
			(2560, 1920),
			(3200, 2400),
			(5760, 4320)
		},
		["9:16"] = new(int, int)[7]
		{
			(720, 1280),
			(900, 1600),
			(1080, 1920),
			(1440, 2560),
			(2160, 3840),
			(2880, 5120),
			(4320, 7680)
		},
		["10:16"] = new(int, int)[7]
		{
			(800, 1280),
			(900, 1440),
			(1050, 1680),
			(1200, 1920),
			(1600, 2560),
			(2400, 3840),
			(3200, 5120)
		},
		["9:21"] = new(int, int)[7]
		{
			(720, 1680),
			(1080, 2560),
			(1440, 3360),
			(1440, 3440),
			(1600, 3840),
			(2160, 5040),
			(2160, 5120)
		},
		["9:32"] = new(int, int)[4]
		{
			(720, 2560),
			(1080, 3840),
			(1440, 5120),
			(2160, 7680)
		},
		["3:4"] = new(int, int)[8]
		{
			(720, 960),
			(900, 1200),
			(1080, 1440),
			(1200, 1600),
			(1440, 1920),
			(1920, 2560),
			(2400, 3200),
			(4320, 5760)
		}
	};

	private readonly Config config;

	private readonly CardCollection cardCollection;

	private readonly ICollectionView cardListView;

	private readonly ICollectionView selectedCardListView;

	private readonly List<CardStack> availableCardStacks = new List<CardStack>();

	private readonly List<CardStack> selectedCardStacks = new List<CardStack>();

	private readonly DataTemplate cardListItemTemplate;

	private readonly ItemsPanelTemplate cardListItemsPanel;

	private readonly DataTemplate cardIconRowTemplate;

	private readonly DispatcherTimer statusTimer;

	private readonly DispatcherTimer launcherOutputTimer;

	private DateTime lastAccountProfileWriteUtc = DateTime.MinValue;

	private bool accountAutoRefreshRunning;

	private readonly object launcherOutputLock = new object();

	private readonly Queue<string> launcherOutputQueue = new Queue<string>();

	private bool refreshingStatus;

	private bool refreshingLogs;

	private bool launcherLogOwned;

	private bool launcherOutputCompleted;

	private bool launcherProcessRunning;

	private CapturedProcess? runningLauncher;

	private bool launcherCancellationRequested;

	private bool serverConfiguring;

	private bool stoppingServer;

	private bool windowClosing;

	private bool closeReady;

	private CancellationTokenSource? serverCommandCancellation;

	private Task<int>? serverCommandTask;

	private int launcherQueuedCharacters;

	private int launcherDroppedLineCount;

	private bool updatingResolutionOptions;

	private string currentCardSearch = "";

	private List<CardStack> filteredCardItems = new List<CardStack>();

	private bool iconCardView;

	private int iconColumnCount;

	private Card? selectedIconCard;

	private Button? selectedIconButton;

	private string serverLogSnapshot = "";

	private string injectionLogSnapshot = "";

	private Task? stopServerTask;

	private SummonSettingsWindow? summonSettingsPanel;

	private bool accountToolRunning;

	private bool firstRunning;

	private bool lastKnownServerRunning;

	private bool lastKnownGameRunning;

	private bool logsCollapsed;

	private double expandedLogWidth = 440.0;

	private PhotoWindow? photoWindow;

	private int hideUiVirtualKey = 121;

	private bool bindingHideUiKey;

	private static string AccountToolPythonPath => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "python", "python.exe"));

	private static string AccountToolScriptPath => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "tools", "fgo_account.py"));

	private static string AccountToolWorkingDirectory => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "artemis"));

	private AccountEntry? SelectedAccount => AccountComboBox.SelectedItem as AccountEntry;

	private ComboBox InputModeComboBox => ControlsPanel.InputModeSelector;

	public MainWindow()
	{
		//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0218: Expected O, but got Unknown
		//IL_0230: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_024e: Expected O, but got Unknown
		InitializeComponent();
		ApplyProtectedBranding();
		// The channel name is hashed from the game folder, the same way the launch script hands it
		// to the hook, so the root has to be right before the channel is opened.
		GameCommunication.GameRoot = GamePaths.GameRoot;
		GameCommunication.Initialize();
		config = Config.Load(Path.Combine(GamePaths.GameRoot, "deck.json"));
		GamePaths.ResolveMovedCards(config, GamePaths.GameRoot);
		TrcDB trcDb = new TrcDB("trcdb.json");
		cardCollection = new CardCollection(trcDb, config.CardsPath, config.SelectedCards, config.SelectedCardCopies);
		cardListView = CollectionViewSource.GetDefaultView(availableCardStacks);
		selectedCardListView = CollectionViewSource.GetDefaultView(selectedCardStacks);
		cardListItemTemplate = CardList.ItemTemplate;
		cardListItemsPanel = CardList.ItemsPanel;
		cardIconRowTemplate = (DataTemplate)FindResource("CardIconRowTemplate");
		CardList.ItemsSource = (IEnumerable)cardListView;
		SelectedCardList.ItemsSource = (IEnumerable)selectedCardListView;
		CardTypeComboBox.SelectionChanged += CardTypeComboBox_OnSelectionChanged;
		CardList.SizeChanged += CardList_OnSizeChanged;
		CardsPathTextBox.Text = config.CardsPath;
		SearchTextBox.Text = "";
		LoadLauncherSettings();
		LoadLayoutSettings();
		try
		{
			PublishDeck();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException)
		{
			// A deck that cannot be published must not keep the window from opening.
			RuntimeStatusText.Text = "The deck could not be published: " + ex.Message;
		}
		UpdateDeckStatus();
		OwnedCardsOnlyCheckBox.IsChecked = true;
		SetCardViewMode(useIcons: true);
		ApplyOwnedCardFilter();
		launcherOutputTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(125.0)
		};
		launcherOutputTimer.Tick += delegate
		{
			FlushLauncherOutput();
		};
		statusTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.0)
		};
		statusTimer.Tick += async delegate
		{
			// Nothing on screen to update while the window is out of the way.
			if (base.WindowState == WindowState.Minimized)
			{
				return;
			}
			await RefreshRuntimeStatusAsync();
			await RefreshLogPanelsAsync();
			await RefreshAccountsIfChangedAsync();
			ApplyProtectedBranding();
		};
		statusTimer.Start();
		base.Loaded += async delegate
		{
			// Photo mode opens the game's model archives, so it is built when its tab is first
			// shown rather than on every start.
			await RefreshRuntimeStatusAsync();
			await RefreshLogPanelsAsync();
			await RefreshAccountsAsync(showErrors: false);
			await RunFirstRunAsync();
			AboutVersionText.Text = "Version " + UpdateSettings.Version + ", an English build of the FGO Arcade local platform.";
			SectionTabs.Tag = UpdateSettings.Version;
			await CheckForUpdateAsync(announce: false);
		};
	}

	private async Task RunFirstRunAsync()
	{
		firstRunning = true;
		accountToolRunning = true;
		SetAccountControlsEnabled(enabled: false);
		try
		{
			FirstRun firstRun = new FirstRun(this, async (string[] arguments) =>
			{
				AccountToolResult accountToolResult = await RunAccountToolAsync(arguments);
				return new FirstRun.ToolResult(accountToolResult.Ok, string.IsNullOrWhiteSpace(accountToolResult.Message) ? accountToolResult.Error : accountToolResult.Message, accountToolResult.Root);
			}, delegate(string message)
			{
				RuntimeStatusText.Text = message;
			}, AppendAccountLog);
			if (await firstRun.RunAsync())
			{
				LoadLauncherSettings();
				await RefreshAccountsAsync(showErrors: false);
			}
		}
		catch (Exception ex)
		{
			AppendAccountLog("First-run setup error: " + ex.Message);
			ThemedMessageBox.Show(this, "The first-run setup did not finish:\n" + ex.Message + "\n\nYou can still set everything up by hand from the Account and Settings pages.", "FGOAC scooby - First Run", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, foreground: true);
		}
		finally
		{
			firstRunning = false;
			accountToolRunning = false;
			SetAccountControlsEnabled(enabled: true);
			await RefreshRuntimeStatusAsync();
		}
	}

	private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
	{
		if (closeReady)
		{
			return;
		}
		e.Cancel = true;
		if (windowClosing || (summonSettingsPanel != null && !summonSettingsPanel.DiscardConfirmed()))
		{
			return;
		}
		windowClosing = true;
		statusTimer.Stop();
		launcherOutputTimer.Stop();
		RuntimeStatusText.Text = "Closing the launcher and shutting down the local server...";
		try
		{
			SaveLayoutSettings();
			PublishDeck();
			if (!IsThisGameRunning())
			{
				CancelPendingLauncher();
				await StopLocalServerAsync();
			}
			else
			{
				await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0));
				string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "Stop-FGOLocalServerWhenIdle.ps1"));
				if (!File.Exists(fullPath))
				{
					throw new FileNotFoundException("The server cleanup script that runs after the game exits is missing", fullPath);
				}
				ProcessStartInfo processStartInfo = new ProcessStartInfo(PowerShellHost.Executable)
				{
					UseShellExecute = false,
					CreateNoWindow = true
				};
				string[] array = new string[8]
				{
					"-NoProfile",
					"-NonInteractive",
					"-ExecutionPolicy",
					"Bypass",
					"-File",
					fullPath,
					"-FrontendProcessId",
					Environment.ProcessId.ToString()
				};
				foreach (string item in array)
				{
					processStartInfo.ArgumentList.Add(item);
				}
				using (Process.Start(processStartInfo) ?? throw new IOException("Could not start the server cleanup process"))
				{
				}
			}
		}
		catch (Exception ex)
		{
			AppendServerControlLog("Closing the launcher: " + ex.Message);
			windowClosing = false;
			statusTimer.Start();
			launcherOutputTimer.Start();
			RuntimeStatusText.Text = "The server did not shut down completely and the launcher stayed open: " + ex.Message;
			return;
		}
		closeReady = true;
		Close();
	}

	/// <summary>
	/// Reading MainModule opens the process and walks its module list, which is far too much to do
	/// every two seconds, so the answer is remembered per process id. In the normal case there is no
	/// ago.exe at all and the check costs one process enumeration.
	/// </summary>
	private static readonly Dictionary<int, bool> GameProcessCache = new Dictionary<int, bool>();

	private static bool IsThisGameRunning()
	{
		string b = Path.Combine(GamePaths.GameRoot, "ago.exe");
		Process[] processesByName = Process.GetProcessesByName("ago");
		if (processesByName.Length == 0)
		{
			GameProcessCache.Clear();
			return false;
		}
		bool found = false;
		HashSet<int> alive = new HashSet<int>();
		foreach (Process process in processesByName)
		{
			using (process)
			{
				alive.Add(process.Id);
				if (!GameProcessCache.TryGetValue(process.Id, out var isOurs))
				{
					isOurs = false;
					try
					{
						isOurs = string.Equals(process.MainModule?.FileName, b, StringComparison.OrdinalIgnoreCase);
						GameProcessCache[process.Id] = isOurs;
					}
					catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException) ? 1 : 0) != 0)
					{
						// The module list is not readable while the game is still starting; ask again next tick.
					}
				}
				found |= isOurs;
			}
		}
		foreach (int gone in GameProcessCache.Keys.Where((int id) => !alive.Contains(id)).ToList())
		{
			GameProcessCache.Remove(gone);
		}
		return found;
	}

	private Updater.Release availableUpdate;

	private DateTime statusHoldUntil = DateTime.MinValue;

	/// <summary>A status sentence the player should get to read before the two-second tick replaces it.</summary>
	private void HoldStatus(string text)
	{
		RuntimeStatusText.Text = text;
		statusHoldUntil = DateTime.UtcNow.AddSeconds(12.0);
	}

	/// <summary>
	/// Looks for a newer release. On startup a failure says nothing on screen and only reaches
	/// logs\update.log; the button on the About page reports either way, in one sentence.
	/// </summary>
	private async Task CheckForUpdateAsync(bool announce)
	{
		if (announce)
		{
			AboutUpdateStatusText.Text = "Checking...";
		}
		try
		{
			availableUpdate = await Updater.CheckAsync();
		}
		catch (Exception ex)
		{
			Updater.Log("Check failed: " + ex);
			if (announce)
			{
				AboutUpdateStatusText.Text = "The update check could not reach GitHub: " + ex.Message + ". Check your connection, or open " + UpdateSettings.ReleasesUrl + " yourself.";
			}
			return;
		}
		if (availableUpdate == null)
		{
			if (announce)
			{
				AboutUpdateStatusText.Text = "You are on the latest version (" + UpdateSettings.Version + ").";
			}
			return;
		}
		UpdateBannerText.Text = "Update " + availableUpdate.Version + " is available.";
		UpdateBanner.Visibility = Visibility.Visible;
		if (announce)
		{
			AboutUpdateStatusText.Text = "Update " + availableUpdate.Version + " is available. Install it from the banner on the Play page.";
		}
	}

	private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
	{
		Button button = (Button)sender;
		button.IsEnabled = false;
		try
		{
			await CheckForUpdateAsync(announce: true);
			// The header reads the status line, so the answer has to go there as well, and stay
			// long enough to read: the two-second refresh writes over it otherwise.
			RuntimeStatusText.Text = AboutUpdateStatusText.Text;
			statusHoldUntil = DateTime.UtcNow.AddSeconds(12.0);
		}
		finally
		{
			button.IsEnabled = true;
		}
	}

	private void UpdateLaterButton_OnClick(object sender, RoutedEventArgs e)
	{
		UpdateBanner.Visibility = Visibility.Collapsed;
	}

	private async void UpdateInstallButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (availableUpdate == null)
		{
			return;
		}
		Updater.Release release = availableUpdate;
		UpdateBanner.Visibility = Visibility.Collapsed;
		StartGameButton.IsEnabled = false;
		string installRoot = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		// The installer's output arrives on a background thread.
		bool installed = await Updater.InstallAsync(release, installRoot, delegate(string message)
		{
			base.Dispatcher.Invoke(delegate
			{
				RuntimeStatusText.Text = message;
				AppendInjectionLog(message + Environment.NewLine);
			});
		}, CancellationToken.None);
		if (!installed)
		{
			await RefreshRuntimeStatusAsync();
			return;
		}
		availableUpdate = null;
		AboutUpdateStatusText.Text = "Version " + release.Version + " is installed.";
		if (Updater.HasStagedLauncher(out var stagedPath))
		{
			RuntimeStatusText.Text = "Version " + release.Version + " is ready - the launcher restarts to finish.";
			Updater.SwapAndRestart(stagedPath);
			Close();
			return;
		}
		RuntimeStatusText.Text = "Version " + release.Version + " is installed. Close and reopen the launcher to run it.";
	}

	private void AboutLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
	{
		e.Handled = true;
		try
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show(this, "Could not open the link: " + ex.Message, "About", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
		}
		else if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void CloseButton_OnClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ApplyProtectedBranding()
	{
		base.Title = ProtectedBranding.WindowTitle;
		HeaderBrandText.Text = ProtectedBranding.HeaderText;
	}

	private void PublishDeck()
	{
		GameCommunication.GameRoot = GamePaths.GameRoot;
		config.CardsPath = cardCollection.Path;
		config.SelectedCards = cardCollection.GetSelectedPaths();
		config.SelectedCardCopies = cardCollection.SelectedCards.Select((Card c) => c.CopyNumber).ToList();
		config.Save(Path.Combine(GamePaths.GameRoot, "deck.json"));
		if (!GameCommunication.UpdateCards(cardCollection.SelectedCards))
		{
			HoldStatus("The shared deck buffer is busy - the game will read deck.json instead");
		}
		UpdateDeckStatus();
	}

	private void UpdateDeckStatus()
	{
		DeckStatusText.Text = $"{cardCollection.SelectedCards.Count} of 30 cards - FGO 11.00 format 6";
		UpdateHeroCard();
	}

	/// <summary>
	/// The Play page shows the first card of the deck, which is the Servant the cabinet reads first.
	/// With an empty deck it shows the game's own mark instead.
	/// </summary>
	private void UpdateHeroCard()
	{
		Card lead = cardCollection.SelectedCards.FirstOrDefault();
		HeroCardImage.Source = lead?.Thumbnail;
		bool haveCard = HeroCardImage.Source != null;
		HeroCardImage.Visibility = (haveCard ? Visibility.Visible : Visibility.Collapsed);
		HeroFallbackImage.Visibility = (haveCard ? Visibility.Collapsed : Visibility.Visible);
	}

	private void Reload()
	{
		string text = CardsPathTextBox.Text.Trim();
		if (text.Length != 0)
		{
			List<string> selectedPaths = cardCollection.GetSelectedPaths();
			List<int> selectedCopies = cardCollection.SelectedCards.Select((Card c) => c.CopyNumber).ToList();
			cardCollection.Path = text;
			cardCollection.Reload(selectedPaths, null, selectedCopies);
			cardListView.Refresh();
			selectedCardListView.Refresh();
			ApplyOwnedCardFilter();
			PublishDeck();
		}
	}

	private void ApplyOwnedCardFilter()
	{
		AccountEntry selectedAccount = SelectedAccount;
		if (selectedAccount != null && cardCollection.SyncOwnedCopies(selectedAccount.OwnedCardCounts))
		{
			selectedCardListView.Refresh();
			PublishDeck();
		}
		bool ownedOnly = OwnedCardsOnlyCheckBox.IsChecked == true && selectedAccount != null;
		availableCardStacks.Clear();
		availableCardStacks.AddRange(CardStack.BuildEntities(cardCollection.Cards.Concat(cardCollection.SelectedCards)));
		selectedCardStacks.Clear();
		selectedCardStacks.AddRange(CardStack.Build(cardCollection.SelectedCards));
		selectedCardListView.Refresh();
		HashSet<int> ownedIds = selectedAccount?.OwnedTradingCardIds ?? new HashSet<int>();
		int selectedCardType = SelectedCardTypeId();
		cardListView.Filter = delegate(object item)
		{
			if (!(item is CardStack cardStack))
			{
				return false;
			}
			Card card = cardStack.Card;
			if (selectedCardType != 0 && card.CardTypeId != selectedCardType)
			{
				return false;
			}
			if (ownedOnly && !cardStack.Variants.Any((Card c) => ownedIds.Contains(c.TrcId)))
			{
				return false;
			}
			if (currentCardSearch.Length == 0)
			{
				return true;
			}
			return cardStack.EnglishName.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || cardStack.JapaneseName.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || cardStack.EntityLabel.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || cardStack.Variants.Any((Card c) => c.FileName.Contains(currentCardSearch, StringComparison.OrdinalIgnoreCase) || c.TrcId.ToString().Contains(currentCardSearch));
		};
		cardListView.Refresh();
		filteredCardItems = ((IEnumerable)cardListView).Cast<CardStack>().ToList();
		int count = filteredCardItems.Count;
		UpdateCardItemsSource();
		int value = availableCardStacks.Count((CardStack stack) => stack.Card.CardTypeId == 1);
		int value2 = availableCardStacks.Count((CardStack stack) => stack.Card.CardTypeId == 2);
		string value3 = selectedCardType switch
		{
			1 => $"Servants {value:N0}", 
			2 => $"Craft Essences {value2:N0}", 
			_ => $"Servants {value:N0} / Craft Essences {value2:N0}", 
		};
		CardCatalogSummaryText.Text = $"{value3} - {count:N0} shown - drag into the deck, or double-click to pick art and quantity";
	}

	private int SelectedCardTypeId()
	{
		if (CardTypeComboBox.SelectedItem is ComboBoxItem { Tag: var tag } && int.TryParse(tag?.ToString(), out var result))
		{
			return result;
		}
		return 0;
	}

	private void CardTypeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		ApplyOwnedCardFilter();
	}

	private void OwnedCardsOnlyCheckBox_OnChanged(object sender, RoutedEventArgs e)
	{
		if (cardListView != null)
		{
			ApplyOwnedCardFilter();
		}
	}

	private void Search()
	{
		currentCardSearch = SearchTextBox.Text.Trim();
		ApplyOwnedCardFilter();
	}

	private void CardPickerSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		if (cardListView != null && sender is TextBox textBox)
		{
			currentCardSearch = textBox.Text.Trim();
			ApplyOwnedCardFilter();
		}
	}

	private void SetCardViewMode(bool useIcons)
	{
		iconCardView = useIcons;
		ClearIconSelection();
		CardList.ItemTemplate = (useIcons ? cardIconRowTemplate : cardListItemTemplate);
		CardList.ItemsPanel = cardListItemsPanel;
		ScrollViewer.SetCanContentScroll((DependencyObject)(object)CardList, canContentScroll: true);
		VirtualizingPanel.SetIsVirtualizing((DependencyObject)(object)CardList, value: true);
		VirtualizingPanel.SetVirtualizationMode((DependencyObject)(object)CardList, VirtualizationMode.Recycling);
		SetViewButtonState(ListViewModeButton, !useIcons);
		SetViewButtonState(IconViewModeButton, useIcons);
		UpdateCardItemsSource();
	}

	private static void SetViewButtonState(Button button, bool active)
	{
		if (!active)
		{
			((DependencyObject)button).ClearValue(Control.BackgroundProperty);
			((DependencyObject)button).ClearValue(Control.BorderBrushProperty);
		}
		else
		{
			button.Background = (Brush)Application.Current.Resources["PlateHighBrush"];
			button.BorderBrush = (Brush)Application.Current.Resources["IceBrush"];
		}
	}

	private void UpdateCardItemsSource()
	{
		if (!iconCardView)
		{
			CardList.ItemsSource = (IEnumerable)cardListView;
			return;
		}
		int columns = Math.Max(1, (int)Math.Floor(Math.Max(144.0, CardList.ActualWidth - 18.0) / 144.0));
		iconColumnCount = columns;
		CardList.ItemsSource = (from item in filteredCardItems.Select((CardStack card, int index) => new { card, index })
			group item by item.index / columns into @group
			select new CardIconRow(@group.Select(item => item.card).ToArray())).ToList();
	}

	private void ListViewModeButton_OnClick(object sender, RoutedEventArgs e)
	{
		SetCardViewMode(useIcons: false);
	}

	private void IconViewModeButton_OnClick(object sender, RoutedEventArgs e)
	{
		SetCardViewMode(useIcons: true);
	}

	private void CardList_OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		if (iconCardView)
		{
			Size newSize = e.NewSize;
			if (Math.Max(1, (int)Math.Floor(Math.Max(144.0, newSize.Width - 18.0) / 144.0)) != iconColumnCount)
			{
				UpdateCardItemsSource();
			}
		}
	}

	private void IconCardButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: CardStack tag } button)
		{
			Card card = tag.Card;
			if (selectedIconButton != null && selectedIconButton != button)
			{
				((DependencyObject)selectedIconButton).ClearValue(Control.BackgroundProperty);
				((DependencyObject)selectedIconButton).ClearValue(Control.BorderBrushProperty);
			}
			selectedIconCard = card;
			selectedIconButton = button;
			button.Background = new SolidColorBrush(Color.FromRgb(53, 24, 50));
			button.BorderBrush = new SolidColorBrush(Color.FromRgb(168, 79, 150));
			SelectedCardList.UnselectAll();
		}
	}

	private void IconCardButton_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		IconCardButton_OnClick(sender, e);
		AddSelectedCard();
		e.Handled = true;
	}

	private void ClearIconSelection()
	{
		if (selectedIconButton != null)
		{
			((DependencyObject)selectedIconButton).ClearValue(Control.BackgroundProperty);
			((DependencyObject)selectedIconButton).ClearValue(Control.BorderBrushProperty);
		}
		selectedIconButton = null;
		selectedIconCard = null;
	}

	private async void BuildThumbnailCacheButton_OnClick(object sender, RoutedEventArgs e)
	{
		BuildThumbnailCacheButton.IsEnabled = false;
		List<Card> cards = (from @group in cardCollection.Cards.Concat(cardCollection.SelectedCards).GroupBy<Card, string>((Card card) => card.Path, StringComparer.OrdinalIgnoreCase)
			select @group.First()).ToList();
		try
		{
			var (value, num) = await Task.Run(() => Card.WarmThumbnailCache(cards, delegate(int processed, int total)
			{
				((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Func<object>)(() => BuildThumbnailCacheButton.Content = $"Caching {processed:N0}/{total:N0}"), Array.Empty<object>());
			}));
			BuildThumbnailCacheButton.Content = ((num == 0) ? $"Cached +{value:N0}" : $"Done +{value:N0} / failed {num:N0}");
		}
		finally
		{
			BuildThumbnailCacheButton.IsEnabled = true;
		}
	}

	private void AddSelectedCard()
	{
		Card card = (iconCardView ? selectedIconCard : (CardList.SelectedItem as CardStack)?.Card);
		if (card == null)
		{
			return;
		}
		CardVariantsWindow cardVariantsWindow = new CardVariantsWindow(card, cardCollection.Cards, cardCollection.SelectedCards, SelectedAccount?.OwnedCardCounts ?? new Dictionary<int, int>(), OwnedCardsOnlyCheckBox.IsChecked == true)
		{
			Owner = this
		};
		if (cardVariantsWindow.ShowDialog() != true)
		{
			return;
		}
		foreach (KeyValuePair<ushort, int> quantity in cardVariantsWindow.Quantities)
		{
			CardStack.Move(cardCollection, quantity.Key, quantity.Value, add: true);
		}
		ClearIconSelection();
		ApplyOwnedCardFilter();
		PublishDeck();
	}

	private void RemoveSelectedCard()
	{
		if (SelectedCardList.SelectedItem is CardStack cardStack)
		{
			int selectedIndex = SelectedCardList.SelectedIndex;
			CardQuantityWindow cardQuantityWindow = new CardQuantityWindow(cardStack.DisplayName, cardStack.Count, add: false)
			{
				Owner = this
			};
			if (cardQuantityWindow.ShowDialog() == true && CardStack.Move(cardCollection, cardStack.TrcId, cardQuantityWindow.Quantity, add: false) != 0)
			{
				cardListView.Refresh();
				selectedCardListView.Refresh();
				ApplyOwnedCardFilter();
				SelectedCardList.SelectedIndex = Math.Min(selectedIndex, selectedCardStacks.Count - 1);
				PublishDeck();
			}
		}
	}

	private async Task RefreshRuntimeStatusAsync()
	{
		if (refreshingStatus || windowClosing)
		{
			return;
		}
		refreshingStatus = true;
		try
		{
			bool gameRunning = (lastKnownGameRunning = IsThisGameRunning());
			int[] configuredPorts = ServerSettingsView.ConfiguredPorts().Take(3).ToArray();
			bool[] source = await Task.WhenAll(configuredPorts.Select(IsPortOpenAsync));
			string text = (source.All((bool value) => value) ? ("Server " + string.Join('/', configuredPorts) + " OK") : ("Server ports " + string.Join('/', source.Select((bool value) => (!value) ? "down" : "up"))));
			if (!serverConfiguring && !stoppingServer && !windowClosing && !firstRunning && DateTime.UtcNow >= statusHoldUntil)
			{
				RuntimeStatusText.Text = text + " - Game " + (gameRunning ? "running" : "not running");
			}
			StartGameButton.IsEnabled = !gameRunning && !launcherProcessRunning && !serverConfiguring && !stoppingServer && !windowClosing && !firstRunning;
			StopGameButton.IsEnabled = gameRunning || launcherProcessRunning;
			SetAccountControlsEnabled(!accountToolRunning);
		}
		finally
		{
			refreshingStatus = false;
		}
	}

	private static async Task<bool> IsPortOpenAsync(int port)
	{
		using TcpClient client = new TcpClient();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500.0));
		try
		{
			await client.ConnectAsync("127.0.0.1", port, timeout.Token);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> WaitForServerPortsClosedAsync()
	{
		int[] ports = ServerSettingsView.ConfiguredPorts().Take(3).ToArray();
		DateTime deadline = DateTime.UtcNow.AddSeconds(10.0);
		do
		{
			if ((await Task.WhenAll(ports.Select(IsPortOpenAsync))).All((bool isOpen) => !isOpen))
			{
				return true;
			}
			await Task.Delay(200);
		}
		while (DateTime.UtcNow < deadline);
		return false;
	}

	private static string ReadTail(string path, int maximumLines = 140, int maximumBytes = 196608)
	{
		if (!File.Exists(path))
		{
			return "Waiting for the log file: " + path;
		}
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			long num = Math.Max(0L, fileStream.Length - maximumBytes);
			fileStream.Seek(num, SeekOrigin.Begin);
			using StreamReader streamReader = new StreamReader(fileStream, Encoding.UTF8, true, 4096, false);
			if (num > 0)
			{
				streamReader.ReadLine();
			}
			string[] array = streamReader.ReadToEnd().Replace("\0", "").Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
			return string.Join(Environment.NewLine, array.Skip(Math.Max(0, array.Length - maximumLines)));
		}
		catch (IOException ex)
		{
			return "The log file is busy right now: " + ex.Message;
		}
		catch (UnauthorizedAccessException ex2)
		{
			return "Could not read the log: " + ex2.Message;
		}
	}

	private async Task RefreshLogPanelsAsync()
	{
		if (refreshingLogs || windowClosing)
		{
			return;
		}
		refreshingLogs = true;
		try
		{
			string serverPath = Path.GetFullPath(Path.Combine(GamePaths.LogsRoot, "fgo.log"));
			string text = Path.Combine(GamePaths.LogsRoot, "fgo-inject-live.log");
			string injectionPath = (File.Exists(text) ? text : Path.Combine(GamePaths.LogsRoot, "fgo-last-launch.log"));
			bool readInjectionLog = !launcherLogOwned;
			var (text2, text3) = await Task.Run(() => (ReadTail(serverPath), readInjectionLog ? ReadTail(injectionPath) : null));
			if (!string.Equals(text2, serverLogSnapshot, StringComparison.Ordinal))
			{
				serverLogSnapshot = text2;
				ServerLogTextBox.Text = text2;
				ServerLogTextBox.ScrollToEnd();
			}
			if (!launcherLogOwned && text3 != null && !string.Equals(text3, injectionLogSnapshot, StringComparison.Ordinal))
			{
				injectionLogSnapshot = text3;
				InjectionLogTextBox.Text = text3;
				InjectionLogTextBox.ScrollToEnd();
			}
		}
		finally
		{
			refreshingLogs = false;
		}
	}

	private static ProcessStartInfo CreatePowerShellStartInfo(string scriptPath, bool redirectOutput, IEnumerable<string> arguments)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = PowerShellHost.Executable,
			WorkingDirectory = GamePaths.GameRoot,
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			ErrorDialog = false,
			RedirectStandardOutput = redirectOutput,
			RedirectStandardError = redirectOutput
		};
		if (redirectOutput)
		{
			UTF8Encoding standardErrorEncoding = (UTF8Encoding)(processStartInfo.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			processStartInfo.StandardErrorEncoding = standardErrorEncoding;
		}
		processStartInfo.ArgumentList.Add("-NoLogo");
		processStartInfo.ArgumentList.Add("-NoProfile");
		processStartInfo.ArgumentList.Add("-NonInteractive");
		processStartInfo.ArgumentList.Add("-WindowStyle");
		processStartInfo.ArgumentList.Add("Hidden");
		processStartInfo.ArgumentList.Add("-ExecutionPolicy");
		processStartInfo.ArgumentList.Add("Bypass");
		processStartInfo.ArgumentList.Add("-File");
		processStartInfo.ArgumentList.Add(scriptPath);
		foreach (string argument in arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		return processStartInfo;
	}

	private static Process StartPowerShellScript(string scriptPath, params string[] arguments)
	{
		return Process.Start(CreatePowerShellStartInfo(scriptPath, redirectOutput: false, arguments)) ?? throw new InvalidOperationException("Could not start " + scriptPath);
	}

	private static CapturedProcess StartCapturedPowerShellScript(string scriptPath, Action<string, bool> onOutputLine, params string[] arguments)
	{
		Process process = new Process
		{
			StartInfo = CreatePowerShellStartInfo(scriptPath, redirectOutput: true, arguments)
		};
		TaskCompletionSource<object?> outputClosed = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<object?> errorClosed = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				outputClosed.TrySetResult(null);
			}
			else
			{
				onOutputLine(e.Data, arg2: false);
			}
		};
		process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				errorClosed.TrySetResult(null);
			}
			else
			{
				onOutputLine(e.Data, arg2: true);
			}
		};
		try
		{
			if (!process.Start())
			{
				throw new InvalidOperationException("Could not start " + scriptPath);
			}
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}
		catch
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
			process.Dispose();
			throw;
		}
		return new CapturedProcess(process, Task.WhenAll<object>(outputClosed.Task, errorClosed.Task));
	}

	private void BeginLauncherOutputCapture(string launcherPath)
	{
		lock (launcherOutputLock)
		{
			launcherOutputQueue.Clear();
			launcherQueuedCharacters = 0;
			launcherDroppedLineCount = 0;
			launcherOutputCompleted = false;
		}
		launcherLogOwned = true;
		injectionLogSnapshot = "";
		InjectionLogTextBox.Clear();
		AppendInjectionLog("[launcher] Starting " + Path.GetFileName(launcherPath) + "; receiving stdout / stderr..." + Environment.NewLine);
		launcherOutputTimer.Start();
	}

	private void QueueLauncherOutput(string line, bool standardError)
	{
		string text = (standardError ? ("[stderr] " + line + Environment.NewLine) : (line + Environment.NewLine));
		if (text.Length > 32768)
		{
			text = "[launcher] ...single line of output truncated..." + Environment.NewLine + text.Substring(text.Length - 32768);
		}
		lock (launcherOutputLock)
		{
			launcherOutputQueue.Enqueue(text);
			launcherQueuedCharacters += text.Length;
			while (launcherQueuedCharacters > 262144 && launcherOutputQueue.Count > 1)
			{
				string text2 = launcherOutputQueue.Dequeue();
				launcherQueuedCharacters -= text2.Length;
				launcherDroppedLineCount++;
			}
		}
	}

	private void CompleteLauncherOutputCapture(int exitCode)
	{
		QueueLauncherOutput($"[launcher] PowerShell exited: {exitCode} (0x{(uint)exitCode:X8})", exitCode != 0);
		lock (launcherOutputLock)
		{
			launcherOutputCompleted = true;
		}
	}

	private void FlushLauncherOutput()
	{
		StringBuilder stringBuilder = new StringBuilder();
		bool flag;
		lock (launcherOutputLock)
		{
			if (launcherDroppedLineCount > 0)
			{
				stringBuilder.Append("[launcher] Output too fast - the panel dropped the oldest ").Append(launcherDroppedLineCount).Append(" lines; the log file still has everything the script wrote.")
					.AppendLine();
				launcherDroppedLineCount = 0;
			}
			while (launcherOutputQueue.Count > 0 && (stringBuilder.Length == 0 || stringBuilder.Length + launcherOutputQueue.Peek().Length <= 65536))
			{
				string text = launcherOutputQueue.Dequeue();
				launcherQueuedCharacters -= text.Length;
				stringBuilder.Append(text);
			}
			flag = launcherOutputCompleted && launcherOutputQueue.Count == 0 && launcherDroppedLineCount == 0;
		}
		if (stringBuilder.Length > 0)
		{
			AppendInjectionLog(stringBuilder.ToString());
		}
		if (flag)
		{
			launcherOutputTimer.Stop();
		}
	}

	private void AppendInjectionLog(string text)
	{
		InjectionLogTextBox.AppendText(text);
		if (InjectionLogTextBox.Text.Length > 524288)
		{
			string text2 = InjectionLogTextBox.Text;
			int startIndex = text2.Length - 393216;
			int num = text2.IndexOf('\n', startIndex);
			if (num >= 0 && num + 1 < text2.Length)
			{
				startIndex = num + 1;
			}
			InjectionLogTextBox.Text = "[launcher] ...the panel keeps only the newest output, see logs for the full record..." + Environment.NewLine + text2.Substring(startIndex);
		}
		injectionLogSnapshot = InjectionLogTextBox.Text;
		InjectionLogTextBox.CaretIndex = InjectionLogTextBox.Text.Length;
		InjectionLogTextBox.ScrollToEnd();
	}

	private async void ServerSettings_OnConfigure(object? sender, EventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		if (Process.GetProcessesByName("ago").Length != 0 || launcherProcessRunning)
		{
			ThemedMessageBox.Show("Quit the game before changing the server settings.", "Server Settings");
			return;
		}
		serverConfiguring = true;
		ServerSettingsPanel.IsEnabled = false;
		StartGameButton.IsEnabled = false;
		try
		{
			_ = 5;
			try
			{
				string[] arguments = ServerSettingsPanel.ApplyArguments();
				string[] obj = (string[])arguments.Clone();
				obj[0] = "validate";
				await ServerSettingsView.RunTool(obj);
				Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server"));
				if (await RunServerCommandAsync(start: false) != 0)
				{
					throw new IOException("The server did not stop, so the settings were not changed - see logs/server-control.log.");
				}
				if (windowClosing)
				{
					goto end_IL_00b4;
				}
				await ServerSettingsView.RunTool(arguments);
				await ServerSettingsPanel.ReloadAsync();
				await RefreshRuntimeStatusAsync();
				await RefreshAccountsAsync(showErrors: false);
				ThemedMessageBox.Show("Server settings saved. The server starts when you click Start Server or Play.", "Server Settings");
				goto end_IL_0091;
				end_IL_00b4:;
			}
			catch (Exception ex)
			{
				if (!windowClosing)
				{
					ThemedMessageBox.Show(ex.Message, "Server Settings", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				goto end_IL_0091;
			}
			end_IL_0091:;
		}
		finally
		{
			serverConfiguring = false;
			ServerSettingsPanel.IsEnabled = true;
			await RefreshRuntimeStatusAsync();
		}
	}

	private async void StartServerButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "Start-FGOLocalServer.ps1"));
		if (!File.Exists(fullPath))
		{
			ThemedMessageBox.Show("The server start script is missing:\n" + fullPath, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		serverConfiguring = true;
		RuntimeStatusText.Text = "Starting and checking the local server...";
		try
		{
			if (await RunServerCommandAsync(start: true) != 0 && !windowClosing)
			{
				ThemedMessageBox.Show(StartupDiagnostics.Explain(10) + "\n\n" + ReadTail(Path.Combine(GamePaths.LogsRoot, "server-control.log")), "Server Did Not Start", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!windowClosing)
			{
				ThemedMessageBox.Show(ex2.Message, "Server Did Not Start", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			serverConfiguring = false;
			await RefreshRuntimeStatusAsync();
			if (!windowClosing)
			{
				await RefreshAccountsAsync(showErrors: false);
			}
		}
	}

	private static void AppendServerControlLog(string text)
	{
		try
		{
			Directory.CreateDirectory(GamePaths.LogsRoot);
			File.AppendAllText(Path.Combine(GamePaths.LogsRoot, "server-control.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n", Encoding.UTF8);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
	}

	private async Task<int> RunServerCommandAsync(bool start)
	{
		if (serverCommandTask != null)
		{
			throw new InvalidOperationException("The previous server control command has not finished.");
		}
		using CancellationTokenSource cancellation = new CancellationTokenSource();
		serverCommandCancellation = cancellation;
		Task<int> task = (serverCommandTask = ExecuteServerCommandAsync(start, cancellation.Token));
		try
		{
			return await task;
		}
		finally
		{
			serverCommandTask = null;
			serverCommandCancellation = null;
		}
	}

	private static async Task<int> ExecuteServerCommandAsync(bool start, CancellationToken cancellation)
	{
		string script = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", start ? "Start-FGOLocalServer.ps1" : "Stop-FGOLocalServer.ps1"));
		if (!File.Exists(script))
		{
			throw new FileNotFoundException("The server control script is missing.", script);
		}
		await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0), cancellation);
		cancellation.ThrowIfCancellationRequested();
		CapturedProcess captured = StartCapturedPowerShellScript(script, delegate(string line, bool error)
		{
			AppendServerControlLog(line);
		});
		using (captured.Process)
		{
			try
			{
				await ProcessCompletion.WaitAsync(captured.Process, TimeSpan.FromSeconds(start ? 120 : 50), cancellation);
				await Task.WhenAny(captured.StreamsCompleted, Task.Delay(1500));
				return captured.Process.ExitCode;
			}
			catch (Exception ex)
			{
				AppendServerControlLog(ex.Message);
				throw;
			}
		}
	}

	private async Task CancelServerCommandAsync()
	{
		Task<int> task = serverCommandTask;
		if (task != null)
		{
			serverCommandCancellation?.Cancel();
			try
			{
				await task;
			}
			catch (Exception ex)
			{
				AppendServerControlLog("Server control cancelled: " + ex.Message);
			}
			await Task.Yield();
		}
	}

	private async Task StopLocalServerAsync()
	{
		// One stop at a time: a second caller joins the stop already under way. The shared task is
		// released here, after it settles, so a stop that fails at once cannot stay pinned.
		Task pending = stopServerTask;
		if (pending == null)
		{
			pending = StopLocalServerCoreAsync();
			stopServerTask = pending;
		}
		try
		{
			await pending;
		}
		finally
		{
			if (stopServerTask == pending)
			{
				stopServerTask = null;
			}
		}
	}

	private async Task StopLocalServerCoreAsync()
	{
		await CancelServerCommandAsync();
		if (await RunServerCommandAsync(start: false) != 0)
		{
			throw new IOException("The local server did not stop completely - see logs/server-control.log. The database is left running so writes are not cut off.");
		}
	}

	private async void StopServerButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (stoppingServer || windowClosing)
		{
			return;
		}
		if (IsThisGameRunning() || launcherProcessRunning)
		{
			ThemedMessageBox.Show("Stop the game before stopping the local server.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (serverConfiguring && serverCommandTask == null)
			{
				return;
			}
			stoppingServer = true;
			RuntimeStatusText.Text = "Stopping the local server...";
			StartGameButton.IsEnabled = false;
			try
			{
				await StopLocalServerAsync();
			}
			catch (Exception ex)
			{
				if (!windowClosing)
				{
					ThemedMessageBox.Show(ex.Message, "Server Did Not Stop", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}
			finally
			{
				stoppingServer = false;
				await RefreshRuntimeStatusAsync();
				if (!windowClosing)
				{
					await RefreshAccountsAsync(showErrors: false);
				}
			}
		}
	}

	private void SummonTab_OnSelected(object sender, RoutedEventArgs e)
	{
		if (sender != e.OriginalSource || summonSettingsPanel != null)
		{
			return;
		}
		try
		{
			summonSettingsPanel = new SummonSettingsWindow(GamePaths.GameRoot);
			SummonTabContent.Content = summonSettingsPanel;
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show(this, "Could not open the draw rate settings:\n" + ex.Message, "Draw Rates", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private static AccountToolResult AccountToolFailure(string error, string message)
	{
		return new AccountToolResult
		{
			Error = error,
			Message = message
		};
	}

	private static async Task<AccountToolResult> RunAccountToolAsync(params string[] arguments)
	{
		if (!File.Exists(AccountToolPythonPath))
		{
			return AccountToolFailure("tool_missing", "Python runtime not found: " + AccountToolPythonPath);
		}
		if (!File.Exists(AccountToolScriptPath))
		{
			return AccountToolFailure("tool_missing", "Account tool script not found: " + AccountToolScriptPath);
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = AccountToolPythonPath,
			WorkingDirectory = AccountToolWorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			ErrorDialog = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
		};
		processStartInfo.Environment["PYTHONUTF8"] = "1";
		processStartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
		processStartInfo.ArgumentList.Add(AccountToolScriptPath);
		foreach (string item in arguments)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Could not start the account tool process");
		Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
		Task<string> stderrTask = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60.0));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			return AccountToolFailure("timeout", "The account tool timed out after 60 seconds");
		}
		string stdout = await stdoutTask;
		string stderr = await stderrTask;
		JsonObject jsonObject = null;
		try
		{
			jsonObject = JsonNode.Parse(stdout.Trim()) as JsonObject;
		}
		catch (JsonException)
		{
		}
		if (jsonObject == null)
		{
			return new AccountToolResult
			{
				Error = "bad_output",
				Message = "The account tool did not return valid JSON",
				Stderr = stderr,
				RawOutput = stdout
			};
		}
		return new AccountToolResult
		{
			Ok = (jsonObject["ok"]?.GetValue<bool>() ?? false),
			Error = (jsonObject["error"]?.GetValue<string>() ?? ""),
			Message = (jsonObject["message"]?.GetValue<string>() ?? ""),
			Stderr = stderr,
			RawOutput = stdout,
			Root = jsonObject
		};
	}

	private void AppendAccountLog(string message)
	{
		AppendInjectionLog("[account] " + message + Environment.NewLine);
	}

	private void SetAccountControlsEnabled(bool enabled)
	{
		AccountComboBox.IsEnabled = enabled;
		bool flag = enabled && !lastKnownGameRunning;
		UseAccountButton.IsEnabled = flag;
		CreateAccountButton.IsEnabled = flag;
		ResetAccountButton.IsEnabled = flag;
		AccountUpgradePanel.IsEnabled = flag && SelectedAccount != null;
		DeleteAccountButton.IsEnabled = flag && SelectedAccount != null;
		RepairAccountButton.IsEnabled = flag;
		GrantBenefitsButton.IsEnabled = enabled && SelectedAccount != null;
		RefreshAccountsButton.IsEnabled = enabled;
		SummonHistoryButton.IsEnabled = enabled && SelectedAccount != null;
		OwnedCardsOnlyCheckBox.IsEnabled = enabled && SelectedAccount != null;
	}

	private async Task ExecuteAccountActionAsync(Func<Task> action)
	{
		if (accountToolRunning)
		{
			return;
		}
		accountToolRunning = true;
		SetAccountControlsEnabled(enabled: false);
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			AppendAccountLog("Account operation error: " + ex.Message);
			ThemedMessageBox.Show("Account operation failed:\n" + ex.Message, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			accountToolRunning = false;
			SetAccountControlsEnabled(enabled: true);
		}
	}

	private void SelectAccountById(int aimeId)
	{
		if (AccountComboBox.ItemsSource is IEnumerable<AccountEntry> source)
		{
			AccountEntry accountEntry = source.FirstOrDefault((AccountEntry account) => account.AimeId == aimeId);
			if (accountEntry != null)
			{
				AccountComboBox.SelectedItem = accountEntry;
			}
		}
	}

	private async Task RefreshAccountsAsync(bool showErrors)
	{
		int? previouslySelectedAimeId = SelectedAccount?.AimeId;
		AccountToolResult accountToolResult = await RunAccountToolAsync("list", "--json");
		if (!accountToolResult.Ok || accountToolResult.Root == null)
		{
			lastKnownServerRunning = false;
			AccountComboBox.ItemsSource = null;
			ApplyOwnedCardFilter();
			CurrentAccountText.Text = "Account tool unavailable";
			CurrentAccessCodeText.Text = "—";
			string text = (string.IsNullOrWhiteSpace(accountToolResult.Message) ? accountToolResult.Error : accountToolResult.Message);
			AppendAccountLog("list failed: " + text);
			if (showErrors)
			{
				ThemedMessageBox.Show("Could not read the local account list:\n" + text, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			return;
		}
		lastKnownServerRunning = accountToolResult.Root["server_running"]?.GetValue<bool>() ?? false;
		string text2 = accountToolResult.Root["current_access_code"]?.GetValue<string>() ?? "";
		List<AccountEntry> list = new List<AccountEntry>();
		if (accountToolResult.Root["accounts"] is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					list.Add(new AccountEntry
					{
						AimeId = (jsonObject["aime_id"]?.GetValue<int>() ?? 0),
						MasterName = (jsonObject["master_name"]?.GetValue<string>() ?? ""),
						AccountMode = (jsonObject["account_mode"]?.GetValue<string>() ?? ""),
						AccessCode = (jsonObject["access_code"]?.GetValue<string>() ?? ""),
						IsCurrent = (jsonObject["is_current"]?.GetValue<bool>() ?? false),
						ServantCount = (jsonObject["servant_count"]?.GetValue<int>() ?? 0),
						SummonResultCount = (jsonObject["summon_result_count"]?.GetValue<int>() ?? 0),
						SummonHistory = ParseSummonHistory(jsonObject["summon_history"]),
						OwnedCardCount = (jsonObject["owned_card_count"]?.GetValue<int>() ?? 0),
						OwnedCardCopyCount = (jsonObject["owned_card_copy_count"]?.GetValue<int>() ?? 0),
						PendingPrintCount = (jsonObject["pending_print_count"]?.GetValue<int>() ?? 0),
						OwnedTradingCardIds = ParseOwnedCardIds(jsonObject["owned_cards"]),
						OwnedCardCounts = ParseOwnedCardCounts(jsonObject["owned_cards"]),
						MasterLevel = (jsonObject["master_level"]?.GetValue<int>() ?? 1),
						MasterExp = (jsonObject["master_exp"]?.GetValue<int>() ?? 0),
						MasterLevelExp = (jsonObject["master_level_exp"]?.GetValue<int>() ?? 0),
						MasterNextLevelExp = (jsonObject["master_next_level_exp"]?.GetValue<int>() ?? 0),
						MasterExpToNext = (jsonObject["master_exp_to_next"]?.GetValue<int>() ?? 0),
						QpAmount = (jsonObject["qp_amnt"]?.GetValue<int>() ?? 0),
						FriendPointAmount = (jsonObject["fp_amnt"]?.GetValue<int>() ?? 0),
						ManaPrismAmount = (jsonObject["mana_prism_amnt"]?.GetValue<int>() ?? 0),
						SummonPointAmount = (jsonObject["summon_point_amnt"]?.GetValue<int>() ?? 0),
						ClearedQuestCount = (jsonObject["cleared_quest_count"]?.GetValue<int>() ?? 0),
						TrackedQuestCount = (jsonObject["tracked_quest_count"]?.GetValue<int>() ?? 0)
					});
				}
			}
		}
		AccountComboBox.ItemsSource = list;
		AccountEntry accountEntry = list.FirstOrDefault((AccountEntry account) => account.IsCurrent);
		AccountEntry accountEntry2 = (previouslySelectedAimeId.HasValue ? list.FirstOrDefault((AccountEntry account) => account.AimeId == previouslySelectedAimeId.Value) : null);
		AccountComboBox.SelectedItem = accountEntry2 ?? accountEntry ?? ((list.Count > 0) ? list[0] : null);
		CurrentAccountText.Text = ((accountEntry != null) ? $"{accountEntry.MasterName} (ID {accountEntry.AimeId})" : ((list.Count == 0) ? "(no accounts yet - click New Account)" : "(not set)"));
		CurrentAccessCodeText.Text = ((text2.Length > 0) ? text2 : "—");
		AppendAccountLog($"Loaded {list.Count} local accounts; server {(lastKnownServerRunning ? "running" : "not running")}");
		string accountProfilesPath = GetAccountProfilesPath();
		if (File.Exists(accountProfilesPath))
		{
			lastAccountProfileWriteUtc = File.GetLastWriteTimeUtc(accountProfilesPath);
		}
	}

	private static string GetAccountProfilesPath()
	{
		return Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "state", "fgo-players.json"));
	}

	private async Task RefreshAccountsIfChangedAsync()
	{
		if (accountToolRunning || accountAutoRefreshRunning || windowClosing)
		{
			return;
		}
		string accountProfilesPath = GetAccountProfilesPath();
		if (!File.Exists(accountProfilesPath) || File.GetLastWriteTimeUtc(accountProfilesPath) == lastAccountProfileWriteUtc)
		{
			return;
		}
		accountAutoRefreshRunning = true;
		try
		{
			await RefreshAccountsAsync(showErrors: false);
		}
		finally
		{
			accountAutoRefreshRunning = false;
		}
	}

	private static List<SummonHistoryEntry> ParseSummonHistory(JsonNode? node)
	{
		List<SummonHistoryEntry> list = new List<SummonHistoryEntry>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonObject jsonObject)
			{
				list.Add(new SummonHistoryEntry
				{
					ConfirmedAt = (jsonObject["confirmed_at"]?.GetValue<string>() ?? ""),
					LotteryType = (jsonObject["tc_lottery_type"]?.GetValue<int>() ?? 0),
					LineupId = (jsonObject["lineup_id"]?.GetValue<int>() ?? 0),
					DrawIndex = (jsonObject["draw_index"]?.GetValue<int>() ?? 1),
					DrawCount = (jsonObject["draw_count"]?.GetValue<int>() ?? 1),
					TradingCardId = (jsonObject["tc_id"]?.GetValue<int>() ?? 0),
					ServantId = (jsonObject["servant_id"]?.GetValue<int>() ?? 0),
					CraftEssenceId = (jsonObject["craft_essence_id"]?.GetValue<int>() ?? 0),
					CardTypeId = (jsonObject["card_type_id"]?.GetValue<int>() ?? 1),
					DisplayName = (jsonObject["display_name"]?.GetValue<string>() ?? ""),
					Sid = (jsonObject["sid"]?.GetValue<string>() ?? "")
				});
			}
		}
		return list;
	}

	private static HashSet<int> ParseOwnedCardIds(JsonNode? node)
	{
		HashSet<int> hashSet = new HashSet<int>();
		if (!(node is JsonArray jsonArray))
		{
			return hashSet;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonObject jsonObject)
			{
				int num = jsonObject["tc_id"]?.GetValue<int>() ?? 0;
				if (num > 0)
				{
					hashSet.Add(num);
				}
			}
		}
		return hashSet;
	}

	private static Dictionary<int, int> ParseOwnedCardCounts(JsonNode? node)
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		if (node is JsonArray source)
		{
			foreach (JsonObject item in source.OfType<JsonObject>())
			{
				int num = item["tc_id"]?.GetValue<int>() ?? 0;
				int num2 = item["count"]?.GetValue<int>() ?? 0;
				if (num > 0 && num2 > 0)
				{
					dictionary[num] = num2;
				}
			}
		}
		return dictionary;
	}

	private void AccountComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		AccountEntry selectedAccount = SelectedAccount;
		RefreshAccountDetails(selectedAccount);
		SummonHistoryButton.IsEnabled = selectedAccount != null;
		GrantBenefitsButton.IsEnabled = selectedAccount != null && !accountToolRunning;
		AccountUpgradePanel.IsEnabled = selectedAccount != null && !lastKnownGameRunning && !accountToolRunning;
		DeleteAccountButton.IsEnabled = selectedAccount != null && !lastKnownGameRunning && !accountToolRunning;
		OwnedCardsOnlyCheckBox.IsEnabled = selectedAccount != null;
		ApplyOwnedCardFilter();
		CurrentSummonSummaryText.Text = ((selectedAccount == null) ? "Lv.1 - EXP 0 - 0 summons" : $"Lv.{selectedAccount.MasterLevel} - EXP {selectedAccount.MasterExp:N0} - Quests cleared {selectedAccount.ClearedQuestCount} - Cards printed {selectedAccount.OwnedCardCount} - Pending {selectedAccount.PendingPrintCount}");
	}

	private void RefreshAccountDetails(AccountEntry? account)
	{
		AccountDetailsTable.Items.Clear();
		if (account != null)
		{
			string item = ((account.MasterNextLevelExp > 0) ? $"{account.MasterLevelExp:N0} / {account.MasterNextLevelExp:N0} ({account.MasterExpToNext:N0} to go)" : "Max level reached");
			(string, string, string)[] array = new(string, string, string)[15]
			{
				("Master Name", account.MasterName, "master"),
				("Aime ID", account.AimeId.ToString(), ""),
				("Account Mode", account.ModeLabel, ""),
				("Master Level", $"Lv.{account.MasterLevel}", "master"),
				("Total EXP", $"{account.MasterExp:N0}", ""),
				("EXP This Level", item, ""),
				("QP", $"{account.QpAmount:N0}", "qp"),
				("Friend Points", $"{account.FriendPointAmount:N0}", "friend"),
				("Mana Prisms", $"{account.ManaPrismAmount:N0}", "prism"),
				("Summon Points", $"{account.SummonPointAmount:N0}", "summon"),
				("Quests Cleared", $"{account.ClearedQuestCount} / {account.TrackedQuestCount} tracked", ""),
				("Servants Owned", $"{account.ServantCount:N0}", "master"),
				("Printed Cards Owned", $"{account.OwnedCardCount:N0} unique / {account.OwnedCardCopyCount:N0} copies", ""),
				("Prints Pending", $"{account.PendingPrintCount:N0}", ""),
				("Print History", $"{account.SummonHistory.Count:N0} recent / {account.SummonResultCount:N0} total", "")
			};
			for (int i = 0; i < array.Length; i++)
			{
				(string, string, string) tuple = array[i];
				AccountDetailsTable.Items.Add(AccountDetailRow.Create(tuple.Item1, tuple.Item2));
			}
		}
	}

	private void SummonHistoryButton_OnClick(object sender, RoutedEventArgs e)
	{
		AccountEntry selectedAccount = SelectedAccount;
		if (selectedAccount != null)
		{
			DataGrid dataGrid = new DataGrid
			{
				IsReadOnly = true,
				AutoGenerateColumns = false,
				CanUserAddRows = false,
				CanUserDeleteRows = false,
				HeadersVisibility = DataGridHeadersVisibility.Column,
				GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
				ItemsSource = selectedAccount.SummonHistory,
				Margin = new Thickness(10.0)
			};
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Time",
				Binding = new Binding("ConfirmedAt"),
				Width = 190.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Pool",
				Binding = new Binding("PoolLabel"),
				Width = 150.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Position",
				Binding = new Binding("DrawPosition"),
				Width = 80.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Result",
				Binding = new Binding("DisplayName"),
				Width = new DataGridLength(1.0, DataGridLengthUnitType.Star)
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "TC ID",
				Binding = new Binding("TradingCardId"),
				Width = 80.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Type",
				Binding = new Binding("CardTypeLabel"),
				Width = 115.0
			});
			dataGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Entity ID",
				Binding = new Binding("EntityId"),
				Width = 80.0
			});
			Window obj = new Window
			{
				Owner = this,
				Title = $"Print History - {selectedAccount.MasterName} (Aime {selectedAccount.AimeId})",
				Width = 1050.0,
				Height = 580.0,
				MinWidth = 760.0,
				MinHeight = 400.0,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = new SolidColorBrush(Color.FromRgb(12, 10, 14)),
				Foreground = Brushes.White,
				Content = dataGrid
			};
			WindowTheme.Apply(obj);
			obj.ShowDialog();
		}
	}

	private static void ShowServerRunningAccountWarning()
	{
		ThemedMessageBox.Show("The local server is running, so this account operation cannot run.\nClick Stop Server at the top, then try again.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
	}

	private static void ShowGameRunningAccountWarning()
	{
		ThemedMessageBox.Show("The game is running, so accounts cannot be switched, created, deleted, reset or repaired.\nEnd the current game session first, so the access code still matches the save that is logged in.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
	}

	private bool ReportAccountToolFailure(AccountToolResult result, string operation)
	{
		if (result.Ok)
		{
			return false;
		}
		string text = ((result.Error.Length > 0 && result.Message.Length > 0) ? (result.Error + ": " + result.Message) : ((result.Message.Length > 0) ? result.Message : result.Error));
		AppendAccountLog(operation + " failed: " + text);
		if (result.Stderr.Trim().Length > 0)
		{
			AppendAccountLog("stderr: " + result.Stderr.Trim());
		}
		if (result.Error == "server_running")
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
		}
		else
		{
			ThemedMessageBox.Show("Account operation (" + operation + ") failed:\n" + text, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		return true;
	}

	private async void RefreshAccountsButton_OnClick(object sender, RoutedEventArgs e)
	{
		await ExecuteAccountActionAsync(() => RefreshAccountsAsync(showErrors: true));
	}

	private async void UseAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			lastKnownGameRunning = true;
			SetAccountControlsEnabled(enabled: true);
			ShowGameRunningAccountWarning();
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
			return;
		}
		lastKnownServerRunning = false;
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("Pick an account in the Select drop-down first.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult result = await RunAccountToolAsync("use", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(result, "use"))
			{
				AppendAccountLog($"Current account set to {account.MasterName} (ID {account.AimeId}); access code written to DEVICE/aime.txt");
				await RefreshAccountsAsync(showErrors: false);
				ThemedMessageBox.Show($"Switched to the account {account.MasterName} (ID {account.AimeId}).", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}

	private async void CreateAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			lastKnownGameRunning = true;
			SetAccountControlsEnabled(enabled: true);
			ShowGameRunningAccountWarning();
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
			return;
		}
		lastKnownServerRunning = false;
		NewAccountDialog newAccountDialog = new NewAccountDialog
		{
			Owner = this
		};
		if (newAccountDialog.ShowDialog() != true)
		{
			return;
		}
		string name = newAccountDialog.AccountName;
		string mode = newAccountDialog.AccountMode;
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("create", "--name", name, "--mode", mode, "--json");
			if (!ReportAccountToolFailure(accountToolResult, "create"))
			{
				int aimeId = (accountToolResult.Root?["aime_id"]?.GetValue<int>()).GetValueOrDefault();
				string accessCode = accountToolResult.Root?["access_code"]?.GetValue<string>() ?? "";
				AppendAccountLog($"Created account {name} (ID {aimeId}), mode {mode}, access code {accessCode}");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(aimeId);
				ThemedMessageBox.Show($"Account created: {name} (ID {aimeId})\nAccess code: {accessCode}", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				if (ThemedMessageBox.Show("Switch to the new account now?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
				{
					AccountToolResult result = await RunAccountToolAsync("use", "--aime-id", aimeId.ToString(), "--json");
					if (!ReportAccountToolFailure(result, "use"))
					{
						AppendAccountLog($"Switched to the new account {name} (ID {aimeId})");
						await RefreshAccountsAsync(showErrors: false);
						ThemedMessageBox.Show($"Switched to the new account {name} (ID {aimeId}).", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					}
				}
			}
		});
	}

	private async void DeleteAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			lastKnownGameRunning = true;
			SetAccountControlsEnabled(enabled: true);
			ShowGameRunningAccountWarning();
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("Pick the account to delete in the Select drop-down first.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
			return;
		}
		lastKnownServerRunning = false;
		string text = (account.IsCurrent ? "\n\nThis is the current account. After it is deleted the launcher switches to a remaining account; if none are left the current access code is cleared." : "");
		if (ThemedMessageBox.Show($"Permanently delete the account {account.MasterName} (Aime ID {account.AimeId})?\n\nMaster level: Lv.{account.MasterLevel}\nPrinted cards: {account.OwnedCardCount} unique / {account.OwnedCardCopyCount} copies\nQuests cleared: {account.ClearedQuestCount}\n\n" + "The save data, the Aime database identity, the card mapping and any leftover account backups are deleted permanently, and no recoverable copy is kept." + text, "Confirm Account Deletion", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("delete", "--aime-id", account.AimeId.ToString(), "--yes", "--json");
			if (!ReportAccountToolFailure(accountToolResult, "delete"))
			{
				bool deletedCurrent = accountToolResult.Root?["deleted_current"]?.GetValue<bool>() == true;
				int replacementAimeId = (accountToolResult.Root?["replacement_aime_id"]?.GetValue<int>()).GetValueOrDefault();
				JsonArray jsonArray = accountToolResult.Root?["backup_cleanup"]?["errors"] as JsonArray;
				bool cleanupComplete = jsonArray == null || jsonArray.Count == 0;
				AppendAccountLog(cleanupComplete ? $"Account {account.MasterName} (ID {account.AimeId}) fully deleted; identity, card mapping and leftover backups cleaned up" : $"Account {account.MasterName} (ID {account.AimeId}) was deleted, but some old backups were not cleaned up");
				if (!cleanupComplete)
				{
					AppendAccountLog("The account was deleted, but some old backups could not be cleaned up: " + string.Join("; ", jsonArray.Select((JsonNode node) => node?.GetValue<string>() ?? "unknown error")));
				}
				await RefreshAccountsAsync(showErrors: false);
				string value = ((!deletedCurrent) ? "" : ((replacementAimeId > 0) ? $"\nThe current account switched automatically to Aime ID {replacementAimeId}." : "\nThe current access code is cleared - create a new account before starting the game."));
				ThemedMessageBox.Show($"The account {account.MasterName} (ID {account.AimeId}) was fully deleted.{value}\n\n" + (cleanupComplete ? "Its save data, Aime identity, card mapping and leftover backups have all been cleaned up; the lowest free ID will be reused by the next new account." : "The save data, Aime identity and card mapping were deleted, but some old backups could not be cleaned up - see the log on the right. The lowest free ID will still be reused by the next new account."), "FGOAC scooby", MessageBoxButton.OK, cleanupComplete ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
			}
		});
	}

	private async void AccountUpgrade_OnClick(object sender, RoutedEventArgs e)
	{
		if (accountToolRunning || !(sender is Button button))
		{
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			return;
		}
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			ShowGameRunningAccountWarning();
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			ShowServerRunningAccountWarning();
			return;
		}
		string action = button.Tag.ToString();
		string title = button.Content.ToString();
		string value = ((action == "clear-gifts") ? "This deletes every unclaimed present without collecting the rewards inside. All other progress is kept, and the save is backed up before anything is written." : "All other progress is kept, and the save is backed up before anything is written.");
		if (ThemedMessageBox.Show(this, $"Run {title} on the account {account.MasterName} (ID {account.AimeId})?\n\n{value}", title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("upgrade", "--aime-id", account.AimeId.ToString(), "--action", action, "--json");
			if (!ReportAccountToolFailure(accountToolResult, "upgrade"))
			{
				AppendAccountLog($"{title} finished: {account.MasterName}; backup: {accountToolResult.Root?["backup"]}");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(account.AimeId);
				ThemedMessageBox.Show(this, $"Account {account.MasterName}: {title} finished.", title);
			}
		});
	}

	private async void ResetAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			lastKnownGameRunning = true;
			SetAccountControlsEnabled(enabled: true);
			ShowGameRunningAccountWarning();
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("Pick the account to reset in the Select drop-down first.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
			return;
		}
		lastKnownServerRunning = false;
		if (ThemedMessageBox.Show($"Reset the account {account.MasterName} (ID {account.AimeId}) to a brand new normal account?\n\nThis clears owned Servants, points, items, print history and all game progress. Resetting the current account also clears the sortie deck, and it cannot be undone.", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult result = await RunAccountToolAsync("reset", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(result, "reset"))
			{
				if (account.IsCurrent)
				{
					cardCollection.DeselectAll();
					selectedCardListView.Refresh();
					cardListView.Refresh();
					PublishDeck();
				}
				AppendAccountLog($"Account {account.MasterName} (ID {account.AimeId}) reset to a new normal account");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(account.AimeId);
				ThemedMessageBox.Show("Reset finished: the account " + account.MasterName + " has had its Servants, resources and progress cleared.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}

	private async void RepairAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			lastKnownGameRunning = true;
			SetAccountControlsEnabled(enabled: true);
			ShowGameRunningAccountWarning();
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("Pick the account to repair first.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
			return;
		}
		lastKnownServerRunning = false;
		if (ThemedMessageBox.Show("This rebuilds the EXP, materials, Bond and quest progress of the account " + account.MasterName + " from the recorded cabinet traffic.\n\nThe account files are backed up first. Continue?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("repair", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(accountToolResult, "repair"))
			{
				int captures = (accountToolResult.Root?["repair"]?["captures"]?.GetValue<int>()).GetValueOrDefault();
				int questRows = (accountToolResult.Root?["repair"]?["quest_rows"]?.GetValue<int>()).GetValueOrDefault();
				AppendAccountLog($"Account {account.AimeId} repaired from history: {captures} results, {questRows} quest states");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(account.AimeId);
				ThemedMessageBox.Show($"Repair finished: {captures} past results and {questRows} quest states processed.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}

	private async void GrantBenefitsButton_OnClick(object sender, RoutedEventArgs e)
	{
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("Pick the account that should receive the bonus items first.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("gift", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(accountToolResult, "catalog"))
			{
				List<BenefitGrantEntry> entries = new List<BenefitGrantEntry>();
				if (accountToolResult.Root?["items"] is JsonArray jsonArray)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject)
						{
							entries.Add(new BenefitGrantEntry
							{
								Key = (jsonObject["key"]?.GetValue<string>() ?? ""),
								Category = (jsonObject["category"]?.GetValue<string>() ?? ""),
								Name = (jsonObject["name"]?.GetValue<string>() ?? ""),
								ItemId = (jsonObject["item_id"]?.ToString() ?? ""),
								Current = (jsonObject["current"]?.GetValue<int>() ?? 0),
								MaxAmount = (jsonObject["max_amount"]?.GetValue<int>() ?? 0)
							});
						}
					}
				}
				if (entries.Count == 0)
				{
					ThemedMessageBox.Show("The installed data has no bonus items that can be granted.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				else
				{
					DataGrid table = new DataGrid
					{
						AutoGenerateColumns = false,
						CanUserAddRows = false,
						CanUserDeleteRows = false,
						HeadersVisibility = DataGridHeadersVisibility.Column,
						GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
						ItemsSource = entries,
						Margin = new Thickness(10.0),
						SelectionMode = DataGridSelectionMode.Single
					};
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "Category",
						Binding = new Binding("Category"),
						Width = 120.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "Item",
						Binding = new Binding("Name"),
						Width = new DataGridLength(1.0, DataGridLengthUnitType.Star),
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "ID",
						Binding = new Binding("ItemId"),
						Width = 105.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "Current",
						Binding = new Binding("Current")
						{
							StringFormat = "N0"
						},
						Width = 100.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "Max",
						Binding = new Binding("MaxAmount")
						{
							StringFormat = "N0"
						},
						Width = 110.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "Amount to Grant",
						Binding = new Binding("GrantText")
						{
							Mode = BindingMode.TwoWay,
							UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
						},
						Width = 145.0,
						EditingElementStyle = BenefitAmountEditorStyle()
					});
					Button button = new Button
					{
						Content = "Send to Present Box",
						MinWidth = 165.0,
						Margin = new Thickness(5.0)
					};
					Button element = new Button
					{
						Content = "Cancel",
						MinWidth = 90.0,
						Margin = new Thickness(5.0),
						IsCancel = true
					};
					StackPanel stackPanel = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						HorizontalAlignment = HorizontalAlignment.Right,
						Margin = new Thickness(10.0, 0.0, 10.0, 10.0)
					};
					stackPanel.Children.Add(element);
					stackPanel.Children.Add(button);
					Button button2 = new Button
					{
						Content = "Fill All to Max",
						Margin = new Thickness(5.0)
					};
					Button button3 = new Button
					{
						Content = "Clear All",
						Margin = new Thickness(5.0)
					};
					stackPanel.Children.Insert(0, button2);
					stackPanel.Children.Insert(1, button3);
					button2.Click += delegate
					{
						table.CancelEdit();
						table.CancelEdit(DataGridEditingUnit.Row);
						foreach (BenefitGrantEntry item2 in entries)
						{
							item2.GrantText = Math.Max(0, item2.MaxAmount - item2.Current).ToString();
						}
						table.Items.Refresh();
					};
					button3.Click += delegate
					{
						table.CancelEdit();
						table.CancelEdit(DataGridEditingUnit.Row);
						foreach (BenefitGrantEntry item3 in entries)
						{
							item3.GrantText = "0";
						}
						table.Items.Refresh();
					};
					DockPanel dockPanel = new DockPanel();
					TextBlock element2 = new TextBlock
					{
						Text = "Enter an amount or use Fill All to Max; 0 sends nothing. The server puts the items in the Present Box on the game's next request, and they reach your inventory once you claim them. Amounts are capped at the maximum, counting what you already hold and what is still waiting in the Present Box. Only items with a verified gift protocol are listed.",
						Margin = new Thickness(12.0, 10.0, 12.0, 0.0),
						Foreground = Brushes.LightGray,
						TextWrapping = TextWrapping.Wrap
					};
					DockPanel.SetDock(element2, Dock.Top);
					DockPanel.SetDock(stackPanel, Dock.Bottom);
					dockPanel.Children.Add(element2);
					dockPanel.Children.Add(stackPanel);
					dockPanel.Children.Add(table);
					Window dialog = new Window
					{
						Owner = this,
						Title = $"Grant Bonus Items - {account.MasterName} (Aime {account.AimeId})",
						Width = 980.0,
						Height = 680.0,
						MinWidth = 760.0,
						MinHeight = 460.0,
						WindowStartupLocation = WindowStartupLocation.CenterOwner,
						Background = new SolidColorBrush(Color.FromRgb(12, 10, 14)),
						Foreground = Brushes.White,
						Content = dockPanel
					};
					WindowTheme.Apply(dialog);
					button.Click += delegate
					{
						table.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
						table.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
						foreach (BenefitGrantEntry item4 in entries)
						{
							if (!int.TryParse(item4.GrantText, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
							{
								ThemedMessageBox.Show("The amount must be a whole number from 0 to 2,147,483,647.", "Bonus Items");
								return;
							}
							item4.GrantAmount = result;
						}
						if (!entries.Any((BenefitGrantEntry item) => item.GrantAmount > 0))
						{
							ThemedMessageBox.Show("Enter an amount greater than 0 for at least one item.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
						}
						else
						{
							dialog.DialogResult = true;
						}
					};
					if (dialog.ShowDialog() == true)
					{
						List<string> list = new List<string>
						{
							"gift",
							"--aime-id",
							account.AimeId.ToString()
						};
						foreach (BenefitGrantEntry item5 in entries.Where((BenefitGrantEntry item) => item.GrantAmount > 0))
						{
							list.Add("--item");
							list.Add($"{item5.Key}={item5.GrantAmount}");
						}
						list.Add("--json");
						AccountToolResult accountToolResult2 = await RunAccountToolAsync(list.ToArray());
						if (!ReportAccountToolFailure(accountToolResult2, "grant"))
						{
							List<string> summary = new List<string>();
							if (accountToolResult2.Root?["grants"] is JsonArray jsonArray2)
							{
								foreach (JsonNode item6 in jsonArray2)
								{
									if (item6 is JsonObject jsonObject2)
									{
										string value = jsonObject2["name"]?.GetValue<string>() ?? "Item";
										int value2 = jsonObject2["applied"]?.GetValue<int>() ?? 0;
										jsonObject2["after"]?.GetValue<int>();
										jsonObject2["clamped"]?.GetValue<bool>();
										summary.Add($"{value}: requested {value2:N0}");
									}
								}
							}
							AppendAccountLog($"Granted {summary.Count} bonus items to account {account.AimeId}");
							await RefreshAccountsAsync(showErrors: false);
							SelectAccountById(account.AimeId);
							ThemedMessageBox.Show("Queued for the Present Box - reopen the Present Box in game to claim. The amount actually granted is capped at the maximum.\nAfter a first-time server code update the server has to be restarted once.\n\n" + string.Join("\n", summary), "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
						}
					}
				}
			}
		});
	}

	private static string? SelectedTag(ComboBox comboBox)
	{
		return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
	}

	private static Style BenefitAmountEditorStyle()
	{
		return new Style(typeof(TextBox))
		{
			Setters = 
			{
				(SetterBase)new Setter(Control.ForegroundProperty, Brushes.White),
				(SetterBase)new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(24, 19, 27))),
				(SetterBase)new Setter(TextBoxBase.CaretBrushProperty, Brushes.White),
				(SetterBase)new Setter(TextBoxBase.SelectionBrushProperty, Brushes.DarkMagenta),
				(SetterBase)new Setter(Control.PaddingProperty, new Thickness(4.0, 2.0, 4.0, 2.0)),
				(SetterBase)new Setter(FrameworkElement.MarginProperty, new Thickness(0.0))
			}
		};
	}

	private static void SelectTag(ComboBox comboBox, string value)
	{
		foreach (object item in (IEnumerable)comboBox.Items)
		{
			if (item is ComboBoxItem { Tag: var tag } comboBoxItem && string.Equals(tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
			{
				comboBox.SelectedItem = comboBoxItem;
				return;
			}
		}
		comboBox.SelectedIndex = 0;
	}

	private static double ReadLayoutNumber(JsonObject layout, string name, double fallback, double minimum, double maximum)
	{
		if (!double.TryParse(layout[name]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return Math.Clamp(result, minimum, maximum);
	}

	private void SetLogsCollapsed(bool collapsed)
	{
		logsCollapsed = collapsed;
		Grid logPanel = LogPanel;
		Visibility visibility = (LogPanelSplitter.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible));
		logPanel.Visibility = visibility;
		LogPanelColumn.MinWidth = ((!collapsed) ? 310 : 0);
		LogPanelColumn.Width = new GridLength(collapsed ? 0.0 : expandedLogWidth);
		LogSplitterColumn.Width = new GridLength((!collapsed) ? 7 : 0);
		ToggleLogsButton.Content = (collapsed ? "Show logs" : "Hide logs");
	}

	private void ToggleLogs_OnClick(object sender, RoutedEventArgs e)
	{
		if (!logsCollapsed)
		{
			expandedLogWidth = Math.Max(310.0, LogPanelColumn.ActualWidth);
		}
		SetLogsCollapsed(!logsCollapsed);
		SaveLayoutSettings();
	}

	private void LoadLayoutSettings()
	{
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		string path = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		try
		{
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject && jsonObject["uiLayout"] is JsonObject jsonObject2)
			{
				base.Width = ReadLayoutNumber(jsonObject2, "windowWidth", base.Width, base.MinWidth, 7680.0);
				base.Height = ReadLayoutNumber(jsonObject2, "windowHeight", base.Height, base.MinHeight, 4320.0);
				LogPanelColumn.Width = new GridLength(ReadLayoutNumber(jsonObject2, "logPanelWidth", 440.0, 310.0, 1600.0));
				expandedLogWidth = LogPanelColumn.Width.Value;
				SetLogsCollapsed(jsonObject2["logsCollapsed"]?.GetValue<bool>() ?? false);
				SettingsPanelRow.Height = new GridLength(52.0);
				DeckPanelRow.Height = new GridLength(ReadLayoutNumber(jsonObject2, "deckHeight", 195.0, 195.0, 700.0));
				ServerLogRow.Height = new GridLength(ReadLayoutNumber(jsonObject2, "serverLogHeight", 300.0, 150.0, 1400.0));
				double num = ReadLayoutNumber(jsonObject2, "windowLeft", double.NaN, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth);
				double num2 = ReadLayoutNumber(jsonObject2, "windowTop", double.NaN, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);
				if (!double.IsNaN(num) && !double.IsNaN(num2))
				{
					base.Left = num;
					base.Top = num2;
					base.WindowStartupLocation = WindowStartupLocation.Manual;
				}
				if (string.Equals(jsonObject2["windowState"]?.ToString(), "maximized", StringComparison.OrdinalIgnoreCase))
				{
					base.WindowState = WindowState.Maximized;
				}
			}
		}
		catch
		{
		}
	}

	private void SaveLayoutSettings()
	{
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		string text = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		try
		{
			JsonObject jsonObject = ((File.Exists(text) && JsonNode.Parse(File.ReadAllText(text)) is JsonObject jsonObject2) ? jsonObject2 : new JsonObject());
			Rect val = (Rect)((base.WindowState == WindowState.Normal) ? new Rect(base.Left, base.Top, base.ActualWidth, base.ActualHeight) : base.RestoreBounds);
			jsonObject["uiLayout"] = new JsonObject
			{
				["windowWidth"] = Math.Round(Math.Max(base.MinWidth, val.Width), 1),
				["windowHeight"] = Math.Round(Math.Max(base.MinHeight, val.Height), 1),
				["windowLeft"] = Math.Round(val.Left, 1),
				["windowTop"] = Math.Round(val.Top, 1),
				["windowState"] = ((base.WindowState == WindowState.Maximized) ? "maximized" : "normal"),
				["logPanelWidth"] = Math.Round(logsCollapsed ? expandedLogWidth : LogPanelColumn.ActualWidth, 1),
				["logsCollapsed"] = logsCollapsed,
				["settingsHeight"] = Math.Round(SettingsPanelRow.ActualHeight, 1),
				["deckHeight"] = Math.Round(DeckPanelRow.ActualHeight, 1),
				["serverLogHeight"] = Math.Round(ServerLogRow.ActualHeight, 1)
			};
			string text2 = text + $".{Environment.ProcessId}.layout.tmp";
			File.WriteAllText(text2, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			if (File.Exists(text))
			{
				File.Replace(text2, text, text + ".bak", ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(text2, text);
			}
		}
		catch
		{
		}
	}

	private static bool TryParseResolution(string? text, out int width, out int height)
	{
		width = 0;
		height = 0;
		Match match = Regex.Match(text ?? "", "^\\s*(\\d{3,4})\\s*[xX×]\\s*(\\d{3,4})\\s*$");
		if (match.Success && int.TryParse(match.Groups[1].Value, out width))
		{
			return int.TryParse(match.Groups[2].Value, out height);
		}
		return false;
	}

	private static string DetectAspectRatio(int width, int height)
	{
		foreach (var (result, source) in ResolutionPresets)
		{
			if (source.Any(((int Width, int Height) item) => item.Width == width && item.Height == height))
			{
				return result;
			}
		}
		if (height <= 0)
		{
			return "custom";
		}
		double aspect = (double)width / (double)height;
		(string, double) tuple = (from item in new (string Name, double Value)[10]
			{
				("4:3", 1.3333333333333333),
				("16:10", 1.6),
				("16:9", 1.7777777777777777),
				("21:9", 2.3333333333333335),
				("32:9", 3.5555555555555554),
				("9:16", 0.5625),
				("10:16", 0.625),
				("9:21", 0.42857142857142855),
				("9:32", 9.0 / 32.0),
				("3:4", 0.75)
			}
			select (Name: item.Name, Math.Abs(aspect - item.Value)) into item
			orderby item.Item2
			select item).First();
		if (!(tuple.Item2 <= 0.08))
		{
			return "custom";
		}
		return tuple.Item1;
	}

	private void PopulateResolutionOptions(string aspectRatio, string? preferredText)
	{
		updatingResolutionOptions = true;
		try
		{
			ResolutionComboBox.Items.Clear();
			string preferred = preferredText?.Trim().Replace('×', 'x') ?? "";
			if (!ResolutionPresets.TryGetValue(aspectRatio, out (int, int)[] value))
			{
				ResolutionComboBox.Text = (TryParseResolution(preferred, out var _, out var _) ? preferred : "1920x1080");
				SyncCustomResolutionInputs(ResolutionComboBox.Text);
				return;
			}
			(int, int)[] array = value;
			for (int height = 0; height < array.Length; height++)
			{
				var (value2, value3) = array[height];
				ResolutionComboBox.Items.Add(new ComboBoxItem
				{
					Content = $"{value2}x{value3}"
				});
			}
			string text;
			int preferredWidth;
			int preferredHeight;
			if (value.Any(((int Width, int Height) item) => string.Equals($"{item.Width}x{item.Height}", preferred, StringComparison.OrdinalIgnoreCase)))
			{
				text = preferred;
			}
			else if (TryParseResolution(preferred, out preferredWidth, out preferredHeight))
			{
				(int, int) tuple2 = value.OrderBy(((int Width, int Height) item) => Math.Abs(item.Width - preferredWidth) + Math.Abs(item.Height - preferredHeight)).First();
				text = $"{tuple2.Item1}x{tuple2.Item2}";
			}
			else
			{
				text = $"{value[0].Item1}x{value[0].Item2}";
			}
			ResolutionComboBox.Text = text;
			SyncCustomResolutionInputs(text);
		}
		finally
		{
			updatingResolutionOptions = false;
		}
	}

	private void AspectRatioComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!updatingResolutionOptions)
		{
			PopulateResolutionOptions(SelectedTag(AspectRatioComboBox) ?? "custom", CurrentResolutionText());
		}
	}

	private string CurrentResolutionText()
	{
		if (!int.TryParse(CustomWidthTextBox.Text, out var result) || !int.TryParse(CustomHeightTextBox.Text, out var result2))
		{
			return ResolutionComboBox.Text;
		}
		return $"{result}x{result2}";
	}

	private void SyncCustomResolutionInputs(string? resolutionText)
	{
		if (TryParseResolution(resolutionText, out var width, out var height))
		{
			CustomWidthTextBox.Text = width.ToString();
			CustomHeightTextBox.Text = height.ToString();
		}
	}

	private void ResolutionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!updatingResolutionOptions)
		{
			string text = (ResolutionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
			SyncCustomResolutionInputs(text ?? ResolutionComboBox.Text);
		}
	}

	private void PopulateMonitors(string device)
	{
		MonitorComboBox.Items.Clear();
		MonitorComboBox.Items.Add(new ComboBoxItem
		{
			Content = "Follow Primary Display",
			Tag = ""
		});
		bool flag = string.IsNullOrEmpty(device);
		foreach (DisplayMonitor.Entry item in DisplayMonitor.GetConnected())
		{
			MonitorComboBox.Items.Add(new ComboBoxItem
			{
				Content = item.Label,
				Tag = item.Device
			});
			flag |= string.Equals(item.Device, device, StringComparison.OrdinalIgnoreCase);
		}
		if (!flag)
		{
			MonitorComboBox.Items.Add(new ComboBoxItem
			{
				Content = device + " (disconnected - using the primary display)",
				Tag = device
			});
		}
		SelectTag(MonitorComboBox, device);
	}

	private void MonitorComboBox_OnDropDownOpened(object sender, EventArgs e)
	{
		PopulateMonitors(SelectedTag(MonitorComboBox) ?? "");
	}

	private void LoadLauncherSettings()
	{
		string path = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		string value = "exclusive";
		string text = "keyboard";
		int num = 60;
		int num2 = 1920;
		int num3 = 1080;
		string text2 = "";
		string device = "";
		try
		{
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject)
			{
				object obj = jsonObject["displayMode"]?.GetValue<string>();
				if (obj == null)
				{
					JsonNode? jsonNode = jsonObject["windowed"];
					obj = ((jsonNode != null && jsonNode.GetValue<bool>()) ? "windowed" : "exclusive");
				}
				value = (string)obj;
				text = jsonObject["inputMode"]?.GetValue<string>() ?? text;
				num = jsonObject["targetFps"]?.GetValue<int>() ?? num;
				num2 = jsonObject["resolutionWidth"]?.GetValue<int>() ?? num2;
				num3 = jsonObject["resolutionHeight"]?.GetValue<int>() ?? num3;
				text2 = jsonObject["aspectRatio"]?.GetValue<string>() ?? "";
				device = jsonObject["monitorDevice"]?.GetValue<string>() ?? "";
				ChineseEnabledCheckBox.IsChecked = jsonObject["chineseEnabled"]?.GetValue<bool>() ?? false;
				GpuCompatCheckBox.IsChecked = GpuCompat.IsInstalled;
				GpuCompatCheckBox.IsEnabled = GpuCompat.SourceAvailable || GpuCompat.IsInstalled;
				if (!GpuCompat.SourceAvailable)
				{
					GpuCompatHelpText.Text = "The layer's file is missing: " + GpuCompat.SourcePath;
				}
				if (jsonObject["graphics"] is JsonObject jsonObject2)
				{
					SelectTag(SmaaComboBox, (jsonObject2["smaa"]?.GetValue<int>() ?? 0).ToString());
					SelectTag(RenderScaleComboBox, (jsonObject2["renderScale"]?.GetValue<int>() ?? 100).ToString());
					SelectTag(ShadowResolutionComboBox, (jsonObject2["shadowResolution"]?.GetValue<int>() ?? 0).ToString());
					HideTargetLinesCheckBox.IsChecked = jsonObject2["hideTargetLines"]?.GetValue<bool>() ?? false;
					DamageNumberScaleSlider.Value = Math.Clamp(jsonObject2["damageNumberScale"]?.GetValue<double>() ?? 100.0, 0.0, 200.0);
					DamageTextureScaleSlider.Value = Math.Clamp(jsonObject2["damageTextureScale"]?.GetValue<double>() ?? 100.0, 0.0, 200.0);
					DamageNumberOpacitySlider.Value = Math.Clamp(jsonObject2["damageNumberOpacity"]?.GetValue<double>() ?? 100.0, 0.0, 100.0);
					DamageTextureOpacitySlider.Value = Math.Clamp(jsonObject2["damageTextureOpacity"]?.GetValue<double>() ?? 100.0, 0.0, 100.0);
					SelectTag(AnisotropyComboBox, (jsonObject2["anisotropy"]?.GetValue<int>() ?? 16).ToString());
					MotionBlurCheckBox.IsChecked = jsonObject2["motionBlur"]?.GetValue<bool>() ?? false;
					DepthOfFieldCheckBox.IsChecked = jsonObject2["depthOfField"]?.GetValue<bool>() ?? true;
					BloomCheckBox.IsChecked = jsonObject2["bloom"]?.GetValue<bool>() ?? true;
					HideUiCheckBox.IsChecked = jsonObject2["hideUi"]?.GetValue<bool>() ?? false;
					DisableCameraShakeCheckBox.IsChecked = jsonObject2["disableCameraShake"]?.GetValue<bool>() ?? false;
					HideCabinetHudCheckBox.IsChecked = jsonObject2["hideCabinetHud"]?.GetValue<bool>() ?? false;
					hideUiVirtualKey = jsonObject2["hideUiKey"]?.GetValue<int>() ?? 121;
					if (hideUiVirtualKey < 8 || hideUiVirtualKey > 254)
					{
						hideUiVirtualKey = 121;
					}
					UpdateHideUiKeyLabel();
				}
			}
		}
		catch (Exception ex)
		{
			RuntimeStatusText.Text = "Could not read the display settings: " + ex.Message;
		}
		SelectTag(DisplayModeComboBox, value);
		PopulateMonitors(device);
		SelectTag(InputModeComboBox, text);
		ControlsPanel.RestorePreferredInputMode(text);
		SelectTag(FrameRateComboBox, num.ToString());
		string text3 = ((text2 == "custom") ? "custom" : (ResolutionPresets.ContainsKey(text2) ? text2 : DetectAspectRatio(num2, num3)));
		updatingResolutionOptions = true;
		SelectTag(AspectRatioComboBox, text3);
		updatingResolutionOptions = false;
		PopulateResolutionOptions(text3, $"{num2}x{num3}");
	}

	private void ControlsPanel_OnSaveRequested(object? sender, EventArgs e)
	{
		if (SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			HoldStatus("Control settings saved - they take effect the next time the game starts.");
		}
	}

	private bool SaveLauncherSettings(out string displayMode, out int width, out int height, out string inputMode, out int targetFps)
	{
		displayMode = SelectedTag(DisplayModeComboBox) ?? "exclusive";
		inputMode = SelectedTag(InputModeComboBox) ?? "keyboard";
		int result;
		bool flag = int.TryParse(SelectedTag(FrameRateComboBox), out result);
		if (flag)
		{
			bool flag2;
			switch (result)
			{
			case 60:
			case 90:
			case 120:
			case 144:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		targetFps = (flag ? result : 60);
		width = 0;
		height = 0;
		if (!int.TryParse(CustomWidthTextBox.Text, out width) || !int.TryParse(CustomHeightTextBox.Text, out height) || width < 480 || width > 7680 || height < 480 || height > 7680)
		{
			ThemedMessageBox.Show("Enter whole numbers from 480 to 7680 in the width and height boxes, for example 1920x1080, 720x1280 or 1080x2560.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		try
		{
			string text = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
			if (!ControlsPanel.SaveBindings())
			{
				return false;
			}
			JsonObject jsonObject = ((File.Exists(text) && JsonNode.Parse(File.ReadAllText(text)) is JsonObject jsonObject2) ? jsonObject2 : new JsonObject());
			jsonObject["displayMode"] = displayMode;
			jsonObject["monitorDevice"] = SelectedTag(MonitorComboBox) ?? "";
			jsonObject["resolutionWidth"] = width;
			jsonObject["resolutionHeight"] = height;
			jsonObject["aspectRatio"] = SelectedTag(AspectRatioComboBox) ?? DetectAspectRatio(width, height);
			jsonObject["windowed"] = displayMode != "exclusive";
			jsonObject["inputMode"] = inputMode;
			jsonObject["showControlGuides"] = false;
			jsonObject["targetFps"] = targetFps;
			jsonObject["chineseEnabled"] = ChineseEnabledCheckBox.IsChecked == true;
			jsonObject["gpuCompat"] = GpuCompatCheckBox.IsChecked == true;
			JsonObject jsonObject3 = (jsonObject["graphics"] as JsonObject) ?? new JsonObject();
			jsonObject3["smaa"] = (int.TryParse(SelectedTag(SmaaComboBox), out var result2) ? result2 : 0);
			JsonObject jsonObject4 = jsonObject3;
			bool flag2 = int.TryParse(SelectedTag(RenderScaleComboBox), out var result3);
			if (flag2)
			{
				bool flag3;
				switch (result3)
				{
				case 100:
				case 125:
				case 150:
				case 200:
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = flag3;
			}
			jsonObject4["renderScale"] = (flag2 ? result3 : 100);
			jsonObject3["anisotropy"] = (int.TryParse(SelectedTag(AnisotropyComboBox), out var result4) ? result4 : 16);
			jsonObject4 = jsonObject3;
			flag2 = int.TryParse(SelectedTag(ShadowResolutionComboBox), out var result5);
			if (flag2)
			{
				bool flag3 = ((result5 == 1024 || result5 == 2048 || result5 == 4096) ? true : false);
				flag2 = flag3;
			}
			jsonObject4["shadowResolution"] = (flag2 ? result5 : 0);
			jsonObject3["motionBlur"] = MotionBlurCheckBox.IsChecked == true;
			jsonObject3["depthOfField"] = DepthOfFieldCheckBox.IsChecked == true;
			jsonObject3["bloom"] = BloomCheckBox.IsChecked == true;
			jsonObject3["hideUi"] = HideUiCheckBox.IsChecked == true;
			jsonObject3["disableCameraShake"] = DisableCameraShakeCheckBox.IsChecked == true;
			jsonObject3["hideCabinetHud"] = HideCabinetHudCheckBox.IsChecked == true;
			jsonObject3["hideUiKey"] = hideUiVirtualKey;
			jsonObject3["hideTargetLines"] = HideTargetLinesCheckBox.IsChecked == true;
			jsonObject3["damageNumberScale"] = Math.Round(DamageNumberScaleSlider.Value, 1);
			jsonObject3["damageTextureScale"] = Math.Round(DamageTextureScaleSlider.Value, 1);
			jsonObject3["damageNumberOpacity"] = Math.Round(DamageNumberOpacitySlider.Value, 1);
			jsonObject3["damageTextureOpacity"] = Math.Round(DamageTextureOpacitySlider.Value, 1);
			if (jsonObject["graphics"] != jsonObject3)
			{
				jsonObject["graphics"] = jsonObject3;
			}
			jsonObject.Remove("startDeckReaderUI");
			string text2 = text + $".{Environment.ProcessId}.tmp";
			File.WriteAllText(text2, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			if (File.Exists(text))
			{
				File.Replace(text2, text, text + ".bak", ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(text2, text);
			}
			return true;
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show("Could not save the launch settings:\n" + ex.Message, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return false;
		}
	}

	private void PhotoSettings_OnClick(object sender, RoutedEventArgs e)
	{
		if (photoWindow != null)
		{
			return;
		}
		int? gamePid = null;
		Process[] processesByName = Process.GetProcessesByName("ago");
		foreach (Process process in processesByName)
		{
			using (process)
			{
				try
				{
					if (string.Equals(process.MainModule?.FileName, Path.Combine(GamePaths.GameRoot, "ago.exe"), StringComparison.OrdinalIgnoreCase))
					{
						gamePid = process.Id;
						break;
					}
				}
				catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
				{
				}
			}
		}
		photoWindow = new PhotoWindow(this, Path.Combine(GamePaths.GameRoot, "fgo-launcher.json"), gamePid);
		PhotoTabContent.Content = photoWindow;
	}

	private void UpdateHideUiKeyLabel()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		HideUiKeyButton.Content = ((object)KeyInterop.KeyFromVirtualKey(hideUiVirtualKey)/*cast due to constrained. prefix*/).ToString();
	}

	private void HideUiKeyButton_OnClick(object sender, RoutedEventArgs e)
	{
		bindingHideUiKey = true;
		HideUiKeyButton.Content = "Press a key (Esc to cancel)";
		HideUiKeyButton.Focus();
	}

	private void HideUiKeyButton_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		bindingHideUiKey = false;
		UpdateHideUiKeyLabel();
	}

	private void HideUiKeyButton_OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Invalid comparison between Unknown and I4
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Invalid comparison between Unknown and I4
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Invalid comparison between Unknown and I4
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Invalid comparison between Unknown and I4
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		if (!bindingHideUiKey)
		{
			return;
		}
		e.Handled = true;
		Key val = (((int)e.Key == 156) ? e.SystemKey : e.Key);
		if ((uint)(val - 70) > 1u && (uint)(val - 116) > 5u)
		{
			if ((int)val != 13)
			{
				hideUiVirtualKey = KeyInterop.VirtualKeyFromKey(val);
			}
			bindingHideUiKey = false;
			UpdateHideUiKeyLabel();
		}
	}

	private void SaveGraphicsButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (!SaveLauncherSettings(out string _, out int _, out int _, out string _, out int _))
		{
			return;
		}
		try
		{
			GpuCompat.Apply(GpuCompatCheckBox.IsChecked == true);
		}
		catch (Exception ex)
		{
			HoldStatus("The graphics compatibility layer could not be changed: " + ex.Message);
			return;
		}
		HoldStatus("Graphics settings saved - they take effect the next time the game starts.");
	}

	private void ResetDamageUi_OnClick(object sender, RoutedEventArgs e)
	{
		Slider damageNumberScaleSlider = DamageNumberScaleSlider;
		double value = (DamageTextureScaleSlider.Value = 100.0);
		damageNumberScaleSlider.Value = value;
		Slider damageNumberOpacitySlider = DamageNumberOpacitySlider;
		value = (DamageTextureOpacitySlider.Value = 100.0);
		damageNumberOpacitySlider.Value = value;
	}

	private async void StartGameButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		PublishDeck();
		if (!SaveLauncherSettings(out string displayMode, out int width, out int height, out string inputMode, out int targetFps) || (cardCollection.SelectedCards.Count == 0 && ThemedMessageBox.Show("The deck is empty. Start the game anyway?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes))
		{
			return;
		}
		string launcher = Path.Combine(GamePaths.GameRoot, "FGO_Launcher.ps1");
		if (!File.Exists(launcher))
		{
			ThemedMessageBox.Show("The launch script is missing:\n" + launcher, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		launcherCancellationRequested = false;
		launcherProcessRunning = true;
		StopGameButton.IsEnabled = true;
		StartGameButton.IsEnabled = false;
		RuntimeStatusText.Text = $"Starting {displayMode} {width}x{height} @ {targetFps} FPS; deck synced";
		BeginLauncherOutputCapture(launcher);
		CapturedProcess launcherProcess;
		try
		{
			QueueLauncherOutput("[launcher] Checking the PowerShell runtime...", standardError: false);
			await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0));
			if (launcherCancellationRequested || windowClosing)
			{
				launcherProcessRunning = false;
				CompleteLauncherOutputCapture(0);
				await RefreshRuntimeStatusAsync();
				return;
			}
			QueueLauncherOutput("[launcher] Runtime ready, starting the game script...", standardError: false);
			launcherProcess = StartCapturedPowerShellScript(launcher, QueueLauncherOutput, "-DisplayMode", displayMode, "-ResolutionWidth", width.ToString(), "-ResolutionHeight", height.ToString(), "-InputMode", inputMode, "-TargetFps", targetFps.ToString());
		}
		catch (Exception ex)
		{
			launcherProcessRunning = false;
			QueueLauncherOutput("[launcher] Could not start PowerShell: " + ex.Message, standardError: true);
			CompleteLauncherOutputCapture(-1);
			StartGameButton.IsEnabled = true;
			RuntimeStatusText.Text = "The launcher did not start";
			ThemedMessageBox.Show("Could not start the game script:\n" + ex.Message, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		runningLauncher = launcherProcess;
		MonitorLauncherAsync(launcherProcess);
	}

	private async Task MonitorLauncherAsync(CapturedProcess launcherProcess)
	{
		int exitCode = -1;
		try
		{
			while (!launcherProcess.Process.HasExited)
			{
				await Task.Delay(200);
			}
			if (await Task.WhenAny(launcherProcess.StreamsCompleted, Task.Delay(TimeSpan.FromSeconds(3.0))) == launcherProcess.StreamsCompleted)
			{
				await launcherProcess.StreamsCompleted;
			}
			else
			{
				QueueLauncherOutput("[launcher] Timed out waiting for stdout / stderr to finish - the last few lines may be incomplete.", standardError: true);
			}
			exitCode = launcherProcess.Process.ExitCode;
		}
		catch (Exception ex)
		{
			QueueLauncherOutput("[launcher] Could not monitor the launcher: " + ex.Message, standardError: true);
		}
		finally
		{
			if (runningLauncher == launcherProcess)
			{
				runningLauncher = null;
			}
			launcherProcess.Process.Dispose();
			launcherProcessRunning = false;
			CompleteLauncherOutputCapture(exitCode);
		}
		await RefreshRuntimeStatusAsync();
		if (exitCode != 0 && !launcherCancellationRequested && !windowClosing)
		{
			string text = $"Launcher code {exitCode}: {StartupDiagnostics.Explain(exitCode)}; the full output is in the Game Log panel.";
			RuntimeStatusText.Text = text;
			QueueLauncherOutput(text, standardError: true);
		}
	}

	private void CancelPendingLauncher()
	{
		launcherCancellationRequested = true;
		try
		{
			if (runningLauncher != null && !runningLauncher.Process.HasExited)
			{
				runningLauncher.Process.Kill();
			}
		}
		catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppendServerControlLog(ex.Message);
		}
	}

	private async void StopGameButton_OnClick(object sender, RoutedEventArgs e)
	{
		// The game is in front when this is asked, so the question has to come with it.
		if (ThemedMessageBox.Show(this, "Stop the current game session?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, foreground: true) != MessageBoxResult.Yes)
		{
			return;
		}
		CancelPendingLauncher();
		// With the separator, a sibling folder such as AppBackup does not match.
		string gameRoot = Path.GetFullPath(GamePaths.GameRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		Process[] processesByName = Process.GetProcessesByName("ago");
		foreach (Process process in processesByName)
		{
			try
			{
				ProcessModule? mainModule = process.MainModule;
				if (mainModule != null && mainModule.FileName?.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) == true)
				{
					process.CloseMainWindow();
				}
			}
			catch
			{
			}
		}
		await Task.Delay(3000);
		string[] array = new string[3] { "ago", "amdaemon", "inject" };
		for (int i = 0; i < array.Length; i++)
		{
			processesByName = Process.GetProcessesByName(array[i]);
			foreach (Process process2 in processesByName)
			{
				try
				{
					ProcessModule? mainModule2 = process2.MainModule;
					if (mainModule2 != null && mainModule2.FileName?.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) == true)
					{
						process2.Kill(entireProcessTree: true);
					}
				}
				catch
				{
				}
			}
		}
		await RefreshRuntimeStatusAsync();
	}

	private static void ShowGuide(string filename, string title)
	{
		string text = Path.Combine(GamePaths.GameRoot, "manuals", filename);
		if (!File.Exists(text))
		{
			ThemedMessageBox.Show("The controls guide image is missing:\n" + text, title, MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		BitmapImage source = new BitmapImage(new Uri(text, UriKind.Absolute));
		Window obj = new Window
		{
			Title = title,
			Width = 980.0,
			Height = 680.0,
			Background = Brushes.Black,
			Content = new Image
			{
				Source = source,
				Stretch = Stretch.Uniform
			},
			Owner = Application.Current.MainWindow
		};
		WindowTheme.Apply(obj);
		obj.Show();
	}

	private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
	{
		string logsRoot = GamePaths.LogsRoot;
		Directory.CreateDirectory(logsRoot);
		Process.Start(new ProcessStartInfo("explorer.exe", logsRoot)
		{
			UseShellExecute = true
		});
	}

	private void KeyboardGuideButton_OnClick(object sender, RoutedEventArgs e)
	{
		ShowGuide("keyboard-controls.png", "FGO Keyboard Controls");
	}

	private void ControllerGuideButton_OnClick(object sender, RoutedEventArgs e)
	{
		ShowGuide("controller-controls.png", "FGO Controller Controls");
	}

	private void CardList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!iconCardView && (CardList.SelectedItem as CardStack)?.Card != null)
		{
			SelectedCardList.UnselectAll();
		}
	}

	private void SelectedCardList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if ((SelectedCardList.SelectedItem as CardStack)?.Card != null)
		{
			CardList.UnselectAll();
			ClearIconSelection();
		}
	}

	private void CardList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (!iconCardView)
		{
			ListView cardList = CardList;
			object originalSource = e.OriginalSource;
			if (ItemsControl.ContainerFromElement(cardList, (DependencyObject)((originalSource is DependencyObject) ? originalSource : null)) is ListViewItem)
			{
				e.Handled = true;
				AddSelectedCard();
			}
		}
	}

	private void SelectedCardList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		RemoveSelectedCard();
	}

	private void SelectCardButton_OnClick(object sender, RoutedEventArgs e)
	{
		AddSelectedCard();
	}

	private void UnselectCardButton_OnClick(object sender, RoutedEventArgs e)
	{
		RemoveSelectedCard();
	}

	private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
	{
		Reload();
	}

	private void SearchButton_OnClick(object sender, RoutedEventArgs e)
	{
		Search();
	}

	private void CardsPathTextBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		if ((int)e.Key == 6)
		{
			Reload();
		}
	}

	private void SearchTextBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		if ((int)e.Key == 6)
		{
			Search();
		}
	}

	private void ClearButton_OnClick(object sender, RoutedEventArgs e)
	{
		cardCollection.DeselectAll();
		ApplyOwnedCardFilter();
		PublishDeck();
	}

}
