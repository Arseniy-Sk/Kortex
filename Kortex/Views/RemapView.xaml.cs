using System.Windows;
using System.Windows.Controls;
using Kortex.ViewModels;

namespace Kortex.Views
{
    public partial class RemapView : UserControl
    {
        public RemapView()
        {
            InitializeComponent();
            Board.KeyClicked += OnKeyClicked;
        }

        private void OnKeyClicked(KeyCapVm cap)
        {
            var shell = DataContext as ShellVm;
            if (shell != null) shell.Remap.SelectedCap = cap;
        }
    }
}
