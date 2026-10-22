using System;
using System.Threading;
using System.Windows.Forms;

namespace RatEyeGroundTruth
{
	internal static class Program
	{
		[STAThread]
		private static void Main(string[] args)
		{
			ApplicationConfiguration.Initialize();

			using var mutex = new Mutex(true, "RatEyeGroundTruth.SingleInstance", out var isFirstInstance);
			if (!isFirstInstance)
			{
				MessageBox.Show("RatEye Ground Truth is already running, look for it in the system tray.", "RatEye Ground Truth");
				return;
			}

			Application.Run(new AppController(args));
		}
	}
}
