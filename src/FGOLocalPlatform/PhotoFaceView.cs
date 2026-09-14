using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FGOLocalPlatform.PhotoAssets;

namespace FGOLocalPlatform;

public sealed class PhotoFaceView : StackPanel, IDisposable
{
	private sealed record Actor(ulong Id, string Token, uint Model)
	{
		public override string ToString()
		{
			return $"{Token} - Model {Model}";
		}
	}

	private readonly string gameRoot;

	private readonly ComboBox actors = new ComboBox();

	private readonly ComboBox expressions = new ComboBox();

	private readonly Slider frame = new Slider
	{
		Minimum = 0.0,
		Maximum = 60.0,
		TickFrequency = 1.0,
		IsSnapToTickEnabled = true
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private readonly TextBlock time = new TextBlock();

	private MemoryMappedFile? mapping;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private bool syncing;

	private bool dirty;

	private Actor? editing;

	private readonly Dictionary<Actor, (FaceMotionEntry? Entry, double Frame)> saved = new Dictionary<Actor, (FaceMotionEntry, double)>();

	private bool playing;

	private readonly Stopwatch playback = new Stopwatch();

	private double playbackFrame;

	private readonly Button play = new Button
	{
		Content = "Play Expression",
		Margin = new Thickness(0.0, 8.0, 8.0, 8.0)
	};

	private readonly DispatcherTimer playbackTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(33.0)
	};

	public ulong SelectedActorId => (actors.SelectedItem as Actor)?.Id ?? 0;

	public string SelectedActorToken => (actors.SelectedItem as Actor)?.Token ?? "";

