using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RatEyeGroundTruth
{
	/// <summary>
	/// Stores annotated screenshots as <c>{id}.png</c> with a <c>{id}.json</c> next to it
	/// </summary>
	public sealed class GroundTruthStore
	{
		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};

		public string RootDir { get; }

		public GroundTruthStore(string rootDir)
		{
			RootDir = rootDir;
			Directory.CreateDirectory(rootDir);
		}

		public int Count() => Directory.EnumerateFiles(RootDir, "*.json").Count();

		/// <returns>Path of the json file</returns>
		public string Save(PendingCapture capture, TruthDto truth)
		{
			var imagePath = Path.Combine(RootDir, capture.Id + ".png");
			var jsonPath = Path.Combine(RootDir, capture.Id + ".json");

			var entry = new GroundTruthEntry
			{
				Id = capture.Id,
				Screenshot = Path.GetFileName(imagePath),
				CapturedAt = capture.CapturedAt,
				Source = capture.Source,
				ScreenWidth = capture.Screenshot.Width,
				ScreenHeight = capture.Screenshot.Height,
				Scale = capture.Scale,
				Mouse = new PointDto { X = capture.Mouse.X, Y = capture.Mouse.Y },
				Highlighted = capture.Highlighted,
				Truth = truth,
				Scan = ScanDto.From(capture.Scan),
			};

			// Write the json last so a json file always has its screenshot
			capture.Screenshot.Save(imagePath, ImageFormat.Png);
			File.WriteAllText(jsonPath, JsonSerializer.Serialize(entry, JsonOptions));
			return jsonPath;
		}
	}
}
