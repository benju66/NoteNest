using NUnit.Framework;
using NoteNest.Core.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Tests.Services
{
	[TestFixture]
	public class MarkdownServiceTests
	{
		private MarkdownService _service;

		[SetUp]
		public void Setup()
		{
			_service = new MarkdownService(AppLogger.Instance);
		}

		[Test]
		public void DetectFormatFromContent_Markdown_ReturnsMarkdown()
		{
			var content = "# Title\n**bold** and *italic*";
			Assert.That(_service.DetectFormatFromContent(content), Is.EqualTo(NoteFormat.Markdown));
		}

		[Test]
		public void Sanitize_RemovesScript()
		{
			var content = "hello <script>alert('x')</script> world";
			var sanitized = _service.SanitizeMarkdown(content);
			Assert.That(sanitized.Contains("<script"), Is.False);
		}

		[Test]
		public void ConvertToMarkdown_AutoLinksUrls()
		{
			var content = "Visit https://example.com";
			var md = _service.ConvertToMarkdown(content);
			Assert.That(md, Does.Contain("[https://example.com](https://example.com)"));
		}

		[Test]
		public void StripMarkdown_RemovesFormatting()
		{
			var md = "# Title\n**bold** [link](https://x)";
			var plain = _service.StripMarkdownForIndex(md);
			Assert.That(plain, Does.Contain("Title"));
			Assert.That(plain, Does.Contain("bold"));
			Assert.That(plain, Does.Not.Contain("**"));
		}
	}
}