	public PhotoFaceView(string root)
	{
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Expected O, but got Unknown
		gameRoot = root;
		playbackTimer.Tick += delegate
		{
			if (playing && ipc != null)
			{
				frame.Value = Math.Min(frame.Maximum, playbackFrame + playback.Elapsed.TotalSeconds * 60.0);
				PublishSelection();
				if (frame.Value >= frame.Maximum)
				{
					StopPlayback();
				}
			}
		};
		base.Children.Add(new TextBlock
		{
			Text = "Face Animation",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		base.Children.Add(actors);
		base.Children.Add(expressions);
		base.Children.Add(frame);
		base.Children.Add(PhotoNumberInput.TimeRow(frame, StopPlayback));
		base.Children.Add(status);
		PhotoMotionUi.Configure(expressions);
		actors.SelectionChanged += delegate
		{
			if (!syncing)
			{
				if (editing != null)
				{
					saved[editing] = (expressions.SelectedItem as FaceMotionEntry, frame.Value);
				}
				StopPlayback();
				editing = actors.SelectedItem as Actor;
				syncing = true;
				expressions.ItemsSource = ((editing != null) ? FaceMotionCatalog.ListForModel(gameRoot, editing.Token) : null);
				expressions.SelectedItem = null;
				if (editing != null && saved.TryGetValue(editing, out var previous))
				{
					expressions.SelectedItem = expressions.Items.Cast<FaceMotionEntry>().FirstOrDefault((FaceMotionEntry e) => e.Motion.Name == previous.Entry?.Motion.Name);
					frame.Maximum = Math.Max(1.0, Math.Min(36000.0, Math.Ceiling((previous.Entry?.Motion.DurationSeconds ?? 1f) * 60f)));
					frame.Value = previous.Frame;
				}
				syncing = false;
				dirty = true;
			}
		};
		expressions.SelectionChanged += delegate
		{
			if (!syncing)
			{
				StopPlayback();
				dirty = true;
				if (expressions.SelectedItem is FaceMotionEntry faceMotionEntry)
				{
					frame.Maximum = Math.Max(1.0, Math.Min(36000.0, Math.Ceiling(faceMotionEntry.Motion.DurationSeconds * 60f)));
					frame.Value = 0.0;
				}
			}
		};
		frame.ValueChanged += delegate
		{
			time.Text = $"Time: {frame.Value / 60.0:F2} s";
			dirty = true;
		};
		play.Click += delegate
		{
			if (playing)
			{
				StopPlayback();
			}
			else if (expressions.SelectedItem != null)
			{
				playbackFrame = ((frame.Value >= frame.Maximum) ? 0.0 : frame.Value);
				playback.Restart();
				playing = true;
				play.Content = "Pause Expression";
				playbackTimer.Start();
			}
		};
		base.Children.Add(play);
		Button button = new Button
		{
			Content = "Restore Original Expression",
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			expressions.SelectedItem = null;
			dirty = true;
		};
		base.Children.Add(button);
	}

	public void Refresh(int? gamePid, bool active)
	{
		if (pid != gamePid)
		{
			Dispose();
			pid = gamePid;
		}
		base.IsEnabled = active;
		if (!active || !gamePid.HasValue)
		{
			StopPlayback();
			saved.Clear();
			editing = null;
			status.Text = "Enter photo mode, then pick a character and an expression; you can play it or drag the timeline.";
			return;
		}
		try
		{
			if (ipc == null)
			{
				mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhotoFace_{gamePid.Value}");
				ipc = mapping.CreateViewAccessor(0L, 5416L);
				if (ipc.ReadInt32(0L) != 1162037062 || ipc.ReadInt32(4L) != 1)
				{
					Dispose();
					status.Text = "The game's expression interface version does not match - restart the game.";
					return;
				}
			}
			int num = ipc.ReadInt32(8L);
			int num2 = ipc.ReadInt32(12L);
			if ((num & 1) != 0 || num2 < 0 || num2 > 64)
			{
				return;
			}
			List<Actor> snapshot = new List<Actor>();
			for (int i = 0; i < num2; i++)
			{
				int num3 = 296 + i * 80;
				byte[] array = new byte[64];
				ipc.ReadArray(num3 + 16, array, 0, 64);
				int num4 = Array.IndexOf(array, (byte)0);
				if (num4 < 0)
				{
					num4 = 64;
				}
				snapshot.Add(new Actor(ipc.ReadUInt64(num3), Encoding.ASCII.GetString(array, 0, num4), ipc.ReadUInt32(num3 + 8)));
			}
			if (ipc.ReadInt32(8L) != num)
			{
				return;
			}
			Actor[] array2 = saved.Keys.Where((Actor a) => !snapshot.Contains(a)).ToArray();
			foreach (Actor key in array2)
			{
				saved.Remove(key);
			}
			if (!actors.Items.Cast<Actor>().ToArray().SequenceEqual(snapshot))
			{
				Actor selected = actors.SelectedItem as Actor;
				syncing = true;
				actors.ItemsSource = snapshot;
				actors.SelectedItem = snapshot.FirstOrDefault((Actor a) => a == selected);
				syncing = false;
				if (actors.SelectedItem == null)
				{
					expressions.ItemsSource = null;
					if (selected != null)
					{
						dirty = true;
					}
				}
				if (actors.SelectedItem == null && snapshot.Count == 1)
				{
					actors.SelectedIndex = 0;
				}
			}
			PublishSelection();
			TextBlock textBlock = status;
			textBlock.Text = ipc.ReadInt32(20L) switch
			{
				1 => playing ? "Playing" : "Expression applied", 
				-1 => "That character is no longer in the list.", 
				-2 => "The game has not loaded that expression.", 
				-3 => "Character data is not available right now.", 
				_ => $"{num2} characters available", 
			};
		}
		catch (FileNotFoundException)
		{
			status.Text = "The running game has not loaded the expression module.";
		}
		catch (IOException)
		{
			Dispose();
			status.Text = "The expression connection was lost.";
		}
	}

	private void PublishSelection()
	{
		if (dirty && ipc != null && ipc.ReadInt32(16L) == 0)
		{
			ipc.Write(24L, SelectedActorId);
			ipc.Write(32L, (long)frame.Value);
			byte[] array = new byte[256];
			string s = (expressions.SelectedItem as FaceMotionEntry)?.Motion.Name ?? "";
			byte[] bytes = Encoding.ASCII.GetBytes(s);
			if (bytes.Length > 255)
			{
				status.Text = "That expression name is too long.";
				return;
			}
			Array.Copy(bytes, array, bytes.Length);
			ipc.WriteArray(40L, array, 0, 256);
			ipc.Write(16L, 1);
			dirty = false;
		}
	}

	public void Dispose()
	{
		StopPlayback();
		saved.Clear();
		editing = null;
		ipc?.Dispose();
		mapping?.Dispose();
		ipc = null;
		mapping = null;
		syncing = true;
		actors.ItemsSource = null;
		expressions.ItemsSource = null;
		syncing = false;
		dirty = false;
	}

	private void StopPlayback()
	{
		playing = false;
		playback.Stop();
		playbackTimer.Stop();
		play.Content = "Play Expression";
	}
}
