using System;
using System.IO;

namespace QuotaHub.Services
{
    /// <summary>
    /// Resolves file paths in both packaged (MSIX) and unpackaged (portable) runs.
    /// Packaged behavior is unchanged; unpackaged falls back to the exe directory
    /// and %LocalAppData%\QuotaHub.
    /// </summary>
    internal static class AppPaths
    {
        private static string? _dataFolderPath;

        public static string GetIconPath()
        {
            string baseDirPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
            if (File.Exists(baseDirPath))
            {
                return baseDirPath;
            }

            try
            {
                return Path.Combine(
                    Windows.ApplicationModel.Package.Current.InstalledLocation.Path,
                    "Assets",
                    "TrayIcon.ico");
            }
            catch
            {
                return baseDirPath;
            }
        }

        public static string GetDataFolderPath()
        {
            if (_dataFolderPath != null)
            {
                return _dataFolderPath;
            }

            try
            {
                _dataFolderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                return _dataFolderPath;
            }
            catch
            {
                // No package identity (portable run).
            }

            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuotaHub");
            Directory.CreateDirectory(fallback);
            _dataFolderPath = fallback;
            return _dataFolderPath;
        }
    }
}
