using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace GUIVideoProcessing
{
	/// <summary>
	/// ========================= MySQL WRITER (BACKGROUND THREAD) =========================
	///
	/// Úloha:
	/// - prijíma záznamy (rozpoznané čísla + čas) cez thread-safe frontu,
	/// - zapisuje ich do MySQL databázy,
	/// - ak sa nedá pripojiť alebo spojenie spadne, opakovane sa pripája v slučke.
	///
	/// Prečo samostatná trieda:
	/// - WinForms UI thread NESMIE byť blokovaný prácou s databázou,
	/// - pipeline/capture thread tiež nechceme brzdiť sieťovými operáciami,
	/// - preto dávame DB zápisy do samostatného background tasku.
	///
	/// Thread-safety:
	/// - do vnútra triedy sa posiela len immutable dátový objekt NumRegRecord
	/// - zápisy sa bufferujú cez ConcurrentQueue
	/// - signalizácia cez SemaphoreSlim
	/// </summary>
	public sealed class MySqlWriter : IDisposable
	{
		/// <summary>
		/// Jedna položka pre zápis do DB.
		/// Pozn.: je to "record" (immutable) => bezpečné medzi vláknami.
		/// </summary>
		public sealed record NumRegRecord(DateTime TimestampUtc, int? LeftDigit, int? RightDigit, string? Text);

		private readonly Logger? _logger;
		private readonly Settings _settings;

		// Fronta záznamov, ktoré čakajú na zápis do DB.
		private readonly ConcurrentQueue<NumRegRecord> _queue = new();

		// SemaphoreSlim slúži ako "signal" – keď príde nová položka, uvoľníme semafor.
		private readonly SemaphoreSlim _signal = new(0);

		private readonly CancellationTokenSource _cts = new();
		private Task? _worker;

		public MySqlWriter(Settings settings, Logger? logger)
		{
			_settings = settings;
			_logger = logger;
		}

		/// <summary>
		/// Spustí background worker.
		/// Volaj raz pri štarte aplikácie (alebo pri START streamu).
		/// </summary>
		public void Start()
		{
			// Neštartuj dvakrát.
			if (_worker != null)
				return;

			_worker = Task.Run(() => WorkerLoopAsync(_cts.Token));
		}

		/// <summary>
		/// Pridá záznam do fronty na zápis do DB (neblokuje volajúce vlákno).
		/// </summary>
		public void Enqueue(NumRegRecord record)
		{
			// Ak je DB vypnutá, nemá zmysel nič ukladať (ale UI si to môže zobrazovať).
			if (!_settings.MySqlEnabled)
				return;

			_queue.Enqueue(record);
			_signal.Release();
		}

		/// <summary>
		/// Zostaví connection string zo Settings.json.
		/// </summary>
		private string BuildConnectionString()
		{
			var csb = new MySqlConnectionStringBuilder
			{
				Server = _settings.MySqlHost,
				Port = (uint)_settings.MySqlPort,
				Database = _settings.MySqlDatabase,
				UserID = _settings.MySqlUsername,
				Password = _settings.MySqlPassword,
				// Dôležité pre stabilitu:
				// - ConnectionTimeout: aby sa connect nepokúšal "donekonečna"
				ConnectionTimeout = 5,
				// - DefaultCommandTimeout: aby insert nemohol visieť minúty
				DefaultCommandTimeout = 5,
				// Odporúčané pri niektorých MySQL serveroch:
				SslMode = MySqlSslMode.None,
				// MySQL 8.0+ (caching_sha2_password) bez SSL vyžaduje RSA key retrieval:
				AllowPublicKeyRetrieval = true,
			};

			return csb.ConnectionString;
		}

		/// <summary>
		/// Background slučka:
		/// 1) pokus o pripojenie (v slučke)
		/// 2) keď je pripojené, zapisuje položky z fronty
		/// 3) ak spojenie padne, ide späť do reconnect slučky
		/// </summary>
		private async Task WorkerLoopAsync(CancellationToken token)
		{
			// Ak je DB vypnutá, worker môže pokojne skončiť.
			if (!_settings.MySqlEnabled)
			{
				_logger?.Info("MySqlWriter: MySqlEnabled=false, writer not started.");
				return;
			}

			int delaySec = _settings.MySqlReconnectDelaySeconds;
			if (delaySec < 1) delaySec = 1;

			while (!token.IsCancellationRequested)
			{
				// ========================= 1) RECONNECT SLUČKA =========================
				MySqlConnection? conn = null;
				try
				{
					string cs = BuildConnectionString();
					conn = new MySqlConnection(cs);

					_logger?.Info($"MySqlWriter: connecting to {_settings.MySqlHost}:{_settings.MySqlPort}/{_settings.MySqlDatabase} ...");
					await conn.OpenAsync(token);
					_logger?.Info("MySqlWriter: connected.");

					// Voliteľne: zabezpeč, že tabuľka existuje (jednoduchý CREATE TABLE IF NOT EXISTS).
					await EnsureTableAsync(conn, token);

					// ========================= 2) ZÁPISY =========================
					await WriteLoopAsync(conn, token);
				}
				catch (OperationCanceledException)
				{
					// aplikácia končí
					return;
				}
				catch (Exception ex)
				{
					_logger?.Warn($"MySqlWriter: connection/write failed. Will retry in {delaySec}s. Error: {ex.Message}");
				}
				finally
				{
					try { conn?.Close(); } catch { /* ignore */ }
					try { conn?.Dispose(); } catch { /* ignore */ }
				}

				// ========================= 3) DELAY PRED ĎALŠÍM POKUSOM =========================
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(delaySec), token);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}

		/// <summary>
		/// Vytvorí tabuľku, ak neexistuje.
		/// Schéma je jednoduchá: čas + left/right + text.
		///
		/// Pozn.: ak chceš inú schému, uprav CREATE TABLE a INSERT.
		/// </summary>
		private async Task EnsureTableAsync(MySqlConnection conn, CancellationToken token)
		{
			string table = _settings.MySqlTable;
			if (string.IsNullOrWhiteSpace(table))
				table = "numreg_readings";

			// Bezpečné: table názov nemôžeme parametrizeovať, preto aspoň základná validácia.
			// Povolené len: písmená, čísla, podčiarkovník.
			foreach (char c in table)
			{
				if (!(char.IsLetterOrDigit(c) || c == '_'))
					throw new InvalidOperationException("MySqlTable contains invalid characters. Allowed: [A-Za-z0-9_]");
			}

			string sql = $@"
CREATE TABLE IF NOT EXISTS `{table}` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `ts_utc` DATETIME(3) NOT NULL,
  `left_digit` INT NULL,
  `right_digit` INT NULL,
  `text` VARCHAR(16) NULL,
  PRIMARY KEY (`id`),
  INDEX (`ts_utc`)
) ENGINE=InnoDB;";

			using var cmd = new MySqlCommand(sql, conn);
			await cmd.ExecuteNonQueryAsync(token);
		}

		/// <summary>
		/// Zapisuje položky z fronty, kým je spojenie OK.
		/// Ak insert zlyhá (napr. spojenie padne), vyhodí výnimku a outer loop spraví reconnect.
		/// </summary>
		private async Task WriteLoopAsync(MySqlConnection conn, CancellationToken token)
		{
			string table = string.IsNullOrWhiteSpace(_settings.MySqlTable) ? "numreg_readings" : _settings.MySqlTable;

			while (!token.IsCancellationRequested)
			{
				// Čakaj, kým príde signál, že niečo je vo fronte.
				await _signal.WaitAsync(token);

				// Vypíš všetko, čo je aktuálne vo fronte (batch), aby sme minimalizovali DB roundtrips.
				while (_queue.TryDequeue(out var record))
				{
					string sql = $"INSERT INTO `{table}` (ts_utc, left_digit, right_digit, text) VALUES (@ts, @l, @r, @t);";

					using var cmd = new MySqlCommand(sql, conn);
					cmd.Parameters.AddWithValue("@ts", record.TimestampUtc);
					cmd.Parameters.AddWithValue("@l", (object?)record.LeftDigit ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@r", (object?)record.RightDigit ?? DBNull.Value);
					cmd.Parameters.AddWithValue("@t", (object?)record.Text ?? DBNull.Value);

					await cmd.ExecuteNonQueryAsync(token);
				}
			}
		}

		public void Dispose()
		{
			try { _cts.Cancel(); } catch { /* ignore */ }
			try { _worker?.Wait(1000); } catch { /* ignore */ }
			try { _cts.Dispose(); } catch { /* ignore */ }
			try { _signal.Dispose(); } catch { /* ignore */ }
		}
	}
}
