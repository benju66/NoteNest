using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.FolderTags.Repositories;

namespace NoteNest.UI.Plugins.TodoPlugin.Services;

/// <summary>
/// Service for detecting patterns and suggesting folder tags.
/// </summary>
public interface IFolderTagSuggestionService
{
    /// <summary>
    /// Detect if a folder name matches a known pattern (e.g., "25-117 - OP III").
    /// </summary>
    bool DetectPattern(string folderName);
    
    /// <summary>
    /// Suggest tags for a folder based on its name.
    /// Returns empty list if no pattern detected.
    /// </summary>
    List<string> SuggestTags(string folderName);
    
    /// <summary>
    /// Check if we should show a suggestion for a folder.
    /// (e.g., don't show if user already dismissed it, or if folder already has tags)
    /// </summary>
    Task<bool> ShouldShowSuggestionAsync(Guid folderId);
    
    /// <summary>
    /// Mark that user dismissed a suggestion for a folder.
    /// </summary>
    Task DismissSuggestionAsync(Guid folderId);
}

/// <summary>
/// Detects patterns in folder names and suggests tags.
/// Uses the same regex pattern as the original TagGeneratorService.
/// </summary>
public class FolderTagSuggestionService : IFolderTagSuggestionService
{
    private readonly IFolderTagRepository _folderTagRepository;
    private readonly IAppLogger _logger;
    
    // Same pattern as TagGeneratorService: matches "25-117 - OP III" style folders
    private static readonly Regex ProjectPattern = new(
        @"^(\d{2}-\d{3})\s*-\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Track dismissed suggestions in memory (could be persisted to DB if needed)
    private readonly HashSet<Guid> _dismissedSuggestions = new();

    public FolderTagSuggestionService(
        IFolderTagRepository folderTagRepository,
        IAppLogger logger)
    {
        _folderTagRepository = folderTagRepository ?? throw new ArgumentNullException(nameof(folderTagRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool DetectPattern(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return false;

        return ProjectPattern.IsMatch(folderName);
    }

    public List<string> SuggestTags(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return new List<string>();

        var match = ProjectPattern.Match(folderName);
        if (!match.Success)
            return new List<string>();

        try
        {
            var projectCode = match.Groups[1].Value.Trim(); // "25-117"
            var projectName = match.Groups[2].Value.Trim(); // "OP III"

            // Generate 2 tags: project code + project name, and project code only
            var tag1 = $"{projectCode}-{projectName.Replace(" ", "-")}"; // "25-117-OP-III"
            var tag2 = projectCode; // "25-117"

            _logger.Info($"Suggested tags for '{folderName}': [{tag1}, {tag2}]");
            return new List<string> { tag1, tag2 };
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to generate tags for folder '{folderName}'", ex);
            return new List<string>();
        }
    }

    public async Task<bool> ShouldShowSuggestionAsync(Guid folderId)
    {
        try
        {
            // Don't show if user already dismissed
            if (_dismissedSuggestions.Contains(folderId))
                return false;

            // Don't show if folder already has tags
            var hasTags = await _folderTagRepository.HasTagsAsync(folderId);
            if (hasTags)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to check if suggestion should be shown for {folderId}", ex);
            return false;
        }
    }

    public Task DismissSuggestionAsync(Guid folderId)
    {
        _dismissedSuggestions.Add(folderId);
        _logger.Info($"Dismissed suggestion for folder {folderId}");
        return Task.CompletedTask;
    }
}

