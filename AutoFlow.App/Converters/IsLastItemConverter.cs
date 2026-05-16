using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace AutoFlow.App.Converters;

public sealed class IsLastItemConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not ItemsControl itemsControl)
        {
            return false;
        }

        var currentItem = values[0];
        var itemCount = itemsControl.Items.Count;
        if (itemCount == 0)
        {
            return false;
        }

        return ReferenceEquals(itemsControl.Items[itemCount - 1], currentItem);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
