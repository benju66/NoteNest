using System;
using System.Collections.Generic;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public interface ITreeCacheService
    {
        void InvalidateAll();
        void InvalidateCategory(string categoryId);
        Dictionary<string, List<NoteModel>> GetNotesByCategory();
        void SetNotesByCategory(Dictionary<string, List<NoteModel>> notes);
        bool IsCacheValid { get; }
    }
    
    public class TreeCacheService : ITreeCacheService
    {
        private Dictionary<string, List<NoteModel>> _notesByCategory;
        private DateTime _cacheTime;
        private readonly object _cacheLock = new object();
        private readonly IAppLogger _logger;
        private readonly ConfigurationService _config;
        
        public TreeCacheService(IAppLogger logger, ConfigurationService config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        public bool IsCacheValid
        {
            get
            {
                if (!(_config?.Settings?.CacheTreeData ?? true))
                    return false;
                    
                if (_notesByCategory == null)
                    return false;
                    
                var cacheMinutes = _config?.Settings?.TreeCacheMinutes ?? 5;
                var cacheTimeout = TimeSpan.FromMinutes(cacheMinutes);
                
                return DateTime.UtcNow - _cacheTime < cacheTimeout;
            }
        }
        
        public Dictionary<string, List<NoteModel>> GetNotesByCategory()
        {
            lock (_cacheLock)
            {
                return _notesByCategory ?? new Dictionary<string, List<NoteModel>>();
            }
        }
        
        public void SetNotesByCategory(Dictionary<string, List<NoteModel>> notes)
        {
            lock (_cacheLock)
            {
                _notesByCategory = notes ?? new Dictionary<string, List<NoteModel>>();
                _cacheTime = DateTime.UtcNow;
                _logger.Debug($"Tree cache updated with {_notesByCategory.Count} categories");
            }
        }
        
        public void InvalidateAll()
        {
            lock (_cacheLock)
            {
                _notesByCategory = null;
                _logger.Debug("Tree cache invalidated");
            }
        }
        
        public void InvalidateCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return;
            
            lock (_cacheLock)
            {
                if (_notesByCategory?.ContainsKey(categoryId) == true)
                {
                    _notesByCategory.Remove(categoryId);
                    _logger.Debug($"Cache invalidated for category {categoryId}");
                }
            }
        }
    }
}
