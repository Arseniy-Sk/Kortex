using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kortex.ViewModels
{
    public sealed class NotEmptyToVisibility : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool empty = string.IsNullOrEmpty(value as string);
            if ("inv".Equals(p)) return empty ? Visibility.Visible : Visibility.Collapsed;
            return empty ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class BoolToVisibility : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool && (bool)value;
            if ("inv".Equals(p)) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class NullToVisibility : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool isNull = value == null;
            if ("inv".Equals(p)) isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Доля 0..1 в звёздочную ширину колонки (для тонких прогресс-полос).</summary>
    public sealed class RatioToStar : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            double v = value is double ? (double)value : 0;
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            v = Math.Max(0, Math.Min(1, v));
            if ("inv".Equals(p)) v = 1 - v;
            return new GridLength(Math.Max(v, 0.0001), GridUnitType.Star);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class RatioToPercent : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            double v = value is double ? (double)value : 0;
            return Math.Round(v * 100) + "%";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Сравнение с параметром — для подсветки выбранного шага/страницы.</summary>
    public sealed class EqualsToBool : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value != null && value.ToString() == (p == null ? null : p.ToString());
        public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
            (value is bool && (bool)value) ? p : Binding.DoNothing;
    }

    /// <summary>Число &gt; 0 — показать (параметр "inv" инвертирует).</summary>
    public sealed class CountToVisibility : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int n = 0;
            if (value is int) n = (int)value;
            else if (value != null) int.TryParse(value.ToString(), out n);
            bool show = n > 0;
            if ("inv".Equals(p)) show = !show;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
