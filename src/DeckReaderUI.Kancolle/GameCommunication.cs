using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DeckReaderUI.Kancolle;

public static class GameCommunication
{
	private const int CARD_DATA_SIZE = 44;

	private const int UI_MAX_CARD_COUNT = 30;

	private const int MUTEX_TIMEOUT_MS = 250;

	private static Mutex? mutex;

	private static MemoryMappedFile? mmf;

	private static bool init;

	public static string GameRoot { get; set; } = AppContext.BaseDirectory;

	public static string ChannelName(string gameRoot)
	{
		return "FGODeck_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gameRoot).TrimEnd('\\').ToUpperInvariant())));
	}

	public static void Initialize()
	{
		if (!init)
		{
			init = true;
			mutex = new Mutex(initiallyOwned: false, ChannelName(GameRoot) + "_Mutex");
			mmf = MemoryMappedFile.CreateOrOpen(ChannelName(GameRoot), 1321L, MemoryMappedFileAccess.ReadWrite);
		}
	}

	public static bool UpdateCards(List<Card>? cards)
	{
		Initialize();
		Mutex mutex = GameCommunication.mutex ?? throw new InvalidOperationException("Deck communication mutex is unavailable.");
		MemoryMappedFile memoryMappedFile = mmf ?? throw new InvalidOperationException("Deck shared memory is unavailable.");
		bool flag = false;
		try
		{
			flag = mutex.WaitOne(250);
		}
		catch (AbandonedMutexException)
		{
			flag = true;
		}
		if (!flag)
		{
			return false;
		}
		try
		{
			using MemoryMappedViewStream memoryMappedViewStream = memoryMappedFile.CreateViewStream();
			if (cards == null)
			{
				memoryMappedViewStream.WriteByte(0);
				return true;
			}
			if (cards.Count > 30)
			{
				throw new InvalidDataException($"Too many selected cards: {cards.Count}.");
			}
			memoryMappedViewStream.WriteByte((byte)cards.Count);
			foreach (Card card in cards)
			{
				memoryMappedViewStream.Write(card.FullMetadata);
			}
			memoryMappedViewStream.Flush();
			return true;
		}
		finally
		{
			mutex.ReleaseMutex();
		}
	}
}
