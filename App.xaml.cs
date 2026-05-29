namespace EasyGuitarTuner;

public partial class App : Application
{
	readonly AppShell _shell;

	public App(AppShell shell)
	{
		InitializeComponent();
		_shell = shell;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_shell);

#if WINDOWS
		const double width = 249;
		const double height = 540;
		window.Width = width;
		window.Height = height;
		window.MinimumWidth = width;
		window.MinimumHeight = height;
		window.MaximumWidth = width;
		window.MaximumHeight = height;
#endif

		return window;
	}
}
