using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

// OPRAVA: Explicitný alias pre OpenCvSharp.Point, aby sa zabránilo konfliktu s System.Drawing.Point
using Point = OpenCvSharp.Point;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Partial class MainForm - obsahuje metódy pre segmentáciu číslic z cleaned image.
	/// KAPITOLA 5: Digit Segmentation and Recognition
	/// </summary>
	public class DigitSegmentation
	{
		// DOPLNENÉ: logger uložený v tejto triede
		private readonly Logger? _logger;

		public DigitSegmentation(Logger? logger)
		{
			_logger = logger;
		}

		// ========================= DIGIT SEGMENTATION (KAPITOLA 5) =========================

		/// <summary>
		/// MAIN METÓDA: Extrahuje číslice z cleaned image (white digits on black background).
		/// Pipeline: FindContours → Filter → Merge → Sort → Crop → Resize
		/// </summary>
		/// <param name="cleaned">Cleaned image z Pipeline() (white digits on black background)</param>
		/// <returns>List číslic (28x28 px, white on black) zoradených zľava doprava</returns>
		public List<Mat> ExtractDigits(Mat cleaned)
		{
			// Validácia vstupu
			if (cleaned == null || cleaned.Empty())
			{
				_logger?.Warn("ExtractDigits: cleaned image is null or empty");
				return new List<Mat>();
			}

			// Krok 1.1: Nájdi kontúry (white blobs)
			Point[][] contours = FindWhiteBlobs(cleaned);
			_logger?.Info($"Found {contours.Length} contours");

			// Krok 1.2: Získaj bounding rectangles pre každú kontúru
			List<Rect> rectangles = GetBoundingRectangles(contours);

			// Krok 1.3: Filtruj kontúry (remove noise)
			List<Rect> filtered = FilterContours(contours, rectangles, minArea: 200);
			_logger?.Info($"Filtered to {filtered.Count} rectangles");

			// Krok 1.4: Zlúč overlapping rectangles (group segments do číslic)
			List<Rect> merged = MergeOverlappingRects(filtered, overlapThreshold: 0.5);
			_logger?.Info($"Merged to {merged.Count} digit candidates");

			// Krok 1.5: Zoraď zľava doprava
			List<Rect> sorted = SortLeftToRight(merged);

			// Krok 1.6: Extrahuj a resize na 28x28
			List<Mat> digits = ExtractAndResizeDigits(cleaned, sorted, targetSize: 28);
			_logger?.Info($"Extracted {digits.Count} digits (28x28 px)");

			return digits; // Očakávaný výstup: 2 číslice pre "69"
		}

		/// <summary>
		/// Nájde všetky kontúry (obrysy) bielych objektov na čiernom pozadí.
		/// Používa OpenCV FindContours s External mode (len vonkajšie kontúry).
		/// </summary>
		/// <param name="cleaned">Binárny obraz (white digits on black background)</param>
		/// <returns>Pole kontúr - každá kontúra je pole bodov</returns>
		private Point[][] FindWhiteBlobs(Mat cleaned)
		{
			// Cv2.FindContours() - OpenCV funkcia na detekciu kontúr
			Point[][] contours;
			HierarchyIndex[] hierarchy;

			Cv2.FindContours(
				cleaned,                              // Input: binárny obraz
				out contours,                         // Output: pole kontúr
				out hierarchy,                        // Output: hierarchia (parent/child)
				RetrievalModes.External,              // Režim: len vonkajšie kontúry (ignore holes)
				ContourApproximationModes.ApproxSimple // Aproximácia: zjednodušené body
			);

			return contours;
		}

		/// <summary>
		/// Pre každú kontúru vypočíta bounding rectangle (najmenší obdĺžnik, ktorý kontúru obsahuje).
		/// </summary>
		/// <param name="contours">Pole kontúr z FindContours</param>
		/// <returns>List bounding rectangles</returns>
		private List<Rect> GetBoundingRectangles(Point[][] contours)
		{
			List<Rect> rectangles = new List<Rect>();

			foreach (var contour in contours)
			{
				// Cv2.BoundingRect() - vypočíta najmenší axis-aligned rectangle okolo kontúry
				Rect rect = Cv2.BoundingRect(contour);
				rectangles.Add(rect);
			}

			return rectangles;
		}

		/// <summary>
		/// Filtruje kontúry podľa area a aspect ratio - odstráni šum a ponechá len číslice.
		/// Aspect ratio: širka/vyska. Pre číslice očakávame 0.3-0.8 (vyššie než široké).
		/// </summary>
		/// <param name="contours">Pole kontúr</param>
		/// <param name="rectangles">Bounding rectangles</param>
		/// <param name="minArea">Minimálna plocha (default: 200 px²)</param>
		/// <param name="minAspectRatio">Minimálny pomer šírka/výška (default: 0.3)</param>
		/// <param name="maxAspectRatio">Maximálny pomer šírka/výška (default: 0.8)</param>
		/// <returns>Filtrované bounding rectangles</returns>
		private List<Rect> FilterContours(
			Point[][] contours,
			List<Rect> rectangles,
			double minArea = 200,
			double minAspectRatio = 0.3,
			double maxAspectRatio = 0.8)
		{
			List<Rect> filtered = new List<Rect>();

			for (int i = 0; i < contours.Length; i++)
			{
				var contour = contours[i];
				var rect = rectangles[i];

				// 1. Vypočítaj plochu kontúry (v pixeloch²)
				double area = Cv2.ContourArea(contour);

				// 2. Vypočítaj aspect ratio (pomer šírka/výška)
				double aspectRatio = (double)rect.Width / rect.Height;

				// 3. Filter podmienky:
				// - Plocha >= minArea (odstráni malý šum)
				// - Aspect ratio v rozumnom rozsahu pre číslice (0.3-0.8)
				//   Príklad: číslica "1" má aspect ratio ~0.3, číslica "0" má ~0.6
				if (area >= minArea &&
					aspectRatio >= minAspectRatio &&
					aspectRatio <= maxAspectRatio)
				{
					filtered.Add(rect);
				}
			}

			return filtered;
		}

		/// <summary>
		/// Zlúči prekrývajúce sa alebo blízke rectangles do jedného (grouping segmentov číslice).
		/// LCD číslica môže byť rozdelená na 3-4 segmenty - tieto zlúčime do jedného rectangle.
		/// </summary>
		/// <param name="rectangles">Filtrované bounding rectangles</param>
		/// <param name="overlapThreshold">Threshold pre X-overlap (default: 0.5 = 50% overlap)</param>
		/// <returns>Zlúčené rectangles - každý reprezentuje jednu číslicu</returns>
		private List<Rect> MergeOverlappingRects(List<Rect> rectangles, double overlapThreshold = 0.5)
		{
			if (rectangles.Count == 0) return rectangles;

			// 1. Sort rectangles zľava doprava (podľa X pozície)
			rectangles = rectangles.OrderBy(r => r.X).ToList();

			// 2. Grouping algorithm: iteratívne merging
			List<Rect> merged = new List<Rect>();
			Rect current = rectangles[0]; // Začni s prvým rectangle

			for (int i = 1; i < rectangles.Count; i++)
			{
				Rect next = rectangles[i];

				// 3. Check, či 'next' je blízko/overlaps s 'current'
				if (ShouldMerge(current, next, overlapThreshold))
				{
					// Merge: vytvor union rectangle (najmenší rectangle, ktorý obsahuje oba)
					current = UnionRect(current, next);
				}
				else
				{
					// Next je ďaleko → current je hotový, pridaj do výsledku
					merged.Add(current);
					current = next; // Začni nový group
				}
			}

			// 4. Pridaj posledný rectangle
			merged.Add(current);

			return merged;
		}

		/// <summary>
		/// Rozhodne, či by sa 2 rectangles mali zlúčiť (sú blízko alebo sa prekrývajú).
		/// Používa 2 stratégie: X-axis overlap a horizontal proximity.
		/// </summary>
		/// <param name="r1">Prvý rectangle</param>
		/// <param name="r2">Druhý rectangle (vpravo od r1)</param>
		/// <param name="overlapThreshold">Threshold pre X-overlap (0.0-1.0)</param>
		/// <returns>True ak by sa mali zlúčiť, inak False</returns>
		private bool ShouldMerge(Rect r1, Rect r2, double overlapThreshold)
		{
			// Stratégia 1: X-axis overlap
			// - Ak sa rectangles prekrývajú v X osi > threshold → merge
			int overlapX = Math.Max(0, Math.Min(r1.Right, r2.Right) - Math.Max(r1.Left, r2.Left));
			double overlapRatio = (double)overlapX / Math.Min(r1.Width, r2.Width);

			if (overlapRatio > overlapThreshold)
				return true;

			// Stratégia 2: Horizontal proximity
			// - Ak je horizontálna vzdialenosť medzi r1 a r2 < threshold → merge
			int gap = r2.Left - r1.Right; // Medzera medzi rectangles
			int avgWidth = (r1.Width + r2.Width) / 2;

			if (gap >= 0 && gap < avgWidth * 0.3) // Gap < 30% priemernej šírky
				return true;

			return false;
		}

		/// <summary>
		/// Vytvorí union rectangle (najmenší rectangle obsahujúci r1 aj r2).
		/// </summary>
		/// <param name="r1">Prvý rectangle</param>
		/// <param name="r2">Druhý rectangle</param>
		/// <returns>Union rectangle</returns>
		private Rect UnionRect(Rect r1, Rect r2)
		{
			int x = Math.Min(r1.X, r2.X);
			int y = Math.Min(r1.Y, r2.Y);
			int right = Math.Max(r1.Right, r2.Right);
			int bottom = Math.Max(r1.Bottom, r2.Bottom);

			return new Rect(x, y, right - x, bottom - y);
		}

		/// <summary>
		/// Zoradí rectangles zľava doprava (podľa X pozície).
		/// Číslice na LCD display sú v poradí zľava doprava.
		/// </summary>
		/// <param name="rectangles">Bounding rectangles</param>
		/// <returns>Zoradené rectangles (zľava doprava)</returns>
		private List<Rect> SortLeftToRight(List<Rect> rectangles)
		{
			return rectangles.OrderBy(r => r.X).ToList();
		}

		/// <summary>
		/// Extrahuje a resize-uje každú číslicu na fixnú veľkosť (28x28 px).
		/// Pridá padding, aby číslica mala štvorcový tvar (zachová aspect ratio).
		/// </summary>
		/// <param name="cleaned">Cleaned image (white digits on black background)</param>
		/// <param name="digitRects">Bounding rectangles číslic</param>
		/// <param name="targetSize">Cieľová veľkosť (default: 28x28 ako MNIST)</param>
		/// <returns>List Mat objektov - každý obsahuje jednu číslicu (28x28)</returns>
		private List<Mat> ExtractAndResizeDigits(Mat cleaned, List<Rect> digitRects, int targetSize = 28)
		{
			List<Mat> digits = new List<Mat>();

			foreach (var rect in digitRects)
			{
				// Validácia: rectangle nesmie byť mimo obraz
				if (rect.X < 0 || rect.Y < 0 ||
					rect.Right > cleaned.Width || rect.Bottom > cleaned.Height)
				{
					_logger?.Warn($"Digit rect out of bounds: {rect}");
					continue;
				}

				// 1. CROP: Orezaj číslicu z cleaned image
				Mat digit = new Mat(cleaned, rect); // Mat(source, ROI)

				// 2. PADDING: Pridaj padding, aby číslica mala štvorcový tvar
				// (Dôvod: Resize na 28x28 by deformoval aspect ratio)
				Mat padded = AddSquarePadding(digit);

				// 3. RESIZE: Zmenši/zväčši na 28x28 px
				Mat resized = new Mat();
				Cv2.Resize(padded, resized, new OpenCvSharp.Size(targetSize, targetSize),
						   0, 0, InterpolationFlags.Linear);

				digits.Add(resized);
			}

			return digits;
		}

		/// <summary>
		/// Pridá čierny padding okolo číslice, aby mala štvorcový tvar (zachová aspect ratio).
		/// Číslicu centruje do štvorca.
		/// </summary>
		/// <param name="digit">Číslica (white on black)</param>
		/// <returns>Padded číslica (štvorec, white digit centered on black background)</returns>
		private Mat AddSquarePadding(Mat digit)
		{
			int width = digit.Width;
			int height = digit.Height;
			int size = Math.Max(width, height); // Veľkosť štvorca

			// Vytvor čierny štvorec (size x size)
			Mat padded = Mat.Zeros(size, size, MatType.CV_8UC1);

			// Vypočítaj pozíciu pre centrovanie číslice
			int offsetX = (size - width) / 2;
			int offsetY = (size - height) / 2;

			// Skopíruj číslicu do centra
			Rect roi = new Rect(offsetX, offsetY, width, height);
			digit.CopyTo(new Mat(padded, roi));

			return padded;
		}

		// ========================= DEBUG VISUALIZATION =========================

		/// <summary>
		/// Vykreslí bounding rectangles na obraz (debug visualizácia).
		/// </summary>
		/// <param name="image">Vstupný obraz</param>
		/// <param name="rectangles">Bounding rectangles</param>
		/// <param name="color">Farba rectangles</param>
		/// <param name="thickness">Hrúbka čiary</param>
		/// <returns>Obraz s vykreslenými rectangles</returns>
		private Mat DrawRectangles(Mat image, List<Rect> rectangles, Scalar color, int thickness = 2)
		{
			Mat output = image.Clone();

			foreach (var rect in rectangles)
			{
				Cv2.Rectangle(output, rect, color, thickness);
			}

			return output;
		}
	}
}
