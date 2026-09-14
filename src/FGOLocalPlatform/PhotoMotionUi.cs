using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FGOLocalPlatform;

internal static class PhotoMotionUi
{
	public static void Configure(ComboBox combo)
	{
		combo.DisplayMemberPath = "";
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(TextBlock));
		frameworkElementFactory.SetBinding(TextBlock.TextProperty, new Binding("DisplayName"));
		frameworkElementFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
		combo.ItemTemplate = new DataTemplate
		{
			VisualTree = frameworkElementFactory
		};
		combo.HorizontalContentAlignment = HorizontalAlignment.Stretch;
		combo.MaxDropDownHeight = 280.0;
	}
}
