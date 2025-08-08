// This file is only compiled for the Windows/WPF target per csproj conditions
#if WINDOWS || NET9_0_WINDOWS || NET8_0_WINDOWS || NET7_0_WINDOWS
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    public partial class NoteService
    {
        public async Task SaveNoteWithDocumentAsync(NoteModel note, FlowDocument document)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            if (document == null) throw new ArgumentNullException(nameof(document));

            var directory = Path.GetDirectoryName(note.FilePath);
            if (!await _fileSystem.ExistsAsync(directory))
            {
                await _fileSystem.CreateDirectoryAsync(directory);
            }

            var xamlPath = Path.ChangeExtension(note.FilePath, ".xaml");
            try
            {
                var xamlString = XamlWriter.Save(document);
                await _fileSystem.WriteTextAsync(xamlPath, xamlString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving XAML: {ex.Message}");
                await SaveNoteAsync(note);
                return;
            }

            try
            {
                var textRange = new TextRange(document.ContentStart, document.ContentEnd);
                var plainText = textRange.Text;
                note.Content = plainText;
                await _fileSystem.WriteTextAsync(note.FilePath, plainText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving plain text: {ex.Message}");
            }

            note.LastModified = DateTime.Now;
            note.MarkClean();
        }

        public async Task<FlowDocument> LoadNoteDocumentAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return new FlowDocument();

            var xamlPath = Path.ChangeExtension(filePath, ".xaml");

            try
            {
                if (await _fileSystem.ExistsAsync(xamlPath))
                {
                    var xaml = await _fileSystem.ReadTextAsync(xamlPath);
                    if (!string.IsNullOrEmpty(xaml))
                    {
                        using (var stringReader = new StringReader(xaml))
                        using (var xmlReader = XmlReader.Create(stringReader))
                        {
                            var document = XamlReader.Load(xmlReader) as FlowDocument;
                            if (document != null)
                                return document;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading XAML document: {ex.Message}");
            }

            try
            {
                if (await _fileSystem.ExistsAsync(filePath))
                {
                    var text = await _fileSystem.ReadTextAsync(filePath);

                    var document = new FlowDocument();
                    var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            document.Blocks.Add(new Paragraph());
                        }
                        else
                        {
                            document.Blocks.Add(new Paragraph(new Run(line)));
                        }
                    }

                    return document;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading plain text: {ex.Message}");
            }

            return new FlowDocument(new Paragraph(new Run("")));
        }

        public async Task<bool> HasFormattedVersionAsync(string filePath)
        {
            var xamlPath = Path.ChangeExtension(filePath, ".xaml");
            return await _fileSystem.ExistsAsync(xamlPath);
        }

        public string GetPlainTextFromDocument(FlowDocument document)
        {
            if (document == null) return string.Empty;

            var textRange = new TextRange(document.ContentStart, document.ContentEnd);
            return textRange.Text;
        }

        public async Task MigrateToFormattedAsync(NoteModel note)
        {
            if (!await _fileSystem.ExistsAsync(note.FilePath))
                return;

            var xamlPath = Path.ChangeExtension(note.FilePath, ".xaml");
            if (await _fileSystem.ExistsAsync(xamlPath))
                return;

            var text = await _fileSystem.ReadTextAsync(note.FilePath);
            var document = new FlowDocument(new Paragraph(new Run(text)));

            await SaveNoteWithDocumentAsync(note, document);
        }
    }
}
#endif

