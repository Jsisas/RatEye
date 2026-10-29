using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using RatEye;
using RatStash;

namespace RatEyeGroundTruth
{
	/// <summary>
	/// Scans the item below the mouse using RatEye
	/// </summary>
	public sealed class ScanService
	{
		private readonly string _iconDir;
		private readonly Database _database;
		private readonly ConcurrentDictionary<(float scale, bool highlighted), Lazy<RatEyeEngine>> _engines = new();

		public ScanService(string dataDir, Database scanDatabase)
		{
			_iconDir = Path.Combine(dataDir, "Icons");
			_database = scanDatabase;
			RatEye.Config.LogDebug = false;
		}

		/// <summary>
		/// Load everything needed to scan a screenshot of the given size, so the first scan is fast
		/// </summary>
		public void Warmup(Size resolution, bool highlighted)
		{
			var scale = RatEye.Config.Processing.Resolution2Scale(resolution.Width, resolution.Height);
			_ = GetEngine(scale, highlighted).Value;
		}

		private Lazy<RatEyeEngine> GetEngine(float scale, bool highlighted)
		{
			return _engines.GetOrAdd((scale, highlighted), key => new Lazy<RatEyeEngine>(() =>
			{
				var config = new RatEye.Config
				{
					PathConfig = new RatEye.Config.Path { StaticIcons = _iconDir },
					ProcessingConfig = new RatEye.Config.Processing
					{
						Scale = key.scale,
						IconConfig = new RatEye.Config.Processing.Icon { UseStaticIcons = true },
						InventoryConfig = new RatEye.Config.Processing.Inventory { OptimizeHighlighted = key.highlighted },
					},
				};
				return new RatEyeEngine(config, _database);
			}));
		}

		/// <summary>
		/// Scan the item at a position of the screenshot
		/// </summary>
		/// <param name="screenshot">24bpp screenshot, the bitmap is not modified</param>
		/// <param name="mouse">Mouse position in screenshot pixels</param>
		/// <param name="highlighted">If the item is highlighted (gray background)</param>
		/// <param name="scale">Scale of the screenshot, 1 for 1080p, 2 for 4k</param>
		public ScanResult Scan(Bitmap screenshot, Point mouse, bool highlighted, float scale)
		{
			var stopwatch = Stopwatch.StartNew();
			try
			{
				var engine = GetEngine(scale, highlighted).Value;

				var inventory = engine.NewInventory(screenshot);
				var icon = inventory.LocateIcon(new Vector2(mouse));
				var item = icon?.Item;
				if (icon == null || item == null)
				{
					return new ScanResult { Found = false, ElapsedMs = stopwatch.ElapsedMilliseconds };
				}

				var slotPixels = engine.Config.ProcessingConfig.BaseSlotSize * scale;
				var grid = new Size((int)Math.Round((icon.Size.X - 1) / slotPixels), (int)Math.Round((icon.Size.Y - 1) / slotPixels));
				var location = new Point(icon.Position.X + icon.ItemPosition.X, icon.Position.Y + icon.ItemPosition.Y);
				var size = new Size((int)(grid.Width * slotPixels) + 1, (int)(grid.Height * slotPixels) + 1);

				return new ScanResult
				{
					Found = true,
					ItemId = item.Id,
					ItemName = item.Name,
					Confidence = icon.DetectionConfidence,
					Rotated = icon.Rotated,
					Grid = grid,
					Rect = new Rectangle(location, size),
					ElapsedMs = stopwatch.ElapsedMilliseconds,
				};
			}
			catch (Exception e)
			{
				return new ScanResult { Error = e.Message, ElapsedMs = stopwatch.ElapsedMilliseconds };
			}
		}
	}
}
