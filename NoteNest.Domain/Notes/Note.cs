using System;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Notes.Events;

namespace NoteNest.Domain.Notes
{
    public class Note : AggregateRoot
    {
        public NoteId Id { get; private set; }
        public CategoryId CategoryId { get; private set; }
        public string Title { get; private set; }
        public string Content { get; private set; }
        public string FilePath { get; private set; }
        public bool IsPinned { get; private set; }
        public int Position { get; private set; }

        private Note() { } // For serialization

        public Note(CategoryId categoryId, string title, string content = "")
        {
            Id = NoteId.Create();
            CategoryId = categoryId ?? throw new ArgumentNullException(nameof(categoryId));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Content = content ?? string.Empty;
            CreatedAt = UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteCreatedEvent(Id, CategoryId, Title));
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

            AddDomainEvent(new NoteRenamedEvent(Id, oldTitle, newTitle));
            return Result.Ok();
        }

        public Result UpdateContent(string newContent)
        {
            Content = newContent ?? string.Empty;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteContentUpdatedEvent(Id));
            return Result.Ok();
        }

        public Result MoveTo(CategoryId newCategoryId)
        {
            if (newCategoryId == null)
                return Result.Fail("Category cannot be null");

            var oldCategoryId = CategoryId;
            CategoryId = newCategoryId;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new NoteMovedEvent(Id, oldCategoryId, newCategoryId));
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
                AddDomainEvent(new NotePinnedEvent(Id));
            }
        }

        public void Unpin()
        {
            if (IsPinned)
            {
                IsPinned = false;
                AddDomainEvent(new NoteUnpinnedEvent(Id));
            }
        }
    }
}
