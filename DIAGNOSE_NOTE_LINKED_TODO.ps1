# Diagnostic Script for Note-Linked Todo Issue
# This script collects logs and database state for analysis

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NOTE-LINKED TODO DIAGNOSTIC SCRIPT" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$outputFile = "note_linked_todo_diagnosis.txt"
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$treeDbPath = Join-Path $localAppData "NoteNest\tree.db"
$todosDbPath = Join-Path $localAppData "NoteNest\.plugins\NoteNest.TodoPlugin\todos.db"

# Start output file
"NOTE-LINKED TODO DIAGNOSTIC REPORT" | Out-File $outputFile
"Generated: $(Get-Date)" | Out-File $outputFile -Append
"" | Out-File $outputFile -Append

Write-Host "Checking database files..." -ForegroundColor Yellow

# Check if databases exist
"=== DATABASE FILE CHECK ===" | Out-File $outputFile -Append
"Tree DB: $treeDbPath" | Out-File $outputFile -Append
"Tree DB Exists: $(Test-Path $treeDbPath)" | Out-File $outputFile -Append
"Tree DB Size: $((Get-Item $treeDbPath -ErrorAction SilentlyContinue).Length) bytes" | Out-File $outputFile -Append
"" | Out-File $outputFile -Append
"Todos DB: $todosDbPath" | Out-File $outputFile -Append
"Todos DB Exists: $(Test-Path $todosDbPath)" | Out-File $outputFile -Append
"Todos DB Size: $((Get-Item $todosDbPath -ErrorAction SilentlyContinue).Length) bytes" | Out-File $outputFile -Append
"" | Out-File $outputFile -Append

Write-Host "Querying tree database..." -ForegroundColor Yellow

# Query tree database
if (Test-Path $treeDbPath) {
    "=== TREE DATABASE QUERIES ===" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # Schema version
    "--- Schema Version ---" | Out-File $outputFile -Append
    sqlite3 $treeDbPath "SELECT * FROM schema_version ORDER BY version;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # All tree nodes (recent)
    "--- Recent Tree Nodes (Last 10) ---" | Out-File $outputFile -Append
    sqlite3 $treeDbPath "SELECT id, name, node_type, canonical_path, parent_id FROM tree_nodes WHERE is_deleted = 0 ORDER BY modified_at DESC LIMIT 10;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # Folder tags
    "--- Folder Tags ---" | Out-File $outputFile -Append
    sqlite3 $treeDbPath "SELECT ft.folder_id, tn.name as folder_name, ft.tag, ft.is_auto_suggested, ft.inherit_to_children FROM folder_tags ft LEFT JOIN tree_nodes tn ON ft.folder_id = tn.id;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # Notes in categories
    "--- Notes in Categories ---" | Out-File $outputFile -Append
    sqlite3 $treeDbPath "SELECT n.id as note_id, n.name as note_name, c.id as category_id, c.name as category_name FROM tree_nodes n LEFT JOIN tree_nodes c ON n.parent_id = c.id WHERE n.node_type = 'note' AND n.is_deleted = 0 ORDER BY n.modified_at DESC LIMIT 10;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
}

Write-Host "Querying todos database..." -ForegroundColor Yellow

# Query todos database
if (Test-Path $todosDbPath) {
    "=== TODOS DATABASE QUERIES ===" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # All todos with category info
    "--- All Todos (Last 20) ---" | Out-File $outputFile -Append
    sqlite3 $todosDbPath "SELECT id, text, category_id, source_type, source_note_id, is_orphaned, created_at FROM todos ORDER BY created_at DESC LIMIT 20;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # Note-linked todos specifically
    "--- Note-Linked Todos ---" | Out-File $outputFile -Append
    sqlite3 $todosDbPath "SELECT id, text, category_id, source_note_id, source_file_path, is_orphaned FROM todos WHERE source_type = 'note' ORDER BY created_at DESC LIMIT 10;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # Todo tags
    "--- Todo Tags ---" | Out-File $outputFile -Append
    sqlite3 $todosDbPath "SELECT tt.todo_id, t.text as todo_text, tt.tag, tt.is_auto FROM todo_tags tt INNER JOIN todos t ON tt.todo_id = t.id ORDER BY t.created_at DESC LIMIT 20;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
    
    # Categories in plugin database
    "--- Saved Categories (Plugin) ---" | Out-File $outputFile -Append
    sqlite3 $todosDbPath "SELECT id, name, display_path, parent_id, original_parent_id FROM categories;" | Out-File $outputFile -Append
    "" | Out-File $outputFile -Append
}

Write-Host ""
Write-Host "=== PLEASE FOLLOW THESE STEPS ===" -ForegroundColor Green
Write-Host ""
Write-Host "1. CREATE A NOTE-LINKED TODO:" -ForegroundColor Yellow
Write-Host "   - Open a note in a folder (e.g., 'Projects/25-117 - OP III/Meeting.rtf')"
Write-Host "   - Type: [TODO: Test item]"
Write-Host "   - Save the note (Ctrl+S)"
Write-Host ""
Write-Host "2. CHECK IF TODO APPEARS:" -ForegroundColor Yellow
Write-Host "   - Open Todo Panel"
Write-Host "   - Look for the category"
Write-Host "   - Does the todo appear? (YES/NO)"
Write-Host ""
Write-Host "3. AFTER STEP 2, RUN THIS SCRIPT AGAIN" -ForegroundColor Yellow
Write-Host "   - This will capture the updated database state"
Write-Host ""
Write-Host "4. COLLECT APPLICATION LOGS:" -ForegroundColor Yellow
Write-Host "   - Check console output or log files"
Write-Host "   - Look for [TodoSync], [CreateTodoHandler], [TodoStore], [CategoryTree]"
Write-Host ""

Write-Host "Diagnostic data saved to: $outputFile" -ForegroundColor Green
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

