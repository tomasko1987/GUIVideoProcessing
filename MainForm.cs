using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.IO;     // práca so súbormi (File, StreamWriter)
using System.Text;   // encoding (UTF-8)
using System.Net.Http; // HTTP klient pre LED ovládanie ESP32-CAM
// OPRAVA (KAPITOLA 5): Explicitný alias pre OpenCvSharp typy, aby sa zabránilo konfliktu s System.Drawing
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace GUIVideoProcessing
{
	public partial class MainForm : Form
	{
		// Settings objekt – obsahuje list IP adries načítaný zo Settings.json.
		// Používa sa na naplnenie ComboBoxu (cboUrl) pri štarte aplikácie.
		private Settings _settings;

		// Cesta k súboru Settings.json – nachádza sa v rovnakom priečinku ako .exe súbor.
		// AppDomain.CurrentDomain.BaseDirectory = priečinok, kde je spustený .exe
		private readonly string _settingsPath =
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.json");

		// Zámok (mutex) pre _cap – zabezpečí, že sa _cap nebude Dispose()ovať v rovnakom čase,
		// keď iné vlákno práve volá _cap.Read(frame). Toto priamo zabraňuje AccessViolationException.
		private readonly object _capLock = new object();

		// ROI lock
		private readonly object _roiLock = new object();

		// Logger inštancia – zapisuje asynchrónne do súboru a rotuje logy.
		// OPRAVA: Nullable, pretože sa môže Dispose() a nastaviť na null
		private Logger? _logger;

		// Priečinok pre logy – vedľa exe v podpriečinku "logs".
		private readonly string _logsDir =
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

		// Priečinok pre debug snapshoty – vedľa exe v podpriečinku "snapshots".
		private readonly string _snapshotsDir =
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snapshots");

		// ========================= EXPORT TRÉNOVACÍCH PRÍKLADOV (TensorFlow) =========================
		// Základný priečinok pre export 64×96 PNG obrázkov rozpoznaných číslic.
		// Štruktúra: logs/examples/{0,1,2,...,9,N}/ – každý podpriečinok obsahuje PNG obrázky
		// danej číslice. "N" = nerozpoznaná číslica (fallback).
		private readonly string _examplesDir =
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "examples");

		// Zámok pre logovanie – zabezpečí, že viac vlákien nebude zapisovať naraz
		private readonly object _logLock = new object();

		// Objekt, ktorý číta stream z kamery (ESP32-CAM)
		private VideoCapture? _cap;

		// CancellationTokenSource slúži na zastavenie bežiaceho vlákna so streamom
		private CancellationTokenSource? _cts;

		// Flag, či stream práve beží
		private bool _running;

		// Posledný vypočítaný ROI (výrez z frame) – uložený kvôli tlačidlu ROI
		//private Mat? _lastRoi;
		// ROI výrezy z originálneho frame (bez resize)
		private Mat? _lastRoi;

		// Posledný "up" (resize-ovaná verzia ROI) – uložený kvôli tlačidlu VIEW
		//private Mat? _lastUp;
		// Upscaled ROI (to, čo sa posiela do Pipeline)
		private Mat? _lastUp;

		// Posledný "contrast" po CLAHE – užitočné na debug (prečo threshold dáva šum).
		private Mat? _lastContrast;

		// Posledný "binary" po adaptive threshold + morphology – užitočné na debug.
		private Mat? _lastBinary;

		// Posledný finálny "cleaned" – výsledok po ConnectedComponents filtrovaní.
		//private Mat? _lastCleaned;
		// Voliteľne: drž si aj výsledky separátne (ak ich chceš neskôr zobrazovať pre každú ROI zvlášť)
		private Mat? _lastCleaned;

		// Posledná "matrix" – resized verzia cleaned na NxN (16x16, 32x32, 64x64, 128x128).
		// Používa sa na prípravu vstupu pre neurónové siete alebo ďalšie spracovanie.
		//private Mat? _lastMatrix;
		private Mat? _lastMatrix;

		// Posledná dekódovaná hodnota z 7-seg displeja (napr. 70).
		// Nullable int:
		/// - null = dekódovanie zlyhalo (neboli nájdené číslice / segmenty sú nečitateľné)
		/// - číslo = úspešne dekódovaný výsledok
		private int? _lastSevenSegValue = null;

		// Posledný dekódovaný text (napr. "70") – hodí sa pre debug,
		// ak chceš neskôr zobraziť priamo string (napr. aj s '?' pri chybe).
		private string? _lastSevenSegText = null;

		private DigitSegmentation? _digitSeg;

		// ONNX Digit Recognizer – rozpoznávanie číslic pomocou natrénovanej neurónovej siete.
		private OnnxDigitRecognizer? _onnxRecognizer;

		// ========================= NOVÉ FIELDY PRE ROZDELENIE NA DVE ČÍSLICE =========================
		// Thread-safe: Tieto Mat objekty sú zdieľané medzi capture thread (Pipeline) a UI thread (PictureBox).
		// Prístup k nim je chránený _roiLock zámkom, rovnako ako _lastCleaned, _lastBinary, atď.

		// _lastLeft – ľavá časť cleaned obrazu (prvá číslica).
		// Vytvorený v Pipeline() pomocou fromLeft parametra (percento z ľavej strany).
		// Zobrazuje sa v picLeft PictureBoxe.
		private Mat? _lastLeft;

		// _lastRight – pravá časť cleaned obrazu (druhá číslica).
		// Vytvorený v Pipeline() pomocou fromRight parametra (percento z pravej strany).
		// Zobrazuje sa v picRight PictureBoxe.
		private Mat? _lastRight;

		// _lastLeftValue – dekódovaná hodnota ľavej číslice (0-9).
		// null = dekódovanie zlyhalo.
		private int? _lastLeftValue = null;

		// _lastRightValue – dekódovaná hodnota pravej číslice (0-9).
		// null = dekódovanie zlyhalo.
		private int? _lastRightValue = null;


		// ========================= NUMREG PERIODICKÉ VYHODNOCOVANIE + HISTÓRIA + DB =========================
		// Požiadavka: výsledné čísla nebudeme zapisovať do logu na každom frame,
		// ale budeme ich vyhodnocovať len raz za N sekúnd (default: 10s),
		// ukladať do UI histórie (v groupboxe "NumReg") a zapisovať do MySQL databázy.
		//
		// Thread-safety:
		// - Pipeline/capture thread nesmie priamo manipulovať UI prvkami => používame BeginInvoke().
		// - Hodnotenie každých N sekúnd je riadené cez _numEvalLock + _nextNumEvalUtc.
		// - DB zápis je v samostatnom background worker (MySqlWriter) => pipeline nikdy neblokuje sieťou.
		private readonly object _numEvalLock = new();
		private DateTime _nextNumEvalUtc = DateTime.UtcNow;
		private int _numEvalIntervalSeconds = 10;

		// Writer, ktorý zapisuje rozpoznané hodnoty do MySQL na pozadí.
		private MySqlWriter? _mySqlWriter;

		// ========================= LED OVLÁDANIE (ESP32-CAM) =========================
		// HTTP klient pre posielanie príkazov na ESP32-CAM (LED ovládanie).
		// Singleton – jeden HttpClient pre celú aplikáciu (best practice pre .NET).
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(3) // krátky timeout – kamera je na LAN
		};
		// Konštruktor hlavného okna
		public MainForm()
		{
			// Inicializuje grafické komponenty vytvorené v Designer-i
			InitializeComponent();

			// ========================= NAČÍTANIE SETTINGS.JSON =========================
			// Načíta Settings zo súboru Settings.json.
			// Ak súbor neexistuje, Settings.Load() ho vytvorí s prázdnym listom IPAddress.
			_settings = Settings.Load(_settingsPath);

			// Naplní ComboBox (cboUrl) IP adresami zo Settings.
			// Používame AddRange() pre pridanie celého listu naraz (efektívnejšie než cyklus).
			cboUrl.Items.AddRange(_settings.IPAddress.ToArray());

			// Ak ComboBox obsahuje aspoň jednu položku, vyber prvú ako predvolenú.
			// Toto zabezpečí, že používateľ nemusí manuálne vyberať, ak je len jedna adresa.
			if (cboUrl.Items.Count > 0)
			{
				cboUrl.SelectedIndex = 0; // <- vyber prvú položku (index 0)
			}

			// Pripojí event handler pre zmenu výberu v ComboBoxe.
			// Volá sa vždy, keď používateľ vyberie inú IP adresu.
			// Event handler validuje, či je niečo vybrané, a podľa toho povoľuje/zakazuje tlačidlo START.
			cboUrl.SelectedIndexChanged += CboUrl_SelectedIndexChanged;

			// ========================= NASTAVENIE UI KONTROLIEK Z SETTINGS (KAPITOLA 4) =========================

			// Nastaví ROI parametre z Settings.json (alebo default hodnoty, ak Settings práve vytvorený)
			// ROI X súradnica v percentách (0-100)
			nudX.Value = _settings.ROIX;
			// ROI Y súradnica v percentách (0-100)
			nudY.Value = _settings.ROIY;
			// ROI šírka v percentách (0-100)
			nudWidth.Value = _settings.ROIWidth;
			// ROI výška v percentách (0-100)
			nudHeight.Value = _settings.ROIHeight;

			// Nastaví Resize Scale (faktor zväčšenia ROI) z Settings.json
			// Min: 1, Max: 5, Increment: 0.1 (desatinné hodnoty povolené)
			nudResize.Minimum = 1;
			nudResize.Maximum = 5;
			nudResize.Increment = 0.1M; // 0.1 krok pre jemnejšie nastavenie (bolo 2)
			nudResize.DecimalPlaces = 1; // Zobrazí desatinné miesto (napr. 2.0)
			nudResize.Value = (decimal)_settings.ResizeScale; // Načíta z Settings (default: 2.0)

			// ========================= ADAPTIVE THRESHOLD PARAMETRE Z SETTINGS =========================

			// blockSize musí byť nepárne (napr. 15 optimalizované pre číslice)
			// Načíta sa z Settings.json (default: 15, bolo hardcoded 41)
			nudBlockSize.Minimum = 3;
			nudBlockSize.Maximum = 101;
			nudBlockSize.Value = _settings.AdaptiveThresholdBlockSize; // Načíta z Settings (default: 15)

			// C je konštanta odčítaná od adaptívneho prahu (float/double -> decimal v UI)
			// Načíta sa z Settings.json (default: 5.0)
			nudC.DecimalPlaces = 1;
			nudC.Increment = 0.5M;
			nudC.Minimum = -50;
			nudC.Maximum = 50;
			nudC.Value = (decimal)_settings.AdaptiveThresholdC; // Načíta z Settings (default: 5.0)

			// ========================= CONNECTED COMPONENTS PARAMETRE Z SETTINGS =========================

			// minArea – minimálna plocha objektu (filter malých komponentov/bodiek)
			// Načíta sa z Settings.json (default: 200, bolo hardcoded 350)
			nudMinArea.Minimum = 0;
			nudMinArea.Maximum = 20000;
			nudMinArea.Value = _settings.MinArea; // Načíta z Settings (default: 200, optimalizované pre číslice)

			// ========================= 7-SEG SEGMENT ON THRESHOLD Z SETTINGS =========================
			// Prah pre detekciu ON/OFF segmentu pri 7-seg dekódovaní (0.05 - 1.0). Default: 0.35.
			nudSegmentOnThreshold.DecimalPlaces = 2;
			nudSegmentOnThreshold.Increment = 0.05M;
			nudSegmentOnThreshold.Minimum = 0.05M;
			nudSegmentOnThreshold.Maximum = 1.0M;
			nudSegmentOnThreshold.Value = (decimal)_settings.SegmentOnThreshold;

			// ========================= BILATERAL FILTER PARAMETRE Z SETTINGS =========================

			// Bilateral D (diameter filtra) - načíta z Settings.json (default: 9)
			nudBilateralD.Value = _settings.BilateralD;

			// Bilateral SigmaColor - načíta z Settings.json (default: 75, optimalizované)
			nudBilateralSigmaColor.Value = _settings.BilateralSigmaColor;

			// Bilateral SigmaSpace - načíta z Settings.json (default: 75, optimalizované)
			nudBilateralSigmaSpace.Value = _settings.BilateralSigmaSpace;

			// ========================= CLAHE PARAMETRE Z SETTINGS =========================

			// CLAHE ClipLimit - načíta z Settings.json (default: 3.0, optimalizované)
			nudCLAHEClipLimit.Value = (decimal)_settings.CLAHEClipLimit;

			// CLAHE TileGridSizeX - načíta z Settings.json (default: 4, optimalizované)
			nudCLAHETileGridSizeX.Value = _settings.CLAHETileGridSizeX;

			// CLAHE TileGridSizeY - načíta z Settings.json (default: 4, optimalizované)
			nudCLAHETileGridSizeY.Value = _settings.CLAHETileGridSizeY;

			// ========================= MORPHOLOGY PARAMETRE Z SETTINGS =========================

			// Morphology KernelSize - načíta z Settings.json (default: 3)
			nudMorphologyKernelSize.Value = _settings.MorphologyKernelSize;

			// Morphology OpenIterations - načíta z Settings.json (default: 1)
			nudMorphologyOpenIterations.Value = _settings.MorphologyOpenIterations;

			// Morphology CloseIterations - načíta z Settings.json (default: 1)
			nudMorphologyCloseIterations.Value = _settings.MorphologyCloseIterations;

			// ========================= FROM LEFT / FROM RIGHT PARAMETRE Z SETTINGS =========================
			// Parametre pre rozdelenie cleaned obrazu na ľavú a pravú časť (2 číslice).

			// FromLeft – percento šírky z ľavej strany (1-100%). Default: 50%.
			// Načíta z Settings.json - určuje, koľko percent z celkovej šírky cleaned obrazu
			// sa použije pre detekciu ľavej číslice.
			nudFromLeft.Value = _settings.FromLeft;

			// FromRight – percento šírky z pravej strany (1-100%). Default: 50%.
			// Načíta z Settings.json - určuje, koľko percent z celkovej šírky cleaned obrazu
			// sa použije pre detekciu pravej číslice.
			nudFromRight.Value = _settings.FromRight;

			// ========================= LED INTENZITA Z SETTINGS =========================
			// Načíta intenzitu LED z Settings.json (0-255). Default: 0 (vypnutá).
			trkLedIntensity.Value = Math.Clamp(_settings.LedIntensity, 0, 255);
			lblLedValue.Text = $"Intenzita: {trkLedIntensity.Value}";
			chkLed.Checked = _settings.LedIntensity > 0;

			// Na začiatku je tlačidlo Stop zakázané – stream nebeží
			btnStop.Enabled = false;

			// Inicializácia ComboBoxu pre ChangeMatrix - nastaví default hodnotu na 32x32
			// Položky (16, 32, 64, 128) sú už pridané v Designer.cs cez Items.AddRange()
			// Vyberieme druhú položku (index 1) = 32
			if (cboChangeMatrix.Items.Count > 0)
			{
				cboChangeMatrix.SelectedIndex = 1; // 32 (index: 0=16, 1=32, 2=64, 3=128)
			}

			// Validácia START tlačidla – ak je ComboBox prázdny (žiadne IP adresy), zakáž START.
			// Používateľ nemá z čoho vybrať stream URL, takže spustenie by zlyhalo.
			// Toto sa volá pri štarte aplikácie (po naplnení ComboBoxu).
			ValidateStartButton();


			// Vytvoríme logger:
			// - zapisuje do /logs
			// - prefix log súboru = "log"
			// - min level = Debug (zapisuje všetko)
			// - rotácia veľkosti = 5 MB (v Logger.cs default)
			_logger = new Logger(_logsDir, filePrefix: "log", minLevel: LogLevel.Debug);

			// Log – štart aplikácie.
			_logger.Info("Application started");

			// ========================= VYTVORENIE PRIEČINKOV PRE TRÉNOVACIE PRÍKLADY =========================
			// Vytvorí priečinkovú štruktúru logs/examples/{0,1,...,9,N}/ ak ešte neexistuje.
			// Volané raz pri štarte – Directory.CreateDirectory je idempotentné (ak priečinok
			// už existuje, nerobí nič a nechybuje).
			EnsureExampleDirectories();

			_digitSeg = new DigitSegmentation(_logger);

			// ========================= ONNX DIGIT RECOGNIZER =========================
			_onnxRecognizer = new OnnxDigitRecognizer(_logger);
			string modelPath = _settings.OnnxModelPath;
			if (!Path.IsPathRooted(modelPath))
			{
				modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
			}
			bool onnxAvailable = _onnxRecognizer.LoadModel(modelPath);

			// ========================= NAPLNENIE COMBOBOXU PRE VÝBER METÓDY =========================
			cboRecognitionMethod.Items.Add("7-SEG");
			if (onnxAvailable)
			{
				cboRecognitionMethod.Items.Add("ONNX");
			}

			string preferredMethod = _settings.RecognitionMethod;
			int preferredIndex = cboRecognitionMethod.Items.IndexOf(preferredMethod);
			if (preferredIndex >= 0)
			{
				cboRecognitionMethod.SelectedIndex = preferredIndex;
			}
			else
			{
				cboRecognitionMethod.SelectedIndex = 0;
				_logger.Warn($"Preferred recognition method '{preferredMethod}' not available, using '{cboRecognitionMethod.SelectedItem}'");
			}

			// Načítaj ONNX confidence z Settings do UI
			nudOnnxConfidence.Value = (decimal)_settings.OnnxMinConfidence;

			// ========================= NUMREG INTERVAL (každých N sekúnd) =========================
			// Načíta interval vyhodnocovania z Settings.json.
			// Default je 10s, ale používateľ si ho môže upraviť v Settings.json.
			_numEvalIntervalSeconds = _settings.NumEvalIntervalSeconds;
			if (_numEvalIntervalSeconds < 1) _numEvalIntervalSeconds = 1;
			if (_numEvalIntervalSeconds > 3600) _numEvalIntervalSeconds = 3600;
			_nextNumEvalUtc = DateTime.UtcNow;

			// ========================= MySQL WRITER (background) =========================
			// Spustíme background zapisovač do DB.
			// - ak je MySqlEnabled = false, writer síce beží, ale Enqueue() nič nerobí.
			// - DB pripojenie sa robí v samostatnom vlákne a pri výpadku sa automaticky obnovuje.
			_mySqlWriter = new MySqlWriter(_settings, _logger);
			_mySqlWriter.Start();

			// Vytvoríme priečinok pre snapshoty (ak neexistuje).
			Directory.CreateDirectory(_snapshotsDir);
		}

		/*********************************************************EVENTS*************************************************************************/

		
		/// <summary>
		/// Vymaže históriu rozpoznaných čísiel v UI (ListView v grpNumReg).
		///
		/// Pozn.: DB záznamy to nemaže (tie sú už uložené v MySQL).
		/// </summary>
		private void btnNumRegClear_Click(object? sender, EventArgs e)
		{
			// Bezpečne na UI threade.
			if (lvNumReg.InvokeRequired)
			{
				lvNumReg.BeginInvoke(new Action(() => lvNumReg.Items.Clear()));
				return;
			}

			lvNumReg.Items.Clear();
		}

		// Obsluha kliknutia na tlačidlo START – spustenie streamu
		private async void btnStart_Click(object sender, EventArgs e)
		{
			// log
			_logger.Info("START clicked – capture starting");

			// Ak už stream beží, ignoruj ďalší klik
			if (_running) return;

			// Nastav flag, že stream beží
			_running = true;

			// Deaktivuj tlačidlo START, aby sa nedalo znova stlačiť
			btnStart.Enabled = false;
			// Aktivuj tlačidlo STOP
			btnStop.Enabled = true;

			// Vytvor nový CancellationTokenSource pre toto spustenie
			_cts = new CancellationTokenSource();

			// Spusť capture loop v samostatnej úlohe, aby si neblokoval UI vlákno
			await Task.Run(() => CaptureLoop(_cts.Token));
		}

		// Obsluha kliknutia na tlačidlo STOP – zastavenie streamu
		private void btnStop_Click(object sender, EventArgs e)
		{
			// log
			_logger.Info("STOP clicked – capture stopping");
			// Zavolá metódu, ktorá korektne zastaví stream
			StopCapture();
		}

		// Obsluha tlačidla SAVE – uloží všetky UI nastavenia do Settings.json
		private void btnSave_Click(object sender, EventArgs e)
		{
			try
			{
				// ========================= NAČÍTANIE HODNÔT Z UI KONTROLIEK =========================
				// ROI parametre (percentá)
				_settings.ROIX = (int)GetNudValueSafe(nudX);
				_settings.ROIY = (int)GetNudValueSafe(nudY);
				_settings.ROIWidth = (int)GetNudValueSafe(nudWidth);
				_settings.ROIHeight = (int)GetNudValueSafe(nudHeight);

				// Resize scale (desatinné číslo)
				_settings.ResizeScale = (double)GetNudValueSafe(nudResize);

				// Bilateral Filter parametre
				_settings.BilateralD = (int)GetNudValueSafe(nudBilateralD);
				_settings.BilateralSigmaColor = (int)GetNudValueSafe(nudBilateralSigmaColor);
				_settings.BilateralSigmaSpace = (int)GetNudValueSafe(nudBilateralSigmaSpace);

				// CLAHE parametre
				_settings.CLAHEClipLimit = (double)GetNudValueSafe(nudCLAHEClipLimit);
				_settings.CLAHETileGridSizeX = (int)GetNudValueSafe(nudCLAHETileGridSizeX);
				_settings.CLAHETileGridSizeY = (int)GetNudValueSafe(nudCLAHETileGridSizeY);

				// Adaptive Threshold parametre
				_settings.AdaptiveThresholdBlockSize = (int)GetNudValueSafe(nudBlockSize);
				_settings.AdaptiveThresholdC = (double)GetNudValueSafe(nudC);

				// Morphology parametre
				_settings.MorphologyKernelSize = (int)GetNudValueSafe(nudMorphologyKernelSize);
				_settings.MorphologyOpenIterations = (int)GetNudValueSafe(nudMorphologyOpenIterations);
				_settings.MorphologyCloseIterations = (int)GetNudValueSafe(nudMorphologyCloseIterations);

				// Connected Components parameter
				_settings.MinArea = (int)GetNudValueSafe(nudMinArea);

				// 7-SEG segment ON threshold
				_settings.SegmentOnThreshold = (double)GetNudValueSafe(nudSegmentOnThreshold);

				// FromLeft / FromRight parametre (rozdelenie na 2 číslice)
				_settings.FromLeft = (int)GetNudValueSafe(nudFromLeft);
				_settings.FromRight = (int)GetNudValueSafe(nudFromRight);

				// ONNX confidence threshold
				_settings.OnnxMinConfidence = (float)GetNudValueSafe(nudOnnxConfidence);

				// LED intenzita (0-255)
				_settings.LedIntensity = trkLedIntensity.Value;

				// ========================= ULOŽENIE DO SETTINGS.JSON =========================
				// Uloží aktuálne nastavenia do Settings.json súboru.
				// Tento súbor sa použije pri ďalšom spustení aplikácie ako default hodnoty.
				_settings.Save(_settingsPath);

				// Log úspešného uloženia
				_logger.Info("Settings saved to Settings.json");

				// Zobrazí používateľovi potvrdenie o úspešnom uložení
				MessageBox.Show(
					"Všetky nastavenia boli úspešne uložené do Settings.json.\n" +
					"Pri ďalšom spustení aplikácie sa použijú tieto hodnoty ako default.",
					"Nastavenia uložené",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information
				);
			}
			catch (Exception ex)
			{
				// Log chyby
				_logger.Error($"Failed to save settings: {ex.Message}");

				// Zobrazí používateľovi chybovú hlášku
				MessageBox.Show(
					$"Chyba pri ukladaní nastavení:\n{ex.Message}",
					"Chyba",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error
				);
			}
		}

		/// <summary>
		/// Event handler pre zmenu metódy rozpoznávania číslic.
		/// Prepína medzi 7-SEG a ONNX.
		/// </summary>
		private void cboRecognitionMethod_SelectedIndexChanged(object? sender, EventArgs e)
		{
			string? selectedMethod = cboRecognitionMethod.SelectedItem?.ToString();
			_logger?.Info($"Recognition method changed to: {selectedMethod}");

			if (!string.IsNullOrEmpty(selectedMethod))
			{
				_settings.RecognitionMethod = selectedMethod;
			}
		}

		// ========================= EXPORT TRÉNOVACÍCH PRÍKLADOV PRE TENSORFLOW =========================

		/// <summary>
		/// Vytvorí priečinkovú štruktúru pre trénovacie príklady.
		/// Štruktúra: logs/examples/{0, 1, 2, 3, 4, 5, 6, 7, 8, 9, N}/
		/// Každý podpriečinok bude obsahovať PNG obrázky danej číslice (64×96 px).
		/// Priečinok "N" je pre nerozpoznané číslice (fallback).
		///
		/// Thread-safety: Volaná raz z konštruktora na UI threade.
		/// Directory.CreateDirectory je thread-safe a idempotentné.
		/// </summary>
		private void EnsureExampleDirectories()
		{
			try
			{
				// Vytvorí podpriečinky pre číslice 0-9 (cleaned) aj 0_O..9_O (originál).
				// Každý priečinok bude obsahovať PNG obrázky danej číslice.
				for (int i = 0; i <= 9; i++)
				{
					Directory.CreateDirectory(Path.Combine(_examplesDir, i.ToString()));
					Directory.CreateDirectory(Path.Combine(_examplesDir, i.ToString() + "_O"));
				}

				// Vytvorí priečinok "N" pre nerozpoznané číslice a "N_O" pre ich originály.
				// Ak algoritmus nedokáže určiť číslicu, obrázok sa uloží sem.
				Directory.CreateDirectory(Path.Combine(_examplesDir, "N"));
				Directory.CreateDirectory(Path.Combine(_examplesDir, "N_O"));

				_logger?.Info($"Example directories ensured at: {_examplesDir}");
			}
			catch (Exception ex)
			{
				// Ak sa priečinky nepodarí vytvoriť (napr. permissions), zalogujeme chybu.
				// Export bude nefunkčný, ale aplikácia pobeží ďalej.
				_logger?.Error($"Failed to create example directories: {ex.Message}");
			}
		}

		/// <summary>
		/// Exportuje maticu (left alebo right časť cleaned obrazu) ako 64×96 PNG obrázok
		/// do priečinka podľa rozpoznanej číslice.
		///
		/// Postup:
		///   1) Overí, či vstupný Mat je platný (nie null, nie prázdny).
		///   2) Resize na 64×96 px (šírka × výška) – zachová aspektový pomer 7-seg číslice.
		///      Interpolácia Area je optimálna pre zmenšovanie (anti-aliasing na hranách segmentov).
		///   3) Určí cieľový priečinok: 0-9 podľa rozpoznanej hodnoty, alebo "N" ak nerozpoznaná.
		///   4) Vygeneruje názov súboru: {timestamp}_{L|R}.png
		///      - timestamp = aktuálny čas vo formáte yyyyMMdd_HHmmss_fff (milisekundy pre unikátnosť)
		///      - L = z ľavej matice (prvá číslica), R = z pravej matice (druhá číslica)
		///   5) Uloží ako 8-bit grayscale PNG (bezstratová kompresia).
		///
		/// Thread-safety:
		///   - digitMat je lokálna premenná volajúceho (leftPart/rightPart) – nie je zdieľaná.
		///   - Cv2.Resize a Cv2.ImWrite pracujú s lokálnymi Mat objektmi.
		///   - Názov súboru obsahuje milisekundy + stranu (L/R), takže je prakticky unikátny.
		///   - Directory.CreateDirectory je thread-safe a idempotentné.
		///   - Metóda nepristupuje k žiadnym zdieľaným fieldov – je plne reentrantná.
		///
		/// Výkon:
		///   - Resize z ~434×660 na 64×96 je veľmi rýchly (< 1ms).
		///   - PNG zápis na disk je rýchly pre malé obrázky (~0.5 KB per file).
		///   - Volané len raz za NumEvalIntervalSeconds (default 10s), nie na každom frame.
		/// </summary>
		/// <param name="digitMat">
		///   Vstupná matica – leftPart alebo rightPart z Pipeline().
		///   Binárny obraz (0 = pozadie, 255 = segment). Môže byť null ak Pipeline zlyhalo.
		/// </param>
		/// <param name="recognizedValue">
		///   Rozpoznaná číslica (0-9) alebo null ak algoritmus nedokázal číslicu rozpoznať.
		///   Určuje do ktorého podpriečinka sa obrázok uloží.
		/// </param>
		/// <param name="side">
		///   "L" pre ľavú maticu (prvá číslica) alebo "R" pre pravú maticu (druhá číslica).
		///   Pridáva sa do názvu súboru pre odlíšenie ľavej a pravej číslice.
		/// </param>
		/// <param name="timestamp">
		///   Vygenerovaný timestamp zdieľaný medzi cleaned a original exportom,
		///   aby mali rovnaký názov súboru.
		/// </param>
		/// <param name="folderSuffix">
		///   "" pre cleaned export, "_O" pre originálny (nespracovaný) export.
		/// </param>
		private void ExportDigitExample(Mat? digitMat, int? recognizedValue, string side,
										 string timestamp, string folderSuffix)
		{
			// ========================= VALIDÁCIA VSTUPU =========================
			// Ak Mat je null (Pipeline zlyhalo alebo split nebol možný), nemáme čo exportovať.
			if (digitMat == null)
				return;

			// Ak Mat je prázdny (0 riadkov alebo 0 stĺpcov), Cv2.Resize by zlyhal.
			if (digitMat.Empty())
				return;

			try
			{
				// ========================= 1) RESIZE NA 64×96 =========================
				// Cieľová veľkosť: 64 px šírka × 96 px výška.
				// Toto zodpovedá aspektovému pomeru 2:3, blízkemu tvaru 7-seg číslice (~434:660).
				// InterpolationFlags.Area je najlepšia metóda pre zmenšovanie (downscaling):
				// - výsledok je hladký bez moiré efektov,
				// - na hranách segmentov vznikne jemný anti-aliasing (medzitóny sivej),
				//   čo je pre trénovanie neurónovej siete žiaduce (lepšia generalizácia).
				using var resized = new Mat();
				Cv2.Resize(
					digitMat,                              // vstup: ~434×660 binárny obraz
					resized,                               // výstup: 64×96 grayscale obraz
					new OpenCvSharp.Size(64, 96),          // cieľová veľkosť (šírka, výška)
					0, 0,                                  // fx, fy = 0 (ignorované, keďže Size je zadaná)
					InterpolationFlags.Area                // Area = optimálna pre downscaling
				);

				// ========================= 2) URČENIE CIEĽOVÉHO PRIEČINKA =========================
				// Ak rozpoznávanie uspelo (recognizedValue = 0-9), uložíme do priečinka s číslom.
				// Ak zlyhalo (recognizedValue = null), uložíme do priečinka "N" (Nerozpoznané).
				string folderName = (recognizedValue.HasValue
					? recognizedValue.Value.ToString()     // "0", "1", ... "9"
					: "N")                                 // nerozpoznané → priečinok N
					+ folderSuffix;                        // "" alebo "_O"

				string dirPath = Path.Combine(_examplesDir, folderName);

				// Defensívne: vytvor priečinok ak neexistuje (napr. ak ho niekto medzičasom zmazal).
				// Directory.CreateDirectory je idempotentné – ak priečinok existuje, nerobí nič.
				Directory.CreateDirectory(dirPath);

				// ========================= 3) GENEROVANIE NÁZVU SÚBORU =========================
				// Formát: {timestamp}_{L|R}.png
				// timestamp je zdieľaný medzi cleaned a original exportom (rovnaký názov).
				// Príklad: 20260129_143052_123_L.png
				string fileName = $"{timestamp}_{side}.png";
				string fullPath = Path.Combine(dirPath, fileName);

				// ========================= 4) ULOŽENIE AKO PNG =========================
				// Cv2.ImWrite uloží Mat ako PNG (bezstratová kompresia).
				// Pre 64×96 grayscale obraz je výsledný súbor veľmi malý (~0.3–1 KB).
				// PNG automaticky rozpozná formát podľa prípony .png.
				Cv2.ImWrite(fullPath, resized);

				// Debug log – zapíše cestu k uloženému súboru (užitočné pri ladení).
				_logger?.Debug($"Example exported: {fullPath} (digit={folderName})");
			}
			catch (Exception ex)
			{
				// Export nesmie zhodiť Pipeline – akákoľvek chyba (disk plný, permissions, ...)
				// sa len zaloguje a Pipeline pokračuje ďalej v normálnom spracovaní.
				_logger?.Warn($"Export digit example failed ({side}): {ex.Message}");
			}
		}

		// ========================= LED OVLÁDANIE (ESP32-CAM) =========================

		/// <summary>
		/// Odošle HTTP GET požiadavku na ESP32-CAM pre nastavenie intenzity LED.
		/// Endpoint: http://{IP}/control?var=led_intensity&val={0-255}
		/// (Štandardný endpoint z Arduino IDE CameraWebServer example.)
		/// </summary>
		/// <param name="intensity">Intenzita LED (0 = vypnutá, 255 = max jas)</param>
		private async void SendLedIntensity(int intensity)
		{
			try
			{
				// Získaj aktuálne vybranú URL zo stream ComboBoxu.
				// Stream URL formát: http://192.168.50.96:81/stream
				// Control URL formát: http://192.168.50.96/control?var=led_intensity&val=X
				string? streamUrl = null;
				if (InvokeRequired)
					streamUrl = (string?)Invoke(() => cboUrl.SelectedItem?.ToString());
				else
					streamUrl = cboUrl.SelectedItem?.ToString();

				if (string.IsNullOrEmpty(streamUrl))
				{
					_logger?.Warn("LED: Žiadna URL nie je vybraná v ComboBoxe.");
					return;
				}

				// Extrahuj IP adresu zo stream URL (odstráň port a cestu)
				var uri = new Uri(streamUrl);
				string controlUrl = $"http://{uri.Host}/control?var=led_intensity&val={intensity}";

				_logger?.Info($"LED: Posielam intenzitu {intensity} na {controlUrl}");

				var response = await _httpClient.GetAsync(controlUrl);

				if (response.IsSuccessStatusCode)
				{
					_logger?.Info($"LED: Intenzita nastavená na {intensity}");
				}
				else
				{
					_logger?.Warn($"LED: HTTP odpoveď {(int)response.StatusCode} z {controlUrl}");
				}
			}
			catch (HttpRequestException ex)
			{
				_logger?.Error($"LED: Nepodarilo sa pripojiť ku kamere – {ex.Message}");
			}
			catch (TaskCanceledException)
			{
				_logger?.Warn("LED: Timeout pri pripojení ku kamere.");
			}
			catch (Exception ex)
			{
				_logger?.Error($"LED: Neočakávaná chyba – {ex.Message}");
			}
		}

		/// <summary>
		/// Event handler pre CheckBox LED ON/OFF.
		/// Ak sa zaškrtne, pošle aktuálnu intenzitu z TrackBar-u (min 1).
		/// Ak sa odškrtne, pošle 0 (LED vypnutá).
		/// </summary>
		private void chkLed_CheckedChanged(object sender, EventArgs e)
		{
			if (chkLed.Checked)
			{
				// Ak je trackbar na 0 a používateľ zapol LED, nastav na stred (128)
				if (trkLedIntensity.Value == 0)
				{
					trkLedIntensity.Value = 128;
					lblLedValue.Text = "Intenzita: 128";
				}
				SendLedIntensity(trkLedIntensity.Value);
			}
			else
			{
				SendLedIntensity(0);
			}
		}

		/// <summary>
		/// Event handler pre TrackBar (posuvník) intenzity LED.
		/// Pri každom posunutí aktualizuje label a pošle novú hodnotu na kameru.
		/// </summary>
		private void trkLedIntensity_Scroll(object sender, EventArgs e)
		{
			int val = trkLedIntensity.Value;
			lblLedValue.Text = $"Intenzita: {val}";

			// Ak je LED zapnutá, pošli novú hodnotu; ak je vypnutá a val > 0, automaticky zapni
			if (val > 0)
			{
				if (!chkLed.Checked)
					chkLed.Checked = true; // toto tiež zavolá SendLedIntensity cez CheckedChanged
				else
					SendLedIntensity(val);
			}
			else
			{
				// Intenzita = 0 → vypni LED
				chkLed.Checked = false; // toto zavolá SendLedIntensity(0) cez CheckedChanged
			}
		}

		// Obsluha tlačidla ROI – zobrazí aktuálny ROI v OpenCV okne
		private void btnRoi_Click(object sender, EventArgs e)
		{
			// Lokálna referencia na kópiu ROI.
			// Používame lokálnu premennú preto, aby sme NIKDY neposielali
			// zdieľaný _lastRoi priamo do OpenCV (ImShow ide do native kódu).
			Mat roiCopy = null;

			// Uzamkneme zámok, aby capture thread nemohol v tom istom momente
			// robiť _lastRoi.Dispose() alebo prepísať _lastRoi na nový Mat.
			// Toto je kľúčové proti race-condition chybám typu "_dims".
			lock (_roiLock)
			{
				// Ak _lastRoi ešte neexistuje (nebolo nikdy nastavené),
				// nemáme čo zobrazovať → bezpečne skončíme.
				if (_lastRoi == null)
				{
					MessageBox.Show("ROI ešte nebolo vytvorené.");
					return;
				}

				// Ak _lastRoi existuje, ale je prázdny Mat (nemá dáta),
				// volanie ImShow by padlo na !_src.empty().
				if (_lastRoi.Empty())
				{
					MessageBox.Show("ROI je prázdne.");
					return;
				}

				// Vytvoríme CLONE z _lastRoi:
				// - Clone() vytvorí úplne nový Mat s vlastnou pamäťou
				// - po tomto kroku už roiCopy NIE JE závislý od _lastRoi
				// - capture thread môže _lastRoi pokojne Dispose()nuť,
				//   roiCopy zostane platný
				roiCopy = _lastRoi.Clone();
			} // ← tu sa lock uvoľní, UI thread už pracuje s vlastnou kópiou

			try
			{
				// Extra defensívna kontrola:
				// teoreticky by sa to nemalo stať, ale ak by Clone zlyhal
				// alebo vrátil prázdny Mat, ImShow by opäť padlo.
				if (roiCopy == null || roiCopy.Empty())
				{
					MessageBox.Show("Nepodarilo sa vytvoriť kópiu ROI.");
					return;
				}

				// Zobrazí ROI v samostatnom OpenCV okne.
				// V tomto momente už:
				// - roiCopy má vlastnú pamäť
				// - nie je zdieľaný medzi vláknami
				// - nehrozí race-condition ani Dispose počas ImShow
				Cv2.ImShow("roi", roiCopy);
			}
			finally
			{
				// Uvoľníme unmanaged pamäť roiCopy po skončení ImShow,
				// aby nevznikal memory leak pri opakovanom kliknutí.
				roiCopy?.Dispose();
			}
		}

		// Obsluha tlačidla VIEW – zobrazí zväčšený ROI ("up") v OpenCV okne
		private void btnView_Click(object sender, EventArgs e)
		{
			// Lokálna referencia na kópiu zväčšeného ROI (up).
			// Robíme kópiu preto, aby sme nikdy neposielali zdieľaný _lastUp priamo do ImShow,
			// lebo _lastUp sa môže na capture threade práve meniť alebo disposeovať.
			Mat upCopy = null;

			// Uzamkneme zámok, aby capture thread nemohol v tom istom čase:
			// - urobiť _lastUp.Dispose()
			// - prepísať _lastUp novým Mat
			// - alebo doň ešte len zapisovať výsledok Resize.
			// Toto je kritické proti race-condition chybám typu "_dims".
			lock (_roiLock)
			{
				// Ak _lastUp ešte nikdy nebol vytvorený (napr. ešte nebežal ProcessFrame),
				// nemáme čo zobrazovať → bezpečne skončíme.
				if (_lastUp == null)
				{
					// Zobrazíme informáciu používateľovi (aby vedel, prečo sa nič nedeje).
					MessageBox.Show("UP (zväčšený ROI) ešte nebol vytvorený.");
					// Ukončíme handler tlačidla.
					return;
				}

				// Ak _lastUp existuje, ale je prázdny Mat (nemá dáta),
				// ImShow by mohol spadnúť na empty src alebo na interný assert.
				if (_lastUp.Empty())
				{
					// Informujeme používateľa, že up je prázdny.
					MessageBox.Show("UP (zväčšený ROI) je prázdny.");
					// Ukončíme handler tlačidla.
					return;
				}

				// Vytvoríme Clone() z _lastUp, aby sme získali úplne nezávislý Mat s vlastnou pamäťou.
				// Po uvoľnení locku môže capture thread _lastUp pokojne meniť alebo disposeovať,
				// ale upCopy ostane stabilný a bezpečný pre ImShow.
				upCopy = _lastUp.Clone();
			} // Tu sa lock uvoľní – ďalej pracujeme už len s lokálnou kópiou.

			try
			{
				// Extra defensívna kontrola:
				// Aj keď by Clone() nemal zlyhať, radšej overíme, že kópia je platná.
				if (upCopy == null || upCopy.Empty())
				{
					// Ak by bola kópia neplatná/prázdna, nevoláme ImShow a nevznikne pád.
					MessageBox.Show("Nepodarilo sa vytvoriť kópiu UP obrázka.");
					// Skončíme bezpečne.
					return;
				}

				// Zobrazí upCopy v OpenCV okne s názvom "up".
				// Používame upCopy (nie _lastUp), aby sme eliminovali súbeh (race-condition).
				Cv2.ImShow("up", upCopy);
			}
			finally
			{
				// Uvoľníme unmanaged pamäť lokálnej kópie upCopy,
				// aby nevznikal memory leak pri opakovanom kliknutí na tlačidlo VIEW.
				upCopy?.Dispose();
			}
		}

		// Obsluha debug tlačidla CONTRAST – zobrazí _lastContrast v OpenCV okne
		private void btnDebugContrast_Click(object sender, EventArgs e)
		{
			// Lokálna kópia pre thread-safe zobrazenie (nikdy neposielame zdieľaný Mat do ImShow)
			Mat contrastCopy = null;

			// Lock: _lastContrast môže byť práve prepisovaný alebo Dispose() v Pipeline/StopCapture
			lock (_roiLock)
			{
				// Ak ešte nemáme contrast, nemáme čo zobrazovať
				if (_lastContrast == null)
				{
					MessageBox.Show("CONTRAST ešte nebol vytvorený.");
					return;
				}

				// Ak je prázdny, tiež nemá zmysel zobrazovať
				if (_lastContrast.Empty())
				{
					MessageBox.Show("CONTRAST je prázdny.");
					return;
				}

				// Clone = vlastná pamäť => bezpečné pre ImShow
				contrastCopy = _lastContrast.Clone();
			}

			try
			{
				// Defenzívna kontrola
				if (contrastCopy == null || contrastCopy.Empty())
				{
					MessageBox.Show("Nepodarilo sa vytvoriť kópiu CONTRAST.");
					return;
				}

				// Zobraz v OpenCV okne
				Cv2.ImShow("contrast", contrastCopy);
			}
			finally
			{
				// Upratanie lokálnej kópie
				contrastCopy?.Dispose();
			}
		}

		// Obsluha debug tlačidla BINARY – zobrazí _lastBinary v OpenCV okne
		private void btnDebugBinary_Click(object sender, EventArgs e)
		{
			Mat binaryCopy = null;

			lock (_roiLock)
			{
				if (_lastBinary == null)
				{
					MessageBox.Show("BINARY ešte nebol vytvorený.");
					return;
				}

				if (_lastBinary.Empty())
				{
					MessageBox.Show("BINARY je prázdny.");
					return;
				}

				binaryCopy = _lastBinary.Clone();
			}

			try
			{
				if (binaryCopy == null || binaryCopy.Empty())
				{
					MessageBox.Show("Nepodarilo sa vytvoriť kópiu BINARY.");
					return;
				}

				Cv2.ImShow("binary", binaryCopy);
			}
			finally
			{
				binaryCopy?.Dispose();
			}
		}

		// Obsluha debug tlačidla CLEANED – zobrazí _lastCleaned v OpenCV okne
		private void btnDebugCleaned_Click(object sender, EventArgs e)
		{
			Mat cleanedCopy = null;

			lock (_roiLock)
			{
				if (_lastCleaned == null)
				{
					MessageBox.Show("CLEANED ešte nebol vytvorený.");
					return;
				}

				if (_lastCleaned.Empty())
				{
					MessageBox.Show("CLEANED je prázdny.");
					return;
				}

				cleanedCopy = _lastCleaned.Clone();
			}

			try
			{
				if (cleanedCopy == null || cleanedCopy.Empty())
				{
					MessageBox.Show("Nepodarilo sa vytvoriť kópiu CLEANED.");
					return;
				}

				Cv2.ImShow("cleaned", cleanedCopy);
			}
			finally
			{
				cleanedCopy?.Dispose();
			}
		}

		// Obsluha tlačidla Test Segmentation - testuje segmentáciu číslic z cleaned image
		// KAPITOLA 5: Digit Segmentation
		private void btnTestSegmentation_Click(object sender, EventArgs e)
		{
			Mat cleanedCopy = null;
			List<Mat> digits = null;

			try
			{
				// 1. Získaj kópiu _lastCleaned (thread-safe)
				lock (_roiLock)
				{
					if (_lastCleaned == null)
					{
						MessageBox.Show("CLEANED ešte nebol vytvorený. Spustite stream najprv.");
						return;
					}

					if (_lastCleaned.Empty())
					{
						MessageBox.Show("CLEANED je prázdny.");
						return;
					}

					cleanedCopy = _lastCleaned.Clone();
				}

				// 2. Spusti segmentáciu číslic
				_logger.Info("=== TEST SEGMENTATION START ===");
				digits = _digitSeg?.ExtractDigits(cleanedCopy) ?? new List<Mat>();
				_logger.Info($"=== TEST SEGMENTATION END - Extracted {digits.Count} digits ===");

				// 3. Validácia výsledku
				if (digits == null || digits.Count == 0)
				{
					MessageBox.Show(
						"Segmentácia nenašla žiadne číslice.\n\n" +
						"Možné príčiny:\n" +
						"- ROI neobsahuje číslice\n" +
						"- Threshold parametre sú zlé (cleaned image je prázdny)\n" +
						"- minArea je príliš veľká",
						"Test Segmentation",
						MessageBoxButtons.OK,
						MessageBoxIcon.Warning
					);
					return;
				}

				// 4. Zobraz výsledky v OpenCV oknách
				MessageBox.Show(
					$"Segmentácia úspešná!\n\n" +
					$"Počet extrahovaných číslic: {digits.Count}\n\n" +
					$"Číslice sa zobrazia v OpenCV oknách.\n" +
					$"Stlačte ľubovoľnú klávesu pre zatvorenie okien.",
					"Test Segmentation",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information
				);

				// Zobraz cleaned image s bounding rectangles (debug)
				// Najprv získaj rectangles pre vizualizáciu
				//CvPoint[][] contours = FindWhiteBlobs(cleanedCopy);
				//List<CvRect> rectangles = GetBoundingRectangles(contours);
				//List<CvRect> filtered = FilterContours(contours, rectangles, minArea: 200);
				//List<CvRect> merged = MergeOverlappingRects(filtered, overlapThreshold: 0.5);

				//// Vykreslí rectangles na cleaned image
				//Mat debugImage = DrawRectangles(cleanedCopy, merged, new Scalar(128, 128, 128), thickness: 2);
				//Cv2.ImShow("Debug - Bounding Rectangles", debugImage);

				// Zobraz každú extrahovanú číslicu
				for (int i = 0; i < digits.Count; i++)
				{
					Cv2.ImShow($"Digit {i} (28x28 px)", digits[i]);
				}

				// Čakaj na stlačenie klávesy
				Cv2.WaitKey(0);

				// Zatvor všetky OpenCV okná
				Cv2.DestroyAllWindows();

				// Dispose debug image
				//debugImage?.Dispose();
			}
			catch (Exception ex)
			{
				_logger.Error($"Test Segmentation failed: {ex.Message}");
				MessageBox.Show(
					$"Chyba pri testovaní segmentácie:\n{ex.Message}",
					"Chyba",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error
				);
			}
			finally
			{
				// Cleanup
				cleanedCopy?.Dispose();

				if (digits != null)
				{
					foreach (var digit in digits)
					{
						digit?.Dispose();
					}
				}
			}
		}

		/*********************************************************INTERNAL METHODS***************************************************************/

		/// <summary>
		/// Event handler pre zmenu výberu v ComboBoxe (cboUrl).
		/// Volá sa automaticky, keď používateľ vyberie inú IP adresu zo zoznamu.
		/// Validuje, či je niečo vybrané, a podľa toho povoľuje/zakazuje tlačidlo START.
		/// </summary>
		private void CboUrl_SelectedIndexChanged(object? sender, EventArgs e)
		{
			// Zavolá validáciu START tlačidla.
			// Ak je niečo vybrané v ComboBoxe, START sa povolí. Ak nie, zakáže sa.
			ValidateStartButton();
		}
		/// <summary>
		/// Rozhodne, či práve teraz môžeme urobiť vyhodnotenie číslic (NumReg).
		/// Požiadavka: vyhodnocovať len každých N sekúnd (default 10s).
		///
		/// Thread-safe:
		/// - chránené cez _numEvalLock, aby pipeline thread nikdy nevyhodnocoval 'dvakrát naraz'.
		/// </summary>
		private bool ShouldEvaluateNumRegNow()
		{
			lock (_numEvalLock)
			{
				DateTime now = DateTime.UtcNow;
				if (now < _nextNumEvalUtc)
					return false;

				// Nastav ďalší termín vyhodnotenia.
				// Používame 'now +' (nie '+=') aby sa čas v prípade dlhého výpadku zrovnal a nedobiehal viac hodnotení naraz.
				_nextNumEvalUtc = now.AddSeconds(_numEvalIntervalSeconds);
				return true;
			}
		}

		/// <summary>
		/// Zapíše rozpoznaný výsledok do UI histórie (ListView v grpNumReg).
		///
		/// Dôležité:
		/// - Volá sa z capture/pipeline threadu, takže UI aktualizáciu robíme cez BeginInvoke().
		/// - Držíme len obmedzený počet položiek (aby UI časom nezomrel).
		/// </summary>
		private void AppendNumRegToUi(NumRegEntry entry)
		{
			// Ak UI ešte nie je inicializované, alebo kontrola neexistuje, skonč.
			if (lvNumReg == null || lvNumReg.IsDisposed)
				return;

			// Zápis do res.txt (thread-safe, mimo UI thread).
			// Rotácia: ak res.txt presiahne 10 MB, premenuje sa na res_old.txt a začne nový.
			try
			{
				string timeText = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
				string line = $"{timeText}\t{entry.Text}";
				string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
				Directory.CreateDirectory(logsDir);
				string resPath = Path.Combine(logsDir, "res.txt");

				const long maxResFileSize = 10 * 1024 * 1024; // 10 MB
				if (File.Exists(resPath))
				{
					var fi = new FileInfo(resPath);
					if (fi.Length > maxResFileSize)
					{
						string oldPath = Path.Combine(logsDir, "res_old.txt");
						if (File.Exists(oldPath)) File.Delete(oldPath);
						File.Move(resPath, oldPath);
					}
				}

				File.AppendAllText(resPath, line + Environment.NewLine);
			}
			catch { /* ignoruj chyby zápisu, aby neblokovali UI */ }

			void action()
			{
				// Formát času: lokálny čas pre čitateľnosť (UTC -> Local).
				string timeText = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
				var item = new ListViewItem(timeText);
				item.SubItems.Add(entry.Text);
				lvNumReg.Items.Add(item);

				// Auto-scroll na poslednú položku.
				if (lvNumReg.Items.Count > 0)
					lvNumReg.EnsureVisible(lvNumReg.Items.Count - 1);

				// Ochrana: nedovoľ, aby list rástol donekonečna (pamäť/UI výkon).
				const int maxItems = 500;
				while (lvNumReg.Items.Count > maxItems)
					 lvNumReg.Items.RemoveAt(0);
			}

			if (lvNumReg.InvokeRequired)
				lvNumReg.BeginInvoke(action);
			else
				action();
		}


		/// <summary>
		/// ========================= 7-SEGMENT DEKÓDOVANIE Z "cleaned" (NOVÉ) =========================
		///
		/// Úloha:
		/// - vstup: cleaned (binárny Mat 0/255) z Pipeline()
		/// - výstup: int value (napr. 70) + string text (napr. "70")
		///
		/// Prečo takto:
		/// - 7-seg displej je stabilnejšie dekódovať cez segmenty (bitmask) než cez OCR.
		/// - cleaned už má potlačený šum, takže sa dá použiť ako vstup do dekódovania.
		///
		/// Dôležité:
		/// - Metóda NIKDY nemení vstupný Mat "cleaned"
		/// - Metóda pracuje len s lokálnymi Mat-mi => thread-safe
		/// - Metóda je robustná na invert (niekedy sú segmenty biele, inokedy čierne) – automaticky otočí.
		///
		/// Poznámka:
		/// - Táto implementácia predpokladá LEN 1 ČÍSLICU (upravené pre detekciu jednotlivých číslic).
		/// - Hľadá najväčšiu oblasť v binárnom obraze a dekóduje ju ako jednu číslicu 0-9.
		/// </summary>
		private bool TryDecodeSevenSegmentFromCleaned(Mat cleaned, out int value, out string text)
		{
			value = 0;
			text = string.Empty;

			// ========================= 0) DEFENZÍVNE KONTROLY =========================
			if (cleaned == null || cleaned.Empty())
				return false;

			// Očakávame 1-kanálový binárny obraz.
			// Ak by sem omylom prišiel 3-kanálový Mat, konvertujeme ho.
			Mat bin = null;

			try
			{
				if (cleaned.Channels() == 1)
				{
					// Clone = aby sme nikdy neupravovali pôvodný Mat "cleaned"
					bin = cleaned.Clone();
				}
				else
				{
					// Bezpečný fallback – prevod na gray a následne threshold.
					// V praxi by sem cleaned nemal prísť ako 3-kanálový.
					using var gray = new Mat();
					Cv2.CvtColor(cleaned, gray, ColorConversionCodes.BGR2GRAY);

					bin = new Mat();
					Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Otsu);
				}

				// ========================= 1) AUTO-INVERT (aby segmenty boli BIELE na ČIERNEJ) =========================
				// Pre segmentové meranie je najjednoduchšie, ak "svietiace segmenty" sú biele (255).
				// Keďže tvoj pipeline používa BinaryInv, niekedy môže byť polarita opačná.
				//
				// Heuristika:
				// - Ak je "bielych pixelov" priveľa (napr. > 50%), je pravdepodobné, že biele je pozadie.
				// - Vtedy invertujeme, aby sa pozadie stalo čierne a segmenty biele.
				int white = Cv2.CountNonZero(bin);
				int total = bin.Rows * bin.Cols;

				if (total > 0)
				{
					double ratio = (double)white / total;

					// Ak je obraz "príliš biely", berieme to ako pozadie => invert.
					if (ratio > 0.50)
					{
						Cv2.BitwiseNot(bin, bin);
					}
				}

				// ========================= 2) DEKÓDUJ JEDNU ČÍSLICU Z CELÉHO OBRAZU =========================
				// Vstupný obraz (bin) už obsahuje LEN JEDNO číslo - nie je potrebné hľadať kontúry.
				// Celý obraz je už vyrezaný ROI s jednou číslicou, takže ho dekódujeme priamo.
				//
				// Vytvoríme Rect pre celý obraz a pošleme ho do dekódovania.
				Rect digitRect = new Rect(0, 0, bin.Cols, bin.Rows);

				// ========================= 3) DEKÓDUJ ČÍSLICU CEZ 7-SEG BITMASK =========================
				// Zmeráme 7 segmentových oblastí a spravíme bitmasku → mapovanie na číslicu 0-9.
				int? d1 = DecodeSingle7SegDigit(bin, digitRect);

				if (d1 == null)
					return false;

				// Výsledný text je len jednoziferné číslo (0-9).
				text = $"{d1}";

				// Bezpečný parse na int
				if (!int.TryParse(text, out value))
					return false;

				return true;
			}
			finally
			{
				// Uvoľníme lokálny Mat.
				bin?.Dispose();
			}
		}

		/// <summary>
		/// Dekóduje jednu číslicu zo 7-seg displeja.
		/// Vráti:
		/// - 0..9 ak úspech
		/// - null ak sa bitmask nedá namapovať
		/// </summary>
		private int? DecodeSingle7SegDigit(Mat bin, Rect digitRect)
		{
			// Defenzívne orezanie rectu do hraníc obrazu (aby submat nikdy nespadol).
			Rect r = digitRect;

			if (r.X < 0) r.X = 0;
			if (r.Y < 0) r.Y = 0;
			if (r.X + r.Width > bin.Cols) r.Width = bin.Cols - r.X;
			if (r.Y + r.Height > bin.Rows) r.Height = bin.Rows - r.Y;

			if (r.Width <= 0 || r.Height <= 0)
				return null;

			// Vyrež digit ROI (SubMat) a hneď Clone(),
			// aby sme mali vlastnú pamäť a neviazali sa na bin.
			using Mat digit = new Mat(bin, r).Clone();

			if (digit.Empty())
				return null;

			// Jemný “crop margin” (odstráni okraje, ktoré často obsahujú šum/odlesk).
			// Percentuálne, aby to fungovalo pri rôznych veľkostiach.
			int mx = (int)(digit.Cols * 0.05);
			int my = (int)(digit.Rows * 0.05);

			int x0 = Math.Clamp(mx, 0, digit.Cols - 1);
			int y0 = Math.Clamp(my, 0, digit.Rows - 1);
			int w0 = Math.Clamp(digit.Cols - 2 * mx, 1, digit.Cols);
			int h0 = Math.Clamp(digit.Rows - 2 * my, 1, digit.Rows);

			using Mat d = new Mat(digit, new Rect(x0, y0, w0, h0)).Clone();

			// Rozmery pre výpočet segmentových oblastí.
			int W = d.Cols;
			int H = d.Rows;

			if (W < 10 || H < 10)
				return null;

			// ========================= 7 segmentov (a,b,c,d,e,f,g) =========================
			// Segmenty definujeme ako obdĺžniky v relatívnych percentách.
			// Toto je typický layout 7-seg:
			//
			//   aaaaa
			//  f     b
			//  f     b
			//   ggggg
			//  e     c
			//  e     c
			//   ddddd
			//
			// Prah ON/OFF:
			// - zmeriame pomer bielych pixelov v oblasti
			// - ak > threshold => segment ON
			//
			// Threshold je kompromis: pri slabých segmentoch znížiť, pri šume zvýšiť.
			// Konfigurovateľné cez UI (Pipeline params → 7-Seg prah) a Settings.json.
			double onThreshold = _settings.SegmentOnThreshold;

			// Pomocná lokálna funkcia – zmeria “koľko bieleho” je v segmente.
			bool SegmentOn(Rect seg)
			{
				// Orezanie segmentu do hraníc.
				if (seg.Width <= 0 || seg.Height <= 0) return false;
				if (seg.X < 0 || seg.Y < 0) return false;
				if (seg.X + seg.Width > W) return false;
				if (seg.Y + seg.Height > H) return false;

				using Mat roi = new Mat(d, seg);

				int white = Cv2.CountNonZero(roi);
				int total = roi.Rows * roi.Cols;

				if (total <= 0) return false;

				double ratio = (double)white / total;
				return ratio >= onThreshold;
			}

			// Hrúbky segmentov definované percentami.
			int tH = (int)(H * 0.18); // výška horizontálnych segmentov
			int tW = (int)(W * 0.20); // šírka vertikálnych segmentov

			// Ochrana – minimálne rozmery
			tH = Math.Max(tH, 3);
			tW = Math.Max(tW, 3);

			// Definície segmentových oblastí (trochu “vnútri”, aby okrajový šum nezapínal segment)
			Rect segA = new Rect((int)(W * 0.20), 0, (int)(W * 0.60), tH);
			Rect segD = new Rect((int)(W * 0.20), H - tH, (int)(W * 0.60), tH);
			Rect segG = new Rect((int)(W * 0.20), (int)(H * 0.45), (int)(W * 0.60), tH);

			Rect segF = new Rect(0, (int)(H * 0.10), tW, (int)(H * 0.35));
			Rect segB = new Rect(W - tW, (int)(H * 0.10), tW, (int)(H * 0.35));

			Rect segE = new Rect(0, (int)(H * 0.55), tW, (int)(H * 0.35));
			Rect segC = new Rect(W - tW, (int)(H * 0.55), tW, (int)(H * 0.35));

			// Zisti ON/OFF pre každý segment.
			bool A = SegmentOn(segA);
			bool B = SegmentOn(segB);
			bool C = SegmentOn(segC);
			bool D = SegmentOn(segD);
			bool E = SegmentOn(segE);
			bool F = SegmentOn(segF);
			bool G = SegmentOn(segG);

			// Zlož bitmasku (každý segment 1 bit).
			// poradie bitov: A B C D E F G
			int mask =
				(A ? 1 << 0 : 0) |
				(B ? 1 << 1 : 0) |
				(C ? 1 << 2 : 0) |
				(D ? 1 << 3 : 0) |
				(E ? 1 << 4 : 0) |
				(F ? 1 << 5 : 0) |
				(G ? 1 << 6 : 0);

			// Mapovanie bitmasky na číslo (štandardné 7-seg).
			// Poznámka: Ak máš displej s iným “segment wiring” (zriedkavé), upravíš mapu.
			// Tu je klasické:
			var map = new Dictionary<int, int>
	{
        // A B C D E F G
        { Bits(A:true,  B:true,  C:true,  D:true,  E:true,  F:true,  G:false), 0 },
		{ Bits(A:false, B:true,  C:true,  D:false, E:false, F:false, G:false), 1 },
		{ Bits(A:true,  B:true,  C:false, D:true,  E:true,  F:false, G:true ), 2 },
		{ Bits(A:true,  B:true,  C:true,  D:true,  E:false, F:false, G:true ), 3 },
		{ Bits(A:false, B:true,  C:true,  D:false, E:false, F:true,  G:true ), 4 },
		{ Bits(A:true,  B:false, C:true,  D:true,  E:false, F:true,  G:true ), 5 },
		{ Bits(A:true,  B:false, C:true,  D:true,  E:true,  F:true,  G:true ), 6 },
		{ Bits(A:true,  B:true,  C:true,  D:false, E:false, F:false, G:false), 7 },
		{ Bits(A:true,  B:true,  C:true,  D:true,  E:true,  F:true,  G:true ), 8 },
		{ Bits(A:true,  B:true,  C:true,  D:true,  E:false, F:true,  G:true ), 9 },
	};

			if (map.TryGetValue(mask, out int digitValue))
				return digitValue;

			// Ak maska nesedí, vrátime null (zlyhanie).
			return null;

			// Lokálna helper funkcia, aby sme nemuseli ručne počítať masky “od oka”.
			// Vráti masku v rovnakom poradí bitov A..G ako vyššie.
			static int Bits(bool A, bool B, bool C, bool D, bool E, bool F, bool G)
			{
				return (A ? 1 << 0 : 0) |
					   (B ? 1 << 1 : 0) |
					   (C ? 1 << 2 : 0) |
					   (D ? 1 << 3 : 0) |
					   (E ? 1 << 4 : 0) |
					   (F ? 1 << 5 : 0) |
					   (G ? 1 << 6 : 0);
			}
		}


		/// <summary>
		/// Validuje, či je možné povoliť tlačidlo START.
		/// START je povolené len vtedy, ak:
		/// - ComboBox obsahuje aspoň jednu položku (IP adresu)
		/// - Niečo je vybrané (SelectedIndex >= 0)
		/// - Stream momentálne nebeží (_running == false)
		/// </summary>
		private void ValidateStartButton()
		{
			// Skontroluje, či ComboBox obsahuje aspoň jednu IP adresu a niečo je vybrané.
			// cboUrl.Items.Count > 0 => list nie je prázdny
			// cboUrl.SelectedIndex >= 0 => niečo je vybrané (nie je -1, čo znamená "nič nevybrané")
			// !_running => stream momentálne nebeží (aby sa nezakázal START počas behu)
			bool canStart = cboUrl.Items.Count > 0 && cboUrl.SelectedIndex >= 0 && !_running;

			// Nastav Enabled property tlačidla START podľa výsledku validácie.
			// Ak canStart == true, tlačidlo bude aktívne (klikateľné).
			// Ak canStart == false, tlačidlo bude zakázané (sivé, neklikateľné).
			btnStart.Enabled = canStart;
		}

		/// <summary>
		/// Pri zatváraní okna chceme korektne zastaviť stream a upratať zdroje
		/// </summary>
		/// <param name="e"></param>
		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			// Zavolá StopCapture, aby zastavil slučku a uvoľnil VideoCapture
			StopCapture();
			// Zavolá základnú implementáciu OnFormClosing
			base.OnFormClosing(e);

			// Log – aplikácia sa zatvára.
			_logger?.Info("Application closing");

			// ========================= STOP MySQL WRITER =========================
			// Background writer môže mať otvorené spojenie a bežiaci task.
			// Pri zatváraní aplikácie ho korektne vypneme.
			try { _mySqlWriter?.Dispose(); } catch { /* ignore */ }
			_mySqlWriter = null;

			// ========================= STOP ONNX DIGIT RECOGNIZER =========================
			try { _onnxRecognizer?.Dispose(); } catch { /* ignore */ }
			_onnxRecognizer = null;

			// Dispose logger – dopíše frontu a zavrie súbor.
			_logger?.Dispose();
			_logger = null;
		}

		/// <summary>
		/// Spoločná logika na zastavenie streamu a upratanie zdrojov
		/// </summary>
		private void StopCapture()
		{
			// Ak stream nebeží, nie je čo robiť
			if (!_running) return;

			_logger.Info("StopCapture called");

			// Pošli Cancel do bežiaceho capture vlákna
			_cts?.Cancel();

			// Reset flagu, že stream už nebeží
			_running = false;

			// Uvoľnenie VideoCapture musí byť thread-safe, aby nedošlo k situácii:
			// CaptureLoop je v _cap.Read(frame) a súčasne StopCapture volá _cap.Dispose() → AccessViolationException.
			lock (_capLock) // <-- ZABRAŇUJE súbehu Read() vs Dispose()
			{
				// Ak _cap existuje, uvoľníme natívne zdroje (FFmpeg/OpenCV handle).
				_cap?.Release(); // <-- korektne “pustí” stream zdroj na natívnej strane
				_cap?.Dispose(); // <-- uvoľní unmanaged pamäť/handly; bez locku vie rozbiť Read()
				_cap = null;     // <-- nastaví na null, aby CaptureLoop vedel, že už nemá z čoho čítať
			}

			// ROI a UP sú zdieľané medzi capture threadom (ProcessFrame) a UI threadom (btnRoi/btnView),
			// preto ich tiež uvoľníme pod _roiLock, aby UI náhodou nezobrazovalo Mat práve počas Dispose().
			lock (_roiLock) // <-- ZABRAŇUJE súbehu ImShow() vs Dispose()
			{
				_lastRoi?.Dispose(); // <-- uvoľní unmanaged pamäť ROI
				_lastRoi = null;     // <-- zruší referenciu, aby bolo jasné, že ROI už neexistuje

				_lastUp?.Dispose();  // <-- uvoľní unmanaged pamäť zväčšeného ROI
				_lastUp = null;      // <-- zruší referenciu, aby VIEW nemal čo zobrazovať

				_lastContrast?.Dispose();
				_lastContrast = null;

				_lastBinary?.Dispose();
				_lastBinary = null;

				_lastCleaned?.Dispose();
				_lastCleaned = null;

				// Uvoľní _lastMatrix (resized verzia cleaned na NxN).
				// Toto je nový Mat pridaný v Kapitole 3 (ChangeMatrix feature).
				_lastMatrix?.Dispose();
				_lastMatrix = null;

				// ========================= UVOĽNENIE LEFT A RIGHT MAT OBJEKTOV =========================
				// Uvoľníme _lastLeft a _lastRight Mat objekty (ľavá a pravá časť cleaned obrazu).
				// Tieto sú zdieľané medzi capture threadom (Pipeline) a UI threadom (picLeft/picRight),
				// preto ich uvoľňujeme pod _roiLock, aby UI náhodou nezobrazovalo Mat práve počas Dispose().

				_lastLeft?.Dispose();   // Uvoľní unmanaged pamäť ľavej časti cleaned obrazu
				_lastLeft = null;       // Zruší referenciu

				_lastRight?.Dispose();  // Uvoľní unmanaged pamäť pravej časti cleaned obrazu
				_lastRight = null;      // Zruší referenciu
			}

			// (Voliteľné, ale odporúčané) Uvoľní CancellationTokenSource – aby sa nehromadili unmanaged zdroje.
			// Ak toto pridáš, pridaj ho hneď po _cts?.Cancel().
			_cts?.Dispose(); // <-- uvoľní CTS (nie je kritické pre pád, ale je to čisté upratanie)
			_cts = null;     // <-- aby sa omylom nepoužila stará CTS

			// UI operácie musia bežať na UI vlákne – použijeme Invoke
			Invoke(new Action(() =>
			{
				// Deaktivuj STOP (stream sa zastavil)
				btnStop.Enabled = false;

				// Ak je v PictureBoxe nejaký obrázok, uvoľni ho
				if (picFrame.Image != null)
				{
					picFrame.Image.Dispose();
					picFrame.Image = null;
				}

				if (picCleaned.Image != null)
				{
					picCleaned.Image.Dispose();
					picCleaned.Image = null;
				}

				// Uvoľní obrázok v picMatrix (resized matica NxN).
				// Nový PictureBox pridaný v Kapitole 3 (ChangeMatrix feature).
				if (picMatrix.Image != null)
				{
					picMatrix.Image.Dispose();
					picMatrix.Image = null;
				}

				// ========================= UVOĽNENIE picLeft A picRight IMAGE =========================
				// Uvoľníme obrázky v picLeft a picRight PictureBoxoch (ľavá a pravá číslica).
				// Nové PictureBox komponenty pridané pre zobrazenie rozdelených číslic.

				// Uvoľní obrázok v picLeft (ľavá číslica).
				if (picLeft.Image != null)
				{
					picLeft.Image.Dispose();
					picLeft.Image = null;
				}

				// Uvoľní obrázok v picRight (pravá číslica).
				if (picRight.Image != null)
				{
					picRight.Image.Dispose();
					picRight.Image = null;
				}

				// Validuj START tlačidlo – povolí ho len ak je niečo vybrané v ComboBoxe.
				// Toto zabezpečí správne správanie – ak je ComboBox prázdny, START ostane zakázaný.
				// Ak je vyplnený, START sa povolí (umožní znova spustiť stream).
				ValidateStartButton();
			}));

			_logger.Info("StopCapture finished – resources released");
		}


		/// <summary>
		/// Bezpečne obnoví VideoCapture objekt, keď stream prestane dodávať snímky.
		/// Volá sa z CaptureLoop (watchdog).
		/// </summary>
		/// <param name="url">URL streamu, ktorú chceme znovu otvoriť</param>
		private void TryReconnectCapture(string url)
		{
			// ========================= VALIDÁCIA VSTUPU =========================
			// Ak by URL bola prázdna, nemá zmysel robiť reconnect.
			if (string.IsNullOrWhiteSpace(url))
			{
				_logger.Error("TryReconnectCapture(): URL is empty - cannot reconnect.");
				return;
			}

			// Reconnect musí byť pod _capLock, aby sa nestalo, že v rovnakom čase:
			//  - CaptureLoop volá _cap.Read()
			//  - používateľ klikne STOP (StopCapture robí Dispose)
			//  - watchdog robí Dispose a vytvára nový _cap
			lock (_capLock)
			{
				try
				{
					// ========================= UVOĽNENIE STARÉHO CAPTURE =========================
					// Starý _cap môže byť v "divnom" stave (napr. stream vypadol).
					// Preto ho vždy korektne uvoľníme (Release + Dispose).
					if (_cap != null)
					{
						try { _cap.Release(); } catch { /* ignoruj */ }
						try { _cap.Dispose(); } catch { /* ignoruj */ }
						_cap = null;
					}

					// ========================= OTVORENIE NOVÉHO CAPTURE =========================
					// Vytvoríme nový VideoCapture nad rovnakou URL.
					_cap = new VideoCapture(url);

					bool opened = _cap.IsOpened();
					_logger.Info($"TryReconnectCapture(): reconnect done. IsOpened={opened}, url={url}");

					// Poznámka:
					// Ak opened == false, CaptureLoop bude ďalej bežať, a watchdog skúsi reconnect znova neskôr.
				}
				catch (Exception ex)
				{
					_logger.Error($"TryReconnectCapture(): reconnect failed. {ex}");
				}
			}
		}


		/// <summary>
		/// Hlavná slučka, ktorá číta stream z kamery a spracováva jednotlivé snímky
		/// </summary>
		/// <param name="token"></param>
		private void CaptureLoop(CancellationToken token)
		{
			try
			{
				_logger.Info("CaptureLoop method invoke");

				// Prečíta vybranú URL adresu z ComboBoxu (cboUrl) thread-safe spôsobom.
				// Keďže CaptureLoop beží na background threade, musíme použiť Invoke pre prístup k UI kontrolke.
				// Invoke zabezpečí, že čítanie prebehne na UI threade (bezpečné pre Windows Forms).
				string selectedUrl = (string)Invoke(new Func<string>(() =>
				{
					// Vráti vybranú položku z ComboBoxu ako string.
					// SelectedItem obsahuje IP adresu (napr. "http://192.168.50.96:81/stream").
					// Ak by nebolo nič vybrané, SelectedItem by bolo null – ale to je ošetrené validáciou (START je zakázaný).
					return cboUrl.SelectedItem?.ToString() ?? "";
				}));

				// Vytvorí VideoCapture s URL adresou vybranou z ComboBoxu.
				// selectedUrl obsahuje IP adresu stream servera (ESP32-CAM).
				_cap = new VideoCapture(selectedUrl);

				// Loguje, ktorú stream URL sa pokúša otvoriť (užitočné pre debugging pripojenia).
				_logger.Info($"Opening stream: {selectedUrl}");

				// Overí, či sa stream podarilo otvoriť
				if (!_cap.IsOpened())
				{
					// Ak nie, zobrazí správu a zastaví capture
					_logger.Error("Failed to open stream (VideoCapture.IsOpened() == false)");
					MessageBox.Show("Nepodarilo sa otvoriť stream.");
					StopCapture();
					return;
				}

				// Vytvorí Mat pre aktuálny snímok (frame)
				using var frame = new Mat();

				// ========================= WATCHDOG PRE STREAM (AUTO-RECONNECT) =========================
				// Tento blok rieši situáciu, keď IP/MJPEG stream (napr. ESP32-CAM) po dlhšom čase prestane dodávať dáta.
				// Typické správanie:
				//  - VideoCapture.Read() začne vracať false
				//  - alebo vracia prázdny Mat (frame.Empty() == true)
				// Bez watchdogu by sme robili len "continue" a aplikácia by sa mohla tváriť, že je "zaseknutá".
				//
				// lastGoodFrameUtc:
				//  - čas posledného VALIDNÉHO frame (ok == true && !frame.Empty()).
				// consecutiveReadFailures:
				//  - počet zlyhaní čítania po sebe (Read() == false alebo frame.Empty()).
				//
				// noFrameTimeout:
				//  - ak nepríde validný frame dlhšie než tento čas, spravíme reconnect.
				// failureThreshold:
				//  - ak je príliš veľa failov po sebe, spravíme reconnect aj bez čakania na timeout.
				// failedReadSleepMs:
				//  - krátka pauza pri failoch, aby sa nepálilo CPU v rýchlom fail-loop-e.
				DateTime lastGoodFrameUtc = DateTime.UtcNow;
				int consecutiveReadFailures = 0;
				int totalReconnectAttempts = 0;
				const int maxReconnectAttempts = 50;
				TimeSpan noFrameTimeout = TimeSpan.FromSeconds(5);
				int failureThreshold = 50;
				int failedReadSleepMs = 10;


				// Nekonečná slučka – beží, kým nedostane Cancel
				while (!token.IsCancellationRequested)
				{
					// Premenná, do ktorej si uložíme výsledok čítania (true = frame načítaný).
					bool ok; // <-- používame lokálnu premennú, aby sme po locku nemuseli znovu siahať na _cap

					// Čítanie z _cap musí byť pod _capLock, aby StopCapture nemohol spraviť Dispose()
					// presne v momente, keď prebieha Read() – to je zdroj AccessViolationException.
					//
					// ROZŠÍRENIE (WATCHDOG):
					// Okrem thread-safety tu riešime aj stav, keď stream prestane dodávať snímky.
					// Vtedy Read() vracia false alebo frame.Empty() a bez watchdogu by sme len donekonečna "continue".
					lock (_capLock) // <-- ZABRAŇUJE súbehu Read() vs Dispose()
					{
						// Ak už bol _cap medzičasom zastavený (StopCapture nastavil _cap = null),
						// nemáme čo čítať – bezpečne ukončíme slučku.
						if (_cap == null)
						{
							ok = false;
						}
						else if (!_cap.IsOpened())
						{
							// Stream nie je otvorený – vrátime ok=false a necháme watchdog rozhodnúť, čo ďalej.
							ok = false;
						}
						else
						{
							// Reálne načítanie ďalšieho frame z natívneho streamu.
							ok = _cap.Read(frame); // <-- natívna operácia; musí byť chránená lockom
						}
					}

					// ========================= OŠETRENIE CHYBNÉHO / PRÁZDNEHO FRAME =========================
					// Pri IP/MJPEG streamoch (ESP32-CAM) sa občas stáva, že Read() začne vracať false alebo prázdny frame.
					// Ak by sme len "continue" bez pauzy, môžeme:
					//  - zbytočne zaťažiť CPU (rýchla slučka)
					//  - a hlavne nikdy neobnoviť stream (aplikácia vyzerá, že "zamrzla").
					if (!ok || frame.Empty())
					{
						consecutiveReadFailures++;

						// Krátka pauza, aby pri výpadku streamu aplikácia nepálila CPU.
						Thread.Sleep(failedReadSleepMs);

						// Ako dlho sme bez validného frame?
						TimeSpan sinceLastGood = DateTime.UtcNow - lastGoodFrameUtc;

						// WATCHDOG TRIGGER:
						// 1) veľa failov po sebe
						// 2) alebo dlhý čas bez validného frame
						if (consecutiveReadFailures >= failureThreshold || sinceLastGood > noFrameTimeout)
						{
							totalReconnectAttempts++;

							if (totalReconnectAttempts > maxReconnectAttempts)
							{
								_logger.Error(
									$"Stream watchdog: max reconnect attempts ({maxReconnectAttempts}) reached. Stopping capture.");
								StopCapture();
								return;
							}

							// Exponenciálny backoff: čakanie pred reconnectom (max 30s).
							int backoffMs = Math.Min(1000 * totalReconnectAttempts, 30000);

							_logger.Warn(
								$"Stream watchdog triggered: failures={consecutiveReadFailures}, " +
								$"sinceLastGood={sinceLastGood.TotalSeconds:0.0}s, " +
								$"attempt={totalReconnectAttempts}/{maxReconnectAttempts}. " +
								$"Reconnecting after {backoffMs}ms backoff...");

							Thread.Sleep(backoffMs);

							// Reconnect: bezpečne zrušíme starý VideoCapture a otvoríme nový s rovnakou URL.
							TryReconnectCapture(selectedUrl);

							// Reset watchdog stavu po reconnecte (aby sme sa nehneď neodpálili znova).
							consecutiveReadFailures = 0;
							lastGoodFrameUtc = DateTime.UtcNow;
						}

						continue;
					}

					// ========================= VALIDNÝ FRAME – RESET WATCHDOG =========================
					consecutiveReadFailures = 0;
					totalReconnectAttempts = 0; // Úspešný frame = reset reconnect počítadla
					lastGoodFrameUtc = DateTime.UtcNow;


					// Spracuje frame – vypočíta ROI, uloží _lastRoi a vráti display s nakresleným ROI obdĺžnikom.
					using Mat display = ProcessFrame(frame);

					// HNEĎ POTOM vytvorí/aktualizuje _lastUp zo snapshotu _lastRoi.
					// Up() je thread-safe a používa Clone + lock, takže je bezpečné aj pri bežiacom streame.
					Up();

					// 3) ========================= NOVÉ: spusti CLAHE + threshold + cleaning pipeline =========================
					// Pipeline číta _lastUp, spraví spracovanie a uloží výsledok do _lastCleaned.
					Pipeline();

					// Premeň display na Bitmap pre GUI (ľavý obraz – frame s ROI)
					using Bitmap bmpFrame = BitmapConverter.ToBitmap(display);

					// Zober kópiu cleaned a sprav z toho Bitmap
					Bitmap bmpCleaned = null;

					// Zoberieme snapshot _lastCleaned pod lockom (clone), aby sme nikdy nekonvertovali zdieľaný Mat.
					Mat cleanedCopy = null;

					lock (_roiLock)
					{
						if (_lastCleaned != null && !_lastCleaned.Empty())
						{
							cleanedCopy = _lastCleaned.Clone();
						}
					}

					// Ak máme cleanedCopy, prevedieme ho na Bitmap mimo locku.
					if (cleanedCopy != null)
					{
						try
						{
							bmpCleaned = BitmapConverter.ToBitmap(cleanedCopy);
						}
						finally
						{
							cleanedCopy.Dispose();
						}
					}

					// ========================= NOVÉ: Zober kópiu matrix a sprav z toho Bitmap =========================
					// Rovnaký pattern ako pre cleaned – thread-safe prístup cez Clone.
					Bitmap bmpMatrix = null;

					// Zoberieme snapshot _lastMatrix pod lockom (clone), aby sme nikdy nekonvertovali zdieľaný Mat.
					Mat matrixCopy = null;

					lock (_roiLock)
					{
						// Ak _lastMatrix existuje a nie je prázdny, spravíme kópiu.
						if (_lastMatrix != null && !_lastMatrix.Empty())
						{
							matrixCopy = _lastMatrix.Clone();
						}
					}

					// Ak máme matrixCopy, prevedieme ho na Bitmap mimo locku.
					if (matrixCopy != null)
					{
						try
						{
							// BitmapConverter.ToBitmap() konvertuje Mat → Bitmap (GDI+).
							// Matrix je malý (napr. 32x32), ale PictureBox ho zväčší cez Zoom mode.
							bmpMatrix = BitmapConverter.ToBitmap(matrixCopy);
						}
						finally
						{
							// Uvoľníme lokálnu kópiu matrixCopy (už ju nepotrebujeme).
							matrixCopy.Dispose();
						}
					}

					// ========================= NOVÉ: Zober kópie Left a Right a sprav z toho Bitmap =========================
					// Thread-safe prístup k _lastLeft a _lastRight pomocou Clone pod _roiLock.
					// Vytvoríme bitmapy pre picLeft a picRight PictureBox komponenty.

					Bitmap bmpLeft = null;
					Bitmap bmpRight = null;

					// Zoberieme snapshot _lastLeft a _lastRight pod lockom (clone)
					Mat leftCopy = null;
					Mat rightCopy = null;

					lock (_roiLock)
					{
						// Ak _lastLeft existuje a nie je prázdny, spravíme kópiu.
						if (_lastLeft != null && !_lastLeft.Empty())
						{
							leftCopy = _lastLeft.Clone();
						}

						// Ak _lastRight existuje a nie je prázdny, spravíme kópiu.
						if (_lastRight != null && !_lastRight.Empty())
						{
							rightCopy = _lastRight.Clone();
						}
					}

					// Ak máme leftCopy, prevedieme ho na Bitmap mimo locku.
					if (leftCopy != null)
					{
						try
						{
							// BitmapConverter.ToBitmap() konvertuje Mat → Bitmap (GDI+).
							// leftCopy obsahuje ľavú časť cleaned obrazu (prvá číslica).
							bmpLeft = BitmapConverter.ToBitmap(leftCopy);
						}
						finally
						{
							// Uvoľníme lokálnu kópiu leftCopy.
							leftCopy.Dispose();
						}
					}

					// Ak máme rightCopy, prevedieme ho na Bitmap mimo locku.
					if (rightCopy != null)
					{
						try
						{
							// BitmapConverter.ToBitmap() konvertuje Mat → Bitmap (GDI+).
							// rightCopy obsahuje pravú časť cleaned obrazu (druhá číslica).
							bmpRight = BitmapConverter.ToBitmap(rightCopy);
						}
						finally
						{
							// Uvoľníme lokálnu kópiu rightCopy.
							rightCopy.Dispose();
						}
					}

					// UI update cez Invoke.
					Invoke(new Action(() =>
					{
						// ====== picFrame ======
						picFrame.Image?.Dispose();
						picFrame.Image = (Bitmap)bmpFrame.Clone();

						// ====== picCleaned ======
						// Ak sme dostali cleaned bitmapu, zobrazíme ju.
						// Ak nie, necháme pôvodný obraz (alebo ho môžeš nulovať).
						if (bmpCleaned != null)
						{
							picCleaned.Image?.Dispose();
							picCleaned.Image = bmpCleaned; // tu už NEclone, bmpCleaned vlastníme my
						}

						// ====== picMatrix (NOVÉ) ======
						// Ak sme dostali matrix bitmapu (resized 16x16, 32x32, 64x64, 128x128), zobrazíme ju.
						// PictureBox má SizeMode = Zoom, takže malá matica sa zväčší na celý PictureBox.
						if (bmpMatrix != null)
						{
							// Uvoľníme starý obrázok v picMatrix (ak existuje).
							picMatrix.Image?.Dispose();
							// Nastavíme nový obrázok (bmpMatrix vlastníme my, NEclone).
							picMatrix.Image = bmpMatrix;
						}

						// ====== picLeft (NOVÉ - Left digit) ======
						// Ak sme dostali bmpLeft bitmapu (ľavá časť cleaned obrazu), zobrazíme ju.
						// bmpLeft obsahuje prvú číslicu (ľavú časť cleaned obrazu podľa fromLeft parametra).
						if (bmpLeft != null)
						{
							// Uvoľníme starý obrázok v picLeft (ak existuje).
							picLeft.Image?.Dispose();
							// Nastavíme nový obrázok (bmpLeft vlastníme my, NEclone).
							picLeft.Image = bmpLeft;
						}

						// ====== picRight (NOVÉ - Right digit) ======
						// Ak sme dostali bmpRight bitmapu (pravá časť cleaned obrazu), zobrazíme ju.
						// bmpRight obsahuje druhú číslicu (pravú časť cleaned obrazu podľa fromRight parametra).
						if (bmpRight != null)
						{
							// Uvoľníme starý obrázok v picRight (ak existuje).
							picRight.Image?.Dispose();
							// Nastavíme nový obrázok (bmpRight vlastníme my, NEclone).
							picRight.Image = bmpRight;
						}
					}));

					// Uvoľní Mat `display` (už ho nepotrebujeme)
					display.Dispose();
				}
			}
			catch (Exception ex)
			{
				// log
				_logger.Error("EXCEPTION: " + ex);
				// Ak nastane chyba, zobrazí dialóg s textom výnimky
				MessageBox.Show(ex.Message);
			}
			finally
			{
				// Pri akomkoľvek ukončení slučky upraceme zdroje
				StopCapture();
			}
		}

		/// <summary>
		/// Vráti frame s nakresleným ROI obdĺžnikom pre zobrazenie v PictureBoxe.
		/// </summary>
		private Mat ProcessFrame(Mat frame)
		{
			// =========================
			// 1) ZÁKLADNÉ OVERENIA VSTUPU
			// =========================

			// Ak by niekto omylom zavolal ProcessFrame s null referenciou, skončíme hneď,
			// pretože prístup na frame.Width / frame.Height by hodil NullReferenceException.
			// (Mat je referenčný typ v C#, takže null je možné.)
			if (frame == null)
			{
				// Vrátime prázdny Mat ako bezpečný fallback (aby volajúci nemusel riešiť null).
				return new Mat();
			}

			// Ak je frame prázdny (nemá dáta), nemá zmysel počítať ROI ani kresliť obdĺžnik.
			// Frame.Empty() znamená, že Mat nemá platnú pixelovú pamäť.
			if (frame.Empty())
			{
				// Vrátime klon (alebo prázdny Mat) – klon tu bude tiež prázdny, ale bezpečný.
				// Môžeš dať aj return new Mat(); podľa toho, čo preferuješ.
				return frame.Clone();
			}

			// =========================
			// 2) ROZMERY FRAME
			// =========================

			// Zistí šírku aktuálneho frame v pixeloch (počet stĺpcov).
			int w = frame.Width;

			// Zistí výšku aktuálneho frame v pixeloch (počet riadkov).
			int h = frame.Height;

			// Ochrana: ak by z nejakého dôvodu šírka alebo výška vyšla nulová alebo záporná,
			// ROI sa nedá vypočítať a operácie ako Rectangle/Resize by mohli spadnúť.
			if (w <= 0 || h <= 0)
			{
				// Vrátime klon frame (alebo prázdny Mat) – bezpečné ukončenie.
				return frame.Clone();
			}

			// =========================
			// 3) NAČÍTANIE ROI PARAMETROV Z UI (PERCENTÁ)
			// =========================

			// Prečíta X v percentách z NumericUpDown (0–100) a prevedie na 0.0–1.0.
			// Napr. 25% -> 0.25.
			double px = (double)nudX.Value / 100.0;

			// Prečíta Y v percentách z NumericUpDown (0–100) a prevedie na 0.0–1.0.
			double py = (double)nudY.Value / 100.0;

			// Prečíta šírku ROI v percentách (0–100) a prevedie na 0.0–1.0.
			double pw = (double)nudWidth.Value / 100.0;

			// Prečíta výšku ROI v percentách (0–100) a prevedie na 0.0–1.0.
			double ph = (double)nudHeight.Value / 100.0;

			// Ochrana: percentá by v ideálnom prípade mali byť v rozsahu 0..1.
			// Aj keď NumericUpDown typicky nedovolí mimo rozsah, radšej to ešte “clampneme”,
			// aby to bolo nepriestrelné aj pri zlej konfigurácii kontroliek.
			if (px < 0) px = 0; if (px > 1) px = 1;
			if (py < 0) py = 0; if (py > 1) py = 1;
			if (pw < 0) pw = 0; if (pw > 1) pw = 1;
			if (ph < 0) ph = 0; if (ph > 1) ph = 1;

			// =========================
			// 4) PREPOČET ROI Z PERCENT NA PIXELY
			// =========================

			// Vypočíta ľavú súradnicu X v pixeloch (percentá * šírka).
			// (int) robí orezanie desatinnej časti (floor smerom k nule).
			int x = (int)(w * px);

			// Vypočíta hornú súradnicu Y v pixeloch (percentá * výška).
			int y = (int)(h * py);

			// Vypočíta šírku ROI v pixeloch (percentá * šírka).
			// Pozor: pri malých percentách môže po pretypovaní vyjsť 0.
			int rw = (int)(w * pw);

			// Vypočíta výšku ROI v pixeloch (percentá * výška).
			// Pozor: pri malých percentách môže po pretypovaní vyjsť 0.
			int rh = (int)(h * ph);

			// =========================
			// 5) OCHRANY PROTI PRETEČENIU ROI MIMO FRAME + OCHRANY PROTI 0 ROZMEROM
			// =========================

			// Ochrana: ak by X vyšlo záporné, nastavíme ho na 0 (ľavý okraj frame).
			if (x < 0) x = 0;

			// Ochrana: ak by Y vyšlo záporné, nastavíme ho na 0 (horný okraj frame).
			if (y < 0) y = 0;

			// Ochrana: ak x vyšlo už mimo šírky, tak ROI nemá zmysel (za pravým okrajom).
			// Toto je dôležité napr. ak px=1.0 -> x = w, a potom w - x = 0.
			if (x >= w)
			{
				// Vrátime len display bez ROI – bezpečný výstup, nič nepočítame.
				return frame.Clone();
			}

			// Ochrana: ak y vyšlo už mimo výšky, tak ROI nemá zmysel (za spodným okrajom).
			if (y >= h)
			{
				// Vrátime len display bez ROI – bezpečný výstup.
				return frame.Clone();
			}

			// Ochrana: ak ROI pretiekol za pravý okraj, upravíme šírku tak, aby sa zmestil.
			// Ak by vyšlo rw záporné alebo nula po úprave, ošetríme nižšie.
			if (x + rw > w) rw = w - x;

			// Ochrana: ak ROI pretiekol za spodný okraj, upravíme výšku tak, aby sa zmestil.
			if (y + rh > h) rh = h - y;

			// Kritická ochrana: šírka ROI musí byť aspoň 1 pixel, inak je Rect neplatný.
			// Bez tohto ti bude padať new Mat(frame, roiRect), Resize alebo ImShow (empty/invalid).
			if (rw <= 0)
			{
				// Vrátime len display bez ROI – v praxi to znamená “ROI je príliš malé / nulové”.
				return frame.Clone();
			}

			// Kritická ochrana: výška ROI musí byť aspoň 1 pixel, inak je Rect neplatný.
			if (rh <= 0)
			{
				// Vrátime len display bez ROI – bezpečné ukončenie.
				return frame.Clone();
			}

			// Vytvorí OpenCV Rect definovaný vypočítanými súradnicami a rozmermi.
			// Rect(x,y,width,height) musí mať width>0 a height>0 a musí byť v rámci frame,
			// čo sme vyššie ošetrili.
			var roiRect = new Rect(x, y, rw, rh);


			// =========================
			// 7) THREAD-SAFE ULOŽENIE ROI A UPSCALED ROI (kľúčové pre tvoje "dims" chyby)
			// =========================

			// Uzamkneme zámok, aby UI thread nemohol čítať _lastRoi/_lastUp práve v momente,
			// keď ich tu Dispose-neme alebo prepíšeme. Toto je kritické, aby sa zabránilo
			// race-condition a výnimkám typu: 0 <= _dims && _dims <= CV_MAX_DIM.
			lock (_roiLock)
			{
				// Ak existoval starý ROI Mat, uvoľníme jeho unmanaged pamäť,
				// aby sme nezvyšovali spotrebu RAM a nespôsobili memory leak.
				_lastRoi?.Dispose();

				// Vytvoríme nový Mat ako výrez z frame podľa roiRect.
				// new Mat(frame, roiRect) je SubMat (pohľad do frame), preto hneď Clone(),
				// aby _lastRoi malo vlastnú pamäť a nebolo závislé od životnosti frame.
				_lastRoi = new Mat(frame, roiRect).Clone();
			}

			// =========================
			// 8) DISPLAY KÓPIA PRE GUI + KRESLENIE ROI
			// =========================

			// Vytvoríme kópiu celého frame, ktorú použijeme len na zobrazenie v PictureBoxe,
			// aby sme nemenili originálny frame (ak by si ho chcel ešte niekde ďalej použiť).
			Mat display = frame.Clone();

			// Nakreslíme zelený obdĺžnik, ktorý ukazuje aktuálny ROI:
			// - kreslíme do display (nie do frame), lebo display je určený na zobrazenie
			// - farba je zelená v BGR formáte (0,255,0)
			// - hrúbka čiary je 2 pixely
			Cv2.Rectangle(display, roiRect, new Scalar(0, 255, 0), 2);

			// Vrátime display (frame s nakresleným ROI) volajúcemu – typicky sa zmení na Bitmap a zobrazí v GUI.
			return display;
		}

		/// <summary>
		/// Vytvorí a uloží "up" (zväčšený ROI) do _lastUp.
		/// Metóda je thread-safe:
		/// - ROI si zoberie ako lokálnu kópiu (Clone) pod zámkom,
		/// - Resize spraví mimo zámku,
		/// - výsledok uloží späť do _lastUp pod zámkom.
		/// </summary>
		private void Up()
		{
			// Lokálna kópia ROI (snapshot), s ktorou budeme pracovať mimo locku.
			// Používame ju preto, aby sa nám ROI počas výpočtu Resize nezmenilo/neudisposovalo.
			Mat roiCopy = null;

			// Lokálny Mat pre výsledok Resize (zväčšený ROI).
			// Vytvoríme ho až po kontrole ROI a scale.
			Mat upTemp = null;

			// Bezpečne načítame scale z NumericUpDown (UI control) aj z background threadu.
			// Toto zabráni cross-thread problémom.
			double scale = (double)GetNudValueSafe(nudResize);

			// Ochrana: scale musí byť kladné číslo, inak Resize nedáva zmysel (0 alebo záporné by mohlo spadnúť).
			if (scale <= 0)
			{
				// Ak by bolo scale neplatné, nastavíme konzervatívne 1 (žiadne zväčšenie).
				scale = 1;
			}

			// Uzamkneme ROI zámok, aby _lastRoi nebol práve vymenený/Dispose-nutý v ProcessFrame.
			lock (_roiLock)
			{
				// Ak ROI ešte neexistuje, Up nemá z čoho vytvoriť zväčšený obraz – bezpečne skončíme.
				if (_lastRoi == null)
					return;

				// Ak ROI existuje, ale je prázdny Mat, Resize by padol na empty src – skončíme.
				if (_lastRoi.Empty())
					return;

				// Vytvoríme stabilnú kópiu ROI (Clone), aby sa dalo bezpečne robiť Resize mimo locku.
				roiCopy = _lastRoi.Clone();
			} // lock končí – odteraz už nepracujeme so zdieľaným _lastRoi, ale s lokálnym roiCopy.

			try
			{
				// Ďalšia ochrana: ak by sa roiCopy nepodarilo vytvoriť alebo bol prázdny, nič nerobíme.
				if (roiCopy == null || roiCopy.Empty())
					return;

				// Vytvoríme dočasný Mat pre výsledok Resize.
				upTemp = new Mat();

				// Resize ROI do upTemp:
				// - new Size() prázdny znamená: cieľová veľkosť sa vypočíta z fx/fy (scale).
				// - Cubic interpolácia je kvalitná (užitočné pre OCR), ale je výpočtovo náročnejšia.
				Cv2.Resize(roiCopy, upTemp, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Cubic);

				// Ochrana: ak by Resize vyrobil prázdny výsledok (nemalo by sa stať, ale defensívne),
				// nebudeme ho ukladať do _lastUp.
				if (upTemp.Empty())
					return;

				// Teraz bezpečne uložíme výsledok do zdieľaného _lastUp pod zámkom.
				lock (_roiLock)
				{
					// Uvoľníme starý _lastUp (prevencia memory leak).
					_lastUp?.Dispose();

					// Uložíme nový výsledok; keďže upTemp je náš lokálny Mat, môžeme ho priamo “odovzdať”.
					// Pozor: po tomto priradení už upTemp NEdisposujeme v finally, aby sme nezrušili _lastUp.
					_lastUp = upTemp;

					// Nastavíme upTemp na null, aby ho finally blok náhodou neDispose-nul.
					upTemp = null;
				}
			}
			finally
			{
				// Uvoľníme lokálnu kópiu ROI – už ju nepotrebujeme.
				roiCopy?.Dispose();

				// Ak upTemp nebol uložený do _lastUp (t.j. ostal lokálny), uvoľníme ho tu,
				// aby nevznikal memory leak pri výnimočných stavoch / early return.
				upTemp?.Dispose();
			}
		}

		/// <summary>
		/// ========================= CLAHE + threshold + cleaning pipeline (KAPITOLA 4 - OPTIMALIZOVANÝ) =========================
		/// Pipeline:
		///   1) vezme _lastUp (zväčšený ROI) ako snapshot,
		///   2) spraví grayscale + bilateral (s parametrami z UI/Settings),
		///   3) CLAHE => contrast (s parametrami z UI/Settings),
		///   5) AdaptiveThreshold (BinaryInv) => binary (priamo z contrast, bez Gaussian Blur),
		///   6) Morphology Open + Close (s parametrami z UI/Settings),
		///   7) ConnectedComponentsWithStats a filtrovanie podľa plochy => cleaned,
		///   8)  7-SEGMENT DEKÓDOVANIE,
		///   9) uloží _lastContrast, _lastBinary, _lastCleaned, _lastMatrix (thread-safe).
		///   10) ChangeMatrix - resize cleaned na NxN,
		/// </summary>
		private void Pipeline()
		{
			// Lokálna kópia "up" (snapshot), aby sa počas spracovania nezmenil/neudisposoval.
			Mat upCopy = null;

			// Zober snapshot _lastUp pod lockom.
			lock (_roiLock)
			{
				// Ak up ešte nie je k dispozícii, pipeline nemá čo robiť.
				if (_lastUp == null)
					return;

				// Ak je up prázdny, skončíme (inak by padli OpenCV operácie).
				if (_lastUp.Empty())
					return;

				// Clone = vlastná pamäť pre bezpečné spracovanie mimo locku.
				upCopy = _lastUp.Clone();
			}

			// Ak sa nepodarilo spraviť kópiu, skončíme.
			if (upCopy == null || upCopy.Empty())
			{
				upCopy?.Dispose();
				return;
			}

			// Dočasné Mat-y pre jednotlivé kroky pipeline.
			// Všetky sú lokálne (nie zdieľané), takže tu netreba lock.
			Mat gray = null;
			Mat smooth = null;
			Mat contrast = null;
			Mat binary = null;
			Mat cleaned = null;

			try
			{
				// ========================= 1) GRAY CONVERSION =========================
				// Prevedenie na 1-kanálový grayscale obraz (z BGR formátu ESP32-CAM).
				gray = new Mat();
				Cv2.CvtColor(upCopy, gray, ColorConversionCodes.BGR2GRAY);

				// ========================= 2) BILATERAL FILTER (KAPITOLA 4 - OPTIMALIZOVANÉ) =========================
				// Bilateral filter – edge-preserving smoothing (zníži šum, ale nechá hrany číslic).
				// Parametre načítané z UI (thread-safe cez GetNudValueSafe).
				int bilateralD = (int)GetNudValueSafe(nudBilateralD); // Diameter filtra (default: 9)
				int bilateralSigmaColor = (int)GetNudValueSafe(nudBilateralSigmaColor); // SigmaColor (default: 75, optimalizované)
				int bilateralSigmaSpace = (int)GetNudValueSafe(nudBilateralSigmaSpace); // SigmaSpace (default: 75, optimalizované)

				smooth = new Mat();
				Cv2.BilateralFilter(gray, smooth,
					d: bilateralD,                // Diameter filtra (bolo: 9 hardcoded)
					sigmaColor: bilateralSigmaColor,  // SigmaColor (bolo: 50, teraz: 75 optimalizované pre číslice)
					sigmaSpace: bilateralSigmaSpace   // SigmaSpace (bolo: 50, teraz: 75 optimalizované pre číslice)
				);

				// ========================= 3) CLAHE (KAPITOLA 4 - OPTIMALIZOVANÉ) =========================
				// CLAHE – Contrast Limited Adaptive Histogram Equalization (lokálne zvýšenie kontrastu).
				// Parametre načítané z UI (thread-safe).
				double claheClipLimit = (double)GetNudValueSafe(nudCLAHEClipLimit); // ClipLimit (default: 3.0, optimalizované)
				int claheTileX = (int)GetNudValueSafe(nudCLAHETileGridSizeX); // TileGridSize X (default: 4, optimalizované)
				int claheTileY = (int)GetNudValueSafe(nudCLAHETileGridSizeY); // TileGridSize Y (default: 4, optimalizované)

				using var clahe = Cv2.CreateCLAHE(
					clipLimit: claheClipLimit,                          // ClipLimit (bolo: 2.0, teraz: 3.0 optimalizované)
					tileGridSize: new OpenCvSharp.Size(claheTileX, claheTileY)  // TileGridSize (bolo: 8×8, teraz: 4×4 optimalizované pre malé číslice)
				);
				contrast = new Mat();
				clahe.Apply(smooth, contrast);

				// ========================= 5) ADAPTIVE THRESHOLD (KAPITOLA 4 - VSTUP ZMENENÝ) =========================
				// Adaptive threshold – binarizácia obrazu (pozadie biele, čísla čierne = BinaryInv).
				// blockSize a C parametre načítané z UI (thread-safe).
				int blockSize = (int)GetNudValueSafe(nudBlockSize); // blockSize z UI (default: 15, optimalizované pre malé číslice)

				// blockSize MUSÍ byť nepárne a >= 3, inak AdaptiveThreshold padne/nebude fungovať správne.
				if (blockSize < 3) blockSize = 3;
				if (blockSize % 2 == 0) blockSize += 1; // Ak je párne, pridaj 1 (napr. 14 -> 15)

				// C z UI (decimal -> double) - konštanta odčítaná od adaptívneho prahu
				double c = (double)GetNudValueSafe(nudC); // C z UI (default: 5.0)

				binary = new Mat();
				Cv2.AdaptiveThreshold(
					contrast,  // ZMENENÉ (KAPITOLA 4): vstup je contrast (výstup z CLAHE), NIE smoothGaussian (ktorý bol odstránený)
					binary,    // Výstup: binárny obraz (0 alebo 255)
					maxValue: 255,
					adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
					thresholdType: ThresholdTypes.BinaryInv, // BinaryInv = pozadie biele (255), číslice čierne (0)
					blockSize: blockSize, // blockSize z UI (bolo: 41, teraz: 15 optimalizované)
					c: c                  // C z UI (default: 5.0)
				);

				// ========================= 6) MORPHOLOGY (KAPITOLA 4 - OPTIMALIZOVANÉ) =========================
				// Morphology operácie – odstráni bodky (Open) + vyplní malé diery (Close).
				// Parametre načítané z UI (thread-safe).
				int morphologyKernelSize = (int)GetNudValueSafe(nudMorphologyKernelSize); // KernelSize (default: 3)
				int morphologyOpenIterations = (int)GetNudValueSafe(nudMorphologyOpenIterations); // Open iterations (default: 1)
				int morphologyCloseIterations = (int)GetNudValueSafe(nudMorphologyCloseIterations); // Close iterations (default: 1)

				// Vytvorenie kernelu (štruktúrujúci element) pre morphology operácie
				using Mat kernel = Cv2.GetStructuringElement(
					MorphShapes.Rect,                                              // Obdĺžnikový kernel (štandardný)
					new OpenCvSharp.Size(morphologyKernelSize, morphologyKernelSize)  // Veľkosť kernelu (bolo: 3×3 hardcoded, teraz z UI)
				);

				// Open operácia – odstráni malé bodky/šum (erózia -> dilatácia)
				Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel,
					iterations: morphologyOpenIterations); // Počet iterácií z UI (bolo: 1 hardcoded)

				// Close operácia – vyplní malé diery v čísliciach (dilatácia -> erózia)
				Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel,
					iterations: morphologyCloseIterations); // Počet iterácií z UI (bolo: 1 hardcoded)

				// ========================= 7) Connected components – necháme len veľké objekty (číslice), malé smeti zahodíme =========================.
				Mat labels = new Mat();
				Mat stats = new Mat();
				Mat centroids = new Mat();

				try
				{
					int numLabels = Cv2.ConnectedComponentsWithStats(
						binary, labels, stats, centroids, PixelConnectivity.Connectivity8);

					// cleaned = čierny obraz.
					cleaned = Mat.Zeros(binary.Size(), MatType.CV_8UC1);

					// Index stĺpca pre area v stats tabuľke.
					int statArea = (int)ConnectedComponentsTypes.Area;

					// Minimálna plocha – filter smetí.
					int minArea = (int)GetNudValueSafe(nudMinArea);
					if (minArea < 0) minArea = 0;

					for (int i = 1; i < numLabels; i++)
					{
						int area = stats.At<int>(i, statArea);

						// Malé komponenty zahodíme.
						if (area < minArea)
							continue;

						// Vytvor masku pre konkrétny label.
						using Mat mask = new Mat();
						Cv2.Compare(labels, i, mask, CmpType.EQ);

						// Prekresli na bielo.
						cleaned.SetTo(255, mask);
					}
				}
				finally
				{
					// Uvoľni dočasné Mat-y z connected components časti.
					labels.Dispose();
					stats.Dispose();
					centroids.Dispose();
				}

				//========================= 8) ROZDELENIE NA DVE ČÍSLICE A 7-SEGMENT DEKÓDOVANIE =========================

				// V tomto bode už máme:
				// - cleaned: binárny obraz (0/255) po filtrovaní ConnectedComponents, ktorý obsahuje DVE číslice
				//
				// NOVÁ LOGIKA (podľa Instruction.docx):
				// 1. Rozdelíme cleaned Mat na dve časti: LEFT a RIGHT pomocou fromLeft/fromRight parametrov
				// 2. Dekódujeme každú časť samostatne (každá obsahuje jednu číslicu 0-9)
				// 3. Uložíme výsledky a zalogujeme ich ako "Left : X" a "Right : Y"
				//
				// Thread-safe: Pracujeme s lokálnymi Mat objektmi (cleaned, leftPart, rightPart),
				// výsledky uložíme až nakoniec pod _roiLock.

				// Lokálne Mat objekty pre ľavú a pravú časť cleaned obrazu.
				Mat? leftPart = null;
				Mat? rightPart = null;

				// Výsledky dekódovania pre ľavú a pravú číslicu.
				int? leftValue = null;
				int? rightValue = null;

				// doEval určuje, či v tomto priechode Pipeline spravíme aj 7-seg dekódovanie
				// (a teda aj zápis do UI histórie a do MySQL).
				//
				// Default = false, reálne sa nastaví v try bloku na základe časovača.
				bool doEval = false;

				try
				{
					// ========================= 8.1) NAČÍTANIE fromLeft A fromRight Z UI =========================
					// fromLeft/fromRight sú v percentách (1-100).
					// Načítame ich thread-safe pomocou GetNudValueSafe().
					int fromLeftPercent = (int)GetNudValueSafe(nudFromLeft);
					int fromRightPercent = (int)GetNudValueSafe(nudFromRight);

					// Validácia: percentá musia byť v rozsahu 1-100.
					// Ak by boli mimo rozsahu, použijeme default hodnotu 50%.
					if (fromLeftPercent < 1 || fromLeftPercent > 100) fromLeftPercent = 50;
					if (fromRightPercent < 1 || fromRightPercent > 100) fromRightPercent = 50;

					// ========================= 8.2) VÝPOČET ŠÍRKY LEFT A RIGHT ČASTÍ =========================
					// cleaned má určitú šírku (v pixeloch). Rozdelíme ju na dve časti:
					// - leftPart: ľavých fromLeftPercent % šírky cleaned obrazu
					// - rightPart: pravých fromRightPercent % šírky cleaned obrazu
					//
					// Príklad: cleaned má šírku 200px, fromLeft=50%, fromRight=50%
					//   → leftPart: 0-100px (ľavých 50%)
					//   → rightPart: 100-200px (pravých 50%)

					int cleanedWidth = cleaned.Cols;  // Celková šírka cleaned obrazu v pixeloch
					int cleanedHeight = cleaned.Rows; // Celková výška cleaned obrazu v pixeloch

					// Výpočet šírky ľavej časti v pixeloch.
					// leftWidth = (cleanedWidth * fromLeftPercent) / 100
					int leftWidth = (cleanedWidth * fromLeftPercent) / 100;

					// Výpočet šírky pravej časti v pixeloch.
					// rightWidth = (cleanedWidth * fromRightPercent) / 100
					int rightWidth = (cleanedWidth * fromRightPercent) / 100;

					// Ochrana: šírka musí byť aspoň 1 pixel, inak Mat.SubMat() spadne.
					if (leftWidth < 1) leftWidth = 1;
					if (rightWidth < 1) rightWidth = 1;

					// Ochrana: ak by leftWidth alebo rightWidth presiahli cleanedWidth, orezať na max.
					if (leftWidth > cleanedWidth) leftWidth = cleanedWidth;
					if (rightWidth > cleanedWidth) rightWidth = cleanedWidth;

					// ========================= 8.3) VYTVORENIE LEFT A RIGHT ČASTÍ (SubMat) =========================
					// leftPart: ľavá časť cleaned obrazu (od 0 do leftWidth).
					// Rect(x, y, width, height) - x=0 (začíname zľava), y=0 (celá výška), width=leftWidth, height=cleanedHeight
					Rect leftRect = new Rect(0, 0, leftWidth, cleanedHeight);
					leftPart = new Mat(cleaned, leftRect).Clone(); // Clone() = vlastná pamäť, nie SubMat

					// rightPart: pravá časť cleaned obrazu (od pravého okraja smerom doľava).
					// x = cleanedWidth - rightWidth (začíname zozadu), y=0, width=rightWidth, height=cleanedHeight
					int rightStartX = cleanedWidth - rightWidth;
					if (rightStartX < 0) rightStartX = 0; // Ochrana proti preteču

					Rect rightRect = new Rect(rightStartX, 0, rightWidth, cleanedHeight);
					rightPart = new Mat(cleaned, rightRect).Clone(); // Clone() = vlastná pamäť

					// ========================= 8.3.1) ČASOVAČ VYHODNOCOVANIA (KAŽDÝCH N SEKÚND) =========================
					// Požiadavka: 7-seg rozpoznanie (left/right) nerobíme na každom frame,
					// ale len raz za N sekúnd (default 10s).
					//
					// doEval = true => dnes (tento frame) vykonáme dekódovanie a zapíšeme do UI/DB
					// doEval = false => dekódovanie preskočíme, ale left/right obrázky (picLeft/picRight) sa stále aktualizujú
					doEval = ShouldEvaluateNumRegNow();
					// ========================= 8.4) DEKÓDOVANIE ĽAVEJ/PRAVEJ ČÍSLICE (LEN AK doEval == true) =========================
					// Ak doEval == false, dekódovanie preskočíme, aby sme:
					// - nezahltili CPU (7-seg decode je relatívne drahé),
					// - nemali tisíce log záznamov,
					// - a hlavne dodržali požiadavku: vyhodnocovať len každých N sekúnd.
					if (doEval)
					{
						// ========================= VÝBER METÓDY ROZPOZNÁVANIA =========================
						string selectedMethod = GetComboTextSafe(cboRecognitionMethod);

						if (selectedMethod == "ONNX" && _onnxRecognizer != null && _onnxRecognizer.IsLoaded)
						{
							// ========================= ONNX ROZPOZNÁVANIE =========================
							var (leftDigit, leftConf) = _onnxRecognizer.RecognizeDigit(leftPart);
							var (rightDigit, rightConf) = _onnxRecognizer.RecognizeDigit(rightPart);

							float minConfidence = (float)GetNudValueSafe(nudOnnxConfidence);
							leftValue = (leftDigit >= 0 && leftConf >= minConfidence) ? leftDigit : (int?)null;
							rightValue = (rightDigit >= 0 && rightConf >= minConfidence) ? rightDigit : (int?)null;

							_logger?.Debug($"ONNX: Left={leftDigit} ({leftConf:P0}), Right={rightDigit} ({rightConf:P0})");
						}
						else
						{
							// ========================= 7-SEG DEKÓDÉR (DEFAULT) =========================
							if (TryDecodeSevenSegmentFromCleaned(leftPart, out int lVal, out string lText))
							{
								leftValue = lVal;
							}
							else
							{
								leftValue = null;
							}

							if (TryDecodeSevenSegmentFromCleaned(rightPart, out int rVal, out string rText))
							{
								rightValue = rVal;
							}
							else
							{
								rightValue = null;
							}

							_logger?.Debug($"7-SEG: Left={leftValue?.ToString() ?? "?"}, Right={rightValue?.ToString() ?? "?"}");
						}


						// ========================= EXPORT TRÉNOVACÍCH PRÍKLADOV (64×96 PNG) =========================
						// Ukladanie vzoriek je riadené nastavením StoreSample v Settings.json.
						if (_settings.StoreSample)
						{
							string exportTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

							// Export cleaned (binárnych) výrezov do priečinkov 0-9/N.
							ExportDigitExample(leftPart, leftValue, "L", exportTimestamp, "");
							ExportDigitExample(rightPart, rightValue, "R", exportTimestamp, "");

							// Export originálnych (nespracovaných) výrezov do priečinkov 0_O-9_O/N_O.
							Mat? roiClone = null;
							lock (_roiLock)
							{
								if (_lastRoi != null && !_lastRoi.Empty())
									roiClone = _lastRoi.Clone();
							}

							if (roiClone != null)
							{
								try
								{
									int roiWidth = roiClone.Width;
									int roiHeight = roiClone.Height;

									int origLeftWidth = (roiWidth * fromLeftPercent) / 100;
									int origRightWidth = (roiWidth * fromRightPercent) / 100;
									if (origLeftWidth < 1) origLeftWidth = 1;
									if (origRightWidth < 1) origRightWidth = 1;
									if (origLeftWidth > roiWidth) origLeftWidth = roiWidth;
									if (origRightWidth > roiWidth) origRightWidth = roiWidth;

									using var origLeftPart = new Mat(roiClone, new Rect(0, 0, origLeftWidth, roiHeight)).Clone();
									int origRightStartX = roiWidth - origRightWidth;
									if (origRightStartX < 0) origRightStartX = 0;
									using var origRightPart = new Mat(roiClone, new Rect(origRightStartX, 0, origRightWidth, roiHeight)).Clone();

									ExportDigitExample(origLeftPart, leftValue, "L", exportTimestamp, "_O");
									ExportDigitExample(origRightPart, rightValue, "R", exportTimestamp, "_O");
								}
								finally
								{
									roiClone.Dispose();
								}
							}
						}
					}
					
					//// Zavoláme TryDecodeSevenSegmentFromCleaned pre rightPart.
					//if (TryDecodeSevenSegmentFromCleaned(rightPart, out int rVal, out string rText))
					//{
					//	rightValue = rVal; // Úspešne dekódované (0-9)
					//	_logger?.Debug($"Right : {rVal}"); // Log: "Right : 9"
					//}
					//else
					//{
					//	rightValue = null; // Dekódovanie zlyhalo
					//	_logger?.Debug("Right : (decode failed)");
					//}
				}
				catch (Exception ex)
				{
					// Dekódovanie nesmie zhodiť pipeline – radšej len zalogujeme a pokračujeme.
					_logger?.Warn($"Digit split/decode failed (non-fatal): {ex.Message}");
					leftValue = null;
					rightValue = null;

					// Uvoľníme lokálne Mat-y v prípade výnimky.
					leftPart?.Dispose();
					leftPart = null;
					rightPart?.Dispose();
					rightPart = null;
				}

				//========================= 9) Uloženie výsledkov do zdieľaných fieldov (thread-safe). =========================
				lock (_roiLock)
				{
					// Uvoľni starý contrast a ulož nový (Clone = bezpečná kópia).
					_lastContrast?.Dispose();
					_lastContrast = contrast.Clone();

					// Uvoľni starý binary a ulož nový.
					_lastBinary?.Dispose();
					_lastBinary = binary.Clone();

					// Uvoľni starý cleaned a ulož nový.
					_lastCleaned?.Dispose();
					_lastCleaned = cleaned.Clone();

					// ========================= ULOŽENIE LEFT/RIGHT MAT OBJEKTOV (thread-safe) =========================
					// Uložíme leftPart a rightPart do zdieľaných _lastLeft a _lastRight fieldov.
					// Tieto sa neskôr zobrazia v picLeft a picRight PictureBoxoch (v CaptureLoop).

					// Uvoľni starý _lastLeft a ulož nový leftPart.
					// leftPart už má vlastnú pamäť (Clone()), takže ho môžeme priamo priradiť.
					_lastLeft?.Dispose();
					_lastLeft = leftPart;  // Prevezmeme vlastníctvo leftPart
					leftPart = null;       // Nastavíme na null, aby sa neuvoľnil v finally bloku

					// Uvoľni starý _lastRight a ulož nový rightPart.
					_lastRight?.Dispose();
					_lastRight = rightPart; // Prevezmeme vlastníctvo rightPart
					rightPart = null;        // Nastavíme na null, aby sa neuvoľnil v finally bloku

					// ========================= ULOŽENIE DEKÓDOVANÝCH HODNÔT (thread-safe) =========================
					// Dôležité: 
					// - dekódovanie nerobíme na každom frame, ale len každých N sekúnd (doEval == true).
					// - preto NESMIEME pri doEval==false prepisovať _lastLeftValue/_lastRightValue na null,
					//   inak by UI/DB dostávali “prázdne” hodnoty medzi meraniami.
					if (doEval)
					{
						_lastLeftValue = leftValue;
						_lastRightValue = rightValue;

						// Zloženie textu pre dvojcifernú hodnotu (napr. "70" alebo "?3" alebo "??").
						string composedText = $"{(leftValue?.ToString() ?? "?")}{(rightValue?.ToString() ?? "?")}";
						_lastSevenSegText = composedText;

						// Ak sú obe číslice platné, uložíme aj integer (napr. 70). Inak null.
						if (leftValue != null && rightValue != null)
							_lastSevenSegValue = (leftValue.Value * 10) + rightValue.Value;
						else
							_lastSevenSegValue = null;
					}
				}



				// ========================= 9.1) ZÁZNAM DO UI HISTÓRIE + MySQL (LEN AK doEval == true) =========================
				// Požiadavka:
				// - výsledné čísla už nezapisujeme do logu,
				// - budeme ich vyhodnocovať len každých N sekúnd,
				// - a keď sa vyhodnotia, uložíme ich do UI histórie + odošleme na zápis do MySQL.
				if (doEval)
				{
					// Zostav textový výstup – ak dekódovanie zlyhalo, použijeme '?'
					string leftText = leftValue.HasValue ? leftValue.Value.ToString() : "?";
					string rightText = rightValue.HasValue ? rightValue.Value.ToString() : "?";
					string resultText = leftText + rightText;

					var entry = new NumRegEntry
					{
						TimestampUtc = DateTime.UtcNow,
						LeftDigit = leftValue,
						RightDigit = rightValue,
						Text = resultText
					};

					// 1) UI historický záznam (thread-safe cez BeginInvoke)
					AppendNumRegToUi(entry);

					// 2) Zápis do MySQL databázy – cez background writer (pipeline thread nikdy neblokuje)
					_mySqlWriter?.Enqueue(new MySqlWriter.NumRegRecord(entry.TimestampUtc, entry.LeftDigit, entry.RightDigit, entry.Text));
				}

				// ========================= 10) ChangeMatrix - Resize cleaned na NxN =========================
				// Tento krok vezme cleaned Mat a zmenší ho na fixnú veľkosť NxN (16x16, 32x32, 64x64 alebo 128x128).
				// Účel: Príprava vstupu pre neurónové siete alebo pattern matching (normalizovaná veľkosť).

				// Prečítaj vybranú veľkosť matice z ComboBoxu (thread-safe).
				// GetComboValueSafe() volá Invoke() pre bezpečný prístup z background threadu.
				int matrixSize = GetComboValueSafe(cboChangeMatrix);

				// Ochrana: ak je matrixSize <= 0 (chyba alebo nič nevybrané), preskočíme resize.
				// Toto by sa nemalo stať, lebo v konštruktore nastavujeme default hodnotu 32.
				if (matrixSize > 0)
				{
					// Vytvoríme dočasný Mat pre resized výsledok.
					Mat matrix = new Mat();

					// Resize cleaned Mat na NxN pixelov.
					// - cleaned: vstupný Mat (čierne pozadie, biele objekty)
					// - matrix: výstupný Mat (resized)
					// - new Size(matrixSize, matrixSize): cieľová veľkosť (napr. 32x32)
					// - InterpolationFlags.Area: najlepšia interpolácia pre zmenšovanie (zachováva detaily)
					Cv2.Resize(cleaned, matrix, new OpenCvSharp.Size(matrixSize, matrixSize), 0, 0, InterpolationFlags.Area);

					// Uloženie resized matice do _lastMatrix (thread-safe pod _roiLock).
					lock (_roiLock)
					{
						// Uvoľni starý _lastMatrix (ak existuje) aby nevznikol memory leak.
						_lastMatrix?.Dispose();

						// Ulož nový resized Mat do _lastMatrix.
						// Matrix už má vlastnú pamäť (nie je SubMat), takže nepotrebujeme Clone().
						_lastMatrix = matrix;

						// Nastavíme matrix na null, aby ho finally blok náhodou neDispose-nul.
						// (matrix teraz vlastní _lastMatrix)
						matrix = null;
					}

					// Poznámka: matrix?.Dispose() v finally bloku uvoľní matrix len ak nebol priradený do _lastMatrix.
				}

			}
			finally
			{
				// ========================= DISPOSE LOKÁLNYCH MAT-OV (KAPITOLA 4 - UPRAVENÉ) =========================
				// Uvoľni lokálne Mat-y (aby nevznikali memory leaky).
				upCopy?.Dispose();
				gray?.Dispose();
				smooth?.Dispose();
				contrast?.Dispose();
				binary?.Dispose();
				cleaned?.Dispose();
			}
		}

		/// <summary>
		/// Bezpečne prečíta hodnotu z NumericUpDown aj vtedy, keď je volaná z iného vlákna než UI.
		/// Je to dôležité, lebo CaptureLoop beží typicky na background threade.
		/// </summary>
		private decimal GetNudValueSafe(NumericUpDown nud)
		{
			// Ak sme na UI threade (InvokeRequired == false), môžeme hodnotu čítať priamo.
			if (!nud.InvokeRequired)
			{
				// Priamy návrat aktuálnej hodnoty NumericUpDown.
				return nud.Value;
			}

			// Ak sme na inom (ne-UI) vlákne, musíme si hodnotu vypýtať cez Invoke na UI thread.
			// Invoke je blokujúci – počká, kým UI thread hodnotu prečíta a vráti.
			return (decimal)nud.Invoke(new Func<decimal>(() =>
			{
				// Tento kód už beží na UI threade, takže čítanie Value je bezpečné.
				return nud.Value;
			}));
		}

		/// <summary>
		/// Bezpečne prečíta vybranú hodnotu z ComboBoxu aj vtedy, keď je volaná z iného vlákna než UI.
		/// Používa sa na čítanie cboChangeMatrix z background threadu (CaptureLoop -> Pipeline).
		/// </summary>
		/// <param name="cbo">ComboBox, z ktorého chceme prečítať hodnotu</param>
		/// <returns>Vybratá hodnota ako int (napr. 16, 32, 64, 128), alebo 0 ak nič nie je vybrané</returns>
		private int GetComboValueSafe(ComboBox cbo)
		{
			// Ak sme na UI threade (InvokeRequired == false), môžeme hodnotu čítať priamo.
			if (!cbo.InvokeRequired)
			{
				// Priamy návrat vybranej hodnoty z ComboBoxu.
				// SelectedItem je object, musíme ho skonvertovať na int.
				// Ak nie je nič vybrané (SelectedItem == null), vrátime 0.
				return cbo.SelectedItem != null ? (int)cbo.SelectedItem : 0;
			}

			// Ak sme na inom (ne-UI) vlákne, musíme si hodnotu vypýtať cez Invoke na UI thread.
			// Invoke je blokujúci – počká, kým UI thread hodnotu prečíta a vráti.
			return (int)cbo.Invoke(new Func<int>(() =>
			{
				// Tento kód už beží na UI threade, takže čítanie SelectedItem je bezpečné.
				// SelectedItem je object (môže byť int 16, 32, 64, 128), skonvertujeme na int.
				// Ochrana: ak by bolo null, vrátime 0.
				return cbo.SelectedItem != null ? (int)cbo.SelectedItem : 0;
			}));
		}

		/// <summary>
		/// Bezpečne prečíta vybraný text z ComboBoxu aj z iného vlákna než UI.
		/// </summary>
		private string GetComboTextSafe(ComboBox cbo)
		{
			if (!cbo.InvokeRequired)
			{
				return cbo.SelectedItem?.ToString() ?? "";
			}
			return (string)cbo.Invoke(new Func<string>(() => cbo.SelectedItem?.ToString() ?? ""));
		}

	}
}
