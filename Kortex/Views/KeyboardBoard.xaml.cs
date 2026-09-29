using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kortex.ViewModels;

namespace Kortex.Views
{
    public partial class KeyboardBoard : UserControl
    {
        public static readonly DependencyProperty BoardProperty =
            DependencyProperty.Register(nameof(Board), typeof(KeyboardVm), typeof(KeyboardBoard),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsInteractiveProperty =
            DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(KeyboardBoard),
                new PropertyMetadata(false));

        public KeyboardVm Board
        {
            get => (KeyboardVm)GetValue(BoardProperty);
            set => SetValue(BoardProperty, value);
        }

        public bool IsInteractive
        {
            get => (bool)GetValue(IsInteractiveProperty);
            set => SetValue(IsInteractiveProperty, value);
        }

        public event Action<KeyCapVm> KeyClicked;

        public KeyboardBoard()
        {
            InitializeComponent();
        }

        private void Host_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsInteractive) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is ContentPresenter))
                source = VisualTreeHelper.GetParent(source);

            var presenter = source as ContentPresenter;
            var cap = presenter != null ? presenter.DataContext as KeyCapVm : null;
            if (cap == null || !cap.IsActive) return;

            var handler = KeyClicked;
            if (handler != null) handler(cap);
        }
    }
}
