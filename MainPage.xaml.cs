using EasyGuitarTuner.ViewModels;

namespace EasyGuitarTuner;

public partial class MainPage : ContentPage
{
	readonly TunerViewModel _viewModel;

	public MainPage(TunerViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.StartAsync();
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();
		await _viewModel.StopAsync();
	}
}
