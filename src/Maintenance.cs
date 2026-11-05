using System;
using System.IO;

namespace ScreenLine
{
    internal static class Maintenance
    {
        internal static bool IsInstalled
        {
            get
            {
                string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PickSnap");
                return string.Equals(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'), Path.GetFullPath(standard).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static string RetainedDataPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickSnap", "Data"); } }
    }
}
