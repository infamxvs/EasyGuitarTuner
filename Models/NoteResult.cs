namespace EasyGuitarTuner.Models;

public enum TuningStatus
{
	Flat,
	InTune,
	Sharp
}

public class NoteResult
{
	public string NoteName { get; init; } = "-";
	public int Octave { get; init; }
	public double FrequencyHz { get; init; }
	public double CentsDeviation { get; init; }
	public TuningStatus Status { get; init; }
}
