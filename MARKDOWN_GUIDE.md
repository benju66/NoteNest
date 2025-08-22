# Markdown Support in NoteNest

## Supported Markdown Features

- **Bold**: `**text**` or `__text__`
- *Italic*: `*text*` or `_text_`
- ~~Strikethrough~~: `~~text~~`
- [Links](url): `[text](url)`
- Task Lists: `- [ ] Task` / `- [x] Done`
- Headers: `# H1`, `## H2`, ...
- Tables: Pipe syntax
- Code: `` `inline` `` or fenced blocks ```
- Footnotes: `[^1]`

HTML is disabled for safety. Potentially dangerous content is sanitized.

## Default Format and Detection

- Default format: Markdown (.md). You can change this in Settings > Features.
- Auto-detect format from extension and, optionally, content.
- Both `.md` and `.txt` are fully supported. Search indexing strips markdown.

## Spell Check

- Spell check (English) is enabled by default. Toggle in Settings > Features.

## Tips

- Use the editor toolbar or shortcuts to insert lists and tasks.
- Rename preserves the current extension.
- Format conversion is available under Tools > "Convert Notes Format…".

## Migration

- Tools > Convert Notes Format…
  - Choose target format (.md or .txt)
  - Optionally create `.bak` backups
  - Converts content and updates file extensions

## Safety

- HTML/script is disabled when processing markdown.
- Backups are available during conversion if enabled.
