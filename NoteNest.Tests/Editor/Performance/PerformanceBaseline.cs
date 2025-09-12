using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using NoteNest.UI.Controls.Editor.Converters;

namespace NoteNest.Tests.Editor.Performance
{
    [TestFixture]
    public class PerformanceBaseline
    {
        private MarkdownFlowDocumentConverter _converter;

        [SetUp]
        public void Setup()
        {
            _converter = new MarkdownFlowDocumentConverter();
        }

        [Test]
        public void Document_CurrentConversionTime()
        {
            // Measure and record current conversion performance
            var testMarkdown = GenerateTestMarkdown();
            
            var stopwatch = Stopwatch.StartNew();
            
            // Perform conversion multiple times for accurate measurement
            for (int i = 0; i < 100; i++)
            {
                var document = _converter.ConvertToFlowDocument(testMarkdown, "Calibri", 14);
                var backToMarkdown = _converter.ConvertToMarkdown(document);
            }
            
            stopwatch.Stop();
            
            var averageMs = stopwatch.ElapsedMilliseconds / 100.0;
            
            // Save baseline to file
            var baselineFile = Path.Combine(TestContext.CurrentContext.WorkDirectory, "performance_baseline.txt");
            File.WriteAllText(baselineFile, 
                $"Conversion Time: {averageMs:F2}ms average\n" +
                $"Total for 100 iterations: {stopwatch.ElapsedMilliseconds}ms\n" +
                $"Test Date: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Test Content Size: {testMarkdown.Length} characters");
            
            TestContext.WriteLine($"Performance baseline recorded: {averageMs:F2}ms average per conversion");
            
            // Assert reasonable performance (adjust threshold as needed)
            Assert.That(averageMs, Is.LessThan(50), "Conversion should be under 50ms on average");
        }

        private string GenerateTestMarkdown()
        {
            return @"# Main Header

## Section 1

This is a paragraph with **bold text** and *italic text*.

### Subsection

- Bullet item 1
- Bullet item 2
  - Nested item
  - Another nested item

1. Numbered item 1
2. Numbered item 2
3. Numbered item 3

### Task Lists

- [ ] Todo item 1
- [x] Completed item
- [ ] Todo item 2

### Code Example

```csharp
public void Example()
{
    Console.WriteLine(""Hello World"");
}
```

## Section 2

Another paragraph with [link](https://example.com) and more text.

> This is a blockquote with some content.

---

Final paragraph.";
        }
    }
}
