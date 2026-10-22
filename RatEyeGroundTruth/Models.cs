using System;
using System.Drawing;

namespace RatEyeGroundTruth
{
	/// <summary>
	/// Result of scanning the item below the mouse
	/// </summary>
	public sealed class ScanResult
	{
		public bool Found;
		public string ItemId;
		public string ItemName;
		public float Confidence;
		public bool Rotated;

		/// <summary>Size of the item on screen, in slots</summary>
		public Size Grid;

		/// <summary>Location of the item cell(s) in screenshot pixels</summary>
		public Rectangle Rect;

		public long ElapsedMs;

		/// <summary>Set if scanning threw an exception</summary>
		public string Error;
	}

	/// <summary>
	/// A screenshot which was taken by the hotkey and is waiting to be annotated
	/// </summary>
	public sealed class PendingCapture
	{
		public string Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
		public DateTime CapturedAt = DateTime.Now;
		public Bitmap Screenshot;

		/// <summary>Mouse position in screenshot pixels</summary>
		public Point Mouse;

		/// <summary>Scale of the screenshot, see <see cref="RatEye.Config.Processing.Resolution2Scale"/></summary>
		public float Scale;

		/// <summary>If the item below the mouse is highlighted (gray background)</summary>
		public bool Highlighted;

		public bool Scanning;
		public ScanResult Scan;

		/// <summary>Set once the user changed something, so a late scan result does not overwrite it</summary>
		public bool UserEdited;

		public string Source = "hotkey";
	}

	public sealed class RectDto
	{
		public int X { get; set; }
		public int Y { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }

		public static RectDto From(Rectangle r) => new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
	}

	public sealed class PointDto
	{
		public int X { get; set; }
		public int Y { get; set; }
	}

	/// <summary>
	/// What the user says is actually on the screen
	/// </summary>
	public sealed class TruthDto
	{
		public string ItemId { get; set; }
		public string ItemName { get; set; }
		public bool Rotated { get; set; }
		public int GridWidth { get; set; }
		public int GridHeight { get; set; }
		public RectDto Rect { get; set; }
	}

	/// <summary>
	/// What RatEye detected, kept to compare against <see cref="TruthDto"/> later
	/// </summary>
	public sealed class ScanDto
	{
		public bool Found { get; set; }
		public string ItemId { get; set; }
		public string ItemName { get; set; }
		public float Confidence { get; set; }
		public bool Rotated { get; set; }
		public int GridWidth { get; set; }
		public int GridHeight { get; set; }
		public RectDto Rect { get; set; }
		public long ElapsedMs { get; set; }
		public string Error { get; set; }

		public static ScanDto From(ScanResult s) => s == null ? null : new ScanDto
		{
			Found = s.Found,
			ItemId = s.ItemId,
			ItemName = s.ItemName,
			Confidence = s.Confidence,
			Rotated = s.Rotated,
			GridWidth = s.Grid.Width,
			GridHeight = s.Grid.Height,
			Rect = s.Found ? RectDto.From(s.Rect) : null,
			ElapsedMs = s.ElapsedMs,
			Error = s.Error,
		};
	}

	/// <summary>
	/// Content of the json file which is stored next to every screenshot
	/// </summary>
	public sealed class GroundTruthEntry
	{
		public string Id { get; set; }
		public string Screenshot { get; set; }
		public DateTime CapturedAt { get; set; }
		public string Source { get; set; }
		public int ScreenWidth { get; set; }
		public int ScreenHeight { get; set; }
		public float Scale { get; set; }
		public PointDto Mouse { get; set; }
		public bool Highlighted { get; set; }
		public TruthDto Truth { get; set; }
		public ScanDto Scan { get; set; }
	}
}
