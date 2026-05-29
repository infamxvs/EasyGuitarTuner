namespace EasyGuitarTuner;

public partial class AppShell : Shell
{
	public AppShell(MainPage mainPage)
	{
		InitializeComponent();
		Items.Clear();
		Items.Add(new ShellContent
		{
			Title = "Tuner",
			Content = mainPage,
			Route = "MainPage"
		});
	}
}
