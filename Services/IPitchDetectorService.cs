namespace EasyGuitarTuner.Services;

public interface IPitchDetectorService
{
	double DetectPitch(byte[] pcmData, int sampleRate);
}
