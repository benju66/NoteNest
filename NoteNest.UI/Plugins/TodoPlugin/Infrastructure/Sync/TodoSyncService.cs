using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;
using NoteNest.Infrastructure.Database;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Parsing;
using NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Sync
{
    /// <summary>
    /// Background service that synchronizes todos with RTF notes.
    /// Follows SearchIndexSyncService pattern for event-driven synchronization.
    /// 
    /// Architecture:
    /// - ISaveManager fires NoteSaved event when notes are saved
    /// - This service listens and extracts todos from RTF content
    /// - Reconciles with existing todos (add new, mark orphaned, update seen)
    /// - Non-blocking, graceful degradation if sync fails
    /// </summary>
    public class TodoSyncService : IHostedService, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private readonly ITodoRepository _repository;
        private readonly ITodoStore _todoStore;  // NEW: For UI-synced operations
        private readonly IMediator _mediator;
        private readonly BracketTodoParser _parser;
        private readonly NoteNest.Application.Queries.ITreeQueryService _treeQueryService;  // FIXED: Query projections.db instead of obsolete tree.db
        private readonly ICategoryStore _categoryStore;
        private readonly ICategorySyncService _categorySyncService;
        private readonly ITagInheritanceService _tagInheritanceService;
        private readonly IAppLogger _logger;
        private readonly string _notesRootPath;
        private readonly Timer _debounceTimer;
        private string? _pendingNoteId;
        private string? _pendingFilePath;
        private bool _disposed = false;
        
        public TodoSyncService(
            ISaveManager saveManager,
            ITodoRepository repository,
            ITodoStore todoStore,  // NEW: Inject TodoStore for UI updates
            IMediator mediator,
            BracketTodoParser parser,
            NoteNest.Application.Queries.ITreeQueryService treeQueryService,  // Changed to ITreeQueryService (projections.db)
            ICategoryStore categoryStore,
            ICategorySyncService categorySyncService,
            ITagInheritanceService tagInheritanceService,
            IAppLogger logger,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _treeQueryService = treeQueryService ?? throw new ArgumentNullException(nameof(treeQueryService));
            _categoryStore = categoryStore ?? throw new ArgumentNullException(nameof(categoryStore));
            _categorySyncService = categorySyncService ?? throw new ArgumentNullException(nameof(categorySyncService));
            _tagInheritanceService = tagInheritanceService ?? throw new ArgumentNullException(nameof(tagInheritanceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Get notes root path from configuration (same as TreeDatabaseRepository)
            _notesRootPath = configuration?["NotesPath"] 
                ?? @"C:\Users\Burness\MyNotes\Notes";
            
            // Debounce timer to avoid processing every keystroke during auto-save
            _debounceTimer = new Timer(ProcessPendingNote, null, Timeout.Infinite, Timeout.Infinite);
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("[TodoSync] Starting todo sync service - monitoring note saves for bracket todos");
            
            // Subscribe to save events (same pattern as SearchIndexSyncService)
            _saveManager.NoteSaved += OnNoteSaved;
            
            _logger.Info("✅ TodoSyncService subscribed to note save events");
            
            return Task.CompletedTask;
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("[TodoSync] Stopping todo sync service");
            
            // Unsubscribe from events
            if (_saveManager != null)
            {
                _saveManager.NoteSaved -= OnNoteSaved;
            }
            
            // Stop debounce timer
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Event handler for NoteSaved - extracts and syncs todos from saved notes.
        /// Pattern follows SearchIndexSyncService.OnNoteSaved() and DatabaseMetadataUpdateService.OnNoteSaved().
        /// </summary>
        private void OnNoteSaved(object sender, NoteSavedEventArgs e)
        {
            // GUARD: Validate event data
            if (e == null || string.IsNullOrEmpty(e.FilePath))
            {
                _logger.Warning("[TodoSync] NoteSaved event received with invalid data - skipping");
                return;
            }
            
            // GUARD: Only process RTF files
            if (!e.FilePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug($"[TodoSync] Skipping non-RTF file: {Path.GetFileName(e.FilePath)}");
                return;
            }
            
            // Debounce: Wait 500ms after last save before processing
            // This avoids processing the same note multiple times during rapid auto-saves
            _pendingNoteId = e.NoteId;
            _pendingFilePath = e.FilePath;
            _debounceTimer.Change(500, Timeout.Infinite);
            
            _logger.Debug($"[TodoSync] Note save queued for processing: {Path.GetFileName(e.FilePath)}");
        }
        
        /// <summary>
        /// Process the pending note after debounce delay.
        /// </summary>
        private async void ProcessPendingNote(object? state)
        {
            var noteId = _pendingNoteId;
            var filePath = _pendingFilePath;
            
            if (string.IsNullOrEmpty(noteId) || string.IsNullOrEmpty(filePath))
                return;
            
            try
            {
                await ProcessNoteAsync(noteId, filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoSync] Failed to process note: {Path.GetFileName(filePath)}");
                // Don't rethrow - graceful degradation (app continues working)
            }
        }
        
        /// <summary>
        /// Process a note file: extract todos and reconcile with database.
        /// ROBUST: Uses path-based lookup following DatabaseMetadataUpdateService pattern.
        /// </summary>
        private async Task ProcessNoteAsync(string noteId, string filePath)
        {
            _logger.Info($"[TodoSync] Processing note: {Path.GetFileName(filePath)}");
            
            // STEP 1: Read RTF file
            if (!File.Exists(filePath))
            {
                _logger.Warning($"[TodoSync] File not found: {filePath}");
                // Note: HandleMissingFile uses legacy noteId - skip for now
                return;
            }
            
            string rtfContent;
            try
            {
                rtfContent = await File.ReadAllTextAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoSync] Failed to read file: {filePath}");
                return;
            }
            
            // STEP 2: Parse todos from RTF
            var candidates = _parser.ExtractFromRtf(rtfContent);
            _logger.Debug($"[TodoSync] Found {candidates.Count} todo candidates in {Path.GetFileName(filePath)}");
            
            if (candidates.Count == 0)
            {
                _logger.Debug($"[TodoSync] No todos found in note - nothing to process");
                return;
            }
            
            // STEP 3: Get note from projections.db by path (FIXED - uses event-sourced tree_view)
            var canonicalPath = filePath.ToLowerInvariant();
            var noteNode = await _treeQueryService.GetByPathAsync(canonicalPath);
            
            Guid? categoryId = null;
            
            if (noteNode == null)
            {
                _logger.Debug($"[TodoSync] Note not in tree DB yet: {Path.GetFileName(filePath)} - trying parent folder");
                
                // ✨ FIX: Try parent folder - folders are usually in tree.db before notes
                var parentFolderPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(parentFolderPath))
                {
                    // Convert to canonical format: relative path with forward slashes, lowercase
                    // tree.db stores paths like: "projects/25-111 - test project" (relative to Notes root)
                    var relativePath = Path.GetRelativePath(_notesRootPath, parentFolderPath);
                    var parentCanonical = relativePath.Replace('\\', '/').ToLowerInvariant();
                    
                    _logger.Info($"[TodoSync] Looking up parent folder in tree_view: '{parentCanonical}'");
                    
                    var parentNode = await _treeQueryService.GetByPathAsync(parentCanonical);
                    
                    if (parentNode != null && parentNode.NodeType == TreeNodeType.Category)
                    {
                        categoryId = parentNode.Id;
                        _logger.Info($"[TodoSync] ✅ Using parent folder as category: {parentNode.Name} ({categoryId})");
                        
                        // Auto-add category to todo panel if not already there
                        await EnsureCategoryAddedAsync(categoryId.Value);
                        
                        await ReconcileTodosAsync(Guid.Empty, filePath, candidates, categoryId);
                        return;
                    }
                    else
                    {
                        _logger.Debug($"[TodoSync] Parent folder also not in tree DB yet");
                    }
                }
                
                // Neither note nor parent folder found - create uncategorized
                _logger.Info($"[TodoSync] Creating {candidates.Count} uncategorized todos (will auto-categorize when folder is scanned)");
                await ReconcileTodosAsync(Guid.Empty, filePath, candidates, categoryId: null);
                return;
            }
            
            // STEP 4: Validate node type
            if (noteNode.NodeType != TreeNodeType.Note)
            {
                _logger.Warning($"[TodoSync] Path resolves to {noteNode.NodeType}, not a note: {filePath}");
                return;
            }
            
            // STEP 5: Auto-categorize (category = note's parent folder)
            categoryId = noteNode.ParentId;  // Can be null for root-level notes
            
            if (categoryId.HasValue)
            {
                _logger.Debug($"[TodoSync] Note is in category: {categoryId.Value} - todos will be auto-categorized");
                
                // Auto-add category to todo tree if user hasn't added it yet
                await EnsureCategoryAddedAsync(categoryId.Value);
            }
            else
            {
                _logger.Debug($"[TodoSync] Note is at root level - todos will be uncategorized");
            }
            
            // STEP 6: Reconcile todos with database
            await ReconcileTodosAsync(noteNode.Id, filePath, candidates, categoryId);
        }
        
        /// <summary>
        /// Reconcile extracted todos with existing todos in database.
        /// AUTO-CATEGORIZES: Assigns todos to category based on note's parent folder.
        /// ROBUST: Handles missing categories gracefully (creates uncategorized todos).
        /// </summary>
        private async Task ReconcileTodosAsync(Guid noteGuid, string filePath, List<TodoCandidate> candidates, Guid? categoryId)
        {
            try
            {
                // Category ID already determined in ProcessNoteAsync() - use it directly
                if (categoryId.HasValue)
                {
                    _logger.Debug($"[TodoSync] Auto-categorizing {candidates.Count} todos under category: {categoryId.Value}");
                }
                else
                {
                    _logger.Debug($"[TodoSync] Creating {candidates.Count} uncategorized todos");
                }
                
                // STEP 1: Get existing todos for this note
                var existingTodos = await _repository.GetByNoteIdAsync(noteGuid);
                
                _logger.Debug($"[TodoSync] Reconciling {candidates.Count} candidates with {existingTodos.Count} existing todos");
                
                // Build lookup dictionaries for efficient matching
                var candidatesByStableId = candidates.ToDictionary(c => c.GetStableId());
                var existingByStableId = existingTodos.ToDictionary(t => GetTodoStableId(t));
                
                // PHASE 1: Find new todos (in candidates but not in existing)
                var newCandidates = candidatesByStableId.Keys
                    .Except(existingByStableId.Keys)
                    .Select(id => candidatesByStableId[id])
                    .ToList();
                
                foreach (var candidate in newCandidates)
                {
                    await CreateTodoFromCandidate(candidate, noteGuid, filePath, categoryId);
                }
                
                // PHASE 2: Find orphaned todos (in existing but not in candidates)
                var orphanedIds = existingByStableId.Keys
                    .Except(candidatesByStableId.Keys)
                    .Select(id => existingByStableId[id].Id)
                    .ToList();
                
                foreach (var todoId in orphanedIds)
                {
                    await MarkTodoAsOrphaned(todoId);
                }
                
                // PHASE 3: Update last_seen for existing todos still in note
                var stillPresentIds = existingByStableId.Keys
                    .Intersect(candidatesByStableId.Keys)
                    .Select(id => existingByStableId[id].Id)
                    .ToList();
                
                foreach (var todoId in stillPresentIds)
                {
                    await _repository.UpdateLastSeenAsync(todoId);
                }
                
                _logger.Info($"[TodoSync] Reconciliation complete: {newCandidates.Count} new, {orphanedIds.Count} orphaned, {stillPresentIds.Count} updated");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoSync] Failed to reconcile todos");
            }
        }
        
        /// <summary>
        /// Create a new todo from a candidate.
        /// NEW: Includes auto-categorization based on note's parent category.
        /// USES TodoStore: Ensures UI updates immediately (ObservableCollection auto-refresh).
        /// </summary>
        private async Task CreateTodoFromCandidate(TodoCandidate candidate, Guid noteGuid, string filePath, Guid? categoryId)
        {
            try
            {
                // ✨ CQRS: Use CreateTodoCommand (RTF sync uses same command as manual creation!)
                var command = new Application.Commands.CreateTodo.CreateTodoCommand
                {
                    Text = candidate.Text,
                    CategoryId = categoryId,  // AUTO-CATEGORIZE! Links to note's parent category
                    SourceNoteId = noteGuid,
                    SourceFilePath = filePath,
                    SourceLineNumber = candidate.LineNumber,
                    SourceCharOffset = candidate.CharacterOffset
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoSync] CreateTodoCommand failed: {result.Error}");
                    return;
                }
                
                if (categoryId.HasValue)
                {
                    _logger.Info($"[TodoSync] ✅ Created todo from note via command: \"{candidate.Text}\" [auto-categorized: {categoryId.Value}]");
                }
                else
                {
                    _logger.Info($"[TodoSync] ✅ Created todo from note: \"{candidate.Text}\" [uncategorized] - UI will auto-refresh");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoSync] Failed to create todo: {candidate.Text}");
            }
        }
        
        /// <summary>
        /// Mark a todo as orphaned (source bracket was removed from note).
        /// USES TodoStore: Ensures UI updates when todo state changes.
        /// </summary>
        private async Task MarkTodoAsOrphaned(Guid todoId)
        {
            try
            {
                var todo = _todoStore.GetById(todoId);
                if (todo != null && !todo.IsOrphaned)
                {
                    // ✨ CQRS: Use MarkOrphanedCommand
                    var command = new Application.Commands.MarkOrphaned.MarkOrphanedCommand
                    {
                        TodoId = todoId,
                        IsOrphaned = true
                    };
                    
                    var result = await _mediator.Send(command);
                    
                    if (result.IsFailure)
                    {
                        _logger.Error($"[TodoSync] MarkOrphanedCommand failed: {result.Error}");
                        return;
                    }
                    
                    _logger.Info($"[TodoSync] ✅ Marked todo as orphaned via command: \"{todo.Text}\"");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoSync] Failed to mark todo as orphaned: {todoId}");
            }
        }
        
        /// <summary>
        /// Handle missing file (deleted or moved) - mark all associated todos as orphaned.
        /// </summary>
        private async Task HandleMissingFile(string noteId)
        {
            try
            {
                if (Guid.TryParse(noteId, out var noteGuid))
                {
                    var count = await _repository.MarkOrphanedByNoteAsync(noteGuid);
                    _logger.Info($"[TodoSync] Marked {count} todos as orphaned for missing file");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[TodoSync] Failed to handle missing file");
            }
        }
        
        /// <summary>
        /// Generate stable ID for a todo (matches TodoCandidate.GetStableId logic).
        /// </summary>
        private string GetTodoStableId(TodoItem todo)
        {
            var keyText = todo.Text.Length > 50 ? todo.Text.Substring(0, 50) : todo.Text;
            var lineNumber = todo.SourceLineNumber ?? 0;
            return $"{lineNumber}:{keyText.GetHashCode():X8}";
        }
        
        /// <summary>
        /// Ensure category is added to CategoryStore for RTF-extracted todos.
        /// This automatically makes the category visible in TodoPlugin when todos are extracted.
        /// Builds display path with full breadcrumb for rich context.
        /// </summary>
        private async Task EnsureCategoryAddedAsync(Guid categoryId)
        {
            try
            {
                // Check if category already in store
                var existing = _categoryStore.GetById(categoryId);
                if (existing != null)
                {
                    _logger.Debug($"[TodoSync] Category already in store: {categoryId}");
                    return;
                }
                
                // Get category from tree
                var category = await _categorySyncService.GetCategoryByIdAsync(categoryId);
                if (category == null)
                {
                    _logger.Warning($"[TodoSync] Category not found in tree: {categoryId}");
                    return;
                }
                
                // Build display path by walking up the tree
                var displayPath = await BuildCategoryDisplayPathAsync(categoryId);
                category.DisplayPath = displayPath;
                
                // Set for flat display mode (immediate visibility)
                category.ParentId = null;
                // OriginalParentId already set by CategorySyncService
                
                // Auto-add to CategoryStore (properly awaited to prevent race conditions)
                await _categoryStore.AddAsync(category);
                _logger.Info($"[TodoSync] ✅ Auto-added category to todo panel: {displayPath} (for RTF todos)");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[TodoSync] Failed to auto-add category: {categoryId}");
                // Don't throw - this is a nice-to-have feature, not critical
            }
        }
        
        /// <summary>
        /// Builds display path for a category by walking up the tree.
        /// Example: "Work > Projects > ProjectAlpha"
        /// </summary>
        private async Task<string> BuildCategoryDisplayPathAsync(Guid categoryId)
        {
            try
            {
                var parts = new List<string>();
                var current = categoryId;
                int depth = 0;
                const int maxDepth = 10; // Prevent infinite loops
                
                while (current != Guid.Empty && depth < maxDepth)
                {
                    var category = await _categorySyncService.GetCategoryByIdAsync(current);
                    if (category == null) break;
                    
                    parts.Insert(0, category.Name);
                    depth++;
                    
                    // Move to parent
                    current = category.ParentId ?? Guid.Empty;
                }
                
                // Remove "Notes" if it's the workspace root
                var relevantParts = parts.Where(p => !p.Equals("Notes", StringComparison.OrdinalIgnoreCase)).ToList();
                
                return relevantParts.Count > 0 
                    ? string.Join(" > ", relevantParts) 
                    : "Uncategorized";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to build display path for category: {categoryId}");
                return "Category"; // Fallback
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _debounceTimer?.Dispose();
            _disposed = true;
        }
    }
}


