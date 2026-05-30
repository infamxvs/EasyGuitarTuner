using System.ComponentModel;
using System.Runtime.CompilerServices;
using EasyGuitarTuner.Models;
using EasyGuitarTuner.Services;

namespace EasyGuitarTuner.ViewModels;

public class TunerViewModel : INotifyPropertyChanged
{
	readonly IAudioCaptureService _audioCaptureService;
	readonly IPitchDetectorService _pitchDetectorService;
	readonly IFrequencyStabilizer _frequencyStabilizer;
	readonly INoteAnalyzerService _noteAnalyzerService;
	readonly ISessionLogger _logger;

	string _noteText = "—";
	string _octaveText = string.Empty;
	string _frequencyText = "0.0 Hz";
	string _centsText = string.Empty;
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
			FrequencyText = "0.0 Hz";
			CentsText = string.Empty;
			_smoothedAngle = 0;
			NeedleAngle = 0;
			return;
		}

		NoteText = noteResult.NoteName;
		OctaveText = noteResult.Octave.ToString();
		FrequencyText = $"{noteResult.FrequencyHz:F1} Hz";
		CentsText = $"{noteResult.CentsDeviation:+0;-0;0}";

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
