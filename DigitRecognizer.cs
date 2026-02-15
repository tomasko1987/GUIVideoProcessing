using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Trieda pre rozpoznávanie číslic pomocou ONNX modelu (MNIST).
	/// Používa Microsoft.ML.OnnxRuntime pre inferenciu.
	/// </summary>
	public class DigitRecognizer : IDisposable
	{
		private readonly Logger? _logger;
		private InferenceSession? _session;
		private string? _inputName;
		private bool _disposed = false;

		/// <summary>
		/// Indikuje, či je model načítaný a pripravený na použitie.
		/// </summary>
		public bool IsLoaded => _session != null;

		/// <summary>
		/// Konštruktor - vytvorí inštanciu bez načítaného modelu.
		/// Pre načítanie modelu zavolaj LoadModel().
		/// </summary>
		/// <param name="logger">Logger pre diagnostické správy (môže byť null)</param>
		public DigitRecognizer(Logger? logger)
		{
			_logger = logger;
		}

		/// <summary>
		/// Načíta ONNX model zo súboru.
		/// </summary>
		/// <param name="modelPath">Cesta k ONNX súboru (napr. mnist.onnx)</param>
		/// <returns>True ak sa model úspešne načítal, inak False</returns>
		public bool LoadModel(string modelPath)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(modelPath))
				{
					_logger?.Warn("DigitRecognizer: Model path is empty");
					return false;
				}

				if (!System.IO.File.Exists(modelPath))
				{
					_logger?.Warn($"DigitRecognizer: Model file not found: {modelPath}");
					return false;
				}

				// Dispose existujúcej session ak existuje
				_session?.Dispose();

				// Vytvor novú ONNX session
				var options = new SessionOptions();
				options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

				_session = new InferenceSession(modelPath, options);

				// Získaj názov vstupného tensora
				_inputName = _session.InputMetadata.Keys.First();

				_logger?.Info($"DigitRecognizer: Model loaded successfully from {modelPath}");
				_logger?.Info($"DigitRecognizer: Input name: {_inputName}, shape: [{string.Join(", ", _session.InputMetadata[_inputName].Dimensions)}]");

				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error($"DigitRecognizer: Failed to load model: {ex.Message}");
				_session?.Dispose();
				_session = null;
				return false;
			}
		}

		/// <summary>
		/// Rozpozná číslicu z Mat obrazu (28x28 px, grayscale).
		/// </summary>
		/// <param name="digit">Mat objekt s číslicou (28x28 px, white on black)</param>
		/// <returns>Tuple (predikovaná číslica 0-9, confidence 0.0-1.0) alebo (-1, 0) pri chybe</returns>
		public (int Digit, float Confidence) RecognizeDigit(Mat digit)
		{
			if (_session == null || _inputName == null)
			{
				_logger?.Warn("DigitRecognizer: Model not loaded");
				return (-1, 0f);
			}

			if (digit == null || digit.Empty())
			{
				_logger?.Warn("DigitRecognizer: Input digit is null or empty");
				return (-1, 0f);
			}

			try
			{
				// 1. Príprava vstupných dát
				float[] inputData = PrepareInput(digit);

				// 2. Vytvor vstupný tensor
				// MNIST model očakáva tvar [1, 1, 28, 28] (batch, channels, height, width)
				var inputTensor = new DenseTensor<float>(inputData, new[] { 1, 1, 28, 28 });

				// 3. Vytvor vstupný kontajner
				var inputs = new List<NamedOnnxValue>
				{
					NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
				};

				// 4. Spusti inferenciu
				using var results = _session.Run(inputs);

				// 5. Získaj výstup (logits alebo probabilities)
				var output = results.First().AsTensor<float>();
				float[] outputArray = output.ToArray();

				// 6. Aplikuj softmax ak výstup nie sú pravdepodobnosti
				float[] probabilities = Softmax(outputArray);

				// 7. Nájdi triedu s najvyššou pravdepodobnosťou
				int predictedDigit = 0;
				float maxProb = probabilities[0];

				for (int i = 1; i < probabilities.Length; i++)
				{
					if (probabilities[i] > maxProb)
					{
						maxProb = probabilities[i];
						predictedDigit = i;
					}
				}

				_logger?.Debug($"DigitRecognizer: Predicted {predictedDigit} with confidence {maxProb:P1}");

				return (predictedDigit, maxProb);
			}
			catch (Exception ex)
			{
				_logger?.Error($"DigitRecognizer: Recognition failed: {ex.Message}");
				return (-1, 0f);
			}
		}

		/// <summary>
		/// Rozpozná všetky číslice zo zoznamu a vráti výsledné číslo.
		/// </summary>
		/// <param name="digits">Zoznam Mat objektov s číslicami (zľava doprava)</param>
		/// <returns>Tuple (rozpoznané číslo ako string, priemerná confidence)</returns>
		public (string Text, float AverageConfidence) RecognizeDigits(List<Mat> digits)
		{
			if (digits == null || digits.Count == 0)
			{
				return ("", 0f);
			}

			var results = new List<(int Digit, float Confidence)>();

			foreach (var digit in digits)
			{
				var result = RecognizeDigit(digit);
				results.Add(result);
			}

			// Zostav výsledný text
			string text = "";
			float totalConfidence = 0f;
			int validCount = 0;

			foreach (var (digit, confidence) in results)
			{
				if (digit >= 0)
				{
					text += digit.ToString();
					totalConfidence += confidence;
					validCount++;
				}
				else
				{
					text += "?"; // Nerozpoznaná číslica
				}
			}

			float avgConfidence = validCount > 0 ? totalConfidence / validCount : 0f;

			_logger?.Info($"DigitRecognizer: Recognized '{text}' with average confidence {avgConfidence:P1}");

			return (text, avgConfidence);
		}

		/// <summary>
		/// Pripraví vstupné dáta z Mat objektu pre ONNX model.
		/// Normalizuje hodnoty do rozsahu 0-1.
		/// </summary>
		/// <param name="digit">Mat objekt (28x28 px, grayscale)</param>
		/// <returns>Pole float hodnôt (784 prvkov)</returns>
		private float[] PrepareInput(Mat digit)
		{
			// Uisti sa, že máme správnu veľkosť
			Mat resized;
			if (digit.Width != 28 || digit.Height != 28)
			{
				resized = new Mat();
				Cv2.Resize(digit, resized, new OpenCvSharp.Size(28, 28));
			}
			else
			{
				resized = digit;
			}

			// Uisti sa, že máme grayscale
			Mat gray;
			if (resized.Channels() > 1)
			{
				gray = new Mat();
				Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
			}
			else
			{
				gray = resized;
			}

			// Konvertuj na float array a normalizuj na 0-1
			float[] data = new float[28 * 28];

			for (int y = 0; y < 28; y++)
			{
				for (int x = 0; x < 28; x++)
				{
					byte pixelValue = gray.At<byte>(y, x);
					// Normalizácia: 0-255 -> 0.0-1.0
					data[y * 28 + x] = pixelValue / 255f;
				}
			}

			// Cleanup ak sme vytvorili nové Mat objekty
			if (resized != digit) resized.Dispose();
			if (gray != resized && gray != digit) gray.Dispose();

			return data;
		}

		/// <summary>
		/// Aplikuje softmax funkciu na pole logitov.
		/// Softmax konvertuje logity na pravdepodobnosti (súčet = 1).
		/// </summary>
		/// <param name="logits">Pole logitov (raw output z modelu)</param>
		/// <returns>Pole pravdepodobností</returns>
		private float[] Softmax(float[] logits)
		{
			// Nájdi max hodnotu pre numerickú stabilitu
			float max = logits.Max();

			// Vypočítaj exp(x - max) pre každý element
			float[] exp = logits.Select(x => (float)Math.Exp(x - max)).ToArray();

			// Súčet všetkých exp hodnôt
			float sum = exp.Sum();

			// Vydeľ každý element súčtom
			return exp.Select(x => x / sum).ToArray();
		}

		/// <summary>
		/// Uvoľní zdroje (ONNX session).
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
					_session?.Dispose();
					_session = null;
				}
				_disposed = true;
			}
		}

		~DigitRecognizer()
		{
			Dispose(false);
		}
	}
}
