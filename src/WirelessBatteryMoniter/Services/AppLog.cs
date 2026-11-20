using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WirelessBatteryMoniter.Services
{
    /// <summary>Very small, dependency-free rolling log.</summary>
    public static class AppLog
    {
        private static readonly object Gate = new object();
        private static string _directory;
        private static string _file;

        internal static void UseDiagnosticDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            _directory = directory;
            _file = Path.Combine(directory, "app.log");
        }

        public static string DirectoryPath
        {
            get
            {
                if (_directory != null) return _directory;

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string candidate = Path.Combine(appData, "WirelessBatteryMoniter");
                if (TryPrepareDirectory(candidate))
                {
                    _directory = candidate;
                    return _directory;
                }

                // Some locked-down environments deny writes under %APPDATA%. Try the
                // local profile first, then a per-user data folder next to the exe.
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string localCandidate = Path.Combine(local, "WirelessBatteryMoniter");
                if (TryPrepareDirectory(localCandidate))
                {
                    _directory = localCandidate;
                    return _directory;
                }

                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
                if (TryPrepareDirectory(fallback))
                {
                    _directory = fallback;
                    return _directory;
                }

                _directory = candidate;
                return _directory;
            }
        }

        private static bool TryPrepareDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string FilePath
        {
            get
            {
                if (_file == null) _file = Path.Combine(DirectoryPath, "app.log");
                return _file;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    RotateIfNeeded();
                    StringBuilder line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    line.Append(" [").Append(level).Append("] ");
                    line.Append(message);
                    if (exception != null)
                    {
                        line.AppendLine();
                        line.Append(exception);
                    }
                    File.AppendAllText(FilePath, line.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the application down.
            }

            Debug.WriteLine("[WirelessBatteryMoniter] " + level + " " + message);
        }

        private static void RotateIfNeeded()
        {
            try
            {
                FileInfo info = new FileInfo(FilePath);
                if (!info.Exists || info.Length < 1024 * 1024) return;
                string old = Path.Combine(DirectoryPath, "app.old.log");
                if (File.Exists(old)) File.Delete(old);
                File.Move(FilePath, old);
            }
            catch
            {
            }
        }
    }
}
