using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediatR;
using NoteNest.UI.Plugins.TodoPlugin.Application.Commands.AddTag;
using NoteNest.UI.Plugins.TodoPlugin.Application.Commands.RemoveTag;
using NoteNest.Application.Queries;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Dialog for managing todo tags (both auto and manual).
    /// Event-sourced version - uses ITagQueryService.
    /// </summary>
    public partial class TodoTagDialog : Window
    {
        private readonly Guid _todoId;
        private readonly string _todoText;
        private readonly IMediator _mediator;
        private readonly ITagQueryService _tagQueryService;
        private readonly IAppLogger _logger;
        private readonly ObservableCollection<string> _autoTags;
        private readonly ObservableCollection<string> _manualTags;

        public TodoTagDialog(
            Guid todoId, 
            string todoText,
            IMediator mediator,
            ITagQueryService tagQueryService,
            IAppLogger logger)
        {
            InitializeComponent();
            
            _todoId = todoId;
            _todoText = todoText;
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _tagQueryService = tagQueryService ?? throw new ArgumentNullException(nameof(tagQueryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _autoTags = new ObservableCollection<string>();
            _manualTags = new ObservableCollection<string>();
            AutoTagsListBox.ItemsSource = _autoTags;
            ManualTagsListBox.ItemsSource = _manualTags;
            
            TodoTextBlock.Text = $"Todo: {todoText}";
            
            // Enable/disable Remove button based on selection
            ManualTagsListBox.SelectionChanged += (s, e) =>
            {
                RemoveTagButton.IsEnabled = ManualTagsListBox.SelectedItem != null;
            };
            
            // Load existing tags
            Loaded += (s, e) => _ = LoadTagsAsync();
        }

        private async Task LoadTagsAsync()
        {
            try
            {
                // Load todo tags from projection
                var allTags = await _tagQueryService.GetTagsForEntityAsync(_todoId, "todo");
                
                // Update UI collections on UI thread (required for thread safety)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _autoTags.Clear();
                    _manualTags.Clear();
                    
                    foreach (var tag in allTags)
                    {
                        // Auto tags have source != "manual"
                        if (tag.Source != "manual")
                        {
                            _autoTags.Add(tag.DisplayName);
                        }
                        else
                        {
                            _manualTags.Add(tag.DisplayName);
                        }
                    }
                });
                
                _logger.Info($"Loaded {_autoTags.Count} auto tags and {_manualTags.Count} manual tags for todo {_todoId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load todo tags", ex);
                
                // MessageBox must also be shown on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Failed to load tags: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        private async void AddTag_Click(object sender, RoutedEventArgs e)
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

            // Validate tag format
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

            if (_manualTags.Contains(newTag, StringComparer.OrdinalIgnoreCase) || 
                _autoTags.Contains(newTag, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "This tag already exists.",
                    "Duplicate Tag",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                // Add tag via command
                var command = new AddTagCommand
                {
                    TodoId = _todoId,
                    TagName = newTag
                };

                var result = await _mediator.Send(command);

                if (result.IsSuccess)
                {
                    _manualTags.Add(newTag);
                    NewTagTextBox.Clear();
                    NewTagTextBox.Focus();
                    _logger.Info($"Added tag '{newTag}' to todo");
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to add tag: {result.Error}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Exception adding tag", ex);
                MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (ManualTagsListBox.SelectedItem is string selectedTag)
            {
                try
                {
                    // Remove tag via command
                    var command = new RemoveTagCommand
                    {
                        TodoId = _todoId,
                        TagName = selectedTag
                    };

                    var result = await _mediator.Send(command);

                    if (result.IsSuccess)
                    {
                        _manualTags.Remove(selectedTag);
                        _logger.Info($"Removed tag '{selectedTag}' from todo");
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Failed to remove tag: {result.Error}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Exception removing tag", ex);
                    MessageBox.Show(
                        $"An error occurred: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

