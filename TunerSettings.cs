namespace EasyGuitarTuner;

// Algoritmul de detectie a frecventei fundamentale. Schimba ActivePitchAlgorithm si reconstruieste.
public enum PitchAlgorithm
{
	Hps, // Harmonic Product Spectrum (spectral, FFT) — algoritmul curent
	Yin  // YIN (domeniul timpului) — alternativ
}

// Punct unic de reglaj pentru toti parametrii acordorului.
// Modifica valorile aici si reconstruieste aplicatia (dotnet build).
public static class TunerSettings
{
	// --- Comutator algoritm detectie (la compilare) ---
	// Alege ce detector ruleaza. Schimba valoarea si reconstruieste aplicatia.
	public const PitchAlgorithm ActivePitchAlgorithm = PitchAlgorithm.Yin;
	//public const PitchAlgorithm ActivePitchAlgorithm = PitchAlgorithm.Hps;

	// --- Captura audio (comun ambilor algoritmi) ---
	public const int SampleRateHz = 48000; // rata nativa pe majoritatea telefoanelor Android/iOS (evita resampling)
	public const int AnalysisWindowSamples = 32768; // fereastra de captura (~683ms la 48000 Hz); YIN foloseste doar un subset (YinWindowSamples)

	// --- Detectie pitch HPS (algoritmul curent) ---
	public const double NoiseFloorRms = 0.001; // pragul de VOLUM de la care incepe analiza (comun ambilor algoritmi)
	public const double MinFrequencyHz = 60; // putin sub B1 (61.74 Hz); tampon fata de hum-ul de 50 Hz
	public const double MaxFrequencyHz = 1000; // peste G5 (783.99 Hz) cu marja pentru +50 centi
	public const int HarmonicCount = 7; // armonice folosite de HPS

	// --- Detectie pitch YIN (alternativ, total independent de HPS) ---
	// Activ doar cand ActivePitchAlgorithm = PitchAlgorithm.Yin. Pragul de volum se reia din NoiseFloorRms (comun).
	public const int YinWindowSamples = 4096; // recomandat 2048-4096; 4096 acopera >= 2 perioade din E2 (82 Hz) la 48 kHz, echilibru precizie/latenta
	public const double YinThreshold = 0.10; // valoarea canonica din lucrarea de Cheveigne (2002); 0.10-0.15 uzual, creste spre 0.15 in medii zgomotoase
	public const double YinMinFrequencyHz = 60; // limita de jos a cautarii (tau maxim)
	public const double YinMaxFrequencyHz = 1000; // limita de sus a cautarii (tau minim)
	public const double YinAperiodicityCutoff = 0.80; // gate de zgomot: dip > 0.80 (claritate < 20%) => semnal neperiodic, fara nota

	// --- Stabilizare frecventa ---
	public const int MedianWindow = 10; // cate valori brute intra in filtrul median (respinge outlieri)
	public const double FrequencySmoothingAlpha = 0.8; // EMA peste mediana (mai mic = mai lin, mai mare = mai reactiv)
	public const double JumpThreshold = 0.15; // peste acest salt relativ = schimbare de coarda (snap)
	public const int HoldCycles = 0; // cicluri de liniste cat se pastreaza ultima nota inainte de reset (~1s la 3 analize/sec)

	// --- Analiza nota ---
	public const double ReferencePitchHz = 440.0; // A4 — referinta pentru scala cromatica egal temperata
	public const double InTuneToleranceCents = 2.0; // sub atatia centi nota e considerata acordata

	// --- Afisaj / ac indicator ---
	public const double CentsDeadband = 2.0; // sub atatia centi acul sta fix pe 0 (fara micro-tremur)
	public const double AngleSmoothingAlpha = 0.6; // EMA pe unghiul tinta
	public const double MaxCentsForNeedle = 50.0; // centi la capatul cadranului
	public const double AngleLerpFactor = 0.3; // viteza de glisare a acului spre tinta (per cadru)
	public const double AngleSettleThreshold = 0.05; // sub acest delta acul e considerat ajuns (opreste animatia)
	public const int FrameIntervalMs = 16; // ~60 FPS cat timp acul se misca

	// --- Calibrare unghi ac (in grade) ---
	// Offset de centru: unghiul acului cand nota e perfecta (cents = 0). Ajusteaza-l ca varful sa cada fix pe marcajul "0".
	public const double NeedleAngleOffsetDegrees = 0.0;
	// Amplitudine spre dreapta: cate grade roteste acul ca sa atinga marcajul "+50" (valoare pozitiva).
	public const double MaxNeedleAnglePositiveDegrees = 50.0;
	// Amplitudine spre stanga: cate grade roteste acul ca sa atinga marcajul "-50" (valoare pozitiva, magnitudine).
	public const double MaxNeedleAngleNegativeDegrees = 50.0;

	// --- Calibrare grafica ac (fractii din imaginea de fundal) ---
	public const float PivotXFraction = 0.5f;
	public const float PivotYFraction = 0.56f;

	// --- Test calibrare ---
	// double.NaN = detectie normala din microfon. Orice alta valoare forteaza acul exact la acei centi (pentru screenshot).
	public const double ForcedCentsForCalibration = double.NaN;
}
