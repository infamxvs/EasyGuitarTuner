using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace EasyGuitarTuner.Services;

public class PitchDetectorService : IPitchDetectorService
{
	public double DetectPitch(byte[] pcmData, int sampleRate)
	{
		if (pcmData.Length < 4 || sampleRate <= 0)
			return 0;

		var sampleCount = pcmData.Length / 2;
		var samples = new double[sampleCount];

		for (var i = 0; i < sampleCount; i++)
			samples[i] = BitConverter.ToInt16(pcmData, i * 2) / 32768.0;

		if (!HasEnoughSignal(samples))
			return 0;

		ApplyHannWindow(samples);

		var fftSize = NextPowerOfTwo(sampleCount);
		var buffer = new Complex[fftSize];

		for (var i = 0; i < sampleCount; i++)
			buffer[i] = new Complex(samples[i], 0);

		Fourier.Forward(buffer, FourierOptions.Matlab);

		var halfSize = fftSize / 2;
		var minBin = Math.Max(1, (int)Math.Floor(TunerSettings.MinFrequencyHz * fftSize / sampleRate));
		var maxBin = Math.Min(halfSize, (int)Math.Ceiling(TunerSettings.MaxFrequencyHz * fftSize / sampleRate));

		var magnitudes = new double[halfSize + 1];
		for (var i = 0; i <= halfSize; i++)
			magnitudes[i] = buffer[i].Magnitude;

		// HPS (Harmonic Product Spectrum): inmulteste spectrul cu versiunile comprimate
		// la 1/2, 1/3 etc. Fundamentala iese in evidenta, armonicele se anuleaza.
		var hps = ComputeHarmonicProductSpectrum(magnitudes, minBin, maxBin, halfSize);

		var peakBin = FindPeakBin(hps, minBin, maxBin);
		if (hps[peakBin] <= 0)
			return 0;

		peakBin = CorrectOctaveError(magnitudes, peakBin, minBin);

		var refinedBin = RefinePeakBin(hps, peakBin, maxBin);
		var frequency = refinedBin * sampleRate / fftSize;

		if (frequency < TunerSettings.MinFrequencyHz || frequency > TunerSettings.MaxFrequencyHz)
			return 0;

		return frequency;
	}

	static bool HasEnoughSignal(double[] samples)
	{
		var sumSquares = 0.0;
		foreach (var s in samples)
			sumSquares += s * s;

		var rms = Math.Sqrt(sumSquares / samples.Length);
		return rms >= TunerSettings.NoiseFloorRms;
	}

	static double[] ComputeHarmonicProductSpectrum(double[] magnitudes, int minBin, int maxBin, int halfSize)
	{
		var hps = new double[magnitudes.Length];

		for (var bin = minBin; bin <= maxBin; bin++)
			hps[bin] = magnitudes[bin];

		for (var k = 2; k <= TunerSettings.HarmonicCount; k++)
		{
			for (var bin = minBin; bin <= maxBin; bin++)
			{
				var harmonicBin = bin * k;
				if (harmonicBin <= halfSize)
					hps[bin] *= magnitudes[harmonicBin];
				else
					hps[bin] = 0;
			}
		}

		return hps;
	}

	// Daca peakBin detectat de HPS e dublu fata de fundamentala reala, coboram o octava.
	// Cautam peak-ul local in fereastra +/-2 bins in jurul peakBin/2 si comparam cu peak-ul local
	// in jurul peakBin. Coborim doar daca suboctava are cel putin 60% din amplitudine.
	static int CorrectOctaveError(double[] magnitudes, int peakBin, int minBin)
	{
		var subOctaveBin = peakBin / 2;
		if (subOctaveBin < minBin)
			return peakBin;

		var subOctavePeak = FindLocalMax(magnitudes, subOctaveBin, 2);
		var peakLocalMax = FindLocalMax(magnitudes, peakBin, 2);

		if (subOctavePeak.Value >= peakLocalMax.Value * 0.6)
			return subOctavePeak.Bin;

		return peakBin;
	}

	static (int Bin, double Value) FindLocalMax(double[] spectrum, int centerBin, int radius)
	{
		var bestBin = centerBin;
		var bestValue = 0.0;

		var start = Math.Max(0, centerBin - radius);
		var end = Math.Min(spectrum.Length - 1, centerBin + radius);

		for (var bin = start; bin <= end; bin++)
		{
			if (spectrum[bin] > bestValue)
			{
				bestValue = spectrum[bin];
				bestBin = bin;
			}
		}

		return (bestBin, bestValue);
	}

	static int FindPeakBin(double[] spectrum, int minBin, int maxBin)
	{
		var peakBin = minBin;
		var peakValue = 0.0;

		for (var bin = minBin; bin <= maxBin; bin++)
		{
			if (spectrum[bin] > peakValue)
			{
				peakValue = spectrum[bin];
				peakBin = bin;
			}
		}

		return peakBin;
	}

	static double RefinePeakBin(double[] spectrum, int peakBin, int maxBin)
	{
		if (peakBin <= 0 || peakBin >= maxBin)
			return peakBin;

		var alpha = spectrum[peakBin - 1];
		var beta = spectrum[peakBin];
		var gamma = spectrum[peakBin + 1];
		var denominator = alpha - 2 * beta + gamma;

		if (Math.Abs(denominator) < double.Epsilon)
			return peakBin;

		// Interpolarea parabolica - precizie sub-bin
		return peakBin + 0.5 * (alpha - gamma) / denominator;
	}

	static void ApplyHannWindow(double[] samples)
	{
		if (samples.Length <= 1)
			return;

		for (var i = 0; i < samples.Length; i++)
		{
			var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (samples.Length - 1)));
			samples[i] *= window;
		}
	}

	static int NextPowerOfTwo(int value)
	{
		var power = 1;
		while (power < value)
			power <<= 1;

		return power;
	}
}
