using Plugin.Maui.Audio;

namespace EasyGuitarTuner.Services;

public class AudioCaptureService : IAudioCaptureService
{
	readonly IAudioManager _audioManager;
	readonly ISessionLogger _logger;
	IAudioStreamer? _streamer;
	readonly List<byte> _sampleBuffer = [];
	readonly object _bufferLock = new();

	public event EventHandler<byte[]>? OnAudioCaptured;

	public bool IsCapturing => _streamer?.IsStreaming ?? false;

	public int SampleRate => TunerSettings.SampleRateHz;

	public AudioCaptureService(IAudioManager audioManager, ISessionLogger logger)
	{
		_audioManager = audioManager;
		_logger = logger;
	}

	public async Task StartAsync()
	{
		if (IsCapturing)
			return;

		_streamer = _audioManager.CreateStreamer();
		_streamer.Options.Channels = ChannelType.Mono;
		_streamer.Options.BitDepth = BitDepth.Pcm16bit;
		_streamer.Options.SampleRate = TunerSettings.SampleRateHz;
		_streamer.OnAudioCaptured += OnStreamerCaptured;

		await _streamer.StartAsync();
		_logger.Log($"CAPTURE START | SampleRate={TunerSettings.SampleRateHz} Hz | Buffer={TunerSettings.AnalysisWindowSamples} esantioane");
	}

	public async Task StopAsync()
	{
		if (_streamer is null)
			return;

		_streamer.OnAudioCaptured -= OnStreamerCaptured;
		await _streamer.StopAsync();
		_streamer = null;

		lock (_bufferLock)
			_sampleBuffer.Clear();

		_logger.Log("CAPTURE STOP");
	}

	void OnStreamerCaptured(object? sender, AudioStreamEventArgs args)
	{
		byte[]? dataToEmit = null;

		lock (_bufferLock)
		{
			_sampleBuffer.AddRange(args.Audio);

			var targetBytes = TunerSettings.AnalysisWindowSamples * 2; // 16-bit = 2 bytes per esantion

			if (_sampleBuffer.Count >= targetBytes)
			{
				dataToEmit = _sampleBuffer.Take(targetBytes).ToArray();
				_sampleBuffer.RemoveRange(0, targetBytes / 2); // suprapunere 50% - mai receptiv
			}
		}

		if (dataToEmit is not null)
			OnAudioCaptured?.Invoke(this, dataToEmit);
	}
}
