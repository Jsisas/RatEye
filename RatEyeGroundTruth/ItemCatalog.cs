using System.Collections.Generic;
using System.IO;
using System.Linq;
using RatStash;

namespace RatEyeGroundTruth
{
	public sealed class ItemEntry
	{
		public Item Item { get; }
		public string Id => Item.Id;
		public string Name => Item.Name;
		public string ShortName => Item.ShortName;

		/// <summary>Size in slots, not rotated</summary>
		public int Width { get; }
		public int Height { get; }

		public ItemEntry(Item item)
		{
			Item = item;
			var (w, h) = item.GetSlotSize();
			Width = w;
			Height = h;
		}

		/// <summary>True if the item fills a grid of this size, in either orientation</summary>
		public bool FitsGrid(int width, int height)
		{
			return (Width == width && Height == height) || (Width == height && Height == width);
		}

		public override string ToString() => $"{Name}  [{Width}x{Height}]  ({ShortName})";
	}

	/// <summary>
	/// The items which can be picked as ground truth and the database used for scanning
	/// </summary>
	public sealed class ItemCatalog
	{
		/// <summary>Every item, sorted by name</summary>
		public IReadOnlyList<ItemEntry> Entries { get; }

		/// <summary>Items which RatEye is allowed to match</summary>
		public Database ScanDatabase { get; }

		private readonly Dictionary<string, ItemEntry> _byId;

		private ItemCatalog(IReadOnlyList<ItemEntry> entries, Database scanDatabase)
		{
			Entries = entries;
			ScanDatabase = scanDatabase;
			_byId = entries.ToDictionary(e => e.Id);
		}

		public ItemEntry Find(string id)
		{
			return id != null && _byId.TryGetValue(id, out var entry) ? entry : null;
		}

		public static ItemCatalog Load(string dataDir)
		{
			var localePath = Path.Combine(dataDir, "locales", Language.English.ToBSGCode() + ".json");
			var database = Database.FromFile(Path.Combine(dataDir, "items.json"), true, localePath);

			// Same restrictions as the RatEye tests and RatScanner use
			var scanDatabase = database.Filter(item => !item.QuestItem
				&& item.GetType() != typeof(LootContainer)
				&& item.GetType() != typeof(Pockets));

			var entries = database.GetItems()
				.Where(i => !string.IsNullOrEmpty(i.Name) && !i.Name.Contains("DO_NOT_USE"))
				.Select(i => new ItemEntry(i))
				.OrderBy(e => e.Name)
				.ToList();

			return new ItemCatalog(entries, scanDatabase);
		}
	}
}
