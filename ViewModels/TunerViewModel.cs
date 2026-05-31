using System.ComponentModel;
using System.Runtime.CompilerServices;
using EasyGuitarTuner.Models;
using EasyGuitarTuner.Services;

namespace EasyGuitarTuner.ViewModels;

public class TunerViewModel : INotifyPropertyChanged
{
	// Degrade dupa magnitudinea centilor: verde (acordat) -> galben -> rosu -> rosu inchis (extreme ±50).
	static readonly (double Cents, Color Color)[] ColorStops =
	{
		(0.0, Color.FromArgb("#4FC04F")),  // verde — acordat
		(8.0, Color.FromArgb("#F2A640")),  // galben/chihlimbar
		(25.0, Color.FromArgb("#D83A2E")), // rosu
		(50.0, Color.FromArgb("#6E0F0F")), // rosu inchis — extreme
	};

	// Culoarea afisajului cand nu exista semnal (NoteText = "—").
	static readonly Color IdleColor = Color.FromArgb("#F2A640");

	// Echivalenta nume nota: anglo-saxon (international) -> latin (solfegiu).
	static readonly Dictionary<string, string> LatinNoteNames = new()
	{
		["C"] = "Do", ["C#"] = "Do#",
		["D"] = "Re", ["D#"] = "Re#",
		["E"] = "Mi",
		["F"] = "Fa", ["F#"] = "Fa#",
		["G"] = "Sol", ["G#"] = "Sol#",
		["A"] = "La", ["A#"] = "La#",
		["B"] = "Si",
	};

	readonly IAudioCaptureService _audioCaptureService;
	readonly IPitchDetectorService _pitchDetectorService;
	readonly IFrequencyStabilizer _frequencyStabilizer;
	readonly INoteAnalyzerService _noteAnalyzerService;
	readonly ISessionLogger _logger;

	string _noteText = "—";
	string _octaveText = string.Empty;
	string _latinNoteText = string.Empty;
	string _noteSeparatorText = string.Empty;
	string _frequencyText = "0.0 Hz";
	string _centsText = string.Empty;
	Color _noteColor = IdleColor;
	Color _frequencyColor = IdleColor;
	double _needleAngle;
	bool _isListening;
	double _smoothedAngle;

