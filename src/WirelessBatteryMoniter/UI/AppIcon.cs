using System.Drawing;

namespace WirelessBatteryMoniter.UI
{
    internal static class AppIcon
    {
        internal static Icon Load(int size)
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("WirelessBatteryMoniter.AppIcon"))
            using (var icon = new Icon(stream, size, size))
                return (Icon)icon.Clone();
        }

        internal static Bitmap LoadLogo()
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("WirelessBatteryMoniter.AppLogo"))
            using (var image = Image.FromStream(stream))
                return new Bitmap(image);
        }
    }
}
