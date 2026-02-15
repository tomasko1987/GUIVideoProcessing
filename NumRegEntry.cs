using System;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Jedna položka histórie rozpoznania (čas + číslice).
	/// </summary>
	public sealed class NumRegEntry
	{
		/// <summary>
		/// Čas vyhodnotenia v UTC.
		/// </summary>
		public DateTime TimestampUtc { get; init; }

		/// <summary>
		/// Ľavá číslica (0..9) alebo null, ak dekódovanie zlyhalo.
		/// </summary>
		public int? LeftDigit { get; init; }

		/// <summary>
		/// Pravá číslica (0..9) alebo null, ak dekódovanie zlyhalo.
		/// </summary>
		public int? RightDigit { get; init; }

		/// <summary>
		/// Výsledný text pre UI/logiku (napr. "70", "?3", "??").
		/// </summary>
		public string Text { get; init; } = string.Empty;
	}
}
