using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeckReaderUI.Kancolle;

public class CardCollection
{
	public string Path { get; set; }

	public List<Card> Cards { get; }

	public List<Card> SelectedCards { get; }

	public TrcDB TrcDb { get; }

	public CardCollection(TrcDB trcDb, string path, IEnumerable<string>? selectedFiles = null, IEnumerable<int>? selectedCopies = null)
	{
		TrcDb = trcDb;
		Path = path;
		Cards = new List<Card>();
		SelectedCards = new List<Card>();
		Reload(selectedFiles, null, selectedCopies);
	}

	public void Reload(IEnumerable<string>? selectedFiles = null, string? filter = null, IEnumerable<int>? selectedCopies = null)
	{
		Cards.Clear();
		SelectedCards.Clear();
		if (!Directory.Exists(Path))
		{
			return;
		}
		string[] array = (from file in Directory.GetFiles(Path)
			where file.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
			select file).ToArray();
		Dictionary<string, string> dictionary = array.ToDictionary<string, string>((string file) => System.IO.Path.GetFullPath(file), StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int[] array2 = selectedCopies?.ToArray() ?? Array.Empty<int>();
		int num = 0;
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (selectedFiles != null)
		{
			foreach (string selectedFile in selectedFiles)
			{
				string fullPath = System.IO.Path.GetFullPath(selectedFile);
				int num2 = ((num >= array2.Length) ? 1 : Math.Clamp(array2[num], 1, 30));
				num++;
				if (hashSet2.Add(fullPath + "|" + num2) && dictionary.TryGetValue(fullPath, out var value))
				{
					Card card = new Card(TrcDb, value);
					SelectedCards.Add((num2 == 1) ? card : new Card(card, num2));
					if (num2 == 1)
					{
						hashSet.Add(fullPath);
					}
				}
			}
		}
		string[] array3 = array;
		foreach (string path in array3)
		{
			if (!hashSet.Contains(System.IO.Path.GetFullPath(path)))
			{
				Card card2 = new Card(TrcDb, path);
				if (filter == null || System.IO.Path.GetFileName(card2.Path).ToLower().Contains(filter) || (card2.Trc != null && (card2.Trc.Name.ToLower().Contains(filter) || card2.Trc.NameAlt.ToLower().Contains(filter))))
				{
					Cards.Add(card2);
				}
			}
		}
	}

	public int Select(int index)
	{
		Card item = Cards[index];
		Cards.RemoveAt(index);
		SelectedCards.Add(item);
		return SelectedCards.Count - 1;
	}

	public bool SyncOwnedCopies(IReadOnlyDictionary<int, int> counts)
	{
		bool flag = false;
		foreach (IGrouping<string, Card> item in Cards.Concat(SelectedCards).GroupBy<Card, string>((Card c) => c.Path, StringComparer.OrdinalIgnoreCase).ToList())
		{
			Card card = item.First();
			int value;
			int allowed = Math.Clamp((!counts.TryGetValue(card.TrcId, out value)) ? 1 : value, 1, 30);
			foreach (Card item2 in item.Where((Card c) => c.CopyNumber > allowed))
			{
				Cards.Remove(item2);
				flag |= SelectedCards.Remove(item2);
			}
			HashSet<int> hashSet = (from c in item
				where c.CopyNumber <= allowed
				select c.CopyNumber).ToHashSet();
			Card card2 = item.FirstOrDefault((Card c) => c.CopyNumber == 1) ?? new Card(TrcDb, card.Path);
			for (int num = 1; num <= allowed; num++)
			{
				if (!hashSet.Contains(num))
				{
					Cards.Add((num == 1) ? card2 : new Card(card2, num));
				}
			}
		}
		return flag;
	}

	public int Select(Card card)
	{
		int num = Cards.IndexOf(card);
		if (num >= 0)
		{
			return Select(num);
		}
		return -1;
	}

	public int Deselect(int index)
	{
		Card item = SelectedCards[index];
		SelectedCards.RemoveAt(index);
		Cards.Add(item);
		return Cards.Count - 1;
	}

	public int Deselect(Card card)
	{
		int num = SelectedCards.IndexOf(card);
		if (num >= 0)
		{
			return Deselect(num);
		}
		return -1;
	}

	public void DeselectAll()
	{
		Cards.AddRange(SelectedCards);
		SelectedCards.Clear();
	}

	public List<string> GetSelectedPaths()
	{
		return SelectedCards.Select((Card x) => x.Path).ToList();
	}
}
