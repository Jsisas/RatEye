using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RatEyeGroundTruth
{
	/// <summary>
	/// Registers a system wide hotkey, which works while another application has the focus
	/// </summary>
	internal sealed class HotKeyManager : NativeWindow, IDisposable
	{
		private const int WmHotkey = 0x0312;
		private const int HotkeyId = 0x7A11;

		private const uint ModAlt = 0x1;
		private const uint ModControl = 0x2;
		private const uint ModShift = 0x4;
		private const uint ModWin = 0x8;
		private const uint ModNoRepeat = 0x4000;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		public event EventHandler Pressed;

		private bool _registered;

		public HotKeyManager()
		{
			CreateHandle(new CreateParams());
		}

		/// <summary>
		/// Register a hotkey like <c>Ctrl+Shift+F9</c>
		/// </summary>
		/// <returns><see langword="null"/> on success, otherwise a message describing the problem</returns>
		public string Register(string spec)
		{
			Unregister();

			uint modifiers = ModNoRepeat;
			Keys? key = null;
			foreach (var part in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				switch (part.ToLowerInvariant())
				{
					case "ctrl":
					case "control": modifiers |= ModControl; break;
					case "alt": modifiers |= ModAlt; break;
					case "shift": modifiers |= ModShift; break;
					case "win": modifiers |= ModWin; break;
					default:
						if (!Enum.TryParse<Keys>(part, true, out var parsed)) return $"Unknown key '{part}' in hotkey '{spec}'";
						key = parsed;
						break;
				}
			}

			if (key == null) return $"Hotkey '{spec}' has no key";
			if (!RegisterHotKey(Handle, HotkeyId, modifiers, (uint)key.Value))
			{
				return $"Could not register hotkey '{spec}', it is probably used by another application";
			}

			_registered = true;
			return null;
		}

		private void Unregister()
		{
			if (!_registered) return;
			UnregisterHotKey(Handle, HotkeyId);
			_registered = false;
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId) Pressed?.Invoke(this, EventArgs.Empty);
			base.WndProc(ref m);
		}

		public void Dispose()
		{
			Unregister();
			DestroyHandle();
		}
	}
}
