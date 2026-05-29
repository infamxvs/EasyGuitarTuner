namespace EasyGuitarTuner.Services;

public interface IAudioCaptureService
{
	event EventHandler<byte[]>? OnAudioCaptured;

	bool IsCapturing { get; }

	int SampleRate { get; }

	Task StartAsync();

	Task StopAsync();
}
