using System;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Notes.Events;

namespace NoteNest.Domain.Notes
{
    public class Note : AggregateRoot
    {
        public NoteId NoteId { get; private set; }
        public override Guid Id => Guid.Parse(NoteId.Value);
        public CategoryId CategoryId { get; private set; }
        public string Title { get; private set; }
        public string Content { get; private set; }
        public string FilePath { get; private set; }
        public bool IsPinned { get; private set; }
        public int Position { get; private set; }

        public Note() { } // Public for event sourcing

        public Note(CategoryId categoryId, string title, string content = "")
        {
            NoteId = NoteId.Create();
            CategoryId = categoryId ?? throw new ArgumentNullException(nameof(categoryId));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Content = content ?? string.Empty;
            CreatedAt = UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteCreatedEvent(NoteId, CategoryId, Title));
        }
        
        /// <summary>
        /// Factory method for creating a Note for opening in editor (workspace restoration)
        /// Used when we only have title and file path, not full domain data
        /// </summary>
        public static Note CreateForOpening(string title, string filePath)
        {
            var note = new Note
            {
                NoteId = NoteId.Create(),
                CategoryId = CategoryId.Create(), // Placeholder - will be loaded from database if needed
                Title = title ?? throw new ArgumentNullException(nameof(title)),
                Content = string.Empty, // Content loaded from file by workspace
                FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath)),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            return note;
        }

        public Result Rename(string newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle))
                return Result.Fail("Title cannot be empty");

            if (newTitle.Length > 255)
                return Result.Fail("Title cannot exceed 255 characters");

            var oldTitle = Title;
            Title = newTitle;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteRenamedEvent(NoteId, oldTitle, newTitle));
            return Result.Ok();
        }

        public Result UpdateContent(string newContent)
        {
            Content = newContent ?? string.Empty;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteContentUpdatedEvent(NoteId));
            return Result.Ok();
        }

        public Result MoveTo(CategoryId newCategoryId)
        {
            if (newCategoryId == null)
                return Result.Fail("Category cannot be null");

            var oldCategoryId = CategoryId;
            CategoryId = newCategoryId;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteMovedEvent(NoteId, oldCategoryId, newCategoryId));
            return Result.Ok();
        }

        /// <summary>
        /// Moves note to a new category and updates file path atomically.
        /// Used by drag & drop and move operations.
        /// </summary>
        public Result Move(CategoryId newCategoryId, string newFilePath)
        {
            if (newCategoryId == null)
                return Result.Fail("Category cannot be null");
            
            if (string.IsNullOrWhiteSpace(newFilePath))
                return Result.Fail("File path cannot be empty");

            var oldCategoryId = CategoryId;
            CategoryId = newCategoryId;
            FilePath = newFilePath;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteMovedEvent(NoteId, oldCategoryId, newCategoryId));
            return Result.Ok();
        }

        public void SetFilePath(string filePath)
        {
            FilePath = filePath;
        }

        public void SetPosition(int position)
        {
            Position = position;
        }

        public void Pin()
        {
            if (!IsPinned)
            {
                IsPinned = true;
                AddDomainEvent(new NotePinnedEvent(NoteId));
            }
        }

        public void Unpin()
        {
            if (IsPinned)
            {
                IsPinned = false;
                AddDomainEvent(new NoteUnpinnedEvent(NoteId));
            }
        }
        
        /// <summary>
        /// Apply event to rebuild aggregate state from event stream.
        /// </summary>
        public override void Apply(IDomainEvent @event)
        {
            switch (@event)
            {
                case NoteCreatedEvent e:
                    NoteId = e.NoteId;
                    CategoryId = e.CategoryId;
                    Title = e.Title;
                    Content = string.Empty;
                    FilePath = string.Empty;
                    CreatedAt = e.OccurredAt;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case NoteContentUpdatedEvent e:
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case NoteRenamedEvent e:
                    Title = e.NewTitle;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case NoteMovedEvent e:
                    CategoryId = e.ToCategoryId;  // Fixed: ToCategoryId not NewCategoryId
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case NotePinnedEvent e:
                    IsPinned = true;
                    break;
                    
                case NoteUnpinnedEvent e:
                    IsPinned = false;
                    break;
            }
        }
    }
}

