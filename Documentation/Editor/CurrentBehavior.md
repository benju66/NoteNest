# FormattedTextEditor Current Behavior

## Supported Markdown Features

### Text Formatting
- **Headers** (H1-H6) - `#`, `##`, `###`, etc.
- **Bold emphasis** - `**text**` or `__text__`
- **Italic emphasis** - `*text*` or `_text_`
- **Combined formatting** - Bold + Italic combinations

### Lists
- **Bullet lists** - `-`, `*`, `+` markers
- **Numbered lists** - `1.`, `2.`, etc.
- **Task lists** - `- [ ]` unchecked, `- [x]` checked
- **Nested lists** - Multi-level indentation support
- **Smart list continuation** - Auto-numbering and formatting

### Advanced Features
- **Hyperlinks** - `[text](url)` format
- **Code blocks** - Fenced code blocks with syntax highlighting
- **Line breaks** - Proper paragraph and line break handling
- **Real-time conversion** - Bidirectional markdown ↔ rich text

## List Behavior Features

### Smart List Handling
- **Auto-continuation** - Enter creates new list item
- **Auto-exit** - Double-enter exits list mode
- **Smart indentation** - Tab/Shift+Tab for nesting
- **Backspace handling** - Smart list item removal
- **Renumbering** - Automatic numbered list updates

### Keyboard Shortcuts
- **Ctrl+Shift+B** - Toggle bullet list
- **Ctrl+Shift+N** - Toggle numbered list  
- **Tab** - Indent list item
- **Shift+Tab** - Outdent list item
- **Ctrl+B** - Bold
- **Ctrl+I** - Italic

## Toolbar Features
- **Formatting buttons** - Bold, Italic
- **List buttons** - Bullets, Numbers, Tasks
- **Indentation controls** - Indent, Outdent
- **Split view controls** - Vertical splitting

## Performance Characteristics

### Conversion Performance
- **Real-time updates** - Updates as you type
- **Debounced conversion** - Optimized for typing performance
- **Memory efficient** - Reuses FlowDocument instances
- **Responsive UI** - Non-blocking operations

### Supported Document Sizes
- **Recommended** - Up to 10MB documents
- **Performance threshold** - Good performance for typical notes
- **Memory usage** - Efficient document handling

## Known Issues

### Current Limitations
- **Task list implementation** - Basic checkbox support
- **Code syntax highlighting** - Limited language support
- **Image handling** - Not yet implemented
- **Table support** - Basic table rendering

### Performance Considerations
- **Large documents** - May slow down with very large files
- **Real-time conversion** - Can impact typing in large documents
- **Memory usage** - FlowDocument objects use significant memory

## Integration Points

### Save System Integration
- **UnifiedSaveManager** - Integrated with bulletproof save system
- **Write-Ahead Logging** - All changes logged for recovery
- **Auto-save support** - Configurable auto-save intervals

### Settings Integration
- **EditorSettings** - Centralized configuration
- **Font settings** - Configurable font family and size
- **Behavior settings** - Customizable list handling

### Plugin System
- **Toolbar extensibility** - Can be extended via plugins
- **Command system** - Routed commands for integration
- **Event system** - Editor events available to plugins
