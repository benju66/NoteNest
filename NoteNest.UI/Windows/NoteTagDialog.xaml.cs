using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.NoteTags.Commands.SetNoteTag;
using NoteNest.Application.NoteTags.Repositories;
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

        public NoteTagDialog(
            Guid noteId, 
            string noteTitle,
            IMediator mediator,
            INoteTagRepository noteTagRepository,
            IAppLogger logger)
        {
            InitializeComponent();
            
            _noteId = noteId;
            _noteTitle = noteTitle;
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _noteTagRepository = noteTagRepository ?? throw new ArgumentNullException(nameof(noteTagRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _tags = new ObservableCollection<string>();
            TagsListBox.ItemsSource = _tags;
            
            NotePathText.Text = $"Note: {noteTitle}";
            
            // Enable/disable Remove button based on selection
            TagsListBox.SelectionChanged += (s, e) =>
            {
                RemoveTagButton.IsEnabled = TagsListBox.SelectedItem != null;
            };
            
            // Load existing tags
            Loaded += async (s, e) => await LoadTagsAsync();
        }

        private async Task LoadTagsAsync()
        {
            try
            {
                var noteTags = await _noteTagRepository.GetNoteTagsAsync(_noteId);
                _tags.Clear();
                foreach (var tag in noteTags)
                {
                    _tags.Add(tag.Tag);
                }
                
                _logger.Info($"Loaded {noteTags.Count} tags for note {_noteId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load note tags", ex);
                MessageBox.Show(
                    $"Failed to load existing tags: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

