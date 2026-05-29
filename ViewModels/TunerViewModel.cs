using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EasyGuitarTuner.Models;
using EasyGuitarTuner.Services;

namespace EasyGuitarTuner.ViewModels;

public class TunerViewModel : INotifyPropertyChanged
{
	readonly IAudioCaptureService _audioCaptureService;
	readonly IPitchDetectorService _pitchDetectorService;
	readonly INoteAnalyzerService _noteAnalyzerService;
	readonly ISessionLogger _logger;

	string _noteText = "—";
	string _octaveText = string.Empty;
	string _frequencyText = "0.0 Hz";
	string _centsText = string.Empty;
	double _needleAngle;
	bool _isListening;
	string _toggleButtonText = "Start";
	double _smoothedFrequency;
	double _pendingFrequency;
	int _silenceCount;

	const double SmoothingAlpha = 0.4;
	const double JumpThreshold = 0.15;
	const double SimilarityThreshold = 0.05;
	const int HoldCycles = 3;
	const double MaxCentsForNeedle = 50.0;
	const double MaxNeedleAngleDegrees = 50.0;

	public TunerViewModel(
		IAudioCaptureService audioCaptureService,
		IPitchDetectorService pitchDetectorService,
		INoteAnalyzerService noteAnalyzerService,
		ISessionLogger logger)
	{
		_audioCaptureService = audioCaptureService;
		_pitchDetectorService = pitchDetectorService;
		_noteAnalyzerService = noteAnalyzerService;
		_logger = logger;

		_audioCaptureService.OnAudioCaptured += OnAudioCaptured;
		ToggleListeningCommand = new Command(async () => await ToggleListeningAsync());
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

	public bool IsListening
	{
		get => _isListening;
		private set
		{
			if (SetProperty(ref _isListening, value))
				ToggleButtonText = value ? "Stop" : "Start";
		}
	}

	public string ToggleButtonText
	{
		get => _toggleButtonText;
		private set => SetProperty(ref _toggleButtonText, value);
	}

	public ICommand ToggleListeningCommand { get; }

	async Task ToggleListeningAsync()
	{
		if (IsListening)
		{
			await _audioCaptureService.StopAsync();
			IsListening = false;
			return;
		}

		await _audioCaptureService.StartAsync();
		IsListening = true;
	}

	void OnAudioCaptured(object? sender, byte[] pcmData)
	{
		var rms = ComputeRms(pcmData);
		var rawFrequency = _pitchDetectorService.DetectPitch(pcmData, _audioCaptureService.SampleRate);

		if (rawFrequency > 0)
		{
			_silenceCount = 0;

			var isColdStart = _smoothedFrequency <= 0;
			var isLargeJump = !isColdStart &&
			                  Math.Abs(rawFrequency - _smoothedFrequency) / _smoothedFrequency > JumpThreshold;

			if (isColdStart || isLargeJump)
			{
				if (_pendingFrequency > 0 && IsSimilar(rawFrequency, _pendingFrequency))
				{
					_smoothedFrequency = rawFrequency;
					_pendingFrequency = 0;
				}
				else
				{
					_pendingFrequency = rawFrequency;
				}
			}
			else
			{
				_smoothedFrequency = _smoothedFrequency * (1 - SmoothingAlpha) + rawFrequency * SmoothingAlpha;
				_pendingFrequency = 0;
			}
		}
		else if (_smoothedFrequency > 0)
		{
			_silenceCount++;
			if (_silenceCount >= HoldCycles)
			{
				_smoothedFrequency = 0;
				_pendingFrequency = 0;
				_silenceCount = 0;
			}
		}
		else
		{
			_pendingFrequency = 0;
		}

		var noteResult = _noteAnalyzerService.Analyze(_smoothedFrequency);

		_logger.Log($"RMS={rms:F4} | RAW={rawFrequency:F2} Hz | SMOOTH={_smoothedFrequency:F2} Hz | " +
		            $"NOTA={noteResult.NoteName}{noteResult.Octave} | " +
		            $"CENTS={noteResult.CentsDeviation:+0.0;-0.0;0} | {noteResult.Status}");

		MainThread.BeginInvokeOnMainThread(() => UpdateUi(noteResult));
	}

	static bool IsSimilar(double a, double b)
	{
		if (b <= 0)
			return false;

		return Math.Abs(a - b) / b < SimilarityThreshold;
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
		if (noteResult.FrequencyHz <= 0)
		{
			NoteText = "—";
			OctaveText = string.Empty;
			FrequencyText = "0.0 Hz";
			CentsText = string.Empty;
			NeedleAngle = 0;
			return;
		}

		NoteText = noteResult.NoteName;
		OctaveText = noteResult.Octave.ToString();
		FrequencyText = $"{noteResult.FrequencyHz:F1} Hz";
		CentsText = $"{noteResult.CentsDeviation:+0;-0;0}";
		NeedleAngle = MapCentsToAngle(noteResult.CentsDeviation);
	}

	static double MapCentsToAngle(double cents)
	{
		var clampedCents = Math.Clamp(cents, -MaxCentsForNeedle, MaxCentsForNeedle);
		return clampedCents / MaxCentsForNeedle * MaxNeedleAngleDegrees;
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
