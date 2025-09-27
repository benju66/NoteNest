using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Trees
{
    /// <summary>
    /// Rich domain model for tree nodes with GUID identity and comprehensive metadata.
    /// Supports both categories (folders) and notes (files) in a unified hierarchy.
    /// Files remain the source of truth - this is a rebuildable performance cache.
    /// </summary>
    public class TreeNode : Entity
    {
        // =============================================================================
        // IDENTITY & HIERARCHY
        // =============================================================================
        
        public Guid Id { get; private set; }
        public Guid? ParentId { get; private set; }
        
        // =============================================================================
        // PATH INFORMATION
        // =============================================================================
        
        /// <summary>Normalized relative path (lowercase, forward slashes) - for database queries</summary>
        public string CanonicalPath { get; private set; }
        
        /// <summary>Original case relative path - for UI display</summary>
        public string DisplayPath { get; private set; }
        
        /// <summary>Full absolute path - for file system operations</summary>
        public string AbsolutePath { get; private set; }
        
        // =============================================================================
        // NODE INFORMATION
        // =============================================================================
        
        public TreeNodeType NodeType { get; private set; }
        
        /// <summary>File or folder name without extension</summary>
        public string Name { get; private set; }
        
        /// <summary>File extension (.rtf, .txt, .md) - null for categories</summary>
        public string FileExtension { get; private set; }
        
        // =============================================================================
        // FILE METADATA (cached for performance)
        // =============================================================================
        
        /// <summary>File size in bytes - null for categories</summary>
        public long? FileSize { get; private set; }
        
        public new DateTime CreatedAt { get; private set; }
        public new DateTime ModifiedAt { get; private set; }
        public DateTime? AccessedAt { get; private set; }
        
        // =============================================================================
        // HASH-BASED CHANGE DETECTION
        // =============================================================================
        
        /// <summary>xxHash64 of first 4KB - for fast change detection</summary>
        public string QuickHash { get; private set; }
        
        /// <summary>xxHash64 of complete file - for integrity verification</summary>
        public string FullHash { get; private set; }
        
        public string HashAlgorithm { get; private set; } = "xxHash64";
        public DateTime? HashCalculatedAt { get; private set; }
        
        // =============================================================================
        // UI STATE PERSISTENCE
        // =============================================================================
        
        public bool IsExpanded { get; private set; }
        public bool IsPinned { get; private set; }
        public bool IsSelected { get; private set; }
        
        // =============================================================================
        // ORGANIZATION & CUSTOMIZATION
        // =============================================================================
        
        public int SortOrder { get; private set; }
        public string ColorTag { get; private set; }
        public string IconOverride { get; private set; }
        
        // =============================================================================
        // SOFT DELETE SUPPORT
        // =============================================================================
        
        public bool IsDeleted { get; private set; }
        public DateTime? DeletedAt { get; private set; }
        
        // =============================================================================
        // EXTENSIBILITY
        // =============================================================================
        
        public int MetadataVersion { get; private set; } = 1;
        public string CustomProperties { get; private set; } // JSON
        
        // =============================================================================
        // DOMAIN EVENTS
        // =============================================================================
        
        private readonly List<IDomainEvent> _domainEvents = new();
        public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        // =============================================================================
        // CONSTRUCTORS
        // =============================================================================
        
        private TreeNode() { } // For EF Core and database deserialization

        private TreeNode(
            Guid id,
            Guid? parentId,
            string absolutePath,
            string rootPath,
            TreeNodeType nodeType,
            string name,
            string fileExtension = null)
        {
            Id = id;
            ParentId = parentId;
            AbsolutePath = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath));
            NodeType = nodeType;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            FileExtension = fileExtension;
            
            // Calculate paths
            CanonicalPath = GetCanonicalPath(absolutePath, rootPath);
            DisplayPath = GetDisplayPath(absolutePath, rootPath);
            
            // Initialize timestamps
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = DateTime.UtcNow;
            
            // Default values
            IsExpanded = false;
            IsPinned = false;
            IsSelected = false;
            SortOrder = 0;
            IsDeleted = false;
            MetadataVersion = 1;
        }

        // =============================================================================
        // FACTORY METHODS
        // =============================================================================
        
        /// <summary>
        /// Create a category (folder) tree node
        /// </summary>
        public static TreeNode CreateCategory(string absolutePath, string rootPath, TreeNode parent = null)
        {
            if (!Directory.Exists(absolutePath))
                throw new DirectoryNotFoundException($"Directory not found: {absolutePath}");

            var id = GenerateDeterministicGuid(absolutePath);
            var name = Path.GetFileName(absolutePath);
            
            var node = new TreeNode(
                id: id,
                parentId: parent?.Id,
                absolutePath: absolutePath,
                rootPath: rootPath,
                nodeType: TreeNodeType.Category,
                name: name
            );
            
            // Set file system metadata
            var dirInfo = new DirectoryInfo(absolutePath);
            node.CreatedAt = dirInfo.CreationTimeUtc;
            node.ModifiedAt = dirInfo.LastWriteTimeUtc;
            node.AccessedAt = dirInfo.LastAccessTimeUtc;
            
            // Raise domain event
            node.AddDomainEvent(new TreeNodeCreatedEvent(node.Id, node.NodeType, node.CanonicalPath));
            
            return node;
        }
        
        /// <summary>
        /// Create a note (file) tree node
        /// </summary>
        public static TreeNode CreateNote(string absolutePath, string rootPath, TreeNode parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent), "Notes must have a parent category");
            
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"File not found: {absolutePath}");

            var fileInfo = new FileInfo(absolutePath);
            var id = GenerateDeterministicGuid(absolutePath);
            var name = Path.GetFileNameWithoutExtension(absolutePath);
            var extension = fileInfo.Extension.ToLowerInvariant();
            
            var node = new TreeNode(
                id: id,
                parentId: parent.Id,
                absolutePath: absolutePath,
                rootPath: rootPath,
                nodeType: TreeNodeType.Note,
                name: name,
                fileExtension: extension
            );
            
            // Set file system metadata
            node.FileSize = fileInfo.Length;
            node.CreatedAt = fileInfo.CreationTimeUtc;
            node.ModifiedAt = fileInfo.LastWriteTimeUtc;
            node.AccessedAt = fileInfo.LastAccessTimeUtc;
            
            // Raise domain event
            node.AddDomainEvent(new TreeNodeCreatedEvent(node.Id, node.NodeType, node.CanonicalPath));
            
            return node;
        }

        // =============================================================================
        // HASH OPERATIONS
        // =============================================================================
        
        /// <summary>
        /// Update hash values for change detection
        /// </summary>
        public void UpdateHash(string quickHash, string fullHash = null)
        {
            if (NodeType != TreeNodeType.Note)
                throw new InvalidOperationException("Cannot calculate hash for categories");
                
            QuickHash = quickHash ?? throw new ArgumentNullException(nameof(quickHash));
            if (!string.IsNullOrEmpty(fullHash))
                FullHash = fullHash;
            
            HashCalculatedAt = DateTime.UtcNow;
            ModifiedAt = DateTime.UtcNow;
            
            AddDomainEvent(new TreeNodeHashUpdatedEvent(Id, QuickHash, FullHash));
        }
        
        /// <summary>
        /// Check if file has changed based on hash comparison
        /// </summary>
        public bool HasFileChanged(string currentQuickHash)
        {
            return NodeType == TreeNodeType.Note && QuickHash != currentQuickHash;
        }

        // =============================================================================
        // STATE MANAGEMENT
        // =============================================================================
        
        /// <summary>
        /// Toggle pinned status
        /// </summary>
        public void TogglePinned()
        {
            IsPinned = !IsPinned;
            ModifiedAt = DateTime.UtcNow;
            
            AddDomainEvent(new TreeNodePinnedChangedEvent(Id, IsPinned));
        }
        
        /// <summary>
        /// Set expansion state
        /// </summary>
        public void SetExpanded(bool expanded)
        {
            if (NodeType != TreeNodeType.Category)
                throw new InvalidOperationException("Only categories can be expanded");
                
            IsExpanded = expanded;
            AddDomainEvent(new TreeNodeExpandedChangedEvent(Id, expanded));
        }
        
        /// <summary>
        /// Set selection state
        /// </summary>
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selected)
                AccessedAt = DateTime.UtcNow;
                
            AddDomainEvent(new TreeNodeSelectionChangedEvent(Id, selected));
        }
        
        /// <summary>
        /// Update sort order
        /// </summary>
        public void UpdateSortOrder(int sortOrder)
        {
            SortOrder = sortOrder;
            ModifiedAt = DateTime.UtcNow;
            
            AddDomainEvent(new TreeNodeSortOrderChangedEvent(Id, sortOrder));
        }
        
        /// <summary>
        /// Set color tag
        /// </summary>
        public void SetColorTag(string colorTag)
        {
            ColorTag = colorTag;
            ModifiedAt = DateTime.UtcNow;
            
            AddDomainEvent(new TreeNodeColorTagChangedEvent(Id, colorTag));
        }

        // =============================================================================
        // LIFECYCLE OPERATIONS
        // =============================================================================
        
        /// <summary>
        /// Soft delete the node
        /// </summary>
        public void MarkDeleted()
        {
            IsDeleted = true;
            DeletedAt = DateTime.UtcNow;
            ModifiedAt = DateTime.UtcNow;
            
            AddDomainEvent(new TreeNodeDeletedEvent(Id, NodeType, CanonicalPath));
        }
        
        /// <summary>
        /// Restore from soft delete
        /// </summary>
        public void RestoreFromDelete()
        {
            if (!IsDeleted)
                throw new InvalidOperationException("Node is not deleted");
                
            IsDeleted = false;
            DeletedAt = null;
            ModifiedAt = DateTime.UtcNow;
            
            AddDomainEvent(new TreeNodeRestoredEvent(Id, NodeType, CanonicalPath));
        }
        
        /// <summary>
        /// Update file system metadata from actual file
        /// </summary>
        public Result RefreshFromFileSystem()
        {
            try
            {
                if (NodeType == TreeNodeType.Category)
                {
                    if (!Directory.Exists(AbsolutePath))
                        return Result.Fail($"Directory not found: {AbsolutePath}");
                        
                    var dirInfo = new DirectoryInfo(AbsolutePath);
                    CreatedAt = dirInfo.CreationTimeUtc;
                    ModifiedAt = dirInfo.LastWriteTimeUtc;
                    AccessedAt = dirInfo.LastAccessTimeUtc;
                }
                else
                {
                    if (!File.Exists(AbsolutePath))
                        return Result.Fail($"File not found: {AbsolutePath}");
                        
                    var fileInfo = new FileInfo(AbsolutePath);
                    FileSize = fileInfo.Length;
                    CreatedAt = fileInfo.CreationTimeUtc;
                    ModifiedAt = fileInfo.LastWriteTimeUtc;
                    AccessedAt = fileInfo.LastAccessTimeUtc;
                }
                
                AddDomainEvent(new TreeNodeRefreshedEvent(Id, ModifiedAt));
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to refresh from file system: {ex.Message}");
            }
        }

        // =============================================================================
        // MOVE/RENAME OPERATIONS
        // =============================================================================
        
        /// <summary>
        /// Update node after file system move/rename
        /// </summary>
        public Result UpdatePath(string newAbsolutePath, string rootPath, Guid? newParentId = null)
        {
            try
            {
                var oldPath = AbsolutePath;
                
                AbsolutePath = newAbsolutePath;
                CanonicalPath = GetCanonicalPath(newAbsolutePath, rootPath);
                DisplayPath = GetDisplayPath(newAbsolutePath, rootPath);
                Name = NodeType == TreeNodeType.Category 
                    ? Path.GetFileName(newAbsolutePath)
                    : Path.GetFileNameWithoutExtension(newAbsolutePath);
                
                if (newParentId.HasValue)
                    ParentId = newParentId;
                
                ModifiedAt = DateTime.UtcNow;
                
                AddDomainEvent(new TreeNodeMovedEvent(Id, oldPath, newAbsolutePath, ParentId));
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to update path: {ex.Message}");
            }
        }

        // =============================================================================
        // HELPER METHODS
        // =============================================================================
        
        /// <summary>
        /// Generate consistent GUID from file path for stable identity across app runs
        /// </summary>
        private static Guid GenerateDeterministicGuid(string path)
        {
            // Use UUID v5 (namespace-based, SHA-1) for consistent generation
            var namespaceId = new Guid("6ba7b814-9dad-11d1-80b4-00c04fd430c8"); // File system namespace
            var nameBytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
            
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            sha1.TransformBlock(namespaceId.ToByteArray(), 0, 16, null, 0);
            sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
            
            var hash = sha1.Hash;
            
            // Set version (5) and variant bits according to UUID spec
            hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            
            return new Guid(hash.Take(16).ToArray());
        }
        
        private static string GetCanonicalPath(string absolutePath, string rootPath)
        {
            var relativePath = Path.GetRelativePath(rootPath, absolutePath);
            return relativePath.Replace('\\', '/').ToLowerInvariant();
        }
        
        private static string GetDisplayPath(string absolutePath, string rootPath)
        {
            return Path.GetRelativePath(rootPath, absolutePath).Replace('\\', '/');
        }
        
        private void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }
        
        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }
        
        // =============================================================================
        // DATABASE DESERIALIZATION FACTORY
        // =============================================================================
        
        /// <summary>
        /// Create TreeNode from database data without requiring file system access.
        /// Used for deserializing from database where files may not exist.
        /// </summary>
        public static TreeNode CreateFromDatabase(
            Guid id,
            Guid? parentId,
            string canonicalPath,
            string displayPath,
            string absolutePath,
            TreeNodeType nodeType,
            string name,
            string fileExtension = null,
            long? fileSize = null,
            DateTime? createdAt = null,
            DateTime? modifiedAt = null,
            DateTime? accessedAt = null,
            string quickHash = null,
            string fullHash = null,
            string hashAlgorithm = "xxHash64",
            DateTime? hashCalculatedAt = null,
            bool isExpanded = false,
            bool isPinned = false,
            bool isSelected = false,
            int sortOrder = 0,
            string colorTag = null,
            string iconOverride = null,
            bool isDeleted = false,
            DateTime? deletedAt = null,
            int metadataVersion = 1,
            string customProperties = null)
        {
            var node = new TreeNode
            {
                Id = id,
                ParentId = parentId,
                CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath)),
                DisplayPath = displayPath ?? throw new ArgumentNullException(nameof(displayPath)),
                AbsolutePath = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath)),
                NodeType = nodeType,
                Name = name ?? throw new ArgumentNullException(nameof(name)),
                FileExtension = fileExtension,
                FileSize = fileSize,
                CreatedAt = createdAt ?? DateTime.UtcNow,
                ModifiedAt = modifiedAt ?? DateTime.UtcNow,
                AccessedAt = accessedAt,
                QuickHash = quickHash,
                FullHash = fullHash,
                HashAlgorithm = hashAlgorithm ?? "xxHash64",
                HashCalculatedAt = hashCalculatedAt,
                IsExpanded = isExpanded,
                IsPinned = isPinned,
                IsSelected = isSelected,
                SortOrder = sortOrder,
                ColorTag = colorTag,
                IconOverride = iconOverride,
                IsDeleted = isDeleted,
                DeletedAt = deletedAt,
                MetadataVersion = metadataVersion,
                CustomProperties = customProperties
            };
            
            return node;
        }
        
        // =============================================================================
        // VALIDATION
        // =============================================================================
        
        public bool IsValidNoteFile()
        {
            if (NodeType != TreeNodeType.Note)
                return false;
                
            var validExtensions = new[] { ".rtf", ".txt", ".md", ".markdown" };
            return validExtensions.Contains(FileExtension?.ToLowerInvariant());
        }
        
        public bool CanHaveChildren()
        {
            return NodeType == TreeNodeType.Category && !IsDeleted;
        }
        
        // =============================================================================
        // DISPLAY HELPERS
        // =============================================================================
        
        public string GetDisplayName()
        {
            return NodeType == TreeNodeType.Note && !string.IsNullOrEmpty(FileExtension)
                ? $"{Name}{FileExtension}"
                : Name;
        }
        
        public string GetSizeDisplay()
        {
            if (!FileSize.HasValue) return "";
            
            var size = FileSize.Value;
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024:F1} KB";
            if (size < 1024 * 1024 * 1024) return $"{size / (1024 * 1024):F1} MB";
            return $"{size / (1024 * 1024 * 1024):F1} GB";
        }
        
        public override string ToString()
        {
            return $"{NodeType}: {Name} ({CanonicalPath})";
        }
    }
    
    // =============================================================================
    // ENUMS
    // =============================================================================
    
    public enum TreeNodeType
    {
        Category = 0,  // Folder/Directory
        Note = 1       // File
    }
    
    // =============================================================================
    // DOMAIN EVENTS
    // =============================================================================
    
    public record TreeNodeCreatedEvent(Guid NodeId, TreeNodeType NodeType, string Path) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeDeletedEvent(Guid NodeId, TreeNodeType NodeType, string Path) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeRestoredEvent(Guid NodeId, TreeNodeType NodeType, string Path) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeMovedEvent(Guid NodeId, string OldPath, string NewPath, Guid? NewParentId) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeHashUpdatedEvent(Guid NodeId, string QuickHash, string FullHash) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodePinnedChangedEvent(Guid NodeId, bool IsPinned) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeExpandedChangedEvent(Guid NodeId, bool IsExpanded) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeSelectionChangedEvent(Guid NodeId, bool IsSelected) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeSortOrderChangedEvent(Guid NodeId, int SortOrder) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeColorTagChangedEvent(Guid NodeId, string ColorTag) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
    
    public record TreeNodeRefreshedEvent(Guid NodeId, DateTime RefreshedAt) : IDomainEvent
    {
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
}
