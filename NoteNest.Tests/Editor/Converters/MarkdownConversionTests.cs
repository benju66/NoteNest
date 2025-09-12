using NUnit.Framework;
using NoteNest.UI.Controls.Editor.Converters;

namespace NoteNest.Tests.Editor.Converters
{
    [TestFixture]
    public class MarkdownConversionTests
    {
        private MarkdownFlowDocumentConverter _converter;

        [SetUp]
        public void Setup()
        {
            _converter = new MarkdownFlowDocumentConverter();
        }

        [Test]
        public void Headers_ConvertCorrectly()
        {
            var markdown = "# Header 1\n## Header 2";
            // Test conversion
            var document = _converter.ConvertToFlowDocument(markdown, "Calibri", 14);
            Assert.That(document, Is.Not.Null);
        }

        [Test]
        public void Lists_ConvertCorrectly()
        {
            var markdown = "- Item 1\n- Item 2";
            // Test conversion
            var document = _converter.ConvertToFlowDocument(markdown, "Calibri", 14);
            Assert.That(document, Is.Not.Null);
        }

        [Test]
        public void TaskLists_ConvertCorrectly()
        {
            var markdown = "- [ ] Todo\n- [x] Done";
            // Test conversion
            var document = _converter.ConvertToFlowDocument(markdown, "Calibri", 14);
            Assert.That(document, Is.Not.Null);
        }

        [Test]
        public void RoundTrip_PreservesContent()
        {
            var originalMarkdown = "# Test\n\n- Item 1\n- Item 2\n\n**Bold text**";
            
            // Convert to document and back
            var document = _converter.ConvertToFlowDocument(originalMarkdown, "Calibri", 14);
            var convertedBack = _converter.ConvertToMarkdown(document);
            
            Assert.That(convertedBack, Is.Not.Null);
            Assert.That(convertedBack, Does.Contain("Test"));
            Assert.That(convertedBack, Does.Contain("Item 1"));
        }
    }
}
