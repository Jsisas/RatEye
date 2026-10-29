using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using RatStash;
using Tesseract;
using static System.Net.Mime.MediaTypeNames;

namespace RatEye.Processing
{
	/// <summary>
	/// Represents an icon in the inventory
	/// </summary>
	public class Icon
	{
		private readonly Config _config;
		private readonly Bitmap _icon;
		private Bitmap _scaledIcon;
		private Item _item;
		private ItemExtraInfo _itemExtraInfo;
		private float _detectionConfidence;
		private Vector2 _itemPosition;
		private bool _rotated;
		private string _ocrTitle = "";
		private readonly float _scale;

		/// <summary>
		/// Best template matches of the unrotated and rotated pass
		/// </summary>
		private readonly List<(string match, float confidence, Vector2 pos, bool rotated)> _candidates = new();

		private Config.Processing ProcessingConfig => _config.ProcessingConfig;
		private Config.Path PathConfig => _config.PathConfig;
		private Config.Processing.Icon IconConfig => ProcessingConfig.IconConfig;

		/// <summary>
		/// Sync object to synchronize control flow of multiple threads
		/// </summary>
		private object _sync = new();

		/// <summary>
		/// Position of the icon inside the inventory (top, left)
		/// </summary>
		public Vector2 Position { get; }

		/// <summary>
		/// Size of the icon image, measured in pixel
		/// </summary>
		/// <remarks>
		/// This might not be the exact size of the item and does not account for rotation
		/// </remarks>
		public Vector2 Size { get; }

		/// <summary>
		/// Size of the item, measured in pixels
		/// </summary>
		public Vector2 ItemSize
		{
			get
			{
				var size = IconSlotSize();
				size.X = (int)(size.X * ScaledSlotSize);
				size.Y = (int)(size.Y * ScaledSlotSize);
				return size;
			}
		}

		/// <summary>
		/// The detected item
		/// </summary>
		public Item Item
		{
			get
			{
				SatisfyState(State.Scanned);
				return _item;
			}
		}

		/// <summary>
		/// The detected item extra info
		/// </summary>
		public ItemExtraInfo ItemExtraInfo
		{
			get
			{
				SatisfyState(State.Scanned);
				return _itemExtraInfo;
			}
		}

		/// <summary>
		/// The path to the icon of the detected item
		/// </summary>
		public string IconPath => _config.IconManager.GetIconPath(Item, ItemExtraInfo ?? new ItemExtraInfo());

		/// <summary>
		/// Confidence with which the <see cref="Item"/> was detected/>
		/// </summary>
		public float DetectionConfidence
		{
			get
			{
				SatisfyState(State.Scanned);
				return _detectionConfidence;
			}
		}

		/// <summary>
		/// The exact position of the item
		/// </summary>
		public Vector2 ItemPosition
		{
			get
			{
				SatisfyState(State.Scanned);
				return _itemPosition;
			}
		}

		/// <summary>
		/// <see langword="true"/> if the icon is rotated
		/// </summary>
		public bool Rotated
		{
			get
			{
				SatisfyState(State.Scanned);
				return _rotated;
			}
		}

		internal Icon(Bitmap icon, Vector2 position, Vector2 size, Config config, float scale)
		{
			_config = config;
			_icon = icon;
			Position = position;
			Size = size;
			_scale = scale;
		}

		private float ScaledSlotSize => _scale * ProcessingConfig.BaseSlotSize;

		private enum State
		{
			Default,
			Rescaled,
			Scanned,
		}

		private State _currentState = State.Default;

		private void SatisfyState(State targetState)
		{
			while (_currentState < targetState)
			{
				switch (_currentState + 1)
				{
					case State.Default:
						break;
					case State.Rescaled:
						RescaleIcon();
						break;
					case State.Scanned:
						if (IconConfig.ScanMode == Config.Processing.Icon.ScanModes.TemplateMatching)
						{
							TemplateMatch();
							if (IconConfig.ScanRotatedIcons) TemplateMatch(true);
							Disambiguate();
						}
						else if (IconConfig.ScanMode == Config.Processing.Icon.ScanModes.OCR) OCR();
						break;
					default:
						throw new Exception("Cannot satisfy unknown state.");
				}

				_currentState++;
			}
		}

		private void RescaleIcon()
		{
			Logger.LogDebugBitmap(_icon, "icon/_icon");
			var mul = IconConfig.ScanMode == Config.Processing.Icon.ScanModes.OCR ? 2 : 1;
			_scaledIcon = _icon.Rescale(1f / _scale * mul);
			Logger.LogDebugBitmap(_scaledIcon, "icon/_scaledIcon");
		}

