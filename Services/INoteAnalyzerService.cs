using EasyGuitarTuner.Models;

namespace EasyGuitarTuner.Services;

public interface INoteAnalyzerService
{
	NoteResult Analyze(double frequencyHz);
}
