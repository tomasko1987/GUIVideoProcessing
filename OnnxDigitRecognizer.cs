using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Rozpoznávanie 7-segmentových číslic pomocou ONNX neurónovej siete.
	/// Model očakáva grayscale obraz 64×98 px (shape: [1, 1, 98, 64])
	/// a vracia 10 logitov (shape: [1, 10]) pre číslice 0-9.
	/// </summary>
	public class OnnxDigitRecognizer : IDisposable
	{
		private InferenceSession? _session;
		private string? _inputName;
		private readonly Logger? _logger;

		/// <summary>
		/// Či je model úspešne načítaný a pripravený na inference.
		/// </summary>
		public bool IsLoaded => _session != null;

		public OnnxDigitRecognizer(Logger? logger)
		{
			_logger = logger;
		}

		/// <summary>
		/// Načíta ONNX model zo súboru.
		/// </summary>
		/// <param name="modelPath">Cesta k .onnx súboru</param>
		/// <returns>true ak sa model úspešne načítal</returns>
		public bool LoadModel(string modelPath)
		{
			try
			{
				if (!System.IO.File.Exists(modelPath))
				{
					_logger?.Warn($"ONNX model not found: {modelPath}");
					return false;
				}

				using var options = new SessionOptions();
				options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

				_session = new InferenceSession(modelPath, options);
				_inputName = _session.InputMetadata.Keys.First();

				_logger?.Info($"ONNX model loaded: {modelPath} (input: {_inputName})");
				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error($"Failed to load ONNX model: {ex.Message}");
				_session = null;
				return false;
			}
		}

		/// <summary>
		/// Rozpozná číslicu z Mat obrazu.
		/// </summary>
		/// <param name="digitMat">Obraz číslice (ľubovoľný rozmer, konvertuje sa na 64×98 grayscale)</param>
		/// <returns>Tuple (digit 0-9, confidence 0.0-1.0). Ak zlyhá: (-1, 0)</returns>
		public (int digit, float confidence) RecognizeDigit(Mat digitMat)
		{
			if (_session == null || _inputName == null)
				return (-1, 0f);

			try
			{
				// Konverzia na grayscale ak treba
				using var gray = new Mat();
				if (digitMat.Channels() == 1)
					digitMat.CopyTo(gray);
				else
					Cv2.CvtColor(digitMat, gray, ColorConversionCodes.BGR2GRAY);

				// Resize na 64×98 (šírka × výška) – model shape je [1, 1, 98, 64]
				using var resized = new Mat();
				Cv2.Resize(gray, resized, new OpenCvSharp.Size(64, 98), 0, 0, InterpolationFlags.Area);

				// Vytvorenie input tensora [1, 1, 98, 64]
				int height = 98;
				int width = 64;
				var inputData = new float[1 * 1 * height * width];

				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						byte pixel = resized.At<byte>(y, x);
						inputData[y * width + x] = pixel / 255f;
					}
				}

				var inputTensor = new DenseTensor<float>(inputData, new[] { 1, 1, height, width });
				var inputs = new List<NamedOnnxValue>
				{
					NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
				};

				// Inference
				using var results = _session.Run(inputs);
				var output = results.First();
				var logits = output.AsEnumerable<float>().ToArray();

				// Softmax
				var probabilities = Softmax(logits);

				// Nájdi digit s najvyššou pravdepodobnosťou
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

				return (predictedDigit, maxProb);
			}
			catch (Exception ex)
			{
				_logger?.Warn($"ONNX inference failed: {ex.Message}");
				return (-1, 0f);
			}
		}

		/// <summary>
		/// Softmax funkcia – konvertuje logity na pravdepodobnosti.
		/// </summary>
		private static float[] Softmax(float[] logits)
		{
			float max = logits.Max();
			var exps = logits.Select(l => (float)Math.Exp(l - max)).ToArray();
			float sum = exps.Sum();
			return exps.Select(e => e / sum).ToArray();
		}

		public void Dispose()
		{
			_session?.Dispose();
			_session = null;
		}
	}
}
