using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace RatEyeGroundTruth
{
	public sealed class AppSettings
	{
		/// <summary>
		/// Folder which contains <c>items.json</c>, <c>locales/</c> and <c>Icons/</c>.
		/// Looked up automatically if empty.
		/// </summary>
		public string DataDir { get; set; } = "";

		/// <summary>
		/// Folder in which screenshots and their ground truth are stored
		/// </summary>
		public string OutputDir { get; set; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			"RatEyeGroundTruth");

		/// <summary>
		/// Global hotkey which captures the item below the mouse, for example <c>Ctrl+Shift+F9</c>
		/// </summary>
		public string Hotkey { get; set; } = "Ctrl+Shift+F9";

		/// <summary>
		/// Assume the item below the mouse is highlighted (gray background), which is the case
		/// when hovering it in game. Can be changed per capture in the window.
		/// </summary>
		public bool Highlighted { get; set; } = true;

		/// <summary>
		/// Bring the window to the front after a capture. Otherwise only a tray notification is shown.
		/// </summary>
		public bool ShowWindowOnCapture { get; set; } = true;

		private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

		public static string FilePath => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"RatEyeGroundTruth",
			"settings.json");

		public static AppSettings Load()
		{
			try
			{
				if (File.Exists(FilePath))
				{
					return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
				}
			}
			catch (Exception e)
			{
				MessageBox.Show($"Could not read {FilePath}, using defaults.\n\n{e.Message}", "RatEye Ground Truth");
			}

			return new AppSettings();
		}

		public void Save()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
		}
	}

	internal static class DataLocator
	{
		public static bool IsValid(string dir)
		{
			return !string.IsNullOrWhiteSpace(dir)
				&& File.Exists(Path.Combine(dir, "items.json"))
				&& Directory.Exists(Path.Combine(dir, "Icons"))
				&& Directory.Exists(Path.Combine(dir, "locales"));
		}

		/// <summary>
		/// Find the data folder. Uses the configured one, otherwise looks for the <c>RatEyeTest/Data</c>
		/// folder of this repository and finally asks the user.
		/// </summary>
		/// <returns>The data folder, <see langword="null"/> if the user gave up</returns>
		public static string Resolve(AppSettings settings)
		{
			if (IsValid(settings.DataDir)) return settings.DataDir;

			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "RatEyeTest", "Data");
				if (IsValid(candidate))
				{
					settings.DataDir = candidate;
					settings.Save();
					return candidate;
				}

				dir = dir.Parent;
			}

			while (true)
			{
				using var dialog = new FolderBrowserDialog
				{
					Description = "Select the data folder (contains items.json, locales and Icons)",
					UseDescriptionForTitle = true,
				};
				if (dialog.ShowDialog() != DialogResult.OK) return null;
				if (IsValid(dialog.SelectedPath))
				{
					settings.DataDir = dialog.SelectedPath;
					settings.Save();
					return dialog.SelectedPath;
				}

				MessageBox.Show("That folder does not contain items.json, locales and Icons.", "RatEye Ground Truth");
			}
		}
	}
}
