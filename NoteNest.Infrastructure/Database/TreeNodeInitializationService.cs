using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Hosted service that initializes the TreeNode database by populating it from the file system on first run
    /// </summary>
    public class TreeNodeInitializationService : IHostedService
    {
        private readonly ITreeDatabaseInitializer _initializer;
        private readonly ITreePopulationService _populationService;
        private readonly IConfiguration _configuration;
        private readonly IAppLogger _logger;

        public TreeNodeInitializationService(
            ITreeDatabaseInitializer initializer,
            ITreePopulationService populationService,
            IConfiguration configuration,
            IAppLogger logger)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _populationService = populationService ?? throw new ArgumentNullException(nameof(populationService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("TreeNode initialization service starting...");

                // Step 1: Initialize database schema
                _logger.Info("Initializing TreeNode database schema...");
                if (!await _initializer.InitializeAsync())
                {
                    throw new InvalidOperationException("Failed to initialize TreeNode database schema");
                }

                // Step 2: Check if database needs population
                if (await _populationService.IsDatabaseEmptyAsync())
                {
                    _logger.Info("TreeNode database is empty, populating from file system...");
                    
                    var notesPath = _configuration.GetValue<string>("NotesPath");
                    if (string.IsNullOrEmpty(notesPath))
                    {
                        notesPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                            "NoteNest");
                    }

                    _logger.Info($"Populating TreeNode database from: {notesPath}");
                    
                    var success = await _populationService.PopulateFromFileSystemAsync(notesPath);
                    if (success)
                    {
                        _logger.Info("TreeNode database populated successfully from file system");
                    }
                    else
                    {
                        _logger.Warning("Failed to populate TreeNode database from file system");
                    }
                }
                else
                {
                    _logger.Info("TreeNode database already has data, skipping population");
                }

                // Step 3: Verify database health
                if (!await _initializer.IsHealthyAsync())
                {
                    _logger.Warning("TreeNode database health check failed");
                }
                else
                {
                    _logger.Info("TreeNode database health check passed");
                }

                _logger.Info("TreeNode initialization service completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeNode initialization service failed");
                // Don't throw - allow application to start even if population fails
                // The tree view will just be empty until user creates categories/notes
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("TreeNode initialization service stopping");
            return Task.CompletedTask;
        }
    }
}
