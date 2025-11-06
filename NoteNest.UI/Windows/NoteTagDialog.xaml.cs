using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.NoteTags.Commands.SetNoteTag;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Dialog for managing note tags.
    /// Event-sourced version - uses ITagQueryService.
    /// </summary>
    public partial class NoteTagDialog : Window
    {
        private readonly Guid _noteId;
        private readonly string _noteTitle;
        private readonly IMediator _mediator;
        private readonly ITagQueryService _tagQueryService;
        private readonly ITreeQueryService _treeQueryService;
        private readonly IAppLogger _logger;
        private readonly ObservableCollection<string> _tags;
        private readonly ObservableCollection<string> _inheritedTags;

        public NoteTagDialog(
            Guid noteId, 
            string noteTitle,
            IMediator mediator,
            ITagQueryService tagQueryService,
            ITreeQueryService treeQueryService,
            IAppLogger logger)
        {
            InitializeComponent();
            
            _noteId = noteId;
            _noteTitle = noteTitle;
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _tagQueryService = tagQueryService ?? throw new ArgumentNullException(nameof(tagQueryService));
            _treeQueryService = treeQueryService ?? throw new ArgumentNullException(nameof(treeQueryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _tags = new ObservableCollection<string>();
            _inheritedTags = new ObservableCollection<string>();
            TagsListBox.ItemsSource = _tags;
            InheritedTagsListBox.ItemsSource = _inheritedTags;
            
            NotePathText.Text = $"Note: {noteTitle}";
            
            // Enable/disable Remove button based on selection
            TagsListBox.SelectionChanged += (s, e) =>
            {
                RemoveTagButton.IsEnabled = TagsListBox.SelectedItem != null;
            };
            
            // Load existing tags
            Loaded += (s, e) => _ = LoadTagsAsync();
        }

        private async Task LoadTagsAsync()
        {
            try
            {
                // Load note tags from projection
                var noteTags = await _tagQueryService.GetTagsForEntityAsync(_noteId, "note");
                
                // Load inherited folder tags
                var folderTags = await LoadInheritedFolderTagsAsync(_noteId);
                
                // Update UI collections on UI thread (required for thread safety)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _tags.Clear();
                    foreach (var tag in noteTags)
                    {
                        _tags.Add(tag.DisplayName);
                    }
                    
                    _inheritedTags.Clear();
                    foreach (var folderTag in folderTags)
                    {
                        _inheritedTags.Add(folderTag.DisplayName);
                    }
                });
                
                _logger.Info($"Loaded {noteTags.Count} own tags and {_inheritedTags.Count} inherited tags for note {_noteId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load note tags", ex);
                
                // MessageBox must also be shown on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Failed to load existing tags: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var newTag = NewTagTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newTag))
            {
                MessageBox.Show(
                    "Please enter a tag name.",
                    "Invalid Tag",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Validate tag format (alphanumeric, hyphens, underscores, spaces, ampersands)
            if (!System.Text.RegularExpressions.Regex.IsMatch(newTag, @"^[\w&\s-]+$"))
            {
                MessageBox.Show(
                    "Tags can only contain letters, numbers, spaces, hyphens, ampersands, and underscores.",
                    "Invalid Tag",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (newTag.Length > 50)
            {
                MessageBox.Show(
                    "Tag cannot exceed 50 characters.",
                    "Invalid Tag",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_tags.Contains(newTag, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "This tag already exists.",
                    "Duplicate Tag",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _tags.Add(newTag);
            NewTagTextBox.Clear();
            NewTagTextBox.Focus();
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (TagsListBox.SelectedItem is string selectedTag)
            {
                _tags.Remove(selectedTag);
            }
        }

        private void NewTagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTag_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_tags.Count == 0)
            {
                var result = MessageBox.Show(
                    "No tags specified. This will remove all tags from the note. Continue?",
                    "Confirm",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                // Disable buttons while saving
                var saveButton = sender as Button;
                if (saveButton != null)
                    saveButton.IsEnabled = false;

                _logger.Info($"Saving {_tags.Count} tags for note {_noteId}");

                var command = new SetNoteTagCommand
                {
                    NoteId = _noteId,
                    Tags = _tags.ToList()
                };

                var result = await _mediator.Send(command);

                if (result.Success)
                {
                    _logger.Info($"Successfully saved note tags");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    _logger.Error($"Failed to save note tags: {result.Error}");
                    MessageBox.Show(
                        $"Failed to save tags: {result.Error}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    
                    if (saveButton != null)
                        saveButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Exception saving note tags", ex);
                MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        /// <summary>
        /// Load inherited folder tags for this note.
        /// Gets tags from note's parent category and all ancestor categories.
        /// </summary>
        private async Task<List<TagDto>> LoadInheritedFolderTagsAsync(Guid noteId)
        {
            try
            {
                // 1. Get note's parent category from tree_view projection
                var noteTreeNode = await _treeQueryService.GetByIdAsync(noteId);
                if (noteTreeNode == null || noteTreeNode.ParentId == null || noteTreeNode.ParentId == Guid.Empty)
                {
                    _logger.Debug($"Note {noteId} has no parent category, no inherited tags");
                    return new List<TagDto>();
                }
                
                var categoryId = noteTreeNode.ParentId.Value;
                
                // 2. Get tags for parent category (includes its own tags)
                var categoryTags = await _tagQueryService.GetTagsForEntityAsync(categoryId, "category");
                
                // 3. Recursively get parent category tags
                var ancestorTags = await GetAncestorCategoryTagsAsync(categoryId);
                
                // 4. Merge with deduplication (Union handles case-insensitive via custom comparer)
                var allInheritedTags = categoryTags
                    .Union(ancestorTags, new TagDtoDisplayNameComparer())
                    .ToList();
                
                _logger.Info($"Loaded {allInheritedTags.Count} inherited folder tags for note {_noteId}");
                return allInheritedTags;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load inherited folder tags for note {noteId}");
                return new List<TagDto>();
            }
        }
        
        /// <summary>
        /// Iteratively get tags from ancestor categories with cycle detection.
        /// Converted from recursion to prevent stack overflow on circular references.
        /// </summary>
        private async Task<List<TagDto>> GetAncestorCategoryTagsAsync(Guid categoryId)
        {
            try
            {
                var allAncestorTags = new List<TagDto>();
                var visitedNodes = new HashSet<Guid>(); // Cycle detection
                const int MAX_DEPTH = 20; // Maximum tree depth to prevent infinite loops
                int depth = 0;
                
                var currentId = categoryId;
                
                // Walk up the tree collecting tags with cycle detection
                while (currentId != Guid.Empty && depth < MAX_DEPTH)
                {
                    // Get current category
                    var categoryNode = await _treeQueryService.GetByIdAsync(currentId);
                    
                    // Handle orphaned nodes (node doesn't exist in database)
                    if (categoryNode == null)
                    {
                        _logger.Warning($"[NoteTagDialog] Orphaned node detected: category {currentId} not found in tree_view while loading tags for note {_noteId}");
                        _logger.Warning($"[NoteTagDialog] This indicates data corruption - the parent_id points to a non-existent node");
                        break;
                    }
                    
                    if (categoryNode.ParentId == null || categoryNode.ParentId == Guid.Empty)
                    {
                        break; // Reached root
                    }
                    
                    var parentId = categoryNode.ParentId.Value;
                    
                    // Check for cycle (circular reference detection)
                    if (visitedNodes.Contains(parentId))
                    {
                        _logger.Warning($"[NoteTagDialog] Circular reference detected in category tree at {parentId} while loading tags for note {_noteId}");
                        _logger.Warning($"[NoteTagDialog] This indicates data corruption - please run database repair");
                        break;
                    }
                    visitedNodes.Add(parentId);
                    
                    // Get parent's tags
                    var parentTags = await _tagQueryService.GetTagsForEntityAsync(parentId, "category");
                    allAncestorTags.AddRange(parentTags);
                    
                    // Move to parent for next iteration
                    currentId = parentId;
                    depth++;
                }
                
                if (depth >= MAX_DEPTH)
                {
                    _logger.Warning($"[NoteTagDialog] Maximum tree depth ({MAX_DEPTH}) reached while loading tags for note {_noteId}");
                    _logger.Warning($"[NoteTagDialog] This may indicate a circular reference or an extremely deep tree");
                }
                
                return allAncestorTags;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get ancestor tags for category {categoryId}");
                return new List<TagDto>();
            }
        }
        
        /// <summary>
        /// Comparer for TagDto that compares by DisplayName (case-insensitive).
        /// Used for Union deduplication.
        /// </summary>
        private class TagDtoDisplayNameComparer : IEqualityComparer<TagDto>
        {
            public bool Equals(TagDto x, TagDto y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return string.Equals(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
            }
            
            public int GetHashCode(TagDto obj)
            {
                return obj?.DisplayName?.ToLowerInvariant().GetHashCode() ?? 0;
            }
        }
    }
}

