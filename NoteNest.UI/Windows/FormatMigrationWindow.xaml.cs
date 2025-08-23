using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Windows
{
	public partial class FormatMigrationWindow : Window
	{
		private readonly ConfigurationService _config;
		private readonly IFileSystemProvider _fs;
		private readonly IMarkdownService _md;
		private readonly IAppLogger _logger;

		public MigrationOptions Options { get; private set; } = new MigrationOptions();

		public FormatMigrationWindow(ConfigurationService config, IFileSystemProvider fs, IMarkdownService md, IAppLogger logger)
		{
			InitializeComponent();
			_config = config;
			_fs = fs;
			_md = md;
			_logger = logger;

			RbWorkspace.Checked += (_, __) => ToggleFolder(false);
			RbFolder.Checked += (_, __) => ToggleFolder(true);
			BtnBrowse.Click += BtnBrowse_OnClick;
			ToggleFolder(RbFolder.IsChecked == true);

			// Prefill folder with settings path
			var root = _config.Settings?.DefaultNotePath ?? PathService.RootPath;
			TxtFolder.Text = root;
		}

		private void ToggleFolder(bool on)
		{
			TxtFolder.IsEnabled = on;
			BtnBrowse.IsEnabled = on;
		}

        private void BtnBrowse_OnClick(object? sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select any file inside the target folder",
                CheckFileExists = true,
                Filter = "All files|*.*",
                Multiselect = false,
                InitialDirectory = Directory.Exists(TxtFolder.Text) ? TxtFolder.Text : (PathService.RootPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
            };
            if (dlg.ShowDialog(this) == true)
            {
                var folder = System.IO.Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
                TxtFolder.Text = folder;
            }
        }

		private async void BtnPreview_OnClick(object sender, RoutedEventArgs e)
		{
			LstPreview.Items.Clear();
			var opts = BuildOptions(dryRun:true);
			var service = new FormatMigrationService(_fs, _md, _logger);
			var result = await service.MigrateAsync(opts);
			LstPreview.Items.Add($"Would process {result.TotalFiles} files; convert {result.ConvertedFiles}, skip {result.SkippedFiles}.");
			foreach (var item in result.ChangedFiles.Take(50))
			{
				LstPreview.Items.Add(item);
			}
			if (result.ChangedFiles.Count > 50)
			{
				LstPreview.Items.Add($"...and {result.ChangedFiles.Count - 50} more");
			}
		}

		private async void BtnConvert_OnClick(object sender, RoutedEventArgs e)
		{
			Options = BuildOptions(dryRun:false);
			this.DialogResult = true;
		}

		private MigrationOptions BuildOptions(bool dryRun)
		{
			var root = (RbFolder.IsChecked == true && !string.IsNullOrWhiteSpace(TxtFolder.Text))
				? TxtFolder.Text
				: (_config.Settings?.DefaultNotePath ?? PathService.RootPath);
			var target = (RbTxt.IsChecked == true) ? NoteFormat.PlainText : NoteFormat.Markdown;
			var patterns = (TxtExclude.Text ?? string.Empty)
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim())
				.Where(s => s.Length > 0)
				.ToArray();

			return new MigrationOptions
			{
				RootPath = root,
				TargetFormat = target,
				CreateBackups = CbBackups.IsChecked == true,
				DryRun = dryRun || (CbDryRun.IsChecked == true),
				ExcludePatterns = patterns
			};
		}
	}
}


