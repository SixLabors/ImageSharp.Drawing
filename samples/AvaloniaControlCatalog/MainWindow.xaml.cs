using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ControlCatalog
{
    public partial class MainWindow : Window
    {
        private NativeMenu? _recentMenu;

        public MainWindow()
        {
            InitializeComponent();

            if (TryGetStartupSize(out Size startupSize))
            {
                Width = startupSize.Width;
                Height = startupSize.Height;
            }

            _recentMenu = ((NativeMenu.GetMenu(this)?.Items[0] as NativeMenuItem)?.Menu?.Items[2] as NativeMenuItem)?.Menu;
        }

        public static string MenuQuitHeader => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Quit Avalonia" : "E_xit";

        public static KeyGesture MenuQuitGesture => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ?
            new KeyGesture(Key.Q, KeyModifiers.Meta) :
            new KeyGesture(Key.F4, KeyModifiers.Alt);

        public void OnOpenClicked(object sender, EventArgs args)
        {
            _recentMenu?.Items.Insert(0, new NativeMenuItem("Item " + (_recentMenu.Items.Count + 1)));
        }

        public void OnCloseClicked(object sender, EventArgs args)
        {
            Close();
        }

        /// <summary>
        /// Reads the optional startup window size used by automated renderer captures.
        /// </summary>
        /// <param name="size">The parsed startup size.</param>
        /// <returns><see langword="true"/> when a valid startup size was provided.</returns>
        private static bool TryGetStartupSize(out Size size)
        {
            size = default;
            string? value = Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_WINDOW_SIZE");

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split('x', 'X', ',');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                return false;
            }

            size = new Size(width, height);
            return true;
        }
    }
}
