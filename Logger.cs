using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Log levely – umožnia filtrovať, čo zapisujeme do súboru.
	/// (Napr. v produkcii môžeš vypnúť DEBUG.)
	/// </summary>
	public enum LogLevel
	{
		Debug = 0,
		Info = 1,
		Warn = 2,
		Error = 3
	}

	/// <summary>
	/// Jednoduchý, robustný a thread-safe logger s asynchrónnym zapisovaním:
	/// - všetky volania Log(...) len vložia správu do fronty (neblokujú)
	/// - background task zapisuje do súboru
	/// - log súbory sa rotujú:
	///   1) denne: log_YYYY-MM-DD.txt
	///   2) ak súbor presiahne max veľkosť, pridá sa suffix _001, _002...
	/// </summary>
	public sealed class Logger : IDisposable
	{
		// ========================= KONFIGURÁCIA =========================

		// Priečinok, kam budeme ukladať log súbory (napr. vedľa exe).
		private readonly string _logDirectory;

		// Prefix názvu logu (napr. "log" => log_2026-01-02.txt)
		private readonly string _filePrefix;

		// Maximálna veľkosť jedného log súboru v bajtoch (napr. 5 MB).
		private readonly long _maxBytesPerFile;

		// Minimálny level, ktorý sa má zapisovať (nižšie levely sa ignorujú).
		private readonly LogLevel _minLevel;

		// ========================= INTERNÉ DÁTA =========================

		// Fronta log správ – thread-safe, môže do nej zapisovať UI thread aj capture thread.
		private readonly BlockingCollection<string> _queue;

		// CTS na zastavenie background zapisovača.
		private readonly CancellationTokenSource _cts;

		// Background task, ktorý zapisuje správy z fronty do súboru.
		private readonly Task _writerTask;

		// Aktuálny dátum log súboru (kvôli dennej rotácii).
		private DateTime _currentDate;

		// Aktuálna plná cesta k otvorenému log súboru.
		// OPRAVA: Inicializované v OpenWriterForDate() volanej z konštruktora
		private string _currentFilePath = null!;

		// StreamWriter držíme otvorený (vyšší výkon než open/close pri každom riadku).
		// OPRAVA: Inicializované v OpenWriterForDate() volanej z konštruktora
		private StreamWriter _writer = null!;

		// Zámok pre rotáciu súboru (keď sa mení deň alebo prekročí veľkosť).
		private readonly object _rotateLock = new object();

		/// <summary>
		/// Vytvorí logger.
		/// </summary>
		/// <param name="logDirectory">Priečinok pre logy</param>
		/// <param name="filePrefix">Prefix názvu log súborov</param>
		/// <param name="minLevel">Minimálny level</param>
		/// <param name="maxBytesPerFile">Rotácia podľa veľkosti (bajty)</param>
		public Logger(
			string logDirectory,
			string filePrefix = "log",
			LogLevel minLevel = LogLevel.Debug,
			long maxBytesPerFile = 5 * 1024 * 1024)
		{
			// Uložíme konfiguráciu.
			_logDirectory = logDirectory;
			_filePrefix = filePrefix;
			_minLevel = minLevel;
			_maxBytesPerFile = maxBytesPerFile;

			// Vytvoríme priečinok, ak neexistuje.
			Directory.CreateDirectory(_logDirectory);

			// Inicializácia interných štruktúr.
			_queue = new BlockingCollection<string>(boundedCapacity: 10_000); // ochrana pred nekonečným rastom
			_cts = new CancellationTokenSource();

			// Nastavíme aktuálny dátum a otvoríme prvý súbor.
			_currentDate = DateTime.Today;
			OpenWriterForDate(_currentDate);

			// Spustíme background task, ktorý bude zapisovať.
			_writerTask = Task.Run(() => WriterLoop(_cts.Token));
		}

		/// <summary>
		/// Hlavná verejná metóda.
		/// Nezapisuje priamo do súboru – len vloží riadok do fronty (rýchle, neblokujúce).
		/// </summary>
		public void Log(LogLevel level, string message)
		{
			// Ak je level pod minimálnym, ignorujeme ho.
			if (level < _minLevel)
				return;

			// Timestamp vo formáte, ktorý sa dobre číta a aj triedi.
			string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

			// Výsledný riadok – jednotný formát pre všetky logy.
			string line = $"{ts} | {level.ToString().ToUpper()} | {message}";

			// Skúsime vložiť do fronty.
			// Ak by bola fronta plná, nechceme zhodiť app – radšej dropneme log.
			// (Pri boundedCapacity je to bezpečná poistka proti memory leak, ak by si logoval extrémne veľa.)
			if (!_queue.TryAdd(line))
			{
				MessageBox.Show("V núdzi to potichu zahodíme");
				// V núdzi to potichu zahodíme.
				// Alternatíva: počkať s timeoutom, alebo počítať dropy.
			}
		}

		// Pohodlné skratky – nech to nemusíš písať stále s LogLevel.
		public void Debug(string msg) => Log(LogLevel.Debug, msg);
		public void Info(string msg) => Log(LogLevel.Info, msg);
		public void Warn(string msg) => Log(LogLevel.Warn, msg);

		/// <summary>
		/// Logovanie výnimky (automaticky pripojí stacktrace).
		/// </summary>
		public void Error(string msg, Exception? ex = null)
		{
			if (ex == null)
			{
				Log(LogLevel.Error, msg);
				return;
			}

			// ex.ToString() obsahuje message + stacktrace (a inner exceptions)
			Log(LogLevel.Error, msg + " | EX: " + ex);
		}

		/// <summary>
		/// Background slučka – číta z fronty a zapisuje do súboru.
		/// </summary>
		private void WriterLoop(CancellationToken token)
		{
			try
			{
				// Kým nie je requested cancel, berieme položky z fronty.
				foreach (var line in _queue.GetConsumingEnumerable(token))
				{
					// Pred zápisom skontrolujeme rotáciu (deň/veľkosť).
					RotateIfNeeded();

					// Zapíšeme riadok.
					_writer.WriteLine(line);

					// Flush – aby log nezostal v buffri príliš dlho.
					// (Ak chceš max výkon, môžeš flushovať napr. každých N riadkov.)
					_writer.Flush();
				}
			}
			catch (OperationCanceledException)
			{
				// Normálne ukončenie – nič netreba robiť.
			}
			catch
			{
				// Logger nikdy nesmie zhodiť app.
				// Ak tu nastane chyba, necháme to ticho (alebo by si mohol fallbacknúť do iného súboru).
			}
			finally
			{
				// Pri ukončení zavrieme writer.
				try { _writer?.Dispose(); } catch { }
			}
		}

		/// <summary>
		/// Otvorí nový log súbor pre konkrétny dátum.
		/// </summary>
		private void OpenWriterForDate(DateTime date)
		{
			// Vytvoríme základný názov: log_YYYY-MM-DD.txt
			string baseName = $"{_filePrefix}_{date:yyyy-MM-dd}.txt";

			// Ak už existuje a je príliš veľký, pridáme suffixy _001, _002...
			string path = Path.Combine(_logDirectory, baseName);
			path = ApplySizeRotationSuffixIfNeeded(path);

			// Uložíme aktuálnu cestu.
			_currentFilePath = path;

			// Otvoríme StreamWriter v append režime (neprepisujeme existujúci log).
			_writer = new StreamWriter(
				new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
				Encoding.UTF8);
		}

		/// <summary>
		/// Ak súbor už presiahol max size, vráti cestu s suffixom (_001, _002...).
		/// </summary>
		private string ApplySizeRotationSuffixIfNeeded(string basePath)
		{
			// Ak súbor neexistuje, môžeme použiť basePath.
			if (!File.Exists(basePath))
				return basePath;

			// Ak existuje a je menší než limit, tiež môžeme použiť basePath.
			var info = new FileInfo(basePath);
			if (info.Length < _maxBytesPerFile)
				return basePath;

			// Inak hľadáme prvý voľný suffix.
			// log_2026-01-02_001.txt, log_2026-01-02_002.txt, ...
			string? dir = Path.GetDirectoryName(basePath);
			string name = Path.GetFileNameWithoutExtension(basePath);
			string ext = Path.GetExtension(basePath);

			// OPRAVA: dir môže byť null, použijeme fallback na aktuálny priečinok
			if (string.IsNullOrEmpty(dir))
				dir = ".";

			for (int i = 1; i <= 999; i++)
			{
				string candidate = Path.Combine(dir, $"{name}_{i:000}{ext}");

				if (!File.Exists(candidate))
					return candidate;

				// Ak existuje, skontroluj veľkosť – ak je ešte pod limitom, môžeme pokračovať v tom istom.
				var cInfo = new FileInfo(candidate);
				if (cInfo.Length < _maxBytesPerFile)
					return candidate;
			}

			// Ak by sme sa sem dostali, máme extrémny stav – fallback na basePath (append).
			return basePath;
		}

		/// <summary>
		/// Skontroluje, či sa má rotovať (deň sa zmenil alebo súbor presiahol limit).
		/// </summary>
		private void RotateIfNeeded()
		{
			// Rotáciu chránime lockom, aby sa náhodou nerobila súčasne.
			lock (_rotateLock)
			{
				// 1) Denná rotácia
				DateTime today = DateTime.Today;
				if (today != _currentDate)
				{
					_currentDate = today;

					// Zavrieme starý writer.
					_writer?.Dispose();

					// Otvoríme nový pre nový deň.
					OpenWriterForDate(_currentDate);
					return;
				}

				// 2) Rotácia podľa veľkosti
				try
				{
					var info = new FileInfo(_currentFilePath);
					if (info.Exists && info.Length >= _maxBytesPerFile)
					{
						_writer?.Dispose();
						OpenWriterForDate(_currentDate);
					}
				}
				catch
				{
					// Logger nesmie spadnúť – ignorujeme chyby FileInfo.
				}
			}
		}

		/// <summary>
		/// Korektné ukončenie loggera – dopíše čo je vo fronte a zavrie súbor.
		/// </summary>
		public void Dispose()
		{
			// Zastavíme prijímanie nových položiek.
			try { _queue.CompleteAdding(); } catch { }

			// Pošleme cancel writeru.
			try { _cts.Cancel(); } catch { }

			// Počkáme chvíľu na ukončenie tasku (nech dopíše frontu).
			try { _writerTask.Wait(500); } catch { }

			// Upratanie CTS a fronty.
			try { _cts.Dispose(); } catch { }
			try { _queue.Dispose(); } catch { }

			// Zavrieme writer (pre istotu).
			try { _writer?.Dispose(); } catch { }
		}
	}
}