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
using NoteNest.Application.NoteTags.Repositories;
using NoteNest.Application.Tags.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Dialog for managing note tags.
    /// </summary>
    public partial class NoteTagDialog : Window
    {
        private readonly Guid _noteId;
        private readonly string _noteTitle;
        private readonly IMediator _mediator;
        private readonly INoteTagRepository _noteTagRepository;
        private readonly IAppLogger _logger;
        private readonly ObservableCollection<string> _tags;
        private readonly ObservableCollection<string> _inheritedTags;
        private readonly NoteNest.Application.FolderTags.Repositories.IFolderTagRepository _folderTagRepository;
        private readonly NoteNest.Infrastructure.Database.ITreeDatabaseRepository _treeRepository;
        private readonly IUnifiedTagViewService _unifiedTagViewService;

        public NoteTagDialog(
            Guid noteId, 
            string noteTitle,
            IMediator mediator,
            INoteTagRepository noteTagRepository,
            NoteNest.Application.FolderTags.Repositories.IFolderTagRepository folderTagRepository,
            NoteNest.Infrastructure.Database.ITreeDatabaseRepository treeRepository,
            IUnifiedTagViewService unifiedTagViewService,
            IAppLogger logger)
        {
            InitializeComponent();
            
            _noteId = noteId;
            _noteTitle = noteTitle;
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _noteTagRepository = noteTagRepository ?? throw new ArgumentNullException(nameof(noteTagRepository));
            _folderTagRepository = folderTagRepository ?? throw new ArgumentNullException(nameof(folderTagRepository));
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
            _unifiedTagViewService = unifiedTagViewService ?? throw new ArgumentNullException(nameof(unifiedTagViewService));
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
                // Load data on background thread (this is fine)
                var noteTags = await _noteTagRepository.GetNoteTagsAsync(_noteId);
                var noteNode = await _treeRepository.GetNodeByIdAsync(_noteId);
                
                List<NoteNest.Application.FolderTags.Models.FolderTag> folderTags = new List<NoteNest.Application.FolderTags.Models.FolderTag>();
                if (noteNode?.ParentId != null)
                {
                    folderTags = await _folderTagRepository.GetInheritedTagsAsync(noteNode.ParentId.Value);
                }
                
                // Update UI collections on UI thread (required for thread safety)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _tags.Clear();
                    foreach (var tag in noteTags)
                    {
                        _tags.Add(tag.Tag);
                    }
                    
                    _inheritedTags.Clear();
                    if (folderTags.Count > 0)
                    {
                        foreach (var folderTag in folderTags)
                        {
                            _inheritedTags.Add(folderTag.Tag);
                        }
                        _logger.Info($"Loaded {_inheritedTags.Count} inherited folder tags for note {_noteId}");
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
    }
}

