using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LangJsonEditor
{
	public class EmptyStringToBrushConverter : IValueConverter
	{
		public Brush EmptyBrush { get; set; } =
			new SolidColorBrush(Color.FromRgb(255, 220, 220));

		public Brush NormalBrush { get; set; } = Brushes.Transparent;

		public object Convert(object value, Type targetType,
							  object parameter, CultureInfo culture)
		{
			if (value == null)
				return EmptyBrush;

			if (value is string s && string.IsNullOrWhiteSpace(s))
				return EmptyBrush;

			return NormalBrush;
		}

		public object ConvertBack(object value, Type targetType,
								  object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

}