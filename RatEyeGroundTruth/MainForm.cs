using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RatEyeGroundTruth
{
	internal sealed class AppServices
	{
		public AppSettings Settings;
		public ItemCatalog Catalog;
		public ScanService Scanner;
		public GroundTruthStore Store;

		/// <summary>Folder with the transparent icon of every item, named {id}.png</summary>
		public string IconDir;

		/// <summary>Start scanning a capture, the result is delivered to <see cref="MainForm.OnScanCompleted"/></summary>
		public Action<PendingCapture> RequestScan;
	}

	/// <summary>
	/// Shows the pending captures one after another and lets the user set what is really on the screenshot
	/// </summary>
	internal sealed class MainForm : Form
	{
		/// <summary>Size of one slot in pixels at a scale of 1</summary>
		private const float BaseSlotSize = 63f;

		private readonly AppServices _s;
		private readonly List<PendingCapture> _pending = new();
		private int _savedCount;

		private bool _updating;
		private ItemEntry _selected;

		private ImageEditor _editor;
		private Label _scanLabel;
		private Button _useScan;
		private Button _rescan;
		private CheckBox _highlighted;
		private NumericUpDown _scale;
		private TextBox _search;
		private CheckBox _matchGrid;
		private Label _selectedLabel;
		private ListBox _items;
		private CheckBox _rotated;
		private CheckBox _autoFit;
		private CheckBox _showOverlay;
		private TrackBar _overlayOpacity;
		private readonly Dictionary<(string id, bool rotated), Bitmap> _overlayCache = new();
		private NumericUpDown _gridW, _gridH;
		private NumericUpDown _rectX, _rectY, _rectW, _rectH;
		private Button _save;
		private Button _discard;
		private Label _status;
		private Control _inputPanel;

		/// <summary>Set to really close the form, otherwise closing only hides it</summary>
		public bool AllowClose;

		public int PendingCount => _pending.Count;

		private PendingCapture Current => _pending.Count > 0 ? _pending[0] : null;

		public MainForm(AppServices services)
		{
			_s = services;
			BuildUi();
			_savedCount = _s.Store.Count();
			ShowCurrent();
		}

		#region UI construction

		private void BuildUi()
		{
			Text = "RatEye Ground Truth";
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
			ClientSize = new Size(1280, 800);
			MinimumSize = new Size(960, 640);
			StartPosition = FormStartPosition.CenterScreen;
			KeyPreview = true;

			_editor = new ImageEditor { Dock = DockStyle.Fill };
			_editor.RectChanged += (_, _) =>
			{
				if (_updating) return;
				MarkEdited();
				SetRectFields(_editor.Rect);
			};

			var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
			split.Panel1.Controls.Add(_editor);
			Controls.Add(split);

			var right = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(8),
			};
			right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			split.Panel2.Controls.Add(right);
			_inputPanel = right;

			right.Controls.Add(BuildScanGroup(), 0, 0);
			right.Controls.Add(BuildItemGroup(), 0, 1);
			right.Controls.Add(BuildTruthGroup(), 0, 2);
			right.Controls.Add(BuildButtons(), 0, 3);

			Load += (_, _) =>
			{
				split.SplitterDistance = Math.Max(300, split.Width - (int)(500 * DeviceDpi / 96f));
			};
		}

		private GroupBox BuildScanGroup()
		{
			var group = new GroupBox { Text = "Detection", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
			var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Padding = new Padding(4) };
			group.Controls.Add(table);

			_scanLabel = new Label { AutoSize = false, Height = 54, Dock = DockStyle.Top, Text = "" };
			table.Controls.Add(_scanLabel);

			var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
			_useScan = new Button { Text = "Use detection", AutoSize = true };
			_useScan.Click += (_, _) =>
			{
				var cap = Current;
				if (cap?.Scan is { Found: true } scan) PrefillFromScan(scan);
			};
			_rescan = new Button { Text = "Rescan", AutoSize = true };
			_rescan.Click += (_, _) =>
			{
				var cap = Current;
				if (cap == null || cap.Scanning) return;
				cap.Highlighted = _highlighted.Checked;
				cap.UserEdited = false;
				_s.RequestScan(cap);
				UpdateScanLabel();
			};
			_highlighted = new CheckBox { Text = "Item is highlighted", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
			_highlighted.CheckedChanged += (_, _) =>
			{
				if (!_updating && Current != null) Current.Highlighted = _highlighted.Checked;
			};
			_scale = new NumericUpDown { DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.1m, Maximum = 4m, Value = 1m, Width = 62, Margin = new Padding(3, 3, 3, 3) };
			_scale.ValueChanged += (_, _) =>
			{
				if (_updating || Current == null) return;
				Current.Scale = (float)_scale.Value;
				AfterGridChanged();
			};
			buttons.Controls.AddRange(new Control[] { _useScan, _rescan, _highlighted, Caption("Scale"), _scale });
			table.Controls.Add(buttons);
			return group;
		}

		private GroupBox BuildItemGroup()
		{
			var group = new GroupBox { Text = "Item", Dock = DockStyle.Fill };
			var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			group.Controls.Add(table);

			_search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search name, short name or id" };
			_search.TextChanged += (_, _) => { if (!_updating) RefreshItemList(); };
			_search.KeyDown += (_, e) =>
			{
				if (e.KeyCode != Keys.Down && e.KeyCode != Keys.Up) return;
				if (_items.Items.Count == 0) return;
				var next = _items.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
				_items.SelectedIndex = Math.Clamp(next, 0, _items.Items.Count - 1);
				e.Handled = true;
				e.SuppressKeyPress = true;
			};
			table.Controls.Add(_search, 0, 0);

			_matchGrid = new CheckBox { Text = "Only items that fit the grid size", AutoSize = true };
			_matchGrid.CheckedChanged += (_, _) => { if (!_updating) RefreshItemList(); };
			table.Controls.Add(_matchGrid, 0, 1);

			_selectedLabel = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = "No item selected", Margin = new Padding(3, 4, 3, 4) };
			table.Controls.Add(_selectedLabel, 0, 2);

			_items = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
			_items.SelectedIndexChanged += (_, _) =>
			{
				if (_updating || _items.SelectedItem is not ItemEntry entry) return;
				SelectItem(entry);
			};
			table.Controls.Add(_items, 0, 3);
			return group;
		}

		private GroupBox BuildTruthGroup()
		{
			var group = new GroupBox { Text = "Ground truth", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
			var flow = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoSize = true,
				Padding = new Padding(4),
			};
			group.Controls.Add(flow);

			_rotated = new CheckBox { Text = "Rotated", AutoSize = true };
			_rotated.CheckedChanged += (_, _) =>
			{
				if (_updating) return;
				MarkEdited();
				if (_selected != null) SetGrid(_rotated.Checked ? (_selected.Height, _selected.Width) : (_selected.Width, _selected.Height));
				UpdateOverlay();
				AfterGridChanged();
			};
			_autoFit = new CheckBox { Text = "Resize rectangle with grid size", AutoSize = true, Checked = true };
			flow.Controls.Add(Row(_rotated, _autoFit));

			_showOverlay = new CheckBox { Text = "Show item overlay", AutoSize = true, Checked = true };
			_showOverlay.CheckedChanged += (_, _) => UpdateOverlay();
			_overlayOpacity = new TrackBar { Minimum = 10, Maximum = 100, TickFrequency = 10, Value = 55, Width = 150, AutoSize = false, Height = 28 };
			_overlayOpacity.ValueChanged += (_, _) => UpdateOverlay();
			flow.Controls.Add(Row(_showOverlay, _overlayOpacity));

			_gridW = Number(1, 30);
			_gridH = Number(1, 30);
			EventHandler gridChanged = (_, _) =>
			{
				if (_updating) return;
				MarkEdited();
				AfterGridChanged();
			};
			_gridW.ValueChanged += gridChanged;
			_gridH.ValueChanged += gridChanged;
			flow.Controls.Add(Row(Caption("Grid size"), _gridW, Caption("x"), _gridH, Caption("slots")));

			_rectX = Number(-20000, 20000);
			_rectY = Number(-20000, 20000);
			_rectW = Number(1, 20000);
			_rectH = Number(1, 20000);
			EventHandler rectChanged = (_, _) =>
			{
				if (_updating) return;
				MarkEdited();
				_updating = true;
				_editor.Rect = new Rectangle((int)_rectX.Value, (int)_rectY.Value, (int)_rectW.Value, (int)_rectH.Value);
				_updating = false;
			};
			foreach (var n in new[] { _rectX, _rectY, _rectW, _rectH }) n.ValueChanged += rectChanged;
			flow.Controls.Add(Row(Caption("Position"), _rectX, _rectY));
			flow.Controls.Add(Row(Caption("Size"), _rectW, _rectH));

			var hint = new Label
			{
				AutoSize = true,
				ForeColor = SystemColors.GrayText,
				MaximumSize = new Size(440, 0),
				Text = "Drag the green rectangle to the item cell(s). Wheel: zoom, right drag: pan, arrow keys: nudge (Shift: resize).",
			};
			flow.Controls.Add(hint);
			return group;
		}

		private Control BuildButtons()
		{
			var panel = new Panel { Dock = DockStyle.Top, Height = 84 };

			_discard = new Button { Text = "Discard", Width = 120, Height = 44, Dock = DockStyle.Right };
			_discard.Click += (_, _) => DiscardCurrent();
			_save = new Button { Text = "Save  (Ctrl+Enter)", Height = 44, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) };
			_save.Click += (_, _) => SaveCurrent();

			var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
			buttons.Controls.Add(_save);
			buttons.Controls.Add(_discard);

			_status = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), AutoEllipsis = true };
			panel.Controls.Add(_status);
			panel.Controls.Add(buttons);
			return panel;
		}

		private static FlowLayoutPanel Row(params Control[] controls)
		{
			var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
			row.Controls.AddRange(controls);
			return row;
		}

		private static Label Caption(string text)
		{
			return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 6, 0), MinimumSize = new Size(text.Length > 3 ? 64 : 0, 0) };
		}

		private static NumericUpDown Number(int min, int max)
		{
			return new NumericUpDown { Minimum = min, Maximum = max, Width = 72, Margin = new Padding(3, 2, 3, 2) };
		}

		#endregion

		#region Public interface

		/// <summary>Add a capture to the queue, it is shown right away if nothing else is pending</summary>
		public void AddCapture(PendingCapture capture, bool show)
		{
			_pending.Add(capture);
			if (_pending.Count == 1) ShowCurrent();
			UpdateStatus();
			if (show) ShowAndActivate();
		}

		/// <summary>Called on the UI thread when a scan finished</summary>
		public void OnScanCompleted(PendingCapture capture)
		{
			if (capture != Current) return;
			UpdateScanLabel();
			if (capture.Scan is { Found: true } scan)
			{
				_editor.Reference = scan.Rect;
				_editor.Invalidate();
				if (!capture.UserEdited) PrefillFromScan(scan);
			}
		}

		public void ShowAndActivate()
		{
			if (!Visible) Show();
			if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
			TopMost = true;
			TopMost = false;
			Activate();
			_editor.Focus();
		}

		#endregion

		#region Current capture

		private void ShowCurrent()
		{
			var cap = Current;
			_inputPanel.Enabled = cap != null;
			_updating = true;
			_selected = null;
			_search.Text = "";
			_matchGrid.Checked = false;
			_rotated.Checked = false;
			_gridW.Value = 1;
			_gridH.Value = 1;
			_highlighted.Checked = cap?.Highlighted ?? _s.Settings.Highlighted;
			_scale.Value = (decimal)Math.Clamp(cap?.Scale ?? 1f, 0.1f, 4f);
			_updating = false;

			_editor.Image = cap?.Screenshot;
			if (cap != null)
			{
				var fit = FitSize(1, 1);
				var rect = new Rectangle(cap.Mouse.X - fit.Width / 2, cap.Mouse.Y - fit.Height / 2, fit.Width, fit.Height);
				_editor.Marker = cap.Mouse;
				_editor.Rect = rect;
				SetRectFields(rect);
				CenterOnMouse();
			}
			else
			{
				SetRectFields(Rectangle.Empty);
			}

			UpdateSelectedLabel();
			RefreshItemList();
			UpdateScanLabel();
			UpdateStatus();

			if (cap?.Scan is { Found: true } scan)
			{
				_editor.Reference = scan.Rect;
				PrefillFromScan(scan);
			}
		}

		private void MarkEdited()
		{
			if (Current != null) Current.UserEdited = true;
		}

		private void PrefillFromScan(ScanResult scan)
		{
			var entry = _s.Catalog.Find(scan.ItemId);
			var grid = entry != null
				? (scan.Rotated ? (entry.Height, entry.Width) : (entry.Width, entry.Height))
				: (scan.Grid.Width, scan.Grid.Height);

			_updating = true;
			_rotated.Checked = scan.Rotated;
			_gridW.Value = Math.Clamp(grid.Item1, 1, 30);
			_gridH.Value = Math.Clamp(grid.Item2, 1, 30);
			_matchGrid.Checked = true;
			_selected = entry;
			var rect = new Rectangle(scan.Rect.Location, FitSize(grid.Item1, grid.Item2));
			_editor.Rect = rect;
			_editor.Reference = scan.Rect;
			SetRectFields(rect);
			_updating = false;

			UpdateSelectedLabel();
			RefreshItemList();
		}

		private void SelectItem(ItemEntry entry)
		{
			MarkEdited();
			_selected = entry;

			_updating = true;
			var gw = (int)_gridW.Value;
			var gh = (int)_gridH.Value;
			if (_matchGrid.Checked && entry.FitsGrid(gw, gh))
			{
				// Keep the grid, the orientation follows from it
				_rotated.Checked = !(entry.Width == gw && entry.Height == gh);
			}
			else
			{
				SetGrid(_rotated.Checked ? (entry.Height, entry.Width) : (entry.Width, entry.Height));
			}

			_updating = false;
			UpdateSelectedLabel();
			AfterGridChanged();
		}

		private void SetGrid((int width, int height) grid)
		{
			var wasUpdating = _updating;
			_updating = true;
			_gridW.Value = Math.Clamp(grid.width, 1, 30);
			_gridH.Value = Math.Clamp(grid.height, 1, 30);
			_updating = wasUpdating;
		}

		private void AfterGridChanged()
		{
			if (_autoFit.Checked && Current != null)
			{
				var rect = _editor.Rect;
				rect.Size = FitSize((int)_gridW.Value, (int)_gridH.Value);
				_updating = true;
				_editor.Rect = rect;
				SetRectFields(rect);
				_updating = false;
			}

			if (_matchGrid.Checked) RefreshItemList();
		}

		private Size FitSize(int gridW, int gridH)
		{
			var slot = BaseSlotSize * (Current?.Scale ?? 1f);
			return new Size((int)(gridW * slot) + 1, (int)(gridH * slot) + 1);
		}

		private void SetRectFields(Rectangle rect)
		{
			var wasUpdating = _updating;
			_updating = true;
			_rectX.Value = Math.Clamp(rect.X, (int)_rectX.Minimum, (int)_rectX.Maximum);
			_rectY.Value = Math.Clamp(rect.Y, (int)_rectY.Minimum, (int)_rectY.Maximum);
			_rectW.Value = Math.Clamp(rect.Width, (int)_rectW.Minimum, (int)_rectW.Maximum);
			_rectH.Value = Math.Clamp(rect.Height, (int)_rectH.Minimum, (int)_rectH.Maximum);
			_updating = wasUpdating;
		}

		private void RefreshItemList()
		{
			var tokens = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(t => t.ToLowerInvariant())
				.ToArray();
			var gw = (int)_gridW.Value;
			var gh = (int)_gridH.Value;

			var filtered = _s.Catalog.Entries.Where(e =>
			{
				if (_matchGrid.Checked && !e.FitsGrid(gw, gh)) return false;
				if (tokens.Length == 0) return true;
				var haystack = (e.Name + " " + e.ShortName + " " + e.Id).ToLowerInvariant();
				return tokens.All(haystack.Contains);
			}).ToArray();

			var wasUpdating = _updating;
			_updating = true;
			_items.BeginUpdate();
			_items.Items.Clear();
			_items.Items.AddRange(filtered);
			if (_selected != null && Array.IndexOf(filtered, _selected) is var index and >= 0) _items.SelectedIndex = index;
			_items.EndUpdate();
			_updating = wasUpdating;
		}

		private void UpdateSelectedLabel()
		{
			_selectedLabel.Text = _selected == null ? "No item selected" : $"{_selected.Name}  [{_selected.Width}x{_selected.Height}]";
			UpdateOverlay();
		}

		/// <summary>
		/// Show the transparent icon of the selected item inside the rectangle, rotated like the item on screen
		/// </summary>
		private void UpdateOverlay()
		{
			Bitmap overlay = null;
			if (_showOverlay.Checked && _selected != null) overlay = GetOverlay(_selected.Id, _rotated.Checked);
			_editor.Overlay = overlay;
			_editor.OverlayOpacity = _overlayOpacity.Value / 100f;
		}

		private Bitmap GetOverlay(string id, bool rotated)
		{
			if (_overlayCache.TryGetValue((id, rotated), out var cached)) return cached;

			Bitmap result = null;
			var path = System.IO.Path.Combine(_s.IconDir ?? "", id + ".png");
			if (System.IO.File.Exists(path))
			{
				// Copy so the file is not kept locked
				using var loaded = new Bitmap(path);
				result = new Bitmap(loaded);

				// RatEye turns the screenshot counterclockwise to find rotated items, so on screen they are clockwise
				if (rotated) result.RotateFlip(RotateFlipType.Rotate90FlipNone);
			}

			_overlayCache[(id, rotated)] = result;
			return result;
		}

		private void UpdateScanLabel()
		{
			var cap = Current;
			var scan = cap?.Scan;
			_rescan.Enabled = cap != null && !cap.Scanning;
			_useScan.Enabled = scan is { Found: true };

			if (cap == null) _scanLabel.Text = "Nothing captured.";
			else if (cap.Scanning) _scanLabel.Text = "Scanning...";
			else if (scan == null) _scanLabel.Text = "Not scanned.";
			else if (scan.Error != null) _scanLabel.Text = "Scan failed: " + scan.Error;
			else if (!scan.Found) _scanLabel.Text = $"Nothing detected under the mouse ({scan.ElapsedMs} ms).\nIs the item highlighted? Otherwise untick 'Item is highlighted' and rescan.";
			else
			{
				_scanLabel.Text = $"{scan.ItemName}\n{scan.Confidence:P0} confidence, {(scan.Rotated ? "rotated" : "not rotated")}, "
					+ $"{scan.Grid.Width}x{scan.Grid.Height} slots, {scan.ElapsedMs} ms";
			}
		}

		private void UpdateStatus()
		{
			_status.Text = $"{_pending.Count} pending   |   {_savedCount} saved in {_s.Store.RootDir}";
			Text = _pending.Count > 0 ? $"RatEye Ground Truth - {_pending.Count} pending" : "RatEye Ground Truth";
		}

		#endregion

		#region Save / discard

		private void SaveCurrent()
		{
			var cap = Current;
			if (cap == null) return;

			if (_selected == null)
			{
				MessageBox.Show(this, "Select the item first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				_search.Focus();
				return;
			}

			var rect = new Rectangle((int)_rectX.Value, (int)_rectY.Value, (int)_rectW.Value, (int)_rectH.Value);
			if (!new Rectangle(Point.Empty, cap.Screenshot.Size).Contains(rect))
			{
				MessageBox.Show(this, "The rectangle is not inside the screenshot.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			var truth = new TruthDto
			{
				ItemId = _selected.Id,
				ItemName = _selected.Name,
				Rotated = _rotated.Checked,
				GridWidth = (int)_gridW.Value,
				GridHeight = (int)_gridH.Value,
				Rect = RectDto.From(rect),
			};

			try
			{
				_s.Store.Save(cap, truth);
			}
			catch (Exception e)
			{
				MessageBox.Show(this, "Could not save:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_savedCount++;
			Advance();
		}

		private void DiscardCurrent()
		{
			if (Current == null) return;
			Advance();
		}

		private void Advance()
		{
			var done = Current;
			_pending.RemoveAt(0);
			ShowCurrent();

			// The editor no longer references the bitmap
			// (a running scan works on its own copy, see AppController)
			done.Screenshot.Dispose();
		}

		#endregion

		#region Form behavior

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			CenterOnMouse();
		}

		private void CenterOnMouse()
		{
			if (Current == null) return;
			var zoom = Math.Min(_editor.Width, _editor.Height) / (6 * BaseSlotSize * Current.Scale);
			_editor.CenterOn(Current.Mouse, Math.Clamp(zoom, 0.25f, 16f));
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.Return))
			{
				SaveCurrent();
				return true;
			}

			if (keyData == Keys.Escape)
			{
				Hide();
				return true;
			}

			return base.ProcessCmdKey(ref msg, keyData);
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				Hide();
				return;
			}

			base.OnFormClosing(e);
		}

		#endregion
	}
}
