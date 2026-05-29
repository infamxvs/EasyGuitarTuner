namespace EasyGuitarTuner.Services;

public class FileSessionLogger : ISessionLogger, IDisposable
{
	readonly StreamWriter _writer;

	public FileSessionLogger()
	{
		var path = Path.Combine(Directory.GetCurrentDirectory(), "tuner-session.log");
		_writer = new StreamWriter(path, append: false) { AutoFlush = true };
		Log($"=== Sesiune noua ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===");
		Log($"Working directory: {Directory.GetCurrentDirectory()}");
	}

	public void Log(string message)
	{
		_writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
	}

	public void Dispose()
	{
		Log("=== Sesiune incheiata ===");
		_writer.Dispose();
	}
}
