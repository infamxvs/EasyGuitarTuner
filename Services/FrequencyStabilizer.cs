namespace EasyGuitarTuner.Services;

// Stabilizeaza frecventa bruta in doua etape:
// 1. Filtru median pe ultimele N valori - respinge outlierii izolati (armonice, erori de octava, artefacte).
// 2. EMA peste mediana - netezeste variatiile mici; la salt mare (schimbare reala de coarda) face snap.
// Plus hold time la disparitia semnalului, pentru coardele inalte cu decay scurt.
public class FrequencyStabilizer : IFrequencyStabilizer
{
	readonly Queue<double> _recentRaw = new();
	double _smoothedFrequency;
	int _silenceCount;

	public double Stabilize(double rawFrequencyHz)
	{
		if (rawFrequencyHz <= 0)
			return HandleSilence();

		_silenceCount = 0;
		AddSample(rawFrequencyHz);

		var median = Median();

		if (_smoothedFrequency <= 0 || IsLargeJump(median))
			_smoothedFrequency = median;
		else
			_smoothedFrequency = _smoothedFrequency * (1 - TunerSettings.FrequencySmoothingAlpha) +
			                     median * TunerSettings.FrequencySmoothingAlpha;

		return _smoothedFrequency;
	}

	public void Reset()
	{
		_recentRaw.Clear();
		_smoothedFrequency = 0;
		_silenceCount = 0;
	}

	double HandleSilence()
	{
		if (_smoothedFrequency <= 0)
			return 0;

		_silenceCount++;
		if (_silenceCount >= TunerSettings.HoldCycles)
			Reset();

		return _smoothedFrequency;
	}

	void AddSample(double value)
	{
		_recentRaw.Enqueue(value);
		if (_recentRaw.Count > TunerSettings.MedianWindow)
			_recentRaw.Dequeue();
	}

	bool IsLargeJump(double candidate) =>
		Math.Abs(candidate - _smoothedFrequency) / _smoothedFrequency > TunerSettings.JumpThreshold;

	double Median()
	{
		var sorted = _recentRaw.ToArray();
		Array.Sort(sorted);
		return sorted[sorted.Length / 2];
	}
}
