using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace EasyGuitarTuner.Controls;

public class AnalogMeterView : SKCanvasView
{
	const double MaxCents = 100;
	const double InTuneCents = 15;

	static readonly SKColor ArcColor = SKColor.Parse("#DCE3EB");
	static readonly SKColor BlueHighlight = SKColor.Parse("#2E80E0");
	static readonly SKColor WedgeColor = new SKColor(46, 128, 224, 38);
	static readonly SKColor TickColor = SKColor.Parse("#9CA3AF");
	static readonly SKColor LabelColor = SKColor.Parse("#94A0AE");
	static readonly SKColor NeedleColor = SKColor.Parse("#111827");

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

		var width = e.Info.Width;
		var height = e.Info.Height;

		const float pad = 8f;
		const float strokeHalfFraction = 0.0425f;

		var centerX = width / 2f;
		var centerY = height / 2f;
		var sideMaxRadius = (centerX - pad) / (1f + strokeHalfFraction);
		var topMaxRadius = (centerY - pad) / (1f + strokeHalfFraction);
		var radius = Math.Min(sideMaxRadius, topMaxRadius);

		DrawWedge(canvas, centerX, centerY, radius);
		DrawArc(canvas, centerX, centerY, radius);
		DrawHighlightArc(canvas, centerX, centerY, radius);
		DrawTicksAndLabels(canvas, centerX, centerY, radius);
		DrawNeedle(canvas, centerX, centerY, radius);
		DrawPivot(canvas, centerX, centerY, radius);
	}

	static double CentsToAngleDegrees(double cents)
	{
		return 180.0 + (cents + MaxCents) / (2 * MaxCents) * 180.0;
	}

	static void DrawArc(SKCanvas canvas, float centerX, float centerY, float radius)
	{
		using var arcPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeCap = SKStrokeCap.Round,
			StrokeWidth = radius * 0.085f,
			Color = ArcColor
		};

		var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
		canvas.DrawArc(rect, 180, 180, false, arcPaint);
	}

	static void DrawHighlightArc(SKCanvas canvas, float centerX, float centerY, float radius)
	{
		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeCap = SKStrokeCap.Round,
			StrokeWidth = radius * 0.085f,
			Color = BlueHighlight
		};

		var startAngle = (float)CentsToAngleDegrees(-InTuneCents);
		var sweep = (float)(CentsToAngleDegrees(InTuneCents) - startAngle);

		var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
		canvas.DrawArc(rect, startAngle, sweep, false, paint);
	}

	static void DrawWedge(SKCanvas canvas, float centerX, float centerY, float radius)
	{
		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = WedgeColor
		};

		using var path = new SKPath();
		path.MoveTo(centerX, centerY);

		var startAngle = (float)CentsToAngleDegrees(-InTuneCents);
		var sweep = (float)(CentsToAngleDegrees(InTuneCents) - startAngle);
		var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);

		var startRad = startAngle * Math.PI / 180.0;
		var x1 = centerX + (float)(Math.Cos(startRad) * radius);
		var y1 = centerY + (float)(Math.Sin(startRad) * radius);
		path.LineTo(x1, y1);

		path.ArcTo(rect, startAngle, sweep, false);
		path.Close();

		canvas.DrawPath(path, paint);
	}

	static void DrawTicksAndLabels(SKCanvas canvas, float centerX, float centerY, float radius)
	{
		using var minorTickPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.2f,
			StrokeCap = SKStrokeCap.Round,
			Color = TickColor
		};

		using var majorTickPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.6f,
			StrokeCap = SKStrokeCap.Round,
			Color = TickColor
		};

		using var labelFont = new SKFont
		{
			Size = radius * 0.08f,
			Edging = SKFontEdging.SubpixelAntialias
		};
		using var labelPaint = new SKPaint
		{
			IsAntialias = true,
			Color = LabelColor
		};

		var arcThickness = radius * 0.085f;
		var tickOuterRadius = radius - arcThickness * 0.6f;

		for (var cents = -(int)MaxCents; cents <= MaxCents; cents += 5)
		{
			if (Math.Abs(cents) < InTuneCents)
				continue;

			var angle = CentsToAngleDegrees(cents);
			var rad = angle * Math.PI / 180.0;
			var cos = (float)Math.Cos(rad);
			var sin = (float)Math.Sin(rad);

			var isMajor = cents % 20 == 0;
			var tickLength = isMajor ? radius * 0.08f : radius * 0.045f;
			var paint = isMajor ? majorTickPaint : minorTickPaint;

			var x1 = centerX + cos * (tickOuterRadius - tickLength);
			var y1 = centerY + sin * (tickOuterRadius - tickLength);
			var x2 = centerX + cos * tickOuterRadius;
			var y2 = centerY + sin * tickOuterRadius;
			canvas.DrawLine(x1, y1, x2, y2, paint);

			if (isMajor)
			{
				var labelRadius = tickOuterRadius - radius * 0.18f;
				var labelX = centerX + cos * labelRadius;
				var labelY = centerY + sin * labelRadius + labelFont.Size * 0.35f;
				var text = cents > 0 ? $"+{cents}" : cents.ToString();
				canvas.DrawText(text, labelX, labelY, SKTextAlign.Center, labelFont, labelPaint);
			}
		}
	}

	void DrawNeedle(SKCanvas canvas, float centerX, float centerY, float radius)
	{
		using var needlePaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = radius * 0.018f,
			StrokeCap = SKStrokeCap.Round,
			Color = NeedleColor
		};

		var needleAngleDeg = 270 + NeedleAngle;
		var needleRad = needleAngleDeg * Math.PI / 180;
		var needleLength = radius * 0.93f;
		var endX = centerX + (float)(Math.Cos(needleRad) * needleLength);
		var endY = centerY + (float)(Math.Sin(needleRad) * needleLength);

		canvas.DrawLine(centerX, centerY, endX, endY, needlePaint);
	}

	static void DrawPivot(SKCanvas canvas, float centerX, float centerY, float radius)
	{
		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = NeedleColor
		};
		canvas.DrawCircle(centerX, centerY, radius * 0.055f, paint);
	}
}
