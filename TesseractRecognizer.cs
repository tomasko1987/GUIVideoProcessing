using System;
using System.Drawing;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Tesseract;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Trieda pre rozpoznávanie číslic pomocou Tesseract OCR.
	/// Optimalizovaná pre rozpoznávanie číslic (0-9).
	/// </summary>
	public class TesseractRecognizer : IDisposable
	{
		private readonly Logger? _logger;
		private TesseractEngine? _engine;
		private bool _disposed = false;

		/// <summary>
		/// Indikuje, či je Tesseract engine inicializovaný a pripravený.
		/// </summary>
		public bool IsLoaded => _engine != null;

		/// <summary>
		/// Konštruktor - vytvorí inštanciu bez inicializovaného engine.
		/// Pre inicializáciu zavolaj Initialize().
		/// </summary>
		/// <param name="logger">Logger pre diagnostické správy (môže byť null)</param>
		public TesseractRecognizer(Logger? logger)
		{
			_logger = logger;
		}

		/// <summary>
		/// Inicializuje Tesseract OCR engine.
		/// </summary>
		/// <param name="tessDataPath">Cesta k priečinku s tessdata (jazykové modely)</param>
		/// <param name="language">Jazyk pre OCR (default: "eng")</param>
		/// <returns>True ak sa engine úspešne inicializoval, inak False</returns>
		public bool Initialize(string tessDataPath, string language = "eng")
		{
			try
			{
				if (string.IsNullOrWhiteSpace(tessDataPath))
				{
					_logger?.Warn("TesseractRecognizer: tessdata path is empty");
					return false;
				}

				if (!Directory.Exists(tessDataPath))
				{
					_logger?.Warn($"TesseractRecognizer: tessdata folder not found: {tessDataPath}");
					return false;
				}

				// Skontroluj či existuje jazykový súbor
				string langFile = Path.Combine(tessDataPath, $"{language}.traineddata");
				if (!File.Exists(langFile))
				{
					_logger?.Warn($"TesseractRecognizer: Language file not found: {langFile}");
					return false;
				}

				// Dispose existujúceho engine ak existuje
				_engine?.Dispose();

				// Vytvor nový Tesseract engine
				_engine = new TesseractEngine(tessDataPath, language, EngineMode.Default);

				// Nastav whitelist len na číslice (0-9) pre lepšiu presnosť
				_engine.SetVariable("tessedit_char_whitelist", "0123456789");

				// Nastav PSM (Page Segmentation Mode) na single char alebo single word
				_engine.DefaultPageSegMode = PageSegMode.SingleChar;

				_logger?.Info($"TesseractRecognizer: Engine initialized with language '{language}'");
				_logger?.Info($"TesseractRecognizer: tessdata path: {tessDataPath}");

				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error($"TesseractRecognizer: Failed to initialize: {ex.Message}");
				_engine?.Dispose();
				_engine = null;
				return false;
			}
		}

		/// <summary>
		/// Rozpozná číslicu z Mat obrazu.
		/// </summary>
		/// <param name="digit">Mat objekt s číslicou (grayscale alebo BGR)</param>
		/// <returns>Tuple (predikovaná číslica 0-9, confidence 0.0-1.0) alebo (-1, 0) pri chybe</returns>
		public (int Digit, float Confidence) RecognizeDigit(Mat digit)
		{
			if (_engine == null)
			{
				_logger?.Warn("TesseractRecognizer: Engine not initialized");
				return (-1, 0f);
			}

			if (digit == null || digit.Empty())
			{
				_logger?.Warn("TesseractRecognizer: Input digit is null or empty");
				return (-1, 0f);
			}

			try
			{
				// Konvertuj Mat na Bitmap a potom na Pix pre Tesseract
				using Bitmap bitmap = MatToBitmap(digit);

				// Konvertuj Bitmap na byte array a načítaj ako Pix
				using var ms = new MemoryStream();
				bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
				byte[] imageBytes = ms.ToArray();
				using var pix = Pix.LoadFromMemory(imageBytes);

				// Spusti OCR
				using var page = _engine.Process(pix, PageSegMode.SingleChar);

				string text = page.GetText().Trim();
				float confidence = page.GetMeanConfidence();

				_logger?.Debug($"TesseractRecognizer: Raw text='{text}', confidence={confidence:P0}");

				// Parsuj výsledok
				if (!string.IsNullOrEmpty(text) && text.Length >= 1)
				{
					char firstChar = text[0];
					if (char.IsDigit(firstChar))
					{
						int recognizedDigit = firstChar - '0';
						return (recognizedDigit, confidence);
					}
				}

				return (-1, 0f);
			}
			catch (Exception ex)
			{
				_logger?.Error($"TesseractRecognizer: Recognition failed: {ex.Message}");
				return (-1, 0f);
			}
		}

		/// <summary>
		/// Rozpozná všetky číslice zo zoznamu a vráti výsledné číslo.
		/// </summary>
		/// <param name="digits">Zoznam Mat objektov s číslicami (zľava doprava)</param>
		/// <param name="minConfidence">Minimálna confidence pre akceptovanie číslice</param>
		/// <returns>Tuple (rozpoznané číslo ako string, priemerná confidence)</returns>
		public (string Text, float AverageConfidence) RecognizeDigits(System.Collections.Generic.List<Mat> digits, float minConfidence = 0.5f)
		{
			if (digits == null || digits.Count == 0)
			{
				return ("", 0f);
			}

			string text = "";
			float totalConfidence = 0f;
			int validCount = 0;

			foreach (var digit in digits)
			{
				var (recognizedDigit, confidence) = RecognizeDigit(digit);

				if (recognizedDigit >= 0 && confidence >= minConfidence)
				{
					text += recognizedDigit.ToString();
					totalConfidence += confidence;
					validCount++;
				}
				else
				{
					text += "?";
				}
			}

			float avgConfidence = validCount > 0 ? totalConfidence / validCount : 0f;

			_logger?.Info($"TesseractRecognizer: Recognized '{text}' with average confidence {avgConfidence:P1}");

			return (text, avgConfidence);
		}

		/// <summary>
		/// Konvertuje OpenCV Mat na System.Drawing.Bitmap.
		/// </summary>
		/// <param name="mat">Vstupný Mat objekt</param>
		/// <returns>Bitmap objekt</returns>
		private Bitmap MatToBitmap(Mat mat)
		{
			// Uisti sa, že máme grayscale alebo BGR
			Mat converted;

			if (mat.Channels() == 1)
			{
				// Grayscale -> BGR (Tesseract preferuje farebný obraz)
				converted = new Mat();
				Cv2.CvtColor(mat, converted, ColorConversionCodes.GRAY2BGR);
			}
			else
			{
				converted = mat;
			}

			// Použijeme OpenCvSharp.Extensions pre konverziu
			Bitmap bitmap = BitmapConverter.ToBitmap(converted);

			// Cleanup
			if (converted != mat)
			{
				converted.Dispose();
			}

			return bitmap;
		}

		/// <summary>
		/// Uvoľní zdroje (Tesseract engine).
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (disposing)
				{
					_engine?.Dispose();
					_engine = null;
				}
				_disposed = true;
			}
		}

		~TesseractRecognizer()
		{
			Dispose(false);
		}
	}
}
