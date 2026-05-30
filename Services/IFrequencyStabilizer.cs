namespace EasyGuitarTuner.Services;

public interface IFrequencyStabilizer
{
	double Stabilize(double rawFrequencyHz);

	void Reset();
}
