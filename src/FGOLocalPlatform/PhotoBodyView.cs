using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FGOLocalPlatform.PhotoAssets;

namespace FGOLocalPlatform;

public sealed class PhotoBodyView : StackPanel, IDisposable
{
	private sealed record Saved(ulong Generation, PhotoBodyMotionEntry? Entry, FgoMotion? Motion, PhotoWeaponClip[] Weapons, double Frame);

	private readonly string root;

	private readonly PhotoFaceView selection;

	private readonly ComboBox clips = new ComboBox
	{
		DisplayMemberPath = "DisplayName"
	};

	private readonly Slider frame = new Slider
	{
		Minimum = 0.0,
		Maximum = 1.0,
		IsSnapToTickEnabled = false
	};

	private readonly TextBlock status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap
	};

	private readonly TextBlock time = new TextBlock();

	private readonly Button play = new Button
	{
		Content = "Play Motion",
		Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
	};

	private readonly DispatcherTimer timer = new DispatcherTimer((DispatcherPriority)7)
	{
		Interval = TimeSpan.FromMilliseconds(16.666666666666668)
	};

	private readonly Stopwatch clock = new Stopwatch();

	private MemoryMappedFile? mapping;

	private MemoryMappedViewAccessor? ipc;

	private int? pid;

	private ulong actor;

	private ulong generation;

	private int jointType;

	private int loadVersion;

	private PhotoRigBone[] bones = Array.Empty<PhotoRigBone>();

	private float[] baseline = Array.Empty<float>();

	private FgoMotion? motion;

	private PhotoWeaponClip[] weaponClips = Array.Empty<PhotoWeaponClip>();

	private bool syncing;

	private bool dirty;

	private bool reset;

	private bool playing;

	private bool loaded;

	private bool physicsReset;

	private double firstFrame;

	private readonly Dictionary<ulong, Saved> saved = new Dictionary<ulong, Saved>();

	private void SaveActor()
	{
		if (actor != 0L && loaded && clips.ItemsSource != null)
		{
			saved[actor] = new Saved(generation, clips.SelectedItem as PhotoBodyMotionEntry, motion, weaponClips, frame.Value);
		}
	}

	public PhotoBodyView(string gameRoot, PhotoFaceView actorSelection)
	{
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Expected O, but got Unknown
		root = gameRoot;
		selection = actorSelection;
		PhotoMotionUi.Configure(clips);
		base.Children.Add(new TextBlock
		{
			Text = "Body Animation",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		base.Children.Add(new TextBlock
		{
			Text = "Uses the character picked in Face Animation above. Switching characters keeps the pose you edited, and only motions that fit the current skeleton are listed.",
			TextWrapping = TextWrapping.Wrap
		});
		base.Children.Add(clips);
		base.Children.Add(frame);
		base.Children.Add(PhotoNumberInput.TimeRow(frame, Stop));
		base.Children.Add(play);
		Button button = new Button
		{
			Content = "Restore Original Motion",
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0)
		};
		base.Children.Add(button);
		base.Children.Add(status);
		clips.SelectionChanged += delegate
		{
			if (!syncing)
			{
				LoadSelection();
			}
		};
		frame.ValueChanged += delegate
		{
			time.Text = $"{frame.Value / 60.0:F2} s";
			if (!syncing)
			{
				dirty = true;
				physicsReset = true;
			}
		};
		button.Click += delegate
		{
			Stop();
			clips.SelectedItem = null;
			motion = null;
			reset = true;
			Publish();
		};
		play.Click += delegate
		{
			if (playing)
			{
				Stop();
			}
			else if (motion != null)
			{
				firstFrame = ((frame.Value >= frame.Maximum) ? 0.0 : frame.Value);
				if (firstFrame != frame.Value)
				{
					physicsReset = true;
				}
				clock.Restart();
				playing = true;
				play.Content = "Pause Motion";
				timer.Start();
			}
		};
		timer.Tick += delegate
		{
			syncing = true;
			frame.Value = Math.Min(frame.Maximum, firstFrame + clock.Elapsed.TotalSeconds * 60.0);
			syncing = false;
			dirty = true;
			Publish();
			if (frame.Value >= frame.Maximum)
			{
				Stop();
			}
		};
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
			Stop();
			saved.Clear();
			actor = 0uL;
			loaded = false;
			status.Text = "Enter photo mode, then pick a character.";
			return;
		}
		try
		{
			if (ipc == null)
			{
				mapping = MemoryMappedFile.OpenExisting($"Local\\FGOLocalPhotoBody_{gamePid}");
				ipc = mapping.CreateViewAccessor(0L, 273576L);
				if (ipc.ReadInt32(0L) != 1111705414 || ipc.ReadInt32(4L) != 2)
				{
					Dispose();
					status.Text = "The motion interface version does not match - restart the game.";
					return;
				}
			}
			ulong selectedActorId = selection.SelectedActorId;
			if (selectedActorId != actor)
			{
				if (ipc.ReadInt32(24L) != 0)
				{
					return;
				}
				SaveActor();
				Stop();
				loadVersion++;
				motion = null;
				loaded = false;
				dirty = (reset = false);
				syncing = true;
				clips.ItemsSource = null;
				syncing = false;
				actor = selectedActorId;
				ipc.Write(16L, actor);
				ipc.Write(24L, 1);
			}
			if (actor == 0L)
			{
				status.Text = "Pick a character in Face Animation first.";
				return;
			}
			int num = ipc.ReadInt32(8L);
			int num2 = ipc.ReadInt32(12L);
			if ((num & 1) != 0 || num2 <= 0 || num2 > 512 || ipc.ReadUInt64(40L) != actor)
			{
				return;
			}
			ulong num3 = ipc.ReadUInt64(48L);
			if (!loaded || generation != num3)
			{
				PhotoRigBone[] array = new PhotoRigBone[num2];
				float[] array2 = new float[num2 * 10];
				int num4 = ipc.ReadInt32(32L);
				for (int i = 0; i < num2; i++)
				{
					int num5 = 64 + i * 72;
					byte[] array3 = new byte[64];
					ipc.ReadArray(num5 + 8, array3, 0, 64);
					int num6 = Array.IndexOf(array3, (byte)0);
					if (num6 < 0)
					{
						throw new IOException("The bone name is not valid.");
					}
					array[i] = new PhotoRigBone(ipc.ReadInt32(num5), ipc.ReadInt32(num5 + 4), Encoding.ASCII.GetString(array3, 0, num6));
				}
				ipc.ReadArray(36928L, array2, 0, array2.Length);
				if (ipc.ReadInt32(8L) != num)
				{
					return;
				}
				Stop();
				motion = null;
				dirty = false;
				generation = num3;
				bones = array;
				baseline = array2;
				jointType = num4;
				loaded = true;
				LoadCatalog(selection.SelectedActorToken);
			}
			Publish();
			int num7 = ipc.ReadInt32(28L);
			if (num7 < 0)
			{
				TextBlock textBlock = status;
				textBlock.Text = num7 switch
				{
					-3 => "The character's skeleton changed - pick the motion again.", 
					-4 => "The motion pose failed its check.", 
					_ => "This character's pose cannot be edited.", 
				};
			}
			else if (num7 == 2)
			{
				status.Text = (playing ? "Playing" : "Motion applied");
			}
		}
		catch (FileNotFoundException)
		{
			status.Text = "The running game has not loaded the body motion module.";
		}
		catch (IOException)
		{
			Dispose();
			status.Text = "The motion connection was lost.";
		}
	}

	private async void LoadCatalog(string token)
	{
		int version = ++loadVersion;
		status.Text = "Reading matching motions...";
		PhotoRigBone[] currentBones = bones;
		int type = jointType;
		try
		{
			if (token.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new IOException("The character asset name is not valid.");
			}
			string path = Path.Combine(root, "rom", "mot", "mot_" + token.ToLowerInvariant() + ".farc");
			PhotoBodyMotionEntry[] array = await Task.Run(() => (from e in PhotoBodyMotionCatalog.List(path, type, currentBones)
				where e.Compatible
				select e).ToArray());
			if (version != loadVersion)
			{
				return;
			}
			syncing = true;
			clips.ItemsSource = array;
			if (saved.TryGetValue(actor, out Saved previous) && previous.Generation == generation)
			{
				clips.SelectedItem = array.FirstOrDefault((PhotoBodyMotionEntry e) => e.ArchivePath == previous.Entry?.ArchivePath && e.EntryName == previous.Entry?.EntryName);
				motion = previous.Motion;
				weaponClips = previous.Weapons;
				frame.Maximum = Math.Max(0, (motion?.FrameCount ?? 2) - 1);
				frame.Value = previous.Frame;
			}
			syncing = false;
			status.Text = $"{array.Length} motions available.";
		}
		catch (Exception ex) when (((ex is IOException || ex is FgoFormatException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			if (version == loadVersion)
			{
				status.Text = "Could not read the motions: " + ex.Message;
			}
		}
	}

	private async void LoadSelection()
	{
		Stop();
		motion = null;
		weaponClips = Array.Empty<PhotoWeaponClip>();
		dirty = false;
		int version = ++loadVersion;
		object selectedItem = clips.SelectedItem;
		PhotoBodyMotionEntry entry = selectedItem as PhotoBodyMotionEntry;
		if ((object)entry == null)
		{
			reset = true;
			return;
		}
		PhotoRigBone[] currentBones = bones;
		int type = jointType;
		try
		{
			PhotoWeaponRig[] weaponRigs = ((ipc == null) ? Array.Empty<PhotoWeaponRig>() : PhotoWeaponMotion.Read(ipc));
			FgoMotion parsed = await Task.Run(() => PhotoBodyMotionCatalog.Load(entry, type, currentBones));
			PhotoWeaponClip[] array = await Task.Run(() => PhotoWeaponMotion.Load(entry, weaponRigs));
			if (version == loadVersion)
			{
				weaponClips = array;
				motion = parsed;
				syncing = true;
				frame.Maximum = Math.Max(0, parsed.FrameCount - 1);
				frame.Value = 0.0;
				syncing = false;
				dirty = true;
				physicsReset = true;
				reset = false;
				Publish();
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is FgoFormatException) ? 1 : 0) != 0)
		{
			if (version == loadVersion)
			{
				status.Text = ex.Message;
			}
		}
	}

	private void Publish()
	{
		if (ipc == null || ipc.ReadInt32(24L) != 0)
		{
			return;
		}
		if (reset)
		{
			ipc.Write(24L, 3);
			reset = false;
			dirty = false;
		}
		else if (dirty && motion != null)
		{
			try
			{
				float[] array = PhotoBodyMotionCatalog.Sample(motion, (float)frame.Value, baseline);
				PhotoWeaponMotion.Write(ipc, weaponClips, (float)frame.Value);
				ipc.Write(56L, generation);
				ipc.Write(36L, bones.Length);
				ipc.WriteArray(57408L, array, 0, array.Length);
				ipc.Write(24L, physicsReset ? 4 : 2);
				dirty = false;
				physicsReset = false;
			}
			catch (FgoFormatException ex)
			{
				Stop();
				dirty = false;
				status.Text = ex.Message;
			}
			catch (IOException)
			{
				Dispose();
				status.Text = "The motion connection was lost.";
			}
		}
	}

	private void Stop()
	{
		playing = false;
		timer.Stop();
		clock.Stop();
		play.Content = "Play Motion";
	}

	public void Dispose()
	{
		Stop();
		saved.Clear();
		loadVersion++;
		ipc?.Dispose();
		mapping?.Dispose();
		ipc = null;
		mapping = null;
		actor = 0uL;
		loaded = false;
		motion = null;
		weaponClips = Array.Empty<PhotoWeaponClip>();
		dirty = (reset = false);
		syncing = true;
		clips.ItemsSource = null;
		syncing = false;
	}
}
