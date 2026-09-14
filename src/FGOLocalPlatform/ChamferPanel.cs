using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>
/// The plate every surface in this launcher is made of: a rectangle with the top-left and
/// bottom-right corners cut away, as the game draws its menu plates and panels. Optionally it also
/// draws the four gold corner brackets of the game's dialog panels.
///
/// It is a <see cref="Decorator" /> rather than a templated control on purpose. The child stays in
/// the namescope it was written in, so bindings by element name keep working, and the outline is
/// drawn from the arranged size instead of a binding to ActualWidth, which does not raise change
/// notifications.
/// </summary>
public class ChamferPanel : Decorator
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(ChamferPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(ChamferPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register("StrokeThickness", typeof(double), typeof(ChamferPanel), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty CutProperty = DependencyProperty.Register("Cut", typeof(double), typeof(ChamferPanel), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty OrnamentBrushProperty = DependencyProperty.Register("OrnamentBrush", typeof(Brush), typeof(ChamferPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register("Padding", typeof(Thickness), typeof(ChamferPanel), new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

	private const double OrnamentSize = 15.0;

	private const double OrnamentInset = 6.0;

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

	public double Cut
	{
		get => (double)GetValue(CutProperty);
		set => SetValue(CutProperty, value);
	}

	/// <summary>Set this to draw the game's four corner brackets; leave it null for a plain plate.</summary>
	public Brush OrnamentBrush
	{
		get => (Brush)GetValue(OrnamentBrushProperty);
		set => SetValue(OrnamentBrushProperty, value);
	}

	public Thickness Padding
	{
		get => (Thickness)GetValue(PaddingProperty);
		set => SetValue(PaddingProperty, value);
	}

	protected override Size MeasureOverride(Size constraint)
	{
		Thickness padding = Padding;
		UIElement child = base.Child;
		if (child == null)
		{
			return new Size(padding.Left + padding.Right, padding.Top + padding.Bottom);
		}
		Size inner = new Size(System.Math.Max(0.0, constraint.Width - padding.Left - padding.Right), System.Math.Max(0.0, constraint.Height - padding.Top - padding.Bottom));
		child.Measure(inner);
		return new Size(child.DesiredSize.Width + padding.Left + padding.Right, child.DesiredSize.Height + padding.Top + padding.Bottom);
	}

	protected override Size ArrangeOverride(Size arrangeSize)
	{
		Thickness padding = Padding;
		base.Child?.Arrange(new Rect(padding.Left, padding.Top, System.Math.Max(0.0, arrangeSize.Width - padding.Left - padding.Right), System.Math.Max(0.0, arrangeSize.Height - padding.Top - padding.Bottom)));
		return arrangeSize;
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		double width = base.RenderSize.Width;
		double height = base.RenderSize.Height;
		if (width <= 0.0 || height <= 0.0)
		{
			return;
		}
		double thickness = (Stroke == null) ? 0.0 : StrokeThickness;
		double inset = thickness / 2.0;
		double cut = System.Math.Max(0.0, System.Math.Min(Cut, System.Math.Min(width, height) / 2.0));

		StreamGeometry outline = new StreamGeometry();
		using (StreamGeometryContext context = outline.Open())
		{
			context.BeginFigure(new Point(cut + inset, inset), isFilled: true, isClosed: true);
			context.LineTo(new Point(width - inset, inset), isStroked: true, isSmoothJoin: false);
			context.LineTo(new Point(width - inset, height - cut - inset), isStroked: true, isSmoothJoin: false);
			context.LineTo(new Point(width - cut - inset, height - inset), isStroked: true, isSmoothJoin: false);
			context.LineTo(new Point(inset, height - inset), isStroked: true, isSmoothJoin: false);
			context.LineTo(new Point(inset, cut + inset), isStroked: true, isSmoothJoin: false);
		}
		outline.Freeze();

		Pen pen = null;
		if (thickness > 0.0)
		{
			pen = new Pen(Stroke, thickness);
			pen.Freeze();
		}
		drawingContext.DrawGeometry(Fill, pen, outline);

		Brush ornament = OrnamentBrush;
		if (ornament == null)
		{
			return;
		}
		Pen ornamentPen = new Pen(ornament, 1.2);
		ornamentPen.Freeze();
		DrawOrnament(drawingContext, ornamentPen, OrnamentInset, OrnamentInset, 1.0, 1.0);
		DrawOrnament(drawingContext, ornamentPen, width - OrnamentInset, OrnamentInset, -1.0, 1.0);
		DrawOrnament(drawingContext, ornamentPen, OrnamentInset, height - OrnamentInset, 1.0, -1.0);
		DrawOrnament(drawingContext, ornamentPen, width - OrnamentInset, height - OrnamentInset, -1.0, -1.0);
	}

	/// <summary>The L bracket from the game's panels, mirrored into each corner.</summary>
	private static void DrawOrnament(DrawingContext drawingContext, Pen pen, double x, double y, double signX, double signY)
	{
		double size = OrnamentSize;
		double corner = size * 0.32;
		drawingContext.DrawLine(pen, new Point(x, y + signY * size), new Point(x, y + signY * corner));
		drawingContext.DrawLine(pen, new Point(x, y + signY * corner), new Point(x + signX * corner, y));
		drawingContext.DrawLine(pen, new Point(x + signX * corner, y), new Point(x + signX * size, y));
		double innerOffset = size * 0.24;
		double innerLength = size * 0.62;
		drawingContext.DrawLine(pen, new Point(x + signX * innerOffset, y + signY * innerLength), new Point(x + signX * innerOffset, y + signY * (innerOffset + corner * 0.3)));
		drawingContext.DrawLine(pen, new Point(x + signX * innerOffset, y + signY * (innerOffset + corner * 0.3)), new Point(x + signX * (innerOffset + corner * 0.3), y + signY * innerOffset));
		drawingContext.DrawLine(pen, new Point(x + signX * (innerOffset + corner * 0.3), y + signY * innerOffset), new Point(x + signX * innerLength, y + signY * innerOffset));
	}
}
