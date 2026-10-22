using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using RatEye;

namespace RatEyeGroundTruth
{
	internal static class ScreenCapture
	{
		/// <summary>
		/// Take a screenshot of the monitor which contains the mouse cursor
		/// </summary>
		public static PendingCapture CaptureAtCursor(bool highlighted)
		{
			var cursor = Cursor.Position;
			var bounds = Screen.FromPoint(cursor).Bounds;

			var screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
			using (var g = Graphics.FromImage(screenshot))
			{
				g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
			}

			return new PendingCapture
			{
				Screenshot = screenshot,
				Mouse = new Point(cursor.X - bounds.X, cursor.Y - bounds.Y),
				Scale = Config.Processing.Resolution2Scale(bounds.Width, bounds.Height),
				Highlighted = highlighted,
			};
		}

		/// <summary>
		/// Load a screenshot from disk, the mouse is assumed to be in the center
		/// </summary>
		public static PendingCapture FromFile(string path, Point? mouse, bool highlighted, float? scale = null)
		{
			using var loaded = new Bitmap(path);

			// Always work on 24bpp, that is what the scanner expects
			var screenshot = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format24bppRgb);
			using (var g = Graphics.FromImage(screenshot))
			{
				g.DrawImageUnscaled(loaded, 0, 0);
			}

			return new PendingCapture
			{
				Screenshot = screenshot,
				Mouse = mouse ?? new Point(screenshot.Width / 2, screenshot.Height / 2),
				Scale = scale ?? Config.Processing.Resolution2Scale(screenshot.Width, screenshot.Height),
				Highlighted = highlighted,
				Source = "import:" + System.IO.Path.GetFileName(path),
			};
		}
	}
}
