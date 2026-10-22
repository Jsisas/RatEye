using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RatEyeGroundTruth
{
	/// <summary>
	/// Lives in the tray, listens for the hotkey and feeds captures to the <see cref="MainForm"/>
	/// </summary>
	internal sealed class AppController : ApplicationContext
	{
		private const string Title = "RatEye Ground Truth";

		private readonly AppSettings _settings;
		private readonly NotifyIcon _tray;
		private readonly Control _ui;
		private readonly string[] _args;
		private HotKeyManager _hotKey;
		private AppServices _services;
		private MainForm _form;

		public AppController(string[] args)
		{
			_args = args;
			_settings = AppSettings.Load();

			// Gives us a handle to marshal back to the UI thread from background tasks
			_ui = new Control();
			_ = _ui.Handle;

			var menu = new ContextMenuStrip();
			menu.Items.Add("Show window", null, (_, _) => _form?.ShowAndActivate());
			menu.Items.Add("Import screenshot...", null, (_, _) => ImportWithDialog());
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add("Open dataset folder", null, (_, _) => OpenInExplorer(_settings.OutputDir));
			menu.Items.Add("Open settings file", null, (_, _) => OpenSettingsFile());
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add("Exit", null, (_, _) => Exit());

			_tray = new NotifyIcon
			{
				Icon = SystemIcons.Application,
				Text = Title,
				ContextMenuStrip = menu,
				Visible = true,
			};
			_tray.DoubleClick += (_, _) => _form?.ShowAndActivate();

			var dataDir = DataLocator.Resolve(_settings);
			if (dataDir == null)
			{
				ExitThread();
				return;
			}

			Notify("Loading item database and icons...");
			Task.Run(() => Load(dataDir)).ContinueWith(t => _ui.BeginInvoke(() => OnLoaded(t)));
		}

		private (ItemCatalog catalog, ScanService scanner) Load(string dataDir)
		{
			var catalog = ItemCatalog.Load(dataDir);
			var scanner = new ScanService(dataDir, catalog.ScanDatabase);

			// Loading the icons takes a while, do it before the first capture
			var screen = Screen.PrimaryScreen.Bounds.Size;
			scanner.Warmup(screen, _settings.Highlighted);
			return (catalog, scanner);
		}

		private void OnLoaded(Task<(ItemCatalog catalog, ScanService scanner)> task)
		{
			if (task.IsFaulted)
			{
				MessageBox.Show("Could not load the data:\n\n" + task.Exception?.GetBaseException().Message, Title,
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				Exit();
				return;
			}

			_services = new AppServices
			{
				Settings = _settings,
				Catalog = task.Result.catalog,
				Scanner = task.Result.scanner,
				Store = new GroundTruthStore(_settings.OutputDir),
				IconDir = System.IO.Path.Combine(_settings.DataDir, "Icons"),
				RequestScan = StartScan,
			};
			_form = new MainForm(_services);

			_hotKey = new HotKeyManager();
			_hotKey.Pressed += (_, _) => Capture();
			var error = _hotKey.Register(_settings.Hotkey);
			if (error != null)
			{
				MessageBox.Show(error + $"\n\nChange it in {AppSettings.FilePath} and restart.", Title,
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			else
			{
				Notify($"Ready. Hover an item and press {_settings.Hotkey}.");
			}

			HandleCommandLine();
		}

		private void Capture()
		{
			if (_services == null) return;

			PendingCapture capture;
			try
			{
				// Take the screenshot before anything of ours is shown
				capture = ScreenCapture.CaptureAtCursor(_settings.Highlighted);
			}
			catch (Exception e)
			{
				Notify("Capture failed: " + e.Message);
				return;
			}

			SystemSounds.Asterisk.Play();
			_form.AddCapture(capture, _settings.ShowWindowOnCapture);
			if (!_settings.ShowWindowOnCapture) Notify($"Captured ({_form.PendingCount} pending)");
			StartScan(capture);
		}

		private void StartScan(PendingCapture capture)
		{
			capture.Scanning = true;

			// The scan gets its own copy, the user might discard the capture while it is running
			var copy = (Bitmap)capture.Screenshot.Clone();
			var mouse = capture.Mouse;
			var highlighted = capture.Highlighted;
			var scale = capture.Scale;

			Task.Run(() =>
			{
				using (copy) return _services.Scanner.Scan(copy, mouse, highlighted, scale);
			}).ContinueWith(t => _ui.BeginInvoke(() =>
			{
				capture.Scanning = false;
				capture.Scan = t.IsFaulted
					? new ScanResult { Error = t.Exception?.GetBaseException().Message }
					: t.Result;
				_form.OnScanCompleted(capture);
			}));

			_form.OnScanCompleted(capture);
		}

		#region Import

		private void ImportWithDialog()
		{
			if (_services == null) return;

			using var dialog = new OpenFileDialog
			{
				Title = "Import screenshot",
				Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
				Multiselect = true,
			};
			if (dialog.ShowDialog() != DialogResult.OK) return;
			foreach (var file in dialog.FileNames) Import(file, null);
			_form.ShowAndActivate();
		}

		private void Import(string path, Point? mouse, float? scale = null)
		{
			try
			{
				var capture = ScreenCapture.FromFile(path, mouse, _settings.Highlighted, scale);
				_form.AddCapture(capture, false);
				StartScan(capture);
			}
			catch (Exception e)
			{
				MessageBox.Show($"Could not import {path}:\n\n{e.Message}", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		/// <summary>
		/// <c>--import path [x y] [--scale s]</c> opens a screenshot from disk as if it was captured by the hotkey
		/// </summary>
		private void HandleCommandLine()
		{
			var i = Array.IndexOf(_args, "--import");
			if (i < 0 || i + 1 >= _args.Length) return;

			Point? mouse = null;
			if (i + 3 < _args.Length && int.TryParse(_args[i + 2], out var x) && int.TryParse(_args[i + 3], out var y)) mouse = new Point(x, y);

			float? scale = null;
			var s = Array.IndexOf(_args, "--scale");
			if (s >= 0 && s + 1 < _args.Length && float.TryParse(_args[s + 1], System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out var parsedScale)) scale = parsedScale;

			Import(_args[i + 1], mouse, scale);
			_form.ShowAndActivate();
		}

		#endregion

		private void Notify(string message)
		{
			_tray.ShowBalloonTip(2500, Title, message, ToolTipIcon.Info);
		}

		private static void OpenInExplorer(string path)
		{
			System.IO.Directory.CreateDirectory(path);
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}

		private void OpenSettingsFile()
		{
			_settings.Save();
			Process.Start(new ProcessStartInfo(AppSettings.FilePath) { UseShellExecute = true });
		}

		private void Exit()
		{
			if (_form is { PendingCount: > 0 })
			{
				var answer = MessageBox.Show($"{_form.PendingCount} capture(s) have not been saved. Exit anyway?", Title,
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
				if (answer != DialogResult.Yes) return;
			}

			if (_form != null)
			{
				_form.AllowClose = true;
				_form.Close();
			}

			_hotKey?.Dispose();
			_tray.Visible = false;
			_tray.Dispose();
			ExitThread();
		}
	}
}
