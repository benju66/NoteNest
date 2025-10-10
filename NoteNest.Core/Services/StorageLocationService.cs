using System;
using System.IO;
using Microsoft.Win32;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    public class StorageLocationService
    {
        public string GetOneDrivePath()
        {
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
                return oneDrive;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\OneDrive");
                if (key != null)
                {
                    var userFolder = key.GetValue("UserFolder") as string;
                    if (!string.IsNullOrEmpty(userFolder) && Directory.Exists(userFolder))
                        return userFolder;
                }
            }
            catch { }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var commonPaths = new[] { "OneDrive", "OneDrive - Personal", "OneDrive - Business" };

            foreach (var path in commonPaths)
            {
                var fullPath = Path.Combine(userProfile, path);
                if (Directory.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        public string ResolveNotesPath(StorageMode mode, string? customPath = null)
        {
            switch (mode)
            {
                case StorageMode.OneDrive:
                    var oneDrive = GetOneDrivePath();
                    return oneDrive != null
                        ? Path.Combine(oneDrive, "NoteNest")
                        : GetLocalPath();

                case StorageMode.Custom:
                    return !string.IsNullOrEmpty(customPath) && Directory.Exists(customPath)
                        ? customPath
                        : GetLocalPath();

                case StorageMode.Local:
                default:
                    return GetLocalPath();
            }
        }

        private string GetLocalPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NoteNest"
            );
        }
    }
}


