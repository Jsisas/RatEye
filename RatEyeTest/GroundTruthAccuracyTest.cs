using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using RatEye;
using Xunit;
using Xunit.Abstractions;

namespace RatEyeTest
{
	/// <summary>
	/// Measures scan accuracy on RatEyeGroundTruth captures. Never asserts; skips when no data is configured.
	/// </summary>
	public class GroundTruthAccuracyTest : TestEnvironment
	{
		private readonly ITestOutputHelper _output;

		public GroundTruthAccuracyTest(ITestOutputHelper output)
		{
			_output = output;
		}

		private sealed record PointEntry(int X, int Y);

		private sealed record TruthEntry(string ItemId, bool Rotated, int GridWidth, int GridHeight);

		private sealed record Entry(string Id, string Screenshot, float Scale, PointEntry Mouse, bool Highlighted, TruthEntry Truth);

		[Fact]
		public void Accuracy()
		{
			var dir = Environment.GetEnvironmentVariable("RATEYE_GROUNDTRUTH_DIR");
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
			{
				_output.WriteLine("RATEYE_GROUNDTRUTH_DIR not set, skipping");
				return;
			}

			var iconDir = Environment.GetEnvironmentVariable("RATEYE_ICON_DIR");
			if (string.IsNullOrEmpty(iconDir)) iconDir = "Data/Icons";
			bool? highlightedOverride = bool.TryParse(Environment.GetEnvironmentVariable("RATEYE_HIGHLIGHTED"), out var h) ? h : null;
			var disambiguate = !string.Equals(Environment.GetEnvironmentVariable("RATEYE_DISAMBIGUATE"), "false", StringComparison.OrdinalIgnoreCase);

			var jsonOptions = new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				PropertyNameCaseInsensitive = true,
			};
			var database = GetItemDatabase();
			var engines = new Dictionary<(float scale, bool highlighted), RatEyeEngine>();

			int correct = 0, total = 0;
			foreach (var file in Directory.GetFiles(dir, "*.json"))
			{
				Entry entry;
				try
				{
					entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(file), jsonOptions);
				}
				catch (JsonException)
				{
					continue;
				}
				if (entry?.Truth?.ItemId == null || entry.Screenshot == null || entry.Mouse == null) continue;

				total++;
				var id = entry.Id ?? Path.GetFileNameWithoutExtension(file);
				var highlighted = highlightedOverride ?? entry.Highlighted;
				try
				{
					var key = (entry.Scale, highlighted);
					if (!engines.TryGetValue(key, out var engine))
					{
						var config = new Config
						{
							PathConfig = new Config.Path
							{
								StaticIcons = iconDir,
								TrainedData = "Data/traineddata/best",
							},
							ProcessingConfig = new Config.Processing
							{
								Scale = entry.Scale,
								IconConfig = new Config.Processing.Icon
								{
									UseStaticIcons = true,
									UseShortNameDisambiguation = disambiguate,
								},
								InventoryConfig = new Config.Processing.Inventory { OptimizeHighlighted = highlighted },
							},
						};
						engine = new RatEyeEngine(config, database);
						engines[key] = engine;
					}

					using var screenshot = new Bitmap(Path.Combine(dir, entry.Screenshot));
					var icon = engine.NewInventory(screenshot).LocateIcon(new Vector2(entry.Mouse.X, entry.Mouse.Y));
					var item = icon?.Item;
					if (item == null)
					{
						_output.WriteLine($"NONE  {id}  truth={entry.Truth.ItemId}  got=-");
						continue;
					}

					var ok = item.Id == entry.Truth.ItemId;
					if (ok) correct++;
					var conf = icon.DetectionConfidence.ToString("0.000", CultureInfo.InvariantCulture);
					_output.WriteLine($"{(ok ? "OK" : "WRONG")}  {id}  truth={entry.Truth.ItemId}  got={item.Id} conf={conf} rot={icon.Rotated}");
				}
				catch (Exception e)
				{
					_output.WriteLine($"ERROR  {id}  truth={entry.Truth.ItemId}  got=- {e.Message}");
				}
			}

			var pct = total == 0 ? 0 : 100.0 * correct / total;
			_output.WriteLine($"accuracy: {correct}/{total} ({pct.ToString("0.0", CultureInfo.InvariantCulture)}%)");
		}
	}
}
