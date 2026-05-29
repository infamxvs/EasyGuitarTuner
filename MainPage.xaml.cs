using EasyGuitarTuner.ViewModels;

namespace EasyGuitarTuner;

public partial class MainPage : ContentPage
{
	public MainPage(TunerViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
