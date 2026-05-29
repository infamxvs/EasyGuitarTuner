using EasyGuitarTuner.Models;

namespace EasyGuitarTuner.Services;

public class NoteAnalyzerService : INoteAnalyzerService
{
	readonly (string NoteName, int Octave, double FrequencyHz)[] _guitarStrings =
	[
		("E", 2, 82.41),
		("A", 2, 110.00),
		("D", 3, 146.83),
		("G", 3, 196.00),
		("B", 3, 246.94),
		("E", 4, 329.63)
	];

	public NoteResult Analyze(double frequencyHz)
	{
		if (frequencyHz <= 0)
		{
			return new NoteResult
			{
				NoteName = "-",
				Octave = 0,
				FrequencyHz = 0,
				CentsDeviation = 0,
				Status = TuningStatus.InTune
			};
		}

		var closestString = _guitarStrings[0];
		var smallestDifference = Math.Abs(frequencyHz - closestString.FrequencyHz);

		foreach (var guitarString in _guitarStrings)
		{
			var difference = Math.Abs(frequencyHz - guitarString.FrequencyHz);
			if (difference < smallestDifference)
			{
				smallestDifference = difference;
				closestString = guitarString;
			}
		}

		var cents = 1200 * Math.Log(frequencyHz / closestString.FrequencyHz, 2);
		var status = GetTuningStatus(cents);

		return new NoteResult
		{
			NoteName = closestString.NoteName,
			Octave = closestString.Octave,
			FrequencyHz = frequencyHz,
			CentsDeviation = cents,
			Status = status
		};
	}

	static TuningStatus GetTuningStatus(double cents)
	{
		if (Math.Abs(cents) < 5)
			return TuningStatus.InTune;

		return cents < 0 ? TuningStatus.Flat : TuningStatus.Sharp;
	}
}
