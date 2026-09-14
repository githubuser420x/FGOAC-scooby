using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>
/// The game sets its headings in white with a thin coloured outline. This draws that in one visual:
/// the glyph outlines are built once and stroked, so there is no bitmap effect and nothing for the
/// compositor to re-render on a weak GPU.
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
	public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(OutlinedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(OutlinedText), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(OutlinedText), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register("StrokeThickness", typeof(double), typeof(OutlinedText), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(OutlinedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

	public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(OutlinedText), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

	public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(OutlinedText), new FrameworkPropertyMetadata(FontWeights.Bold, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

	private Geometry glyphs;

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public Brush Fill
	{
		get => (Brush)GetValue(FillProperty);
		set => SetValue(FillProperty, value);
	}

	public Brush Stroke
	{
		get => (Brush)GetValue(StrokeProperty);
		set => SetValue(StrokeProperty, value);
	}

	public double StrokeThickness
	{
		get => (double)GetValue(StrokeThicknessProperty);
		set => SetValue(StrokeThicknessProperty, value);
	}

	public FontFamily FontFamily
	{
		get => (FontFamily)GetValue(FontFamilyProperty);
		set => SetValue(FontFamilyProperty, value);
	}

	public double FontSize
	{
		get => (double)GetValue(FontSizeProperty);
		set => SetValue(FontSizeProperty, value);
	}

	public FontWeight FontWeight
	{
		get => (FontWeight)GetValue(FontWeightProperty);
		set => SetValue(FontWeightProperty, value);
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		Build();
		if (glyphs == null)
		{
			return new Size(0.0, 0.0);
		}
		Rect bounds = glyphs.Bounds;
		double pen = StrokeThickness;
		return new Size(bounds.Right + pen, bounds.Bottom + pen);
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		Build();
		if (glyphs == null)
		{
			return;
		}
		Pen pen = (Stroke == null || StrokeThickness <= 0.0) ? null : new Pen(Stroke, StrokeThickness * 2.0)
		{
			LineJoin = PenLineJoin.Round
		};
		if (pen != null)
		{
			pen.Freeze();
			drawingContext.PushClip(new RectangleGeometry(new Rect(base.RenderSize)));
			drawingContext.DrawGeometry(null, pen, glyphs);
			drawingContext.Pop();
		}
		drawingContext.DrawGeometry(Fill, null, glyphs);
	}

	private void Build()
	{
		string text = Text ?? "";
		if (text.Length == 0)
		{
			glyphs = null;
			return;
		}
		Typeface typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
		FormattedText formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
		double pen = StrokeThickness;
		glyphs = formatted.BuildGeometry(new Point(pen, pen));
		glyphs.Freeze();
	}
}
