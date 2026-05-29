using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace EasyGuitarTuner.Controls;

public class AnalogMeterView : SKCanvasView
{
	// Pozitia pivotului acului, ca fractii din imaginea de fundal.
	// Estimari de start - se calibreaza vizual dupa prima rulare.
	const float PivotXFraction = 0.5f;
	const float PivotYFraction = 0.56f;

	const string BackgroundAsset = "background.png";
	const string NeedleAsset = "needle.png";

	static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

	SKImage? _background;
	SKImage? _needle;
	bool _assetsRequested;

	public static readonly BindableProperty NeedleAngleProperty =
		BindableProperty.Create(nameof(NeedleAngle), typeof(double), typeof(AnalogMeterView), 0.0, propertyChanged: OnNeedleAngleChanged);

	public double NeedleAngle
	{
		get => (double)GetValue(NeedleAngleProperty);
		set => SetValue(NeedleAngleProperty, value);
	}

	public AnalogMeterView()
	{
		PaintSurface += OnPaintSurface;
	}

	static void OnNeedleAngleChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is AnalogMeterView view)
			view.InvalidateSurface();
	}

	void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
	{
		var canvas = e.Surface.Canvas;
		canvas.Clear(SKColors.Transparent);

		EnsureAssetsLoaded();

		if (_background is null || _needle is null)
			return;

		var width = e.Info.Width;
		var height = e.Info.Height;

		var scale = Math.Min((float)width / _background.Width, (float)height / _background.Height);
		var offsetX = (width - _background.Width * scale) / 2f;
		var offsetY = (height - _background.Height * scale) / 2f;

		DrawBackground(canvas, scale, offsetX, offsetY);
		DrawNeedle(canvas, scale, offsetX, offsetY);
	}

	void DrawBackground(SKCanvas canvas, float scale, float offsetX, float offsetY)
	{
		var dest = new SKRect(
			offsetX,
			offsetY,
			offsetX + _background!.Width * scale,
			offsetY + _background.Height * scale);

		canvas.DrawImage(_background, dest, Sampling);
	}

	void DrawNeedle(SKCanvas canvas, float scale, float offsetX, float offsetY)
	{
		var pivotX = offsetX + _background!.Width * scale * PivotXFraction;
		var pivotY = offsetY + _background.Height * scale * PivotYFraction;

		canvas.Save();
		canvas.Translate(pivotX, pivotY);
		canvas.Scale(scale);
		canvas.RotateDegrees((float)NeedleAngle);
		// Baza acului (jos-centru) ramane fixata pe pivot, rotatia se face in jurul ei.
		canvas.DrawImage(_needle, -_needle!.Width / 2f, -_needle.Height, Sampling);
		canvas.Restore();
	}

	void EnsureAssetsLoaded()
	{
		if (_assetsRequested)
			return;

		_assetsRequested = true;
		_ = LoadAssetsAsync();
	}

	async Task LoadAssetsAsync()
	{
		_background = await LoadImageAsync(BackgroundAsset);
		_needle = await LoadImageAsync(NeedleAsset);
		InvalidateSurface();
	}

	static async Task<SKImage?> LoadImageAsync(string fileName)
	{
		using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
		using var bitmap = SKBitmap.Decode(stream);
		return bitmap is null ? null : SKImage.FromBitmap(bitmap);
	}
}
