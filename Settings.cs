using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GUIVideoProcessing
{
	/// <summary>
	/// Trieda reprezentujúca nastavenia aplikácie, ktoré sa ukladajú do Settings.json.
	/// Obsahuje list IP adries pre pripojenie na ESP32-CAM streamy a všetky parametre Image Processing pipeline.
	/// </summary>
	public class Settings
	{
		// ========================= ZÁKLADNÉ NASTAVENIA =========================

		// List IP adries (stream URL) – napr. ["http://192.168.50.96:81/stream", "http://192.168.50.100:81/stream"]
		// Ak je prázdny, aplikácia zakáže tlačidlo START.
		public List<string> IPAddress { get; set; }

		// ========================= ROI PARAMETRE =========================
		// Parametre pre Region of Interest (oblasť výrezu zo streamu) v percentách (0-100).
		// Použité property initializers namiesto priradenia v konštruktore,
		// aby fungovali aj po deserializácii existujúceho Settings.json.

		// X súradnica ROI v percentách (0-100). Default: 22% od ľavého okraja.
		public int ROIX { get; set; } = 22;

		// Y súradnica ROI v percentách (0-100). Default: 56% od horného okraja.
		public int ROIY { get; set; } = 56;

		// Šírka ROI v percentách (0-100). Default: 17% šírky frame.
		public int ROIWidth { get; set; } = 17;

		// Výška ROI v percentách (0-100). Default: 23% výšky frame.
		public int ROIHeight { get; set; } = 23;

		// Scale factor pre zväčšenie ROI (Resize operácia). Default: 4.0 (štvorné zväčšenie).
		public double ResizeScale { get; set; } = 4.0;

		// ========================= BILATERAL FILTER PARAMETRE =========================
		// Bilateral Filter: Odstránenie šumu pri zachovaní hrán (edge-preserving smoothing).

		// Priemer (diameter) filtra v pixeloch. Default: 5.
		// OPRAVA: Zmenšené z 9 na 5 pre LCD display - menej rozmazania, zachová ostré hrany číslic.
		public int BilateralD { get; set; } = 5;

		// Sigma pre farebný priestor (color space). Default: 40 (optimalizované pre LCD display).
		// OPRAVA: Znížené z 75 na 40 - zachová viac hrán a detailov bielych číslic.
		public int BilateralSigmaColor { get; set; } = 40;

		// Sigma pre priestorový priestor (coordinate space). Default: 40.
		// OPRAVA: Znížené z 75 na 40 - zachová viac hrán a detailov.
		public int BilateralSigmaSpace { get; set; } = 40;

		// ========================= CLAHE PARAMETRE =========================
		// CLAHE (Contrast Limited Adaptive Histogram Equalization): Lokálne zvýšenie kontrastu.

		// Limit pre clipping histogramu (sila kontrastu). Default: 5.0 (optimalizované pre LCD display).
		// OPRAVA: Zvýšené z 3.0 na 5.0 - silnejší kontrast medzi bielymi číslicami a zeleným pozadím.
		public double CLAHEClipLimit { get; set; } = 5.0;

		// Veľkosť tile (lokálnej oblasti) pre CLAHE - X rozmer. Default: 2.
		// OPRAVA: Zmenšené z 4 na 2 - jemnejší lokálny efekt pre veľmi malé ROI (17%×23%).
		public int CLAHETileGridSizeX { get; set; } = 2;

		// Veľkosť tile (lokálnej oblasti) pre CLAHE - Y rozmer. Default: 2.
		// OPRAVA: Zmenšené z 4 na 2 - jemnejší lokálny efekt pre veľmi malé ROI.
		public int CLAHETileGridSizeY { get; set; } = 2;

		// ========================= ADAPTIVE THRESHOLD PARAMETRE =========================
		// Adaptive Threshold: Binarizácia obrazu (čierno-biele).

		// Veľkosť bloku pre adaptívny prah. Default: 11 (optimalizované pre LCD display v malom ROI).
		// OPRAVA: Zmenšené z 15 na 11 - lepšia adaptácia na malé ROI (17%×23%) a ostré hrany LCD číslic.
		// Musí byť nepárne číslo >= 3. Menšie hodnoty = jemnejšia lokálna binarizácia.
		public int AdaptiveThresholdBlockSize { get; set; } = 11;

		// Konštanta C odčítaná od váženého priemeru. Default: 2.0.
		// OPRAVA: Znížené z 5.0 na 2.0 - KRITICKÁ ZMENA pre biele LCD číslice!
		// Nižšie C = vyšší prah = viac bielych pixelov v binárnom obraze = lepšia detekcia bielych číslic.
		// C sa ODČÍTAVA od adaptívneho prahu, takže menšie C zvyšuje pravdepodobnosť detekcie svetlých objektov.
		public double AdaptiveThresholdC { get; set; } = 2.0;

		// ========================= MORPHOLOGY PARAMETRE =========================
		// Morphology operácie: Odstránenie šumu (Open) a vyplnenie dier (Close).

		// Veľkosť morphology kernelu (štruktúrujúci element). Default: 3 (3x3 kernel).
		public int MorphologyKernelSize { get; set; } = 3;

		// Počet iterácií pre Morphology Open operáciu (odstránenie malých bodiek). Default: 1.
		public int MorphologyOpenIterations { get; set; } = 1;

		// Počet iterácií pre Morphology Close operáciu (vyplnenie malých dier). Default: 1.
		public int MorphologyCloseIterations { get; set; } = 1;

		// ========================= CONNECTED COMPONENTS PARAMETRE =========================
		// Connected Components: Filtrovanie objektov podľa plochy (odstránenie smetí).

		// Minimálna plocha objektu v pixeloch. Default: 30 (optimalizované pre malé LCD číslice).
		// OPRAVA: Zmenšené z 200 na 30 pre malé ROI (17%×23%) - číslice po Resize sú menšie.
		// Objekty menšie než táto hodnota budú odstránené ako šum/smeti.
		public int MinArea { get; set; } = 30;

		// ========================= 7-SEG DETEKCIA SEGMENTOV =========================
		// Prah pre rozhodnutie ON/OFF segmentu pri 7-segmentovom dekódovaní.
		// Ak je v oblasti segmentu >= tento pomer bielych pixelov, segment je považovaný za zapnutý.
		// Nižšia hodnota = citlivejšia detekcia (zachytí slabšie segmenty), vyššia = odolnejšia voči šumu.
		// Default: 0.35 (35% bielych pixelov).
		public double SegmentOnThreshold { get; set; } = 0.35;

		
		// Či sa majú ukladať vzorky rozpoznaných číslic (PNG) do logs/examples/.
		// true = ukladať, false = neukladať. Default: false.
		public bool StoreSample { get; set; } = false;

		// ========================= NUMERIC RECOGNITION (NUMREG) =========================
		// Ako často sa majú uložiť vyhodnotené čísla (sekundy).
		// Požiadavka: vyhodnocovať každých 10 sekúnd.
		// Default: 10
		public int NumEvalIntervalSeconds { get; set; } = 10;

		// ========================= MYSQL (VOLITEĽNÉ UKLADANIE DO DB) =========================
		// Ak je MySqlEnabled = false, aplikácia nebude DB používať.
		public bool MySqlEnabled { get; set; } = false;

		// Hostname alebo IP adresa MySQL servera (môže byť localhost alebo vzdialené PC).
		public string MySqlHost { get; set; } = "localhost";

		// Port MySQL servera. Default: 3306
		public int MySqlPort { get; set; } = 3306;

		// Názov databázy.
		public string MySqlDatabase { get; set; } = "gui_video_processing";

		// Užívateľské meno.
		public string MySqlUsername { get; set; } = "root";

		// Heslo. (Poznámka: je to v plain texte v Settings.json – je to zámer pre jednoduché lokálne nastavenie.)
		public string MySqlPassword { get; set; } = "";

		// Názov tabuľky, do ktorej sa ukladajú výsledky.
		public string MySqlTable { get; set; } = "numreg_readings";

		// Timeout pripojenia v sekundách (ak je server nedostupný).
		public int MySqlConnectTimeoutSeconds { get; set; } = 5;

		// Ako dlho čakať medzi pokusmi o pripojenie, ak MySQL nie je dostupná.
		public int MySqlReconnectDelaySeconds { get; set; } = 5;

		// ========================= ONNX ROZPOZNÁVANIE ČÍSLIC =========================
		// Cesta k ONNX modelu pre rozpoznávanie 7-segmentových číslic.
		// Relatívna cesta je relatívna k priečinku s aplikáciou.
		public string OnnxModelPath { get; set; } = "model/model.onnx";

		// Minimálna confidence (istota) pre akceptovanie rozpoznanej číslice (0.0-1.0).
		public float OnnxMinConfidence { get; set; } = 0.5f;

		// ========================= VÝBER METÓDY ROZPOZNÁVANIA =========================
		// Aktuálne vybraná metóda rozpoznávania číslic.
		// Možnosti: "7-SEG", "ONNX"
		public string RecognitionMethod { get; set; } = "ONNX";

		// ========================= PARAMETRE PRE ROZDELENIE NA DVE ČÍSLICE =========================
		// Parametre pre rozdelenie cleaned obrazu na ľavú a pravú časť (2 číslice).
		// Každá hodnota je v percentách (1-100) a určuje, koľko percent z celkovej šírky
		// cleaned obrazu sa použije pre detekciu danej číslice.

		// FromLeft – Percento šírky z ľavej strany (1-100%). Default: 50%.
		// Ak je fromLeft = 50, algoritmus vezme ľavých 50% cleaned obrazu a hľadá v ňom ľavú číslicu.
		// Príklad: cleaned má šírku 200px → ľavá časť bude mať šírku 100px (0-100px).
		public int FromLeft { get; set; } = 50;

		// FromRight – Percento šírky z pravej strany (1-100%). Default: 50%.
		// Ak je fromRight = 50, algoritmus vezme pravých 50% cleaned obrazu a hľadá v ňom pravú číslicu.
		// Príklad: cleaned má šírku 200px → pravá časť bude mať šírku 100px (100-200px).
		public int FromRight { get; set; } = 50;

		// ========================= LED OVLÁDANIE (ESP32-CAM) =========================
		// Intenzita LED na ESP32-CAM module (0-255). 0 = vypnutá, 255 = maximálny jas.
		// Posiela sa cez HTTP GET na http://<IP>/control?var=led_intensity&val=<0-255>.
		public int LedIntensity { get; set; } = 0;

		/// <summary>
		/// Konštruktor – inicializuje prázdny list IP adries.
		/// OPRAVA (Kapitola 4): Default hodnoty pipeline parametrov sú teraz v property initializers,
		/// takže fungujú aj po deserializácii existujúceho Settings.json.
		/// </summary>
		public Settings()
		{
			// Vytvorí prázdny list – zabezpečí, že IPAddress nikdy nie je null.
			IPAddress = new List<string>();

			// ========================= DEFAULT HODNOTY =========================
			// OPRAVA: Default hodnoty sú teraz definované priamo v property initializers (riadky 26-97),
			// NIE tu v konštruktore. Dôvod: Pri deserializácii existujúceho Settings.json
			// sa konštruktor NEVOLÁ, takže by property mali hodnotu 0 namiesto optimalizovaných hodnôt.
			// Property initializers sa vykonajú PRED deserializáciou, takže ak JSON neobsahuje
			// property, použije sa default hodnota z initializera.
		}

		/// <summary>
		/// Načíta nastavenia zo súboru Settings.json.
		/// Ak súbor neexistuje, vytvorí nový s prázdnym listom IP adries.
		/// </summary>
		/// <param name="filePath">Plná cesta k súboru Settings.json (typicky vedľa .exe)</param>
		/// <returns>Objekt Settings načítaný z JSON alebo nový prázdny objekt</returns>
		public static Settings Load(string filePath)
		{
			// Skontroluje, či Settings.json existuje na disku.
			if (!File.Exists(filePath))
			{
				// Ak neexistuje, vytvorí nový prázdny Settings objekt.
				var newSettings = new Settings();

				// Uloží ho na disk (vytvorí Settings.json s prázdnym listom IPAddress).
				newSettings.Save(filePath);

				// Vráti novovytvorený objekt volajúcemu (MainForm).
				return newSettings;
			}

			// Ak Settings.json existuje, prečíta celý obsah súboru ako string.
			string json = File.ReadAllText(filePath);

			// Deserializuje JSON string na C# objekt typu Settings.
			// JsonSerializer.Deserialize rozpozná property "IPAddress" a naplní list.
			var settings = JsonSerializer.Deserialize<Settings>(json);

			// Ochrana: ak by deserializácia zlyhala (vrátila null), vytvorí prázdny objekt.
			if (settings == null)
			{
				settings = new Settings();
			}

			// Ochrana: ak by JSON neobsahoval property IPAddress (alebo bola null), vytvorí prázdny list.
			// Toto zabezpečí, že IPAddress nikdy nie je null a aplikácia nespadne na NullReferenceException.
			if (settings.IPAddress == null)
			{
				settings.IPAddress = new List<string>();
			}

			// Vráti načítaný (alebo fallback) Settings objekt.
			return settings;
		}

		/// <summary>
		/// Uloží aktuálne nastavenia do súboru Settings.json.
		/// Používa pekný formát JSON (WriteIndented = true) pre ľudskú čitateľnosť.
		/// </summary>
		/// <param name="filePath">Plná cesta k súboru Settings.json</param>
		public void Save(string filePath)
		{
			// Nastavenia pre JSON serializer – WriteIndented = true znamená pekný formát s odsadením.
			// Bez toho by bol JSON na jednom riadku (menej čitateľný pre manuálne úpravy).
			var options = new JsonSerializerOptions
			{
				WriteIndented = true // <- pekný formát s odsadením (každá property na novom riadku)
			};

			// Serializuje aktuálny objekt Settings do JSON stringu.
			// Výsledok bude napr.: { "IPAddress": ["http://...", "http://..."] }
			string json = JsonSerializer.Serialize(this, options);

			// Zapíše JSON string do súboru Settings.json.
			// Ak súbor existuje, prepíše ho. Ak neexistuje, vytvorí nový.
			File.WriteAllText(filePath, json);
		}
	}
}