		private void TemplateMatch(bool rotated = false)
		{
			SatisfyState(State.Rescaled);

			// NOTE The source image is scaled hence all outgoing pixel values need to be adjusted accordingly
			using var source = _scaledIcon.ToMat();
			if (rotated) Cv2.Rotate(source, source, RotateFlags.Rotate90Counterclockwise);

			Logger.LogDebugMat(source, "icon/source");

			List<(string match, float confidence, Vector2 pos)> staticResults = new();

			if (!IconConfig.UseStaticIcons)
			{
				throw new Exception(
					"No icons for template matching can be used." +
					nameof(IconConfig.UseStaticIcons) + " is false.");
			}

			var iconManager = _config.IconManager;
			var iconSlotSize = IconSlotSize();
			var slotSize = rotated ? new Vector2(iconSlotSize.Y, iconSlotSize.X) : iconSlotSize;
			if (IconConfig.UseStaticIcons)
			{
				if (iconManager.StaticIcons.ContainsKey(slotSize))
				{
					iconManager.StaticIconsLock.EnterReadLock();
					try { staticResults = TemplateMatchSub(source, iconManager.StaticIcons[slotSize]); }
					finally { iconManager.StaticIconsLock.ExitReadLock(); }
				}
			}

			foreach (var candidate in staticResults) _candidates.Add((candidate.match, candidate.confidence, candidate.pos, rotated));

			if (staticResults.Count == 0) return;
			var result = staticResults[0];

			if (!(result.confidence > _detectionConfidence)) return;

			SetTemplateResult(result.match, result.confidence, result.pos, rotated);
		}

		private void SetTemplateResult(string match, float confidence, Vector2 pos, bool rotated)
		{
			_rotated = rotated;
			_itemPosition = (rotated ? new(pos.Y, pos.X) : pos) * _scale;
			_detectionConfidence = confidence;
			_item = _config.IconManager.GetItem(match);
			_itemExtraInfo = _config.IconManager.GetItemExtraInfo(match);
		}

		/// <summary>
		/// Pick between look-alike template matches by reading the short name of the icon
		/// </summary>
		private void Disambiguate()
		{
			if (!IconConfig.UseShortNameDisambiguation || _candidates.Count < 2) return;

			var ordered = _candidates.OrderByDescending(c => c.confidence).ToList();
			var best = ordered[0].confidence;
			var second = ordered[1].confidence;
			if (best - second >= IconConfig.AmbiguityMargin && best >= IconConfig.AmbiguityMinConfidence) return;

			string ocr;
			try
			{
				ocr = ReadShortName(_icon.Rescale(2f / _scale));
			}
			catch (Exception e)
			{
				Logger.LogDebug("Short-name OCR failed: " + e.Message);
				return;
			}
			if (string.IsNullOrEmpty(ocr)) return;

			var bestScore = float.MinValue;
			(string match, float confidence, Vector2 pos, bool rotated)? chosen = null;
			foreach (var candidate in ordered)
			{
				var item = _config.IconManager.GetItem(candidate.match);
				if (item == null) continue;
				var score = candidate.confidence + 0.5f * item.ShortName.Replace("I", "T").CyrillicToLatin().NormedLevenshteinDistance(ocr);
				if (!(score > bestScore)) continue;
				bestScore = score;
				chosen = candidate;
			}
			if (chosen == null) return;

			var c = chosen.Value;
			SetTemplateResult(c.match, c.confidence, c.pos, c.rotated);
			Logger.LogDebug($"Disambiguated '{ocr}' -> {_item?.ShortName}");
		}

		private List<(string match, float confidence, Vector2 pos)> TemplateMatchSub(Mat source, Dictionary<string, Mat> icons)
		{
			var candidateCount = Math.Max(1, IconConfig.CandidateCount);
			var results = new List<(string match, float confidence, Vector2 pos)>(candidateCount + 1);

			Parallel.ForEach(icons, icon =>
			{
				using var matches = source.MatchTemplate(icon.Value, TemplateMatchModes.SqDiffNormed);
				matches.MinMaxLoc(out var minVal, out _, out var minLoc, out _);

				lock (_sync)
				{
					var confidence = (float)(1 - minVal);
					if (results.Count == candidateCount && !(confidence > results[results.Count - 1].confidence)) return;

					var index = results.FindIndex(r => confidence > r.confidence);
					if (index < 0) index = results.Count;
					results.Insert(index, (icon.Key, confidence, new Vector2(minLoc)));
					if (results.Count > candidateCount) results.RemoveAt(results.Count - 1);
				}
			});

			return results;
		}

		/// <summary>
		/// Converts the pixel unit of the icon into the slot unit
		/// </summary>
		/// <returns>Slot size of the icon</returns>
		private Vector2 IconSlotSize()
		{
			// Use converter class to round to nearest int instead of always rounding down
			var x = (Size.X - 1) / ScaledSlotSize;
			var y = (Size.Y - 1) / ScaledSlotSize;
			return new Vector2((int)x, (int)y);
		}
		/// <summary>
		/// Perform optical character recognition on a image
		/// </summary>
		/// <param name="image">Image to perform OCR on</param>
		/// <returns>Detected characters in image</returns>
		private void OCR()
		{
			_itemPosition = Vector2.Zero;
			_ocrTitle = ReadShortName(_scaledIcon);
			SetOCRItem();
		}

