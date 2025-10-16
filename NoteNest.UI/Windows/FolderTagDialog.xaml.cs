using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.FolderTags.Commands.SetFolderTag;
using NoteNest.Application.FolderTags.Repositories;
using NoteNest.Application.Tags.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Dialog for managing folder tags.
    /// </summary>
    public partial class FolderTagDialog : Window
    {
        private readonly Guid _folderId;
        private readonly string _folderPath;
    private readonly IMediator _mediator;
    private readonly IFolderTagRepository _folderTagRepository;
    private readonly IUnifiedTagViewService _unifiedTagViewService;
    private readonly IAppLogger _logger;
        private readonly ObservableCollection<string> _tags;
        private readonly ObservableCollection<string> _inheritedTags;

        public FolderTagDialog(
            Guid folderId, 
            string folderPath,
            IMediator mediator,
            IFolderTagRepository folderTagRepository,
            IUnifiedTagViewService unifiedTagViewService,
            IAppLogger logger)
        {
            InitializeComponent();
            
            _folderId = folderId;
            _folderPath = folderPath;
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _folderTagRepository = folderTagRepository ?? throw new ArgumentNullException(nameof(folderTagRepository));
            _unifiedTagViewService = unifiedTagViewService ?? throw new ArgumentNullException(nameof(unifiedTagViewService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _tags = new ObservableCollection<string>();
            _inheritedTags = new ObservableCollection<string>();
            TagsListBox.ItemsSource = _tags;
            InheritedTagsListBox.ItemsSource = _inheritedTags;
            
            FolderPathText.Text = $"Folder: {folderPath}";
            
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
                var folderTags = await _folderTagRepository.GetFolderTagsAsync(_folderId);
                var allInheritedTags = await _folderTagRepository.GetInheritedTagsAsync(_folderId);
                
                // Update UI collections on UI thread (required for thread safety)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _tags.Clear();
                    foreach (var tag in folderTags)
                    {
                        _tags.Add(tag.Tag);
                    }
                    
                    _inheritedTags.Clear();
                    
                    // Filter to only show tags from ancestors (not this folder's own tags)
                    var ownTagSet = new HashSet<string>(folderTags.Select(t => t.Tag), StringComparer.OrdinalIgnoreCase);
                    foreach (var inheritedTag in allInheritedTags.Where(t => !ownTagSet.Contains(t.Tag)))
                    {
                        _inheritedTags.Add(inheritedTag.Tag);
                    }
                });
                
                _logger.Info($"Loaded {folderTags.Count} own tags and {_inheritedTags.Count} inherited tags for folder {_folderId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load folder tags", ex);
                
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
                    "No tags specified. This will remove all tags from the folder. Continue?",
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

                _logger.Info($"Saving {_tags.Count} tags for folder {_folderId}");

                var command = new SetFolderTagCommand
                {
                    FolderId = _folderId,
                    Tags = _tags.ToList(),
                    InheritToChildren = InheritToChildrenCheckBox.IsChecked ?? true,
                    IsAutoSuggested = false
                };

                var result = await _mediator.Send(command);

                if (result.Success)
                {
                    _logger.Info($"Successfully saved folder tags. New items will inherit these tags.");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    _logger.Error($"Failed to save folder tags: {result.Error}");
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
                _logger.Error($"Exception saving folder tags", ex);
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

