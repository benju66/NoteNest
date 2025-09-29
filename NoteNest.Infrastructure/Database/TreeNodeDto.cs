using System;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Data Transfer Object for TreeNode database mapping.
    /// Maps between database columns and TreeNode domain model.
    /// </summary>
    public class TreeNodeDto
    {
        // Identity
        public string Id { get; set; }
        public string ParentId { get; set; }
        
        // Paths
        public string CanonicalPath { get; set; }
        public string DisplayPath { get; set; }
        public string AbsolutePath { get; set; }
        
        // Node info
        public string NodeType { get; set; }
        public string Name { get; set; }
        public string FileExtension { get; set; }
        
        // Metadata
        public long? FileSize { get; set; }
        public long CreatedAt { get; set; }
        public long ModifiedAt { get; set; }
        public long? AccessedAt { get; set; }
        
        // Hashing
        public string QuickHash { get; set; }
        public string FullHash { get; set; }
        public string HashAlgorithm { get; set; }
        public long? HashCalculatedAt { get; set; }
        
        // State
        public int IsExpanded { get; set; }
        public int IsPinned { get; set; }
        public int IsSelected { get; set; }
        
        // Organization
        public int SortOrder { get; set; }
        public string ColorTag { get; set; }
        public string IconOverride { get; set; }
        
        // Soft delete
        public int IsDeleted { get; set; }
        public long? DeletedAt { get; set; }
        
        // Extensibility
        public int MetadataVersion { get; set; }
        public string CustomProperties { get; set; }
        
        // Additional fields from views
        public int? Level { get; set; }
        public string RootId { get; set; }
        
        /// <summary>
        /// Convert DTO to domain model using database-specific factory
        /// </summary>
        public TreeNode ToDomainModel()
        {
            try
            {
                // Parse enum (handle both uppercase and lowercase database values, with null check)
                if (string.IsNullOrEmpty(NodeType))
                {
                    throw new InvalidOperationException($"Node {Id} has null or empty NodeType - database value: '{NodeType ?? "NULL"}'");
                }
                
                // Add diagnostic logging for troubleshooting
                Console.WriteLine($"[DIAGNOSTIC] Reading node {Id} with NodeType='{NodeType}'");
                var nodeType = Enum.Parse<TreeNodeType>(NodeType, ignoreCase: true);
                
                // Use database factory to avoid file system requirements
                var node = TreeNode.CreateFromDatabase(
                    id: Guid.Parse(Id),
                    parentId: string.IsNullOrEmpty(ParentId) ? null : Guid.Parse(ParentId),
                    canonicalPath: CanonicalPath,
                    displayPath: DisplayPath,
                    absolutePath: AbsolutePath,
                    nodeType: nodeType,
                    name: Name,
                    fileExtension: FileExtension,
                    fileSize: FileSize,
                    createdAt: DateTimeOffset.FromUnixTimeSeconds(CreatedAt).UtcDateTime,
                    modifiedAt: DateTimeOffset.FromUnixTimeSeconds(ModifiedAt).UtcDateTime,
                    accessedAt: AccessedAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(AccessedAt.Value).UtcDateTime : null,
                    quickHash: QuickHash,
                    fullHash: FullHash,
                    hashAlgorithm: HashAlgorithm ?? "xxHash64",
                    hashCalculatedAt: HashCalculatedAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(HashCalculatedAt.Value).UtcDateTime : null,
                    isExpanded: IsExpanded == 1,
                    isPinned: IsPinned == 1,
                    isSelected: IsSelected == 1,
                    sortOrder: SortOrder,
                    colorTag: ColorTag,
                    iconOverride: IconOverride,
                    isDeleted: IsDeleted == 1,
                    deletedAt: DeletedAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(DeletedAt.Value).UtcDateTime : null,
                    metadataVersion: MetadataVersion,
                    customProperties: CustomProperties
                );
                
                return node;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to map TreeNodeDto to domain model: {ex.Message}", ex);
            }
        }
    }
}