		/// <summary>
		/// Read the short name in the top strip of an icon
		/// </summary>
		/// <param name="scaledIcon">Icon image rescaled to twice the 1080p size</param>
		/// <returns>The cleaned up short name</returns>
		private string ReadShortName(Bitmap scaledIcon)
		{
			var topCutoff = 0;
			var leftCutoff = 4;

			// Setup tesseract
			Logger.LogDebugMat(scaledIcon.ToMat());
			var topText = scaledIcon.Crop(leftCutoff, topCutoff, scaledIcon.Width - leftCutoff, 24);
			using var topTextMat = topText.ToMat();

			using var hsv = topTextMat.CvtColor(ColorConversionCodes.BGR2HSV_FULL);
			using var colorFilter = hsv.InRange(new Scalar(93 * (255f / 180f), 16, 97), new Scalar(113 * (255f / 180f), 24, 217));

			Cv2.MorphologyEx(colorFilter, colorFilter, MorphTypes.Close, Mat.Ones(2, 2));
			Cv2.BitwiseNot(colorFilter, colorFilter);
			Logger.LogDebugMat(colorFilter);

			using var final = colorFilter.ToBitmap().Rescale(2);
			Logger.LogDebugBitmap(final);

			// Convert to Pix
			using var pix = PixConverter.ToPix(final);

			// OCR
			Logger.LogDebug("Applying OCR...");
			var engine = GetTesseractEngine();
			string text;
			// Tesseract engines are not thread safe and the engine is shared through the config
			lock (engine)
			{
				using var result = engine.Process(pix);
				text = result.GetText();

				var r = result.GetSegmentedRegions(PageIteratorLevel.TextLine);
				foreach (var x in r)
				{
					Logger.LogDebug("TEXT: " + x);
				}
			}
			Logger.LogDebugMat(topTextMat, "lalalla");

			var rgx = new Regex("[^a-zA-Z0-9 -\\.]");
			var title = rgx.Replace(text.CyrillicToLatin().Trim(), "").Trim();
			Logger.LogDebug("Read: " + title);
			return title;
		}

		/// <summary>
		/// Set the item to one, best matching the scanned title
		/// </summary>
		private void SetOCRItem()
		{
			var slotSize = IconSlotSize();
			var items = _config.RatStashDB.GetItems(i =>
			{
				var v = new Vector2(i.GetSlotSize());
				return v == slotSize || v == slotSize.Flipped;
			});
			_item = items.Aggregate((i1, i2) =>
			{
				var i1Dist = i1.ShortName.Replace("I", "T").CyrillicToLatin().NormedLevenshteinDistance(_ocrTitle);
				var i2Dist = i2.ShortName.Replace("I", "T").CyrillicToLatin().NormedLevenshteinDistance(_ocrTitle);
				return i1Dist > i2Dist ? i1 : i2;
			});
			_detectionConfidence = _item.ShortName.CyrillicToLatin().NormedLevenshteinDistance(_ocrTitle);
			_rotated = new Vector2(_item.GetSlotSize()) != slotSize;
		}

		/// <summary>
		/// Creates an instance of the OCRTesseract class. Initializes Tesseract.
		/// </summary>
		/// <returns>Tesseract instance trained for the bender font</returns>
		private TesseractEngine GetTesseractEngine()
		{
			// Return if tesseract instance was already created
			var tesseractEngine = IconConfig.TesseractEngine;
			if (tesseractEngine != null) return tesseractEngine;

			// Check if trained data is present
			var langCode = _config.ProcessingConfig.Language.ToISO3Code();
			var traineddataPath = $"{PathConfig.TrainedData}\\{langCode}.traineddata";
			if (!System.IO.File.Exists(traineddataPath))
			{
				var message = "Could not find traineddata at: " + traineddataPath;
				var ex = new ArgumentException(message, PathConfig.TrainedData);
				throw ex;
			}

			// Load additional language to expand the primary one
			var addLang = _config.ProcessingConfig.Language switch
			{
				//Language.Chinese => "eng",
				Language.Czech => "+eng",
				Language.Japanese => "+eng",
				Language.Korean => "+eng",
				Language.Russian => "+eng",
				_ => "",
			};

			var language = langCode + addLang;

			// Create a tesseract instance
			IconConfig.TesseractEngine = new TesseractEngine(PathConfig.TrainedData, language, EngineMode.LstmOnly);
			IconConfig.TesseractEngine.DefaultPageSegMode = PageSegMode.RawLine;

			return IconConfig.TesseractEngine;
		}
	}
}
