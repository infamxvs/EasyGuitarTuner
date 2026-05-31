namespace EasyGuitarTuner.Services;

// Detector alternativ bazat pe algoritmul YIN (de Cheveigne & Kawahara, 2002).
// Lucreaza in domeniul timpului: difference function -> CMNDF -> prag absolut -> interpolare parabolica.
// Total independent de PitchDetectorService (HPS). Se activeaza din TunerSettings.ActivePitchAlgorithm.
public class YinPitchDetectorService : IPitchDetectorService
{
	public double DetectPitch(byte[] pcmData, int sampleRate)
	{
		if (pcmData.Length < 4 || sampleRate <= 0)
			return 0;

		var samples = ExtractWindow(pcmData);
		if (samples.Length < 4 || !HasEnoughSignal(samples))
			return 0;

		var halfSize = samples.Length / 2;
		var cmndf = ComputeCmndf(samples, halfSize);

		var tauMin = Math.Max(2, (int)(sampleRate / TunerSettings.YinMaxFrequencyHz));
		var tauMax = Math.Min(halfSize - 1, (int)(sampleRate / TunerSettings.YinMinFrequencyHz));
		if (tauMin >= tauMax)
			return 0;

		var tau = FindAbsoluteThreshold(cmndf, tauMin, tauMax);
		if (cmndf[tau] > TunerSettings.YinAperiodicityCutoff)
			return 0;

		var refinedTau = RefineTau(cmndf, tau);
		if (refinedTau <= 0)
			return 0;

		var frequency = sampleRate / refinedTau;
		if (frequency < TunerSettings.YinMinFrequencyHz || frequency > TunerSettings.YinMaxFrequencyHz)
			return 0;

		return frequency;
	}

	// Optiunea 1: foloseste doar ultimele YinWindowSamples esantioane din bufferul de captura.
	static double[] ExtractWindow(byte[] pcmData)
	{
		var totalSamples = pcmData.Length / 2;
		var windowSize = Math.Min(TunerSettings.YinWindowSamples, totalSamples);
		var startSample = totalSamples - windowSize;

		var samples = new double[windowSize];
		for (var i = 0; i < windowSize; i++)
			samples[i] = BitConverter.ToInt16(pcmData, (startSample + i) * 2) / 32768.0;

		return samples;
	}

	static bool HasEnoughSignal(double[] samples)
	{
		var sumSquares = 0.0;
		foreach (var s in samples)
			sumSquares += s * s;

		var rms = Math.Sqrt(sumSquares / samples.Length);
		return rms >= TunerSettings.NoiseFloorRms;
	}

	// Difference function + normalizare cumulativa (CMNDF). cmndf[0] = 1 prin definitie,
	// ca sa nu fie ales lag-ul zero. Reduce drastic erorile de octava fata de autocorelatia simpla.
	static double[] ComputeCmndf(double[] samples, int halfSize)
	{
		var cmndf = new double[halfSize];
		cmndf[0] = 1.0;

		var runningSum = 0.0;
		for (var tau = 1; tau < halfSize; tau++)
		{
			var difference = 0.0;
			for (var i = 0; i < halfSize; i++)
			{
				var delta = samples[i] - samples[i + tau];
				difference += delta * delta;
			}

			runningSum += difference;
			cmndf[tau] = runningSum > 0 ? difference * tau / runningSum : 1.0;
		}

		return cmndf;
	}

	// Primul dip sub prag, urmarit pana la minimul local. Daca niciunul nu coboara sub prag,
	// se intoarce minimul global (filtrat ulterior de YinAperiodicityCutoff).
	static int FindAbsoluteThreshold(double[] cmndf, int tauMin, int tauMax)
	{
		for (var tau = tauMin; tau <= tauMax; tau++)
		{
			if (cmndf[tau] < TunerSettings.YinThreshold)
			{
				while (tau + 1 <= tauMax && cmndf[tau + 1] < cmndf[tau])
					tau++;

				return tau;
			}
		}

		return GlobalMinimum(cmndf, tauMin, tauMax);
	}

	static int GlobalMinimum(double[] cmndf, int tauMin, int tauMax)
	{
		var bestTau = tauMin;
		var bestValue = cmndf[tauMin];

		for (var tau = tauMin + 1; tau <= tauMax; tau++)
		{
			if (cmndf[tau] < bestValue)
			{
				bestValue = cmndf[tau];
				bestTau = tau;
			}
		}

		return bestTau;
	}

	// Interpolare parabolica in jurul dip-ului pentru precizie sub-esantion.
	static double RefineTau(double[] cmndf, int tau)
	{
		if (tau <= 0 || tau >= cmndf.Length - 1)
			return tau;

		var alpha = cmndf[tau - 1];
		var beta = cmndf[tau];
		var gamma = cmndf[tau + 1];
		var denominator = alpha - 2 * beta + gamma;

		if (Math.Abs(denominator) < double.Epsilon)
			return tau;

		return tau + 0.5 * (alpha - gamma) / denominator;
	}
}
