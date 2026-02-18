namespace GUIVideoProcessing
{
	partial class MainForm
	{
		// Premenná pre komponenty vytvárané Designerom
		private System.ComponentModel.IContainer components = null;

		// ComboBox pre výber URL zo Settings.json
		private System.Windows.Forms.ComboBox cboUrl;
		// Tlačidlo START
		private System.Windows.Forms.Button btnStart;
		// Tlačidlo STOP
		private System.Windows.Forms.Button btnStop;
		// Tlačidlo SAVE - uloží UI nastavenia do Settings.json
		private System.Windows.Forms.Button btnSave;
		// ComboBox pre výber metódy rozpoznávania číslic (7-SEG, ONNX)
		private System.Windows.Forms.ComboBox cboRecognitionMethod;
		// Label a NumericUpDown pre ONNX confidence threshold
		private System.Windows.Forms.Label lblOnnxConfidence;
		private System.Windows.Forms.NumericUpDown nudOnnxConfidence;
		// PictureBox pre zobrazenie frame s vyznačeným ROI
		private System.Windows.Forms.PictureBox picFrame;

		// PictureBox pre zobrazenie cleaned výsledku
		private System.Windows.Forms.PictureBox picCleaned;

		// Debug tlačidlá pre pipeline
		private System.Windows.Forms.GroupBox grpDebug;
		private System.Windows.Forms.Button btnDebugContrast;
		private System.Windows.Forms.Button btnDebugBinary;
		private System.Windows.Forms.Button btnDebugCleaned;
		// Tlačidlo pre testovanie segmentácie číslic (KAPITOLA 5)
		private System.Windows.Forms.Button btnTestSegmentation;

		// GroupBox pre threshold/cleaning parametre
		private System.Windows.Forms.GroupBox grpThreshold;
		private System.Windows.Forms.Label lblBlockSize;
		private System.Windows.Forms.NumericUpDown nudBlockSize;
		private System.Windows.Forms.Label lblC;
		private System.Windows.Forms.NumericUpDown nudC;
		private System.Windows.Forms.Label lblMinArea;
		private System.Windows.Forms.NumericUpDown nudMinArea;
		// Label a NumericUpDown pre 7-SEG segment ON/OFF prah
		private System.Windows.Forms.Label lblSegmentOnThreshold;
		private System.Windows.Forms.NumericUpDown nudSegmentOnThreshold;

		// GroupBox pre nastavenie ROI
		private System.Windows.Forms.GroupBox grpRoi;
		private System.Windows.Forms.NumericUpDown nudX;
		private System.Windows.Forms.NumericUpDown nudY;
		private System.Windows.Forms.NumericUpDown nudWidth;
		private System.Windows.Forms.NumericUpDown nudHeight;
		private System.Windows.Forms.Label lblX;
		private System.Windows.Forms.Label lblY;
		private System.Windows.Forms.Label lblWidth;
		private System.Windows.Forms.Label lblHeight;
		private System.Windows.Forms.Button btnRoi;

		// GroupBox pre nastavenie Resize
		private System.Windows.Forms.GroupBox grpResize;
		private System.Windows.Forms.NumericUpDown nudResize;
		private System.Windows.Forms.Label lblResize;
		private System.Windows.Forms.Button btnView;

		// GroupBox pre ChangeMatrix (resize cleaned na Nx N)
		private System.Windows.Forms.GroupBox grpChangeMatrix;
		// Label pre ChangeMatrix
		private System.Windows.Forms.Label lblChangeMatrix;
		// ComboBox pre výber veľkosti matice (16, 32, 64, 128)
		private System.Windows.Forms.ComboBox cboChangeMatrix;
		// PictureBox pre zobrazenie resized matice
		private System.Windows.Forms.PictureBox picMatrix;

		// ========================= NOVÉ KONTROLY PRE PIPELINE PARAMETRE (KAPITOLA 4) =========================

		// GroupBox pre Bilateral Filter parametre
		private System.Windows.Forms.GroupBox grpBilateral;
		// Label a NumericUpDown pre Bilateral D (diameter)
		private System.Windows.Forms.Label lblBilateralD;
		private System.Windows.Forms.NumericUpDown nudBilateralD;
		// Label a NumericUpDown pre Bilateral SigmaColor
		private System.Windows.Forms.Label lblBilateralSigmaColor;
		private System.Windows.Forms.NumericUpDown nudBilateralSigmaColor;
		// Label a NumericUpDown pre Bilateral SigmaSpace
		private System.Windows.Forms.Label lblBilateralSigmaSpace;
		private System.Windows.Forms.NumericUpDown nudBilateralSigmaSpace;

		// GroupBox pre CLAHE parametre
		private System.Windows.Forms.GroupBox grpClahe;
		// Label a NumericUpDown pre CLAHE ClipLimit
		private System.Windows.Forms.Label lblCLAHEClipLimit;
		private System.Windows.Forms.NumericUpDown nudCLAHEClipLimit;
		// Label a NumericUpDown pre CLAHE TileGridSizeX
		private System.Windows.Forms.Label lblCLAHETileGridSizeX;
		private System.Windows.Forms.NumericUpDown nudCLAHETileGridSizeX;
		// Label a NumericUpDown pre CLAHE TileGridSizeY
		private System.Windows.Forms.Label lblCLAHETileGridSizeY;
		private System.Windows.Forms.NumericUpDown nudCLAHETileGridSizeY;

		// GroupBox pre Morphology parametre
		private System.Windows.Forms.GroupBox grpMorphology;
		// Label a NumericUpDown pre Morphology KernelSize
		private System.Windows.Forms.Label lblMorphologyKernelSize;
		private System.Windows.Forms.NumericUpDown nudMorphologyKernelSize;
		// Label a NumericUpDown pre Morphology OpenIterations
		private System.Windows.Forms.Label lblMorphologyOpenIterations;
		private System.Windows.Forms.NumericUpDown nudMorphologyOpenIterations;
		// Label a NumericUpDown pre Morphology CloseIterations
		private System.Windows.Forms.Label lblMorphologyCloseIterations;
		private System.Windows.Forms.NumericUpDown nudMorphologyCloseIterations;

		// ========================= NOVÉ KONTROLY PRE ROZDELENIE NA 2 ČÍSLICE =========================
		// GroupBox NumReg – obsahuje kontrolky pre rozdelenie cleaned obrazu na ľavú a pravú časť
		private System.Windows.Forms.GroupBox grpNumReg;
		// ========================= NOVÉ UI PRVKY PRE ZAZNAMENÁVANIE ROZPOZNANÝCH ČÍSEL =========================
		private System.Windows.Forms.ListView lvNumReg;
		private System.Windows.Forms.ColumnHeader colNumTime;
		private System.Windows.Forms.ColumnHeader colNumValue;
		private System.Windows.Forms.Button btnNumRegClear;
		// Label a NumericUpDown pre FromLeft (percento z ľavej strany)
		private System.Windows.Forms.Label lblFromLeft;
		private System.Windows.Forms.NumericUpDown nudFromLeft;
		// Label a NumericUpDown pre FromRight (percento z pravej strany)
		private System.Windows.Forms.Label lblFromRight;
		private System.Windows.Forms.NumericUpDown nudFromRight;
		// PictureBox pre zobrazenie ľavej časti cleaned obrazu (Left digit)
		private System.Windows.Forms.PictureBox picLeft;
		// PictureBox pre zobrazenie pravej časti cleaned obrazu (Right digit)
		private System.Windows.Forms.PictureBox picRight;

		// ========================= LED OVLÁDANIE (ESP32-CAM) =========================
		// GroupBox pre LED ovládanie
		private System.Windows.Forms.GroupBox grpLed;
		// CheckBox pre zapnutie/vypnutie LED
		private System.Windows.Forms.CheckBox chkLed;
		// TrackBar pre reguláciu intenzity LED (0-255)
		private System.Windows.Forms.TrackBar trkLedIntensity;
		// Label zobrazujúci aktuálnu hodnotu intenzity
		private System.Windows.Forms.Label lblLedValue;

		/// <summary>
		/// Uvoľnenie zdrojov
		/// </summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <summary>
		/// Metóda vytvorená Designerom – inicializuje všetky ovládacie prvky na Forme
		/// </summary>
		private void InitializeComponent()
		{
			grpDebug = new GroupBox();
			btnDebugContrast = new Button();
			btnDebugBinary = new Button();
			btnDebugCleaned = new Button();
			btnTestSegmentation = new Button();
			picCleaned = new PictureBox();
			grpThreshold = new GroupBox();
			lblBlockSize = new Label();
			nudBlockSize = new NumericUpDown();
			lblC = new Label();
			nudC = new NumericUpDown();
			lblMinArea = new Label();
			nudMinArea = new NumericUpDown();
			lblSegmentOnThreshold = new Label();
			nudSegmentOnThreshold = new NumericUpDown();
			cboUrl = new ComboBox();
			btnStart = new Button();
			btnStop = new Button();
			btnSave = new Button();
			cboRecognitionMethod = new ComboBox();
			lblOnnxConfidence = new Label();
			nudOnnxConfidence = new NumericUpDown();
			picFrame = new PictureBox();
			grpRoi = new GroupBox();
			btnRoi = new Button();
			lblHeight = new Label();
			lblWidth = new Label();
			lblY = new Label();
			lblX = new Label();
			nudHeight = new NumericUpDown();
			nudWidth = new NumericUpDown();
			nudY = new NumericUpDown();
			nudX = new NumericUpDown();
			grpResize = new GroupBox();
			btnView = new Button();
			lblResize = new Label();
			nudResize = new NumericUpDown();
			grpChangeMatrix = new GroupBox();
			lblChangeMatrix = new Label();
			cboChangeMatrix = new ComboBox();
			picMatrix = new PictureBox();
			grpBilateral = new GroupBox();
			lblBilateralD = new Label();
			nudBilateralD = new NumericUpDown();
			lblBilateralSigmaColor = new Label();
			nudBilateralSigmaColor = new NumericUpDown();
			lblBilateralSigmaSpace = new Label();
			nudBilateralSigmaSpace = new NumericUpDown();
			grpClahe = new GroupBox();
			lblCLAHEClipLimit = new Label();
			nudCLAHEClipLimit = new NumericUpDown();
			lblCLAHETileGridSizeX = new Label();
			nudCLAHETileGridSizeX = new NumericUpDown();
			lblCLAHETileGridSizeY = new Label();
			nudCLAHETileGridSizeY = new NumericUpDown();
			grpMorphology = new GroupBox();
			lblMorphologyKernelSize = new Label();
			nudMorphologyKernelSize = new NumericUpDown();
			lblMorphologyOpenIterations = new Label();
			nudMorphologyOpenIterations = new NumericUpDown();
			lblMorphologyCloseIterations = new Label();
			nudMorphologyCloseIterations = new NumericUpDown();
			grpNumReg = new GroupBox();
			lvNumReg = new ListView();
			colNumTime = new ColumnHeader();
			colNumValue = new ColumnHeader();
			btnNumRegClear = new Button();
			lblFromLeft = new Label();
			nudFromLeft = new NumericUpDown();
			lblFromRight = new Label();
			nudFromRight = new NumericUpDown();
			picLeft = new PictureBox();
			picRight = new PictureBox();
			grpLed = new GroupBox();
			chkLed = new CheckBox();
			trkLedIntensity = new TrackBar();
			lblLedValue = new Label();
			grpLed.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)trkLedIntensity).BeginInit();
			grpDebug.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)picCleaned).BeginInit();
			grpThreshold.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudBlockSize).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudC).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudMinArea).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudSegmentOnThreshold).BeginInit();
			((System.ComponentModel.ISupportInitialize)picFrame).BeginInit();
			grpRoi.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudHeight).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudWidth).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudY).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudX).BeginInit();
			grpResize.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudResize).BeginInit();
			grpChangeMatrix.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)picMatrix).BeginInit();
			grpBilateral.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudBilateralD).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudBilateralSigmaColor).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudBilateralSigmaSpace).BeginInit();
			grpClahe.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudCLAHEClipLimit).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudCLAHETileGridSizeX).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudCLAHETileGridSizeY).BeginInit();
			grpMorphology.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudMorphologyKernelSize).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudMorphologyOpenIterations).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudMorphologyCloseIterations).BeginInit();
			grpNumReg.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)nudFromLeft).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudFromRight).BeginInit();
			((System.ComponentModel.ISupportInitialize)picLeft).BeginInit();
			((System.ComponentModel.ISupportInitialize)picRight).BeginInit();
			SuspendLayout();
			// 
			// grpDebug
			// 
			grpDebug.Controls.Add(btnDebugContrast);
			grpDebug.Controls.Add(btnDebugBinary);
			grpDebug.Controls.Add(btnDebugCleaned);
			grpDebug.Controls.Add(btnTestSegmentation);
			grpDebug.Location = new Point(580, 372);
			grpDebug.Name = "grpDebug";
			grpDebug.Size = new Size(200, 165);
			grpDebug.TabIndex = 8;
			grpDebug.TabStop = false;
			grpDebug.Text = "Debug";
			// 
			// btnDebugContrast
			// 
			btnDebugContrast.Location = new Point(15, 25);
			btnDebugContrast.Name = "btnDebugContrast";
			btnDebugContrast.Size = new Size(170, 25);
			btnDebugContrast.TabIndex = 0;
			btnDebugContrast.Text = "Contrast";
			btnDebugContrast.UseVisualStyleBackColor = true;
			btnDebugContrast.Click += btnDebugContrast_Click;
			// 
			// btnDebugBinary
			// 
			btnDebugBinary.Location = new Point(15, 56);
			btnDebugBinary.Name = "btnDebugBinary";
			btnDebugBinary.Size = new Size(170, 25);
			btnDebugBinary.TabIndex = 1;
			btnDebugBinary.Text = "Binary";
			btnDebugBinary.UseVisualStyleBackColor = true;
			btnDebugBinary.Click += btnDebugBinary_Click;
			// 
			// btnDebugCleaned
			// 
			btnDebugCleaned.Location = new Point(15, 87);
			btnDebugCleaned.Name = "btnDebugCleaned";
			btnDebugCleaned.Size = new Size(170, 25);
			btnDebugCleaned.TabIndex = 2;
			btnDebugCleaned.Text = "Cleaned";
			btnDebugCleaned.UseVisualStyleBackColor = true;
			btnDebugCleaned.Click += btnDebugCleaned_Click;
			// 
			// btnTestSegmentation
			// 
			btnTestSegmentation.Location = new Point(15, 118);
			btnTestSegmentation.Name = "btnTestSegmentation";
			btnTestSegmentation.Size = new Size(170, 25);
			btnTestSegmentation.TabIndex = 3;
			btnTestSegmentation.Text = "Test Segmentation";
			btnTestSegmentation.UseVisualStyleBackColor = true;
			btnTestSegmentation.Click += btnTestSegmentation_Click;
			// 
			// picCleaned
			// 
			picCleaned.BorderStyle = BorderStyle.FixedSingle;
			picCleaned.Location = new Point(502, 41);
			picCleaned.Name = "picCleaned";
			picCleaned.Size = new Size(480, 320);
			picCleaned.SizeMode = PictureBoxSizeMode.Zoom;
			picCleaned.TabIndex = 6;
			picCleaned.TabStop = false;
			// 
			// grpThreshold
			// 
			grpThreshold.Controls.Add(lblBlockSize);
			grpThreshold.Controls.Add(nudBlockSize);
			grpThreshold.Controls.Add(lblC);
			grpThreshold.Controls.Add(nudC);
			grpThreshold.Controls.Add(lblMinArea);
			grpThreshold.Controls.Add(nudMinArea);
			grpThreshold.Controls.Add(lblSegmentOnThreshold);
			grpThreshold.Controls.Add(nudSegmentOnThreshold);
			grpThreshold.Location = new Point(374, 370);
			grpThreshold.Name = "grpThreshold";
			grpThreshold.Size = new Size(200, 180);
			grpThreshold.TabIndex = 7;
			grpThreshold.TabStop = false;
			grpThreshold.Text = "Pipeline params";
			// 
			// lblBlockSize
			// 
			lblBlockSize.AutoSize = true;
			lblBlockSize.Location = new Point(12, 28);
			lblBlockSize.Name = "lblBlockSize";
			lblBlockSize.Size = new Size(59, 15);
			lblBlockSize.TabIndex = 0;
			lblBlockSize.Text = "blockSize:";
			// 
			// nudBlockSize
			// 
			nudBlockSize.Location = new Point(100, 26);
			nudBlockSize.Maximum = new decimal(new int[] { 101, 0, 0, 0 });
			nudBlockSize.Minimum = new decimal(new int[] { 3, 0, 0, 0 });
			nudBlockSize.Name = "nudBlockSize";
			nudBlockSize.Size = new Size(80, 23);
			nudBlockSize.TabIndex = 1;
			nudBlockSize.Value = new decimal(new int[] { 11, 0, 0, 0 });
			// 
			// lblC
			// 
			lblC.AutoSize = true;
			lblC.Location = new Point(12, 62);
			lblC.Name = "lblC";
			lblC.Size = new Size(18, 15);
			lblC.TabIndex = 2;
			lblC.Text = "C:";
			// 
			// nudC
			// 
			nudC.DecimalPlaces = 1;
			nudC.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
			nudC.Location = new Point(100, 60);
			nudC.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
			nudC.Minimum = new decimal(new int[] { 50, 0, 0, int.MinValue });
			nudC.Name = "nudC";
			nudC.Size = new Size(80, 23);
			nudC.TabIndex = 3;
			nudC.Value = new decimal(new int[] { 20, 0, 0, 65536 });
			// 
			// lblMinArea
			// 
			lblMinArea.AutoSize = true;
			lblMinArea.Location = new Point(12, 96);
			lblMinArea.Name = "lblMinArea";
			lblMinArea.Size = new Size(55, 15);
			lblMinArea.TabIndex = 4;
			lblMinArea.Text = "minArea:";
			// 
			// nudMinArea
			// 
			nudMinArea.Location = new Point(100, 94);
			nudMinArea.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
			nudMinArea.Name = "nudMinArea";
			nudMinArea.Size = new Size(80, 23);
			nudMinArea.TabIndex = 5;
			nudMinArea.Value = new decimal(new int[] { 30, 0, 0, 0 });
			//
			// lblSegmentOnThreshold
			//
			lblSegmentOnThreshold.AutoSize = true;
			lblSegmentOnThreshold.Location = new Point(12, 130);
			lblSegmentOnThreshold.Name = "lblSegmentOnThreshold";
			lblSegmentOnThreshold.Size = new Size(72, 15);
			lblSegmentOnThreshold.TabIndex = 6;
			lblSegmentOnThreshold.Text = "7-Seg prah:";
			//
			// nudSegmentOnThreshold
			//
			nudSegmentOnThreshold.DecimalPlaces = 2;
			nudSegmentOnThreshold.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
			nudSegmentOnThreshold.Location = new Point(100, 128);
			nudSegmentOnThreshold.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
			nudSegmentOnThreshold.Minimum = new decimal(new int[] { 5, 0, 0, 131072 });
			nudSegmentOnThreshold.Name = "nudSegmentOnThreshold";
			nudSegmentOnThreshold.Size = new Size(80, 23);
			nudSegmentOnThreshold.TabIndex = 7;
			nudSegmentOnThreshold.Value = new decimal(new int[] { 35, 0, 0, 131072 });
			//
			// cboUrl
			// 
			cboUrl.DropDownStyle = ComboBoxStyle.DropDownList;
			cboUrl.FormattingEnabled = true;
			cboUrl.Location = new Point(12, 12);
			cboUrl.Name = "cboUrl";
			cboUrl.Size = new Size(480, 23);
			cboUrl.TabIndex = 0;
			// 
			// btnStart
			// 
			btnStart.Location = new Point(498, 11);
			btnStart.Name = "btnStart";
			btnStart.Size = new Size(75, 25);
			btnStart.TabIndex = 1;
			btnStart.Text = "START";
			btnStart.UseVisualStyleBackColor = true;
			btnStart.Click += btnStart_Click;
			// 
			// btnStop
			// 
			btnStop.Location = new Point(579, 11);
			btnStop.Name = "btnStop";
			btnStop.Size = new Size(75, 25);
			btnStop.TabIndex = 2;
			btnStop.Text = "STOP";
			btnStop.UseVisualStyleBackColor = true;
			btnStop.Click += btnStop_Click;
			// 
			// btnSave
			// 
			btnSave.Location = new Point(660, 11);
			btnSave.Name = "btnSave";
			btnSave.Size = new Size(75, 25);
			btnSave.TabIndex = 3;
			btnSave.Text = "SAVE";
			btnSave.UseVisualStyleBackColor = true;
			btnSave.Click += btnSave_Click;
			//
			// cboRecognitionMethod
			//
			cboRecognitionMethod.DropDownStyle = ComboBoxStyle.DropDownList;
			cboRecognitionMethod.FormattingEnabled = true;
			cboRecognitionMethod.Location = new Point(745, 12);
			cboRecognitionMethod.Name = "cboRecognitionMethod";
			cboRecognitionMethod.Size = new Size(100, 23);
			cboRecognitionMethod.TabIndex = 4;
			cboRecognitionMethod.SelectedIndexChanged += cboRecognitionMethod_SelectedIndexChanged;
			//
			// lblOnnxConfidence
			//
			lblOnnxConfidence.AutoSize = true;
			lblOnnxConfidence.Location = new Point(850, 16);
			lblOnnxConfidence.Name = "lblOnnxConfidence";
			lblOnnxConfidence.Size = new Size(35, 15);
			lblOnnxConfidence.Text = "Conf:";
			//
			// nudOnnxConfidence
			//
			nudOnnxConfidence.DecimalPlaces = 2;
			nudOnnxConfidence.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
			nudOnnxConfidence.Location = new Point(890, 12);
			nudOnnxConfidence.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
			nudOnnxConfidence.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
			nudOnnxConfidence.Name = "nudOnnxConfidence";
			nudOnnxConfidence.Size = new Size(60, 23);
			nudOnnxConfidence.TabIndex = 5;
			nudOnnxConfidence.Value = new decimal(new int[] { 50, 0, 0, 131072 });
			//
			// picFrame
			// 
			picFrame.BorderStyle = BorderStyle.FixedSingle;
			picFrame.Location = new Point(12, 41);
			picFrame.Name = "picFrame";
			picFrame.Size = new Size(480, 320);
			picFrame.SizeMode = PictureBoxSizeMode.Zoom;
			picFrame.TabIndex = 3;
			picFrame.TabStop = false;
			//
			// grpRoi
			//
			grpRoi.Controls.Add(btnRoi);
			grpRoi.Controls.Add(lblHeight);
			grpRoi.Controls.Add(lblWidth);
			grpRoi.Controls.Add(lblY);
			grpRoi.Controls.Add(lblX);
			grpRoi.Controls.Add(nudHeight);
			grpRoi.Controls.Add(nudWidth);
			grpRoi.Controls.Add(nudY);
			grpRoi.Controls.Add(nudX);
			grpRoi.Location = new Point(12, 365);
			grpRoi.Name = "grpRoi";
			grpRoi.Size = new Size(200, 170);
			grpRoi.TabIndex = 4;
			grpRoi.TabStop = false;
			grpRoi.Text = "ROI (%)";
			//
			// btnRoi
			//
			btnRoi.Location = new Point(119, 131);
			btnRoi.Name = "btnRoi";
			btnRoi.Size = new Size(75, 25);
			btnRoi.TabIndex = 8;
			btnRoi.Text = "ROI";
			btnRoi.UseVisualStyleBackColor = true;
			btnRoi.Click += btnRoi_Click;
			//
			// lblHeight
			//
			lblHeight.AutoSize = true;
			lblHeight.Location = new Point(12, 103);
			lblHeight.Name = "lblHeight";
			lblHeight.Size = new Size(46, 15);
			lblHeight.TabIndex = 7;
			lblHeight.Text = "Height:";
			//
			// lblWidth
			//
			lblWidth.AutoSize = true;
			lblWidth.Location = new Point(12, 76);
			lblWidth.Name = "lblWidth";
			lblWidth.Size = new Size(42, 15);
			lblWidth.TabIndex = 6;
			lblWidth.Text = "Width:";
			//
			// lblY
			//
			lblY.AutoSize = true;
			lblY.Location = new Point(12, 49);
			lblY.Name = "lblY";
			lblY.Size = new Size(17, 15);
			lblY.TabIndex = 5;
			lblY.Text = "Y:";
			//
			// lblX
			//
			lblX.AutoSize = true;
			lblX.Location = new Point(12, 22);
			lblX.Name = "lblX";
			lblX.Size = new Size(17, 15);
			lblX.TabIndex = 4;
			lblX.Text = "X:";
			//
			// nudHeight
			//
			nudHeight.Location = new Point(75, 101);
			nudHeight.Name = "nudHeight";
			nudHeight.Size = new Size(60, 23);
			nudHeight.TabIndex = 3;
			nudHeight.Value = new decimal(new int[] { 23, 0, 0, 0 });
			//
			// nudWidth
			//
			nudWidth.Location = new Point(75, 74);
			nudWidth.Name = "nudWidth";
			nudWidth.Size = new Size(60, 23);
			nudWidth.TabIndex = 2;
			nudWidth.Value = new decimal(new int[] { 17, 0, 0, 0 });
			//
			// nudY
			//
			nudY.Location = new Point(75, 47);
			nudY.Name = "nudY";
			nudY.Size = new Size(60, 23);
			nudY.TabIndex = 1;
			nudY.Value = new decimal(new int[] { 56, 0, 0, 0 });
			//
			// nudX
			//
			nudX.Location = new Point(75, 20);
			nudX.Name = "nudX";
			nudX.Size = new Size(60, 23);
			nudX.TabIndex = 0;
			nudX.Value = new decimal(new int[] { 22, 0, 0, 0 });
			// 
			// grpResize
			// 
			grpResize.Controls.Add(btnView);
			grpResize.Controls.Add(lblResize);
			grpResize.Controls.Add(nudResize);
			grpResize.Location = new Point(222, 372);
			grpResize.Name = "grpResize";
			grpResize.Size = new Size(146, 90);
			grpResize.TabIndex = 5;
			grpResize.TabStop = false;
			grpResize.Text = "Resize";
			// 
			// btnView
			// 
			btnView.Location = new Point(60, 59);
			btnView.Name = "btnView";
			btnView.Size = new Size(75, 25);
			btnView.TabIndex = 2;
			btnView.Text = "VIEW";
			btnView.UseVisualStyleBackColor = true;
			btnView.Click += btnView_Click;
			// 
			// lblResize
			// 
			lblResize.AutoSize = true;
			lblResize.Location = new Point(12, 28);
			lblResize.Name = "lblResize";
			lblResize.Size = new Size(42, 15);
			lblResize.TabIndex = 1;
			lblResize.Text = "Resize:";
			// 
			// nudResize
			// 
			nudResize.DecimalPlaces = 1;
			nudResize.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
			nudResize.Location = new Point(75, 26);
			nudResize.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			nudResize.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
			nudResize.Name = "nudResize";
			nudResize.Size = new Size(60, 23);
			nudResize.TabIndex = 0;
			nudResize.Value = new decimal(new int[] { 40, 0, 0, 65536 });
			// 
			// grpChangeMatrix
			// 
			grpChangeMatrix.Controls.Add(lblChangeMatrix);
			grpChangeMatrix.Controls.Add(cboChangeMatrix);
			grpChangeMatrix.Location = new Point(642, 730);
			grpChangeMatrix.Name = "grpChangeMatrix";
			grpChangeMatrix.Size = new Size(200, 90);
			grpChangeMatrix.TabIndex = 9;
			grpChangeMatrix.TabStop = false;
			grpChangeMatrix.Text = "ChangeMatrix";
			// 
			// lblChangeMatrix
			// 
			lblChangeMatrix.AutoSize = true;
			lblChangeMatrix.Location = new Point(12, 28);
			lblChangeMatrix.Name = "lblChangeMatrix";
			lblChangeMatrix.Size = new Size(65, 15);
			lblChangeMatrix.TabIndex = 0;
			lblChangeMatrix.Text = "Matrix size:";
			// 
			// cboChangeMatrix
			// 
			cboChangeMatrix.DropDownStyle = ComboBoxStyle.DropDownList;
			cboChangeMatrix.FormattingEnabled = true;
			cboChangeMatrix.Items.AddRange(new object[] { 16, 32, 64, 128 });
			cboChangeMatrix.Location = new Point(100, 26);
			cboChangeMatrix.Name = "cboChangeMatrix";
			cboChangeMatrix.Size = new Size(80, 23);
			cboChangeMatrix.TabIndex = 1;
			// 
			// picMatrix
			// 
			picMatrix.BorderStyle = BorderStyle.FixedSingle;
			picMatrix.Location = new Point(832, 372);
			picMatrix.Name = "picMatrix";
			picMatrix.Size = new Size(150, 118);
			picMatrix.SizeMode = PictureBoxSizeMode.Zoom;
			picMatrix.TabIndex = 10;
			picMatrix.TabStop = false;
			// 
			// grpBilateral
			// 
			grpBilateral.Controls.Add(lblBilateralD);
			grpBilateral.Controls.Add(nudBilateralD);
			grpBilateral.Controls.Add(lblBilateralSigmaColor);
			grpBilateral.Controls.Add(nudBilateralSigmaColor);
			grpBilateral.Controls.Add(lblBilateralSigmaSpace);
			grpBilateral.Controls.Add(nudBilateralSigmaSpace);
			grpBilateral.Location = new Point(12, 730);
			grpBilateral.Name = "grpBilateral";
			grpBilateral.Size = new Size(200, 120);
			grpBilateral.TabIndex = 11;
			grpBilateral.TabStop = false;
			grpBilateral.Text = "Bilateral Filter";
			// 
			// lblBilateralD
			// 
			lblBilateralD.AutoSize = true;
			lblBilateralD.Location = new Point(10, 26);
			lblBilateralD.Name = "lblBilateralD";
			lblBilateralD.Size = new Size(76, 15);
			lblBilateralD.TabIndex = 0;
			lblBilateralD.Text = "D (diameter):";
			// 
			// nudBilateralD
			// 
			nudBilateralD.Location = new Point(100, 24);
			nudBilateralD.Maximum = new decimal(new int[] { 31, 0, 0, 0 });
			nudBilateralD.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudBilateralD.Name = "nudBilateralD";
			nudBilateralD.Size = new Size(80, 23);
			nudBilateralD.TabIndex = 1;
			nudBilateralD.Value = new decimal(new int[] { 5, 0, 0, 0 });
			// 
			// lblBilateralSigmaColor
			// 
			lblBilateralSigmaColor.AutoSize = true;
			lblBilateralSigmaColor.Location = new Point(10, 56);
			lblBilateralSigmaColor.Name = "lblBilateralSigmaColor";
			lblBilateralSigmaColor.Size = new Size(72, 15);
			lblBilateralSigmaColor.TabIndex = 2;
			lblBilateralSigmaColor.Text = "SigmaColor:";
			// 
			// nudBilateralSigmaColor
			// 
			nudBilateralSigmaColor.Location = new Point(100, 54);
			nudBilateralSigmaColor.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
			nudBilateralSigmaColor.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudBilateralSigmaColor.Name = "nudBilateralSigmaColor";
			nudBilateralSigmaColor.Size = new Size(80, 23);
			nudBilateralSigmaColor.TabIndex = 3;
			nudBilateralSigmaColor.Value = new decimal(new int[] { 40, 0, 0, 0 });
			// 
			// lblBilateralSigmaSpace
			// 
			lblBilateralSigmaSpace.AutoSize = true;
			lblBilateralSigmaSpace.Location = new Point(10, 86);
			lblBilateralSigmaSpace.Name = "lblBilateralSigmaSpace";
			lblBilateralSigmaSpace.Size = new Size(74, 15);
			lblBilateralSigmaSpace.TabIndex = 4;
			lblBilateralSigmaSpace.Text = "SigmaSpace:";
			// 
			// nudBilateralSigmaSpace
			// 
			nudBilateralSigmaSpace.Location = new Point(100, 84);
			nudBilateralSigmaSpace.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
			nudBilateralSigmaSpace.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudBilateralSigmaSpace.Name = "nudBilateralSigmaSpace";
			nudBilateralSigmaSpace.Size = new Size(80, 23);
			nudBilateralSigmaSpace.TabIndex = 5;
			nudBilateralSigmaSpace.Value = new decimal(new int[] { 40, 0, 0, 0 });
			// 
			// grpClahe
			// 
			grpClahe.Controls.Add(lblCLAHEClipLimit);
			grpClahe.Controls.Add(nudCLAHEClipLimit);
			grpClahe.Controls.Add(lblCLAHETileGridSizeX);
			grpClahe.Controls.Add(nudCLAHETileGridSizeX);
			grpClahe.Controls.Add(lblCLAHETileGridSizeY);
			grpClahe.Controls.Add(nudCLAHETileGridSizeY);
			grpClahe.Location = new Point(222, 730);
			grpClahe.Name = "grpClahe";
			grpClahe.Size = new Size(200, 120);
			grpClahe.TabIndex = 12;
			grpClahe.TabStop = false;
			grpClahe.Text = "CLAHE";
			// 
			// lblCLAHEClipLimit
			// 
			lblCLAHEClipLimit.AutoSize = true;
			lblCLAHEClipLimit.Location = new Point(10, 26);
			lblCLAHEClipLimit.Name = "lblCLAHEClipLimit";
			lblCLAHEClipLimit.Size = new Size(58, 15);
			lblCLAHEClipLimit.TabIndex = 0;
			lblCLAHEClipLimit.Text = "ClipLimit:";
			// 
			// nudCLAHEClipLimit
			// 
			nudCLAHEClipLimit.DecimalPlaces = 1;
			nudCLAHEClipLimit.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
			nudCLAHEClipLimit.Location = new Point(100, 24);
			nudCLAHEClipLimit.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
			nudCLAHEClipLimit.Name = "nudCLAHEClipLimit";
			nudCLAHEClipLimit.Size = new Size(80, 23);
			nudCLAHEClipLimit.TabIndex = 1;
			nudCLAHEClipLimit.Value = new decimal(new int[] { 50, 0, 0, 65536 });
			// 
			// lblCLAHETileGridSizeX
			// 
			lblCLAHETileGridSizeX.AutoSize = true;
			lblCLAHETileGridSizeX.Location = new Point(10, 56);
			lblCLAHETileGridSizeX.Name = "lblCLAHETileGridSizeX";
			lblCLAHETileGridSizeX.Size = new Size(81, 15);
			lblCLAHETileGridSizeX.TabIndex = 2;
			lblCLAHETileGridSizeX.Text = "TileGridSize X:";
			// 
			// nudCLAHETileGridSizeX
			// 
			nudCLAHETileGridSizeX.Location = new Point(100, 54);
			nudCLAHETileGridSizeX.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
			nudCLAHETileGridSizeX.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			nudCLAHETileGridSizeX.Name = "nudCLAHETileGridSizeX";
			nudCLAHETileGridSizeX.Size = new Size(80, 23);
			nudCLAHETileGridSizeX.TabIndex = 3;
			nudCLAHETileGridSizeX.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// lblCLAHETileGridSizeY
			// 
			lblCLAHETileGridSizeY.AutoSize = true;
			lblCLAHETileGridSizeY.Location = new Point(10, 86);
			lblCLAHETileGridSizeY.Name = "lblCLAHETileGridSizeY";
			lblCLAHETileGridSizeY.Size = new Size(81, 15);
			lblCLAHETileGridSizeY.TabIndex = 4;
			lblCLAHETileGridSizeY.Text = "TileGridSize Y:";
			// 
			// nudCLAHETileGridSizeY
			// 
			nudCLAHETileGridSizeY.Location = new Point(100, 84);
			nudCLAHETileGridSizeY.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
			nudCLAHETileGridSizeY.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
			nudCLAHETileGridSizeY.Name = "nudCLAHETileGridSizeY";
			nudCLAHETileGridSizeY.Size = new Size(80, 23);
			nudCLAHETileGridSizeY.TabIndex = 5;
			nudCLAHETileGridSizeY.Value = new decimal(new int[] { 2, 0, 0, 0 });
			// 
			// grpMorphology
			// 
			grpMorphology.Controls.Add(lblMorphologyKernelSize);
			grpMorphology.Controls.Add(nudMorphologyKernelSize);
			grpMorphology.Controls.Add(lblMorphologyOpenIterations);
			grpMorphology.Controls.Add(nudMorphologyOpenIterations);
			grpMorphology.Controls.Add(lblMorphologyCloseIterations);
			grpMorphology.Controls.Add(nudMorphologyCloseIterations);
			grpMorphology.Location = new Point(432, 730);
			grpMorphology.Name = "grpMorphology";
			grpMorphology.Size = new Size(200, 120);
			grpMorphology.TabIndex = 13;
			grpMorphology.TabStop = false;
			grpMorphology.Text = "Morphology";
			// 
			// lblMorphologyKernelSize
			// 
			lblMorphologyKernelSize.AutoSize = true;
			lblMorphologyKernelSize.Location = new Point(10, 26);
			lblMorphologyKernelSize.Name = "lblMorphologyKernelSize";
			lblMorphologyKernelSize.Size = new Size(63, 15);
			lblMorphologyKernelSize.TabIndex = 0;
			lblMorphologyKernelSize.Text = "KernelSize:";
			// 
			// nudMorphologyKernelSize
			// 
			nudMorphologyKernelSize.Increment = new decimal(new int[] { 2, 0, 0, 0 });
			nudMorphologyKernelSize.Location = new Point(100, 24);
			nudMorphologyKernelSize.Maximum = new decimal(new int[] { 21, 0, 0, 0 });
			nudMorphologyKernelSize.Minimum = new decimal(new int[] { 3, 0, 0, 0 });
			nudMorphologyKernelSize.Name = "nudMorphologyKernelSize";
			nudMorphologyKernelSize.Size = new Size(80, 23);
			nudMorphologyKernelSize.TabIndex = 1;
			nudMorphologyKernelSize.Value = new decimal(new int[] { 3, 0, 0, 0 });
			// 
			// lblMorphologyOpenIterations
			// 
			lblMorphologyOpenIterations.AutoSize = true;
			lblMorphologyOpenIterations.Location = new Point(10, 56);
			lblMorphologyOpenIterations.Name = "lblMorphologyOpenIterations";
			lblMorphologyOpenIterations.Size = new Size(91, 15);
			lblMorphologyOpenIterations.TabIndex = 2;
			lblMorphologyOpenIterations.Text = "Open Iterations:";
			// 
			// nudMorphologyOpenIterations
			// 
			nudMorphologyOpenIterations.Location = new Point(100, 54);
			nudMorphologyOpenIterations.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			nudMorphologyOpenIterations.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudMorphologyOpenIterations.Name = "nudMorphologyOpenIterations";
			nudMorphologyOpenIterations.Size = new Size(80, 23);
			nudMorphologyOpenIterations.TabIndex = 3;
			nudMorphologyOpenIterations.Value = new decimal(new int[] { 1, 0, 0, 0 });
			// 
			// lblMorphologyCloseIterations
			// 
			lblMorphologyCloseIterations.AutoSize = true;
			lblMorphologyCloseIterations.Location = new Point(10, 86);
			lblMorphologyCloseIterations.Name = "lblMorphologyCloseIterations";
			lblMorphologyCloseIterations.Size = new Size(91, 15);
			lblMorphologyCloseIterations.TabIndex = 4;
			lblMorphologyCloseIterations.Text = "Close Iterations:";
			// 
			// nudMorphologyCloseIterations
			// 
			nudMorphologyCloseIterations.Location = new Point(100, 84);
			nudMorphologyCloseIterations.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			nudMorphologyCloseIterations.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudMorphologyCloseIterations.Name = "nudMorphologyCloseIterations";
			nudMorphologyCloseIterations.Size = new Size(80, 23);
			nudMorphologyCloseIterations.TabIndex = 5;
			nudMorphologyCloseIterations.Value = new decimal(new int[] { 1, 0, 0, 0 });
			//
			// grpNumReg
			//
			grpNumReg.Controls.Add(lvNumReg);
			grpNumReg.Controls.Add(btnNumRegClear);
			grpNumReg.Controls.Add(lblFromLeft);
			grpNumReg.Controls.Add(nudFromLeft);
			grpNumReg.Controls.Add(lblFromRight);
			grpNumReg.Controls.Add(nudFromRight);
			grpNumReg.Controls.Add(picLeft);
			grpNumReg.Controls.Add(picRight);
			grpNumReg.Location = new Point(12, 550);
			grpNumReg.Name = "grpNumReg";
			grpNumReg.Size = new Size(830, 190);
			grpNumReg.TabIndex = 14;
			grpNumReg.TabStop = false;
			grpNumReg.Text = "NumReg (Split & Recognize)";
			//
			// lvNumReg
			//
			lvNumReg.Location = new Point(620, 24);
			lvNumReg.Name = "lvNumReg";
			lvNumReg.Size = new Size(200, 131);
			lvNumReg.View = View.Details;
			lvNumReg.FullRowSelect = true;
			lvNumReg.GridLines = true;
			lvNumReg.HideSelection = false;
			lvNumReg.Columns.AddRange(new ColumnHeader[] { colNumTime, colNumValue });
			lvNumReg.TabIndex = 6;
			//
			// colNumTime
			//
			colNumTime.Text = "Time";
			colNumTime.Width = 125;
			//
			// colNumValue
			//
			colNumValue.Text = "Value";
			colNumValue.Width = 70;
			//
			// btnNumRegClear
			//
			btnNumRegClear.Location = new Point(620, 160);
			btnNumRegClear.Name = "btnNumRegClear";
			btnNumRegClear.Size = new Size(200, 23);
			btnNumRegClear.TabIndex = 7;
			btnNumRegClear.Text = "Clear history";
			btnNumRegClear.UseVisualStyleBackColor = true;
			btnNumRegClear.Click += btnNumRegClear_Click;
			//
			// lblFromLeft
			//
			lblFromLeft.AutoSize = true;
			lblFromLeft.Location = new Point(15, 26);
			lblFromLeft.Name = "lblFromLeft";
			lblFromLeft.Size = new Size(69, 15);
			lblFromLeft.TabIndex = 0;
			lblFromLeft.Text = "FromLeft %:";
			//
			// nudFromLeft
			//
			nudFromLeft.Location = new Point(100, 24);
			nudFromLeft.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
			nudFromLeft.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudFromLeft.Name = "nudFromLeft";
			nudFromLeft.Size = new Size(80, 23);
			nudFromLeft.TabIndex = 1;
			nudFromLeft.Value = new decimal(new int[] { 50, 0, 0, 0 });
			//
			// lblFromRight
			//
			lblFromRight.AutoSize = true;
			lblFromRight.Location = new Point(420, 26);
			lblFromRight.Name = "lblFromRight";
			lblFromRight.Size = new Size(77, 15);
			lblFromRight.TabIndex = 2;
			lblFromRight.Text = "FromRight %:";
			//
			// nudFromRight
			//
			nudFromRight.Location = new Point(510, 24);
			nudFromRight.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
			nudFromRight.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudFromRight.Name = "nudFromRight";
			nudFromRight.Size = new Size(80, 23);
			nudFromRight.TabIndex = 3;
			nudFromRight.Value = new decimal(new int[] { 50, 0, 0, 0 });
			//
			// picLeft
			//
			picLeft.BorderStyle = BorderStyle.FixedSingle;
			picLeft.Location = new Point(15, 55);
			picLeft.Name = "picLeft";
			picLeft.Size = new Size(180, 100);
			picLeft.SizeMode = PictureBoxSizeMode.Zoom;
			picLeft.TabIndex = 4;
			picLeft.TabStop = false;
			//
			// picRight
			//
			picRight.BorderStyle = BorderStyle.FixedSingle;
			picRight.Location = new Point(420, 55);
			picRight.Name = "picRight";
			picRight.Size = new Size(180, 100);
			picRight.SizeMode = PictureBoxSizeMode.Zoom;
			picRight.TabIndex = 5;
			picRight.TabStop = false;
			//
			// grpLed
			//
			grpLed.Controls.Add(chkLed);
			grpLed.Controls.Add(trkLedIntensity);
			grpLed.Controls.Add(lblLedValue);
			grpLed.Location = new Point(842, 500);
			grpLed.Name = "grpLed";
			grpLed.Size = new Size(160, 130);
			grpLed.TabIndex = 15;
			grpLed.TabStop = false;
			grpLed.Text = "LED (ESP32-CAM)";
			//
			// chkLed
			//
			chkLed.AutoSize = true;
			chkLed.Location = new Point(15, 25);
			chkLed.Name = "chkLed";
			chkLed.Size = new Size(60, 19);
			chkLed.TabIndex = 0;
			chkLed.Text = "LED ON";
			chkLed.UseVisualStyleBackColor = true;
			chkLed.CheckedChanged += chkLed_CheckedChanged;
			//
			// trkLedIntensity
			//
			trkLedIntensity.Location = new Point(10, 52);
			trkLedIntensity.Name = "trkLedIntensity";
			trkLedIntensity.Size = new Size(140, 45);
			trkLedIntensity.Minimum = 0;
			trkLedIntensity.Maximum = 255;
			trkLedIntensity.TickFrequency = 16;
			trkLedIntensity.SmallChange = 1;
			trkLedIntensity.LargeChange = 16;
			trkLedIntensity.TabIndex = 1;
			trkLedIntensity.Value = 0;
			trkLedIntensity.Scroll += trkLedIntensity_Scroll;
			//
			// lblLedValue
			//
			lblLedValue.AutoSize = true;
			lblLedValue.Location = new Point(15, 100);
			lblLedValue.Name = "lblLedValue";
			lblLedValue.Size = new Size(80, 15);
			lblLedValue.TabIndex = 2;
			lblLedValue.Text = "Intenzita: 0";
			//
			// MainForm
			//
			ClientSize = new Size(1012, 900);
			Controls.Add(grpResize);
			Controls.Add(grpRoi);
			Controls.Add(picFrame);
			Controls.Add(nudOnnxConfidence);
			Controls.Add(lblOnnxConfidence);
			Controls.Add(cboRecognitionMethod);
			Controls.Add(btnSave);
			Controls.Add(btnStop);
			Controls.Add(btnStart);
			Controls.Add(cboUrl);
			Controls.Add(picCleaned);
			Controls.Add(grpThreshold);
			Controls.Add(grpDebug);
			Controls.Add(grpChangeMatrix);
			Controls.Add(picMatrix);
			Controls.Add(grpBilateral);
			Controls.Add(grpClahe);
			Controls.Add(grpMorphology);
			Controls.Add(grpNumReg);
			Controls.Add(grpLed);
			Name = "MainForm";
			Text = "ESP32-CAM – nultá verzia";
			grpDebug.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)picCleaned).EndInit();
			grpThreshold.ResumeLayout(false);
			grpThreshold.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudBlockSize).EndInit();
			((System.ComponentModel.ISupportInitialize)nudC).EndInit();
			((System.ComponentModel.ISupportInitialize)nudMinArea).EndInit();
			((System.ComponentModel.ISupportInitialize)nudSegmentOnThreshold).EndInit();
			((System.ComponentModel.ISupportInitialize)picFrame).EndInit();
			grpRoi.ResumeLayout(false);
			grpRoi.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudHeight).EndInit();
			((System.ComponentModel.ISupportInitialize)nudWidth).EndInit();
			((System.ComponentModel.ISupportInitialize)nudY).EndInit();
			((System.ComponentModel.ISupportInitialize)nudX).EndInit();
			grpResize.ResumeLayout(false);
			grpResize.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudResize).EndInit();
			grpChangeMatrix.ResumeLayout(false);
			grpChangeMatrix.PerformLayout();
			((System.ComponentModel.ISupportInitialize)picMatrix).EndInit();
			grpBilateral.ResumeLayout(false);
			grpBilateral.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudBilateralD).EndInit();
			((System.ComponentModel.ISupportInitialize)nudBilateralSigmaColor).EndInit();
			((System.ComponentModel.ISupportInitialize)nudBilateralSigmaSpace).EndInit();
			grpClahe.ResumeLayout(false);
			grpClahe.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudCLAHEClipLimit).EndInit();
			((System.ComponentModel.ISupportInitialize)nudCLAHETileGridSizeX).EndInit();
			((System.ComponentModel.ISupportInitialize)nudCLAHETileGridSizeY).EndInit();
			grpMorphology.ResumeLayout(false);
			grpMorphology.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudMorphologyKernelSize).EndInit();
			((System.ComponentModel.ISupportInitialize)nudMorphologyOpenIterations).EndInit();
			((System.ComponentModel.ISupportInitialize)nudMorphologyCloseIterations).EndInit();
			grpNumReg.ResumeLayout(false);
			grpNumReg.PerformLayout();
			((System.ComponentModel.ISupportInitialize)nudFromLeft).EndInit();
			((System.ComponentModel.ISupportInitialize)nudFromRight).EndInit();
			((System.ComponentModel.ISupportInitialize)picLeft).EndInit();
			((System.ComponentModel.ISupportInitialize)picRight).EndInit();
			grpLed.ResumeLayout(false);
			grpLed.PerformLayout();
			((System.ComponentModel.ISupportInitialize)trkLedIntensity).EndInit();
			ResumeLayout(false);
		}
	}

}