	public TunerViewModel(
		IAudioCaptureService audioCaptureService,
		IPitchDetectorService pitchDetectorService,
		IFrequencyStabilizer frequencyStabilizer,
		INoteAnalyzerService noteAnalyzerService,
		ISessionLogger logger)
	{
		_audioCaptureService = audioCaptureService;
		_pitchDetectorService = pitchDetectorService;
		_frequencyStabilizer = frequencyStabilizer;
		_noteAnalyzerService = noteAnalyzerService;
		_logger = logger;

		_audioCaptureService.OnAudioCaptured += OnAudioCaptured;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string NoteText
	{
		get => _noteText;
		private set => SetProperty(ref _noteText, value);
	}

	public string OctaveText
	{
		get => _octaveText;
		private set => SetProperty(ref _octaveText, value);
	}

	public string LatinNoteText
	{
		get => _latinNoteText;
		private set => SetProperty(ref _latinNoteText, value);
	}

	public string NoteSeparatorText
	{
		get => _noteSeparatorText;
		private set => SetProperty(ref _noteSeparatorText, value);
	}

	public string FrequencyText
	{
		get => _frequencyText;
		private set => SetProperty(ref _frequencyText, value);
	}

	public string CentsText
	{
		get => _centsText;
		private set => SetProperty(ref _centsText, value);
	}

	public Color NoteColor
	{
		get => _noteColor;
		private set => SetProperty(ref _noteColor, value);
	}

	public Color FrequencyColor
	{
		get => _frequencyColor;
		private set => SetProperty(ref _frequencyColor, value);
	}

	public double NeedleAngle
	{
		get => _needleAngle;
		private set => SetProperty(ref _needleAngle, value);
	}

	public async Task StartAsync()
	{
		if (_isListening)
			return;

		await _audioCaptureService.StartAsync();
		_isListening = true;
	}

	public async Task StopAsync()
	{
		if (!_isListening)
			return;

		await _audioCaptureService.StopAsync();
		_frequencyStabilizer.Reset();
		_isListening = false;
	}

	void OnAudioCaptured(object? sender, byte[] pcmData)
	{
		var rms = ComputeRms(pcmData);
		var rawFrequency = _pitchDetectorService.DetectPitch(pcmData, _audioCaptureService.SampleRate);
		var stableFrequency = _frequencyStabilizer.Stabilize(rawFrequency);

		var noteResult = _noteAnalyzerService.Analyze(stableFrequency);

		_logger.Log($"RMS={rms:F4} | RAW={rawFrequency:F2} Hz | SMOOTH={stableFrequency:F2} Hz | " +
		            $"NOTA={noteResult.NoteName}{noteResult.Octave} | " +
		            $"CENTS={noteResult.CentsDeviation:+0.0;-0.0;0} | {noteResult.Status}");

		MainThread.BeginInvokeOnMainThread(() => UpdateUi(noteResult));
	}

	static double ComputeRms(byte[] pcmData)
	{
		if (pcmData.Length < 2)
			return 0;

		var sampleCount = pcmData.Length / 2;
		var sumSquares = 0.0;

		for (var i = 0; i < sampleCount; i++)
		{
			var sample = BitConverter.ToInt16(pcmData, i * 2) / 32768.0;
			sumSquares += sample * sample;
		}

		return Math.Sqrt(sumSquares / sampleCount);
	}

	void UpdateUi(NoteResult noteResult)
	{
		if (!double.IsNaN(TunerSettings.ForcedCentsForCalibration))
		{
			ApplyForcedCalibration();
			return;
		}

		if (noteResult.FrequencyHz <= 0)
		{
			NoteText = "—";
			OctaveText = string.Empty;
			LatinNoteText = string.Empty;
			NoteSeparatorText = string.Empty;
			FrequencyText = "0.0 Hz";
			CentsText = string.Empty;
			NoteColor = IdleColor;
			FrequencyColor = IdleColor;
			_smoothedAngle = 0;
			NeedleAngle = 0;
			return;
		}

		NoteText = noteResult.NoteName;
		OctaveText = noteResult.Octave.ToString();
		LatinNoteText = ToLatinName(noteResult.NoteName);
		NoteSeparatorText = "/";
		FrequencyText = $"{noteResult.FrequencyHz:F1} Hz";
		CentsText = $"{noteResult.CentsDeviation:+0;-0;0}";
		UpdateDisplayColors(noteResult.CentsDeviation);

		var targetAngle = MapCentsToAngle(noteResult.CentsDeviation);
		_smoothedAngle = _smoothedAngle * (1 - TunerSettings.AngleSmoothingAlpha) +
		                 targetAngle * TunerSettings.AngleSmoothingAlpha;
		NeedleAngle = _smoothedAngle;
	}

	// Fortare pentru calibrare: ignora microfonul si fixeaza acul exact la centii din TunerSettings.
	void ApplyForcedCalibration()
	{
		var forcedCents = TunerSettings.ForcedCentsForCalibration;
		CentsText = $"{forcedCents:+0;-0;0}";
		var angle = MapCentsToAngle(forcedCents);
		_smoothedAngle = angle;
		NeedleAngle = angle;
	}

	static string ToLatinName(string noteName) =>
		LatinNoteNames.TryGetValue(noteName, out var latin) ? latin : noteName;

	// Culoarea textului interpolata continuu dupa magnitudinea abaterii (degrade verde -> galben -> rosu -> rosu inchis).
	void UpdateDisplayColors(double cents)
	{
		var noteColor = GradientColorFor(Math.Abs(cents));
		NoteColor = noteColor;
		FrequencyColor = Dim(noteColor, 0.9f); // frecventa pastreaza nuanta usor mai inchisa, ca pana acum
	}

	// Cauta intervalul de ancore care contine magnitudinea si interpoleaza liniar intre culorile lor.
	static Color GradientColorFor(double magnitude)
	{
		magnitude = Math.Clamp(magnitude, ColorStops[0].Cents, ColorStops[^1].Cents);

		for (var i = 1; i < ColorStops.Length; i++)
		{
			if (magnitude > ColorStops[i].Cents)
				continue;

			var (lowCents, lowColor) = ColorStops[i - 1];
			var (highCents, highColor) = ColorStops[i];
			var t = (magnitude - lowCents) / (highCents - lowCents);
			return Lerp(lowColor, highColor, t);
		}

		return ColorStops[^1].Color;
	}

	static Color Lerp(Color from, Color to, double t) =>
		new(
			(float)(from.Red + (to.Red - from.Red) * t),
			(float)(from.Green + (to.Green - from.Green) * t),
			(float)(from.Blue + (to.Blue - from.Blue) * t));

	static Color Dim(Color color, float factor) =>
		new(color.Red * factor, color.Green * factor, color.Blue * factor);

	// Deadband: cand abaterea e foarte mica (acordat), fixam acul pe offset-ul de centru ca sa nu mai tremure.
	static double MapCentsToAngle(double cents)
	{
		if (Math.Abs(cents) < TunerSettings.CentsDeadband)
			return TunerSettings.NeedleAngleOffsetDegrees;

		var clampedCents = Math.Clamp(cents, -TunerSettings.MaxCentsForNeedle, TunerSettings.MaxCentsForNeedle);
		var span = clampedCents >= 0
			? TunerSettings.MaxNeedleAnglePositiveDegrees
			: TunerSettings.MaxNeedleAngleNegativeDegrees;

		return TunerSettings.NeedleAngleOffsetDegrees +
		       clampedCents / TunerSettings.MaxCentsForNeedle * span;
	}

	bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
			return false;

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}
}
