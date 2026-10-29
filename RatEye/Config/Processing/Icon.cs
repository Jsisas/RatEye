using Tesseract;

namespace RatEye
{
	public partial class Config
	{
		public partial class Processing
		{
			/// <summary>
			/// The Icon class contains parameters, used by the icon processing module.
			/// </summary>
			public class Icon
			{
				/// <summary>
				/// Different kinds of scan modes which can be used to scan an icon
				/// </summary>
				public enum ScanModes
				{
					/// <summary>
					/// Scan the icon by matching it against a template image of every icon
					/// </summary>
					TemplateMatching,
					/// <summary>
					/// Scan the icon by recognizing the short name in the top right corner
					/// </summary>
					OCR,
				}

				/// <summary>
				/// The scanning mode used for icon scanning
				/// </summary>
				public ScanModes ScanMode = Icon.ScanModes.TemplateMatching;

				/// <summary>
				/// Use the static icons for template matching
				/// </summary>
				public bool UseStaticIcons = false;

				/// <summary>
				/// Scan for 90° rotated icons
				/// </summary>
				public bool ScanRotatedIcons = true;

				/// <summary>
				/// Read the short name of ambiguous template matches and use it to pick between look-alike icons
				/// </summary>
				public bool UseShortNameDisambiguation = true;

				/// <summary>
				/// Number of best template matches kept as candidates for disambiguation
				/// </summary>
				public int CandidateCount = 5;

				/// <summary>
				/// A match is ambiguous when the best two candidates are closer than this confidence margin
				/// </summary>
				public float AmbiguityMargin = 0.02f;

				/// <summary>
				/// A match is ambiguous when the best candidate has a confidence below this value
				/// </summary>
				public float AmbiguityMinConfidence = 0.9f;

				/// <summary>
				/// Tesseract Engine instance used and set by <see cref="RatEye.Processing.Icon"/>
				/// </summary>
				internal TesseractEngine TesseractEngine;

				/// <summary>
				/// Create a new icon config instance
				/// </summary>
				public Icon() { }

				internal string GetHash()
				{
					var components = new string[]
					{
						ScanMode.ToString(),
						UseStaticIcons.ToString(),
						ScanRotatedIcons.ToString(),
						UseShortNameDisambiguation.ToString(),
						CandidateCount.ToString(),
						AmbiguityMargin.ToString(),
						AmbiguityMinConfidence.ToString(),
					};
					return string.Join("<#sep#>", components).SHA256Hash();
				}
			}
		}
	}
}
