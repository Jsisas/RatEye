using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace RatEyeGroundTruth
{
	/// <summary>
	/// Shows a screenshot which can be zoomed and panned, with a rectangle which can be moved,
	/// resized and redrawn. Coordinates of the rectangle are screenshot pixels.
	/// <para/>
	/// Mouse wheel: zoom, right/middle drag: pan, left drag: edit rectangle,
	/// arrow keys: move rectangle (with Shift: resize), Home: center on the mouse marker.
	/// </summary>
	internal sealed class ImageEditor : Control
	{
		[Flags]
		private enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

		private enum DragMode { None, Pan, Move, Resize, Draw }

		private const float MinZoom = 0.1f;
		private const float MaxZoom = 40f;
		private const int HandleTolerance = 6;

		private Bitmap _image;
		private float _zoom = 1;
		private PointF _origin; // screenshot pixel at the top left of the control
		private Rectangle _rect;

		private DragMode _drag;
		private Edge _dragEdges;
		private Point _dragStart;      // screenshot pixel where the drag started
		private Point _dragOffset;     // for moving: mouse offset inside the rectangle
		private Rectangle _dragRect;   // rectangle when the drag started
		private Point _panStartScreen;
		private PointF _panStartOrigin;

		/// <summary>Raised when the user changed <see cref="Rect"/></summary>
		public event EventHandler RectChanged;

		public ImageEditor()
		{
			DoubleBuffered = true;
			TabStop = true;
			BackColor = Color.FromArgb(32, 32, 32);
			SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
		}

		public Bitmap Image
		{
			get => _image;
			set
			{
				_image = value;
				_rect = Rectangle.Empty;
				Reference = Rectangle.Empty;
				Marker = null;
				_origin = PointF.Empty;
				Invalidate();
			}
		}

		/// <summary>Rectangle in screenshot pixels. Setting it does not raise <see cref="RectChanged"/></summary>
		public Rectangle Rect
		{
			get => _rect;
			set
			{
				if (_rect == value) return;
				_rect = value;
				Invalidate();
			}
		}

		/// <summary>Rectangle shown as dashed outline, used for what the scanner found</summary>
		public Rectangle Reference { get; set; }

		private Bitmap _overlay;
		private float _overlayOpacity = 0.5f;

		/// <summary>Image with transparency which is drawn semi transparent into <see cref="Rect"/> to help positioning it</summary>
		public Bitmap Overlay
		{
			get => _overlay;
			set
			{
				_overlay = value;
				Invalidate();
			}
		}

		/// <summary>Opacity of <see cref="Overlay"/>, 0 to 1</summary>
		public float OverlayOpacity
		{
			get => _overlayOpacity;
			set
			{
				_overlayOpacity = Math.Clamp(value, 0f, 1f);
				Invalidate();
			}
		}

		/// <summary>Position of the mouse in the screenshot, shown as cross hair</summary>
		public Point? Marker { get; set; }

		/// <summary>Show the area around a screenshot pixel</summary>
		public void CenterOn(Point p, float zoom)
		{
			_zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
			_origin = new PointF(p.X + 0.5f - Width / 2f / _zoom, p.Y + 0.5f - Height / 2f / _zoom);
			Invalidate();
		}

		#region Coordinates

		private Point ToImage(Point screen)
		{
			return new Point(
				(int)Math.Floor(_origin.X + screen.X / _zoom),
				(int)Math.Floor(_origin.Y + screen.Y / _zoom));
		}

		private RectangleF ToScreen(Rectangle r)
		{
			return new RectangleF(
				(r.X - _origin.X) * _zoom,
				(r.Y - _origin.Y) * _zoom,
				r.Width * _zoom,
				r.Height * _zoom);
		}

		#endregion

		#region Painting

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(BackColor);

			if (_image == null)
			{
				using var brush = new SolidBrush(Color.Gray);
				var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
				g.DrawString("No capture", Font, brush, ClientRectangle, format);
				return;
			}

			var x0 = Math.Max(0, (int)Math.Floor(_origin.X));
			var y0 = Math.Max(0, (int)Math.Floor(_origin.Y));
			var x1 = Math.Min(_image.Width, (int)Math.Ceiling(_origin.X + Width / _zoom));
			var y1 = Math.Min(_image.Height, (int)Math.Ceiling(_origin.Y + Height / _zoom));
			if (x1 > x0 && y1 > y0)
			{
				var dest = new RectangleF((x0 - _origin.X) * _zoom, (y0 - _origin.Y) * _zoom, (x1 - x0) * _zoom, (y1 - y0) * _zoom);
				g.InterpolationMode = _zoom >= 1.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
				g.PixelOffsetMode = PixelOffsetMode.Half;
				g.DrawImage(_image, dest, new RectangleF(x0, y0, x1 - x0, y1 - y0), GraphicsUnit.Pixel);
			}

			if (_overlay != null && !_rect.IsEmpty)
			{
				using var attributes = new ImageAttributes();
				attributes.SetColorMatrix(new ColorMatrix { Matrix33 = _overlayOpacity }, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.Half;
				g.DrawImage(_overlay, Rectangle.Round(ToScreen(_rect)), 0, 0, _overlay.Width, _overlay.Height, GraphicsUnit.Pixel, attributes);
			}

			if (Marker is { } marker)
			{
				var c = ToScreen(new Rectangle(marker.X, marker.Y, 1, 1));
				var cx = c.X + c.Width / 2;
				var cy = c.Y + c.Height / 2;
				using var pen = new Pen(Color.Magenta, 1);
				g.DrawLine(pen, cx - 10, cy, cx + 10, cy);
				g.DrawLine(pen, cx, cy - 10, cx, cy + 10);
			}

			if (!Reference.IsEmpty)
			{
				using var pen = new Pen(Color.Gold, 1) { DashStyle = DashStyle.Dash };
				g.DrawRectangle(pen, Rectangle.Round(ToScreen(Reference)));
			}

			if (!_rect.IsEmpty)
			{
				var r = ToScreen(_rect);
				using var fill = new SolidBrush(Color.FromArgb(30, Color.Lime));
				g.FillRectangle(fill, r);
				using var pen = new Pen(Color.Lime, 1);
				g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

				using var handle = new SolidBrush(Color.Lime);
				foreach (var p in new[]
				{
					new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom),
					new PointF(r.Left + r.Width / 2, r.Top), new PointF(r.Left + r.Width / 2, r.Bottom),
					new PointF(r.Left, r.Top + r.Height / 2), new PointF(r.Right, r.Top + r.Height / 2),
				})
				{
					g.FillRectangle(handle, p.X - 2.5f, p.Y - 2.5f, 5, 5);
				}
			}

			using var info = new SolidBrush(Color.FromArgb(200, Color.White));
			g.DrawString($"{_zoom:0.##}x   {_rect.X},{_rect.Y}  {_rect.Width}x{_rect.Height}", Font, info, 6, 4);
		}

		#endregion

		#region Mouse

		private Edge HitEdges(Point screen)
		{
			if (_rect.IsEmpty) return Edge.None;

			var r = ToScreen(_rect);
			var inX = screen.X >= r.Left - HandleTolerance && screen.X <= r.Right + HandleTolerance;
			var inY = screen.Y >= r.Top - HandleTolerance && screen.Y <= r.Bottom + HandleTolerance;
			if (!inX || !inY) return Edge.None;

			var edges = Edge.None;
			if (Math.Abs(screen.X - r.Left) <= HandleTolerance) edges |= Edge.Left;
			else if (Math.Abs(screen.X - r.Right) <= HandleTolerance) edges |= Edge.Right;
			if (Math.Abs(screen.Y - r.Top) <= HandleTolerance) edges |= Edge.Top;
			else if (Math.Abs(screen.Y - r.Bottom) <= HandleTolerance) edges |= Edge.Bottom;
			return edges;
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
			if (_image == null) return;

			if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
			{
				_drag = DragMode.Pan;
				_panStartScreen = e.Location;
				_panStartOrigin = _origin;
				return;
			}

			if (e.Button != MouseButtons.Left) return;

			var p = ToImage(e.Location);
			_dragStart = p;
			_dragRect = _rect;

			var edges = HitEdges(e.Location);
			if (edges != Edge.None)
			{
				_drag = DragMode.Resize;
				_dragEdges = edges;
			}
			else if (_rect.Contains(p))
			{
				_drag = DragMode.Move;
				_dragOffset = new Point(p.X - _rect.X, p.Y - _rect.Y);
			}
			else
			{
				_drag = DragMode.Draw;
				_rect = new Rectangle(p, new Size(1, 1));
				Invalidate();
				RectChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (_image == null) return;

			switch (_drag)
			{
				case DragMode.None:
					Cursor = CursorFor(HitEdges(e.Location), _rect.Contains(ToImage(e.Location)));
					return;

				case DragMode.Pan:
					_origin = new PointF(
						_panStartOrigin.X - (e.X - _panStartScreen.X) / _zoom,
						_panStartOrigin.Y - (e.Y - _panStartScreen.Y) / _zoom);
					Invalidate();
					return;
			}

			var p = ToImage(e.Location);
			Rectangle updated;
			switch (_drag)
			{
				case DragMode.Move:
					updated = new Rectangle(p.X - _dragOffset.X, p.Y - _dragOffset.Y, _dragRect.Width, _dragRect.Height);
					break;

				case DragMode.Draw:
					updated = Rectangle.FromLTRB(
						Math.Min(_dragStart.X, p.X),
						Math.Min(_dragStart.Y, p.Y),
						Math.Max(_dragStart.X, p.X) + 1,
						Math.Max(_dragStart.Y, p.Y) + 1);
					break;

				default: // Resize
					var left = _dragRect.Left;
					var right = _dragRect.Right;
					var top = _dragRect.Top;
					var bottom = _dragRect.Bottom;
					if (_dragEdges.HasFlag(Edge.Left)) left = Math.Min(p.X, right - 1);
					if (_dragEdges.HasFlag(Edge.Right)) right = Math.Max(p.X + 1, left + 1);
					if (_dragEdges.HasFlag(Edge.Top)) top = Math.Min(p.Y, bottom - 1);
					if (_dragEdges.HasFlag(Edge.Bottom)) bottom = Math.Max(p.Y + 1, top + 1);
					updated = Rectangle.FromLTRB(left, top, right, bottom);
					break;
			}

			SetRectFromUser(updated);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			_drag = DragMode.None;
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			if (_image == null) return;

			var before = new PointF(_origin.X + e.X / _zoom, _origin.Y + e.Y / _zoom);
			_zoom = Math.Clamp(_zoom * (float)Math.Pow(1.25, e.Delta / 120.0), MinZoom, MaxZoom);
			_origin = new PointF(before.X - e.X / _zoom, before.Y - e.Y / _zoom);
			Invalidate();
		}

		private static Cursor CursorFor(Edge edges, bool inside)
		{
			if (edges == (Edge.Left | Edge.Top) || edges == (Edge.Right | Edge.Bottom)) return Cursors.SizeNWSE;
			if (edges == (Edge.Right | Edge.Top) || edges == (Edge.Left | Edge.Bottom)) return Cursors.SizeNESW;
			if ((edges & (Edge.Left | Edge.Right)) != 0) return Cursors.SizeWE;
			if ((edges & (Edge.Top | Edge.Bottom)) != 0) return Cursors.SizeNS;
			return inside ? Cursors.SizeAll : Cursors.Cross;
		}

		#endregion

		#region Keyboard

		protected override bool IsInputKey(Keys keyData)
		{
			switch (keyData & Keys.KeyCode)
			{
				case Keys.Left:
				case Keys.Right:
				case Keys.Up:
				case Keys.Down:
					return true;
			}

			return base.IsInputKey(keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (_image == null) return;

			if (e.KeyCode == Keys.Home)
			{
				if (Marker is { } marker) CenterOn(marker, _zoom);
				e.Handled = true;
				return;
			}

			int dx = 0, dy = 0;
			switch (e.KeyCode)
			{
				case Keys.Left: dx = -1; break;
				case Keys.Right: dx = 1; break;
				case Keys.Up: dy = -1; break;
				case Keys.Down: dy = 1; break;
				default: return;
			}

			e.Handled = true;
			if (_rect.IsEmpty) return;

			var r = _rect;
			if (e.Shift) r = new Rectangle(r.X, r.Y, Math.Max(1, r.Width + dx), Math.Max(1, r.Height + dy));
			else r.Offset(dx, dy);
			SetRectFromUser(r);
		}

		#endregion

		private void SetRectFromUser(Rectangle r)
		{
			if (r == _rect) return;
			_rect = r;
			Invalidate();
			RectChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
