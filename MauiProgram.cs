using EasyGuitarTuner.Services;
using EasyGuitarTuner.ViewModels;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace EasyGuitarTuner;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseSkiaSharp()
			.AddAudio()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ISessionLogger, FileSessionLogger>();
		builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
		RegisterPitchDetector(builder.Services);
		builder.Services.AddSingleton<IFrequencyStabilizer, FrequencyStabilizer>();
		builder.Services.AddSingleton<INoteAnalyzerService, NoteAnalyzerService>();
		builder.Services.AddTransient<TunerViewModel>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	// Inregistreaza detectorul ales in TunerSettings.ActivePitchAlgorithm.
	// Variabila locala evita avertismentul de cod inaccesibil la compararea unei constante.
	static void RegisterPitchDetector(IServiceCollection services)
	{
		var algorithm = TunerSettings.ActivePitchAlgorithm;

		if (algorithm == PitchAlgorithm.Yin)
			services.AddSingleton<IPitchDetectorService, YinPitchDetectorService>();
		else
			services.AddSingleton<IPitchDetectorService, PitchDetectorService>();
	}
}
