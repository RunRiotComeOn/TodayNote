using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;

namespace TodayNote
{
    public sealed class ThemeColorSource : INotifyPropertyChanged
    {
        Color currentColor;
        public event PropertyChangedEventHandler PropertyChanged;
        public SolidColorBrush Brush { get; private set; }
        public Color Value
        {
            get { return currentColor; }
            set
            {
                if (currentColor == value) return;
                currentColor = value;
                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("Value"));
            }
        }

        public ThemeColorSource(Color initial)
        {
            currentColor = initial;
            // Complete the binding BEFORE publishing the brush to resources.
            // Data-bound Freezables cannot be frozen by WPF resource/style caching.
            // Theme changes update the source rather than setting Brush.Color.
            Brush = new SolidColorBrush(initial);
            BindingOperations.SetBinding(Brush, SolidColorBrush.ColorProperty,
                new Binding("Value") { Source = this, Mode = BindingMode.OneWay });
        }
    }
}
