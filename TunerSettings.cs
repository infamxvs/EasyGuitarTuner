namespace EasyGuitarTuner;

// Punct unic de reglaj pentru toti parametrii acordorului.
// Modifica valorile aici si reconstruieste aplicatia (dotnet build).
public static class TunerSettings
{
	// --- Captura audio ---
	public const int SampleRateHz = 48000; // rata nativa pe majoritatea telefoanelor Android/iOS (evita resampling)
	public const int AnalysisWindowSamples = 32768; // fereastra FFT (~683ms la 48000 Hz) — precizie mai buna jos (E2) (~3 analize/sec)

	// --- Detectie pitch ---
	public const double NoiseFloorRms = 0.001; // pragul de VOLUM de la care incepe analiza (mai mare = ignora sunete slabe)
	public const double MinFrequencyHz = 60; // putin sub B1 (61.74 Hz); tampon fata de hum-ul de 50 Hz
	public const double MaxFrequencyHz = 1000; // peste G5 (783.99 Hz) cu marja pentru +50 centi
	public const int HarmonicCount = 7; // armonice folosite de HPS

	// --- Stabilizare frecventa ---
	public const int MedianWindow = 5; // cate valori brute intra in filtrul median (respinge outlieri)
	public const double FrequencySmoothingAlpha = 0.4; // EMA peste mediana (mai mic = mai lin, mai mare = mai reactiv)
	public const double JumpThreshold = 0.15; // peste acest salt relativ = schimbare de coarda (snap)
	public const int HoldCycles = 0; // cicluri de liniste cat se pastreaza ultima nota inainte de reset (~1s la 3 analize/sec)

	// --- Analiza nota ---
	public const double ReferencePitchHz = 440.0; // A4 — referinta pentru scala cromatica egal temperata
	public const double InTuneToleranceCents = 5.0; // sub atatia centi nota e considerata acordata

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
