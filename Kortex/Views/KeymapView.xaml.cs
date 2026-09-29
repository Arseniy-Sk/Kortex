using System.Windows.Controls;
using Kortex.ViewModels;

namespace Kortex.Views
{
    public partial class KeymapView : UserControl
    {
        public KeymapView()
        {
            InitializeComponent();
            Board.KeyClicked += OnKeyClicked;
        }

        private void OnKeyClicked(KeyCapVm cap)
        {
            var shell = DataContext as ShellVm;
            if (shell != null) shell.Keymap.Selected = cap;
        }
    }
}
