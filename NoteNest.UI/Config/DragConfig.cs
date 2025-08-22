using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace NoteNest.UI.Config
{
    /// <summary>
    /// Configuration settings for drag and drop operations
    /// </summary>
    public class DragConfig : INotifyPropertyChanged
    {
        private static DragConfig? _instance;
        private static readonly object _lock = new object();

        // Default values
        private double _dragThresholdPixels = 5.0;
        private int _springLoadDelayMs = 500;
        private double _mouseUpdateIntervalMs = 16.67; // ~60 FPS
        private double _detachThresholdPixels = 100.0;
        private int _cacheCleanupIntervalMinutes = 5;
        private bool _enableSmoothScrolling = true;
        private bool _enableVisualFeedback = true;
        private double _ghostOpacity = 0.7;
        private int _insertionLineThickness = 2;

        public static DragConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DragConfig();
                            _instance.LoadFromFile();
                        }
                    }
                }
                return _instance;
            }
        }

        #region Properties

        public double DragThresholdPixels
        {
            get => _dragThresholdPixels;
            set
            {
                if (SetProperty(ref _dragThresholdPixels, Math.Max(1.0, Math.Min(20.0, value))))
                {
                    SaveToFile();
                }
            }
        }

        public int SpringLoadDelayMs
        {
            get => _springLoadDelayMs;
            set
            {
                if (SetProperty(ref _springLoadDelayMs, Math.Max(100, Math.Min(2000, value))))
                {
                    SaveToFile();
                }
            }
        }

        public double MouseUpdateIntervalMs
        {
            get => _mouseUpdateIntervalMs;
            set
            {
                if (SetProperty(ref _mouseUpdateIntervalMs, Math.Max(8.33, Math.Min(100.0, value))))
                {
                    SaveToFile();
                }
            }
        }

        public double DetachThresholdPixels
        {
            get => _detachThresholdPixels;
            set
            {
                if (SetProperty(ref _detachThresholdPixels, Math.Max(50.0, Math.Min(300.0, value))))
                {
                    SaveToFile();
                }
            }
        }

        public int CacheCleanupIntervalMinutes
        {
            get => _cacheCleanupIntervalMinutes;
            set
            {
                if (SetProperty(ref _cacheCleanupIntervalMinutes, Math.Max(1, Math.Min(60, value))))
                {
                    SaveToFile();
                }
            }
        }

        public bool EnableSmoothScrolling
        {
            get => _enableSmoothScrolling;
            set
            {
                if (SetProperty(ref _enableSmoothScrolling, value))
                {
                    SaveToFile();
                }
            }
        }

        public bool EnableVisualFeedback
        {
            get => _enableVisualFeedback;
            set
            {
                if (SetProperty(ref _enableVisualFeedback, value))
                {
                    SaveToFile();
                }
            }
        }

        public double GhostOpacity
        {
            get => _ghostOpacity;
            set
            {
                if (SetProperty(ref _ghostOpacity, Math.Max(0.1, Math.Min(1.0, value))))
                {
                    SaveToFile();
                }
            }
        }

        public int InsertionLineThickness
        {
            get => _insertionLineThickness;
            set
            {
                if (SetProperty(ref _insertionLineThickness, Math.Max(1, Math.Min(5, value))))
                {
                    SaveToFile();
                }
            }
        }

        #endregion

        #region Computed Properties

        public long MouseUpdateIntervalTicks => (long)(MouseUpdateIntervalMs * TimeSpan.TicksPerMillisecond);
        public TimeSpan SpringLoadDelay => TimeSpan.FromMilliseconds(SpringLoadDelayMs);
        public TimeSpan CacheCleanupInterval => TimeSpan.FromMinutes(CacheCleanupIntervalMinutes);

        #endregion

        #region File Operations

        private static string ConfigFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "NoteNest", "DragConfig.json");

        private void LoadFromFile()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<DragConfigData>(json);
                    if (config != null)
                    {
                        ApplyConfigData(config);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading drag config: {ex.Message}");
            }
        }

        private void SaveToFile()
        {
            try
            {
                var configDir = Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var config = new DragConfigData
                {
                    DragThresholdPixels = _dragThresholdPixels,
                    SpringLoadDelayMs = _springLoadDelayMs,
                    MouseUpdateIntervalMs = _mouseUpdateIntervalMs,
                    DetachThresholdPixels = _detachThresholdPixels,
                    CacheCleanupIntervalMinutes = _cacheCleanupIntervalMinutes,
                    EnableSmoothScrolling = _enableSmoothScrolling,
                    EnableVisualFeedback = _enableVisualFeedback,
                    GhostOpacity = _ghostOpacity,
                    InsertionLineThickness = _insertionLineThickness
                };

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving drag config: {ex.Message}");
            }
        }

        private void ApplyConfigData(DragConfigData config)
        {
            _dragThresholdPixels = Math.Max(1.0, Math.Min(20.0, config.DragThresholdPixels));
            _springLoadDelayMs = Math.Max(100, Math.Min(2000, config.SpringLoadDelayMs));
            _mouseUpdateIntervalMs = Math.Max(8.33, Math.Min(100.0, config.MouseUpdateIntervalMs));
            _detachThresholdPixels = Math.Max(50.0, Math.Min(300.0, config.DetachThresholdPixels));
            _cacheCleanupIntervalMinutes = Math.Max(1, Math.Min(60, config.CacheCleanupIntervalMinutes));
            _enableSmoothScrolling = config.EnableSmoothScrolling;
            _enableVisualFeedback = config.EnableVisualFeedback;
            _ghostOpacity = Math.Max(0.1, Math.Min(1.0, config.GhostOpacity));
            _insertionLineThickness = Math.Max(1, Math.Min(5, config.InsertionLineThickness));
        }

        #endregion

        #region Reset Methods

        public void ResetToDefaults()
        {
            _dragThresholdPixels = 5.0;
            _springLoadDelayMs = 500;
            _mouseUpdateIntervalMs = 16.67;
            _detachThresholdPixels = 100.0;
            _cacheCleanupIntervalMinutes = 5;
            _enableSmoothScrolling = true;
            _enableVisualFeedback = true;
            _ghostOpacity = 0.7;
            _insertionLineThickness = 2;

            OnPropertyChanged(string.Empty);
            SaveToFile();
        }

        public void OptimizeForPerformance()
        {
            MouseUpdateIntervalMs = 33.33;
            EnableSmoothScrolling = false;
            CacheCleanupIntervalMinutes = 10;
            SaveToFile();
        }

        public void OptimizeForResponsiveness()
        {
            MouseUpdateIntervalMs = 8.33;
            SpringLoadDelayMs = 250;
            DragThresholdPixels = 3.0;
            SaveToFile();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }

    internal class DragConfigData
    {
        public double DragThresholdPixels { get; set; } = 5.0;
        public int SpringLoadDelayMs { get; set; } = 500;
        public double MouseUpdateIntervalMs { get; set; } = 16.67;
        public double DetachThresholdPixels { get; set; } = 100.0;
        public int CacheCleanupIntervalMinutes { get; set; } = 5;
        public bool EnableSmoothScrolling { get; set; } = true;
        public bool EnableVisualFeedback { get; set; } = true;
        public double GhostOpacity { get; set; } = 0.7;
        public int InsertionLineThickness { get; set; } = 2;
    }
}
