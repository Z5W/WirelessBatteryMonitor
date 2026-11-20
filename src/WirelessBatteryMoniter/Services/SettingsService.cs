using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.Services
{
    public sealed class SettingsService
    {
        private readonly string _path;

        public SettingsService()
            : this(AppLog.DirectoryPath)
        {
        }

        internal SettingsService(string directory)
        {
            _path = System.IO.Path.Combine(directory, "settings.json");
        }

        public string SettingsPath
        {
            get { return _path; }
        }

        public AppSettings Load()
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(_path))
                {
                    using (FileStream stream = File.OpenRead(_path))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                        settings = serializer.ReadObject(stream) as AppSettings;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to read settings; using defaults.", ex);
                TryBackupCorruptSettings();
            }

            if (settings == null) settings = new AppSettings();
            settings.Normalize();
            settings.StartWithWindows = StartupService.IsEnabled();
            return settings;
        }

        public bool TrySave(AppSettings settings, out string error)
        {
            error = null;
            try
            {
                settings.Normalize();
                Directory.CreateDirectory(AppLog.DirectoryPath);
                string temp = _path + ".tmp";
                using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                    stream.Flush();
                }

                if (File.Exists(_path))
                {
                    string backup = _path + ".bak";
                    try
                    {
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Replace(temp, _path, backup);
                    }
                    catch
                    {
                        File.Copy(temp, _path, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Error("Failed to save settings.", ex);
                return false;
            }
        }

        public void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(AppLog.DirectoryPath);
                System.Diagnostics.Process.Start("explorer.exe", AppLog.DirectoryPath);
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to open settings folder.", ex);
            }
        }

        private void TryBackupCorruptSettings()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string backup = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(_path, backup, true);
                AppLog.Warn("Corrupt settings backed up to " + backup);
            }
            catch
            {
            }
        }
    }
}
