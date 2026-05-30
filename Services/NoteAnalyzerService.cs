using EasyGuitarTuner.Models;

namespace EasyGuitarTuner.Services;

public class NoteAnalyzerService : INoteAnalyzerService
{
	static readonly string[] NoteNames =
	[
		"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
	];

	const int MidiNoteForA4 = 69;

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

		var midiNote = (int)Math.Round(MidiNoteForA4 + 12 * Math.Log2(frequencyHz / TunerSettings.ReferencePitchHz));
		var targetFrequency = ChromaticFrequency(midiNote);

		var cents = 1200 * Math.Log2(frequencyHz / targetFrequency);
		var status = GetTuningStatus(cents);

		return new NoteResult
		{
			NoteName = NoteNames[((midiNote % 12) + 12) % 12],
			Octave = midiNote / 12 - 1,
			FrequencyHz = frequencyHz,
			CentsDeviation = cents,
			Status = status
		};
	}

	static double ChromaticFrequency(int midiNote) =>
		TunerSettings.ReferencePitchHz * Math.Pow(2, (midiNote - MidiNoteForA4) / 12.0);

	static TuningStatus GetTuningStatus(double cents)
	{
		if (Math.Abs(cents) < TunerSettings.InTuneToleranceCents)
			return TuningStatus.InTune;

		return cents < 0 ? TuningStatus.Flat : TuningStatus.Sharp;
	}
}
