using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace QuotaHub
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            AppWindow.Hide();
            args.Handled = true;
        }
    }
}
