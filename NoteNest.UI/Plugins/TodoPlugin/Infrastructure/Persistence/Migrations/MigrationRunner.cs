using System;
using System.Data;
using System.IO;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Runs database migrations for Todo Plugin database.
    /// Migrations are applied in order based on version number.
    /// </summary>
    public class MigrationRunner
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;

        public MigrationRunner(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Apply all pending migrations to bring database to latest version.
        /// </summary>
        public void ApplyMigrations()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var currentVersion = GetCurrentVersion(connection);
            _logger.Info($"[MigrationRunner] Current database version: {currentVersion}");

            // Define migrations (in order)
            var migrations = new[]
            {
                new Migration { Version = 2, Name = "Migration_002_AddIsAutoToTodoTags.sql" },
                new Migration { Version = 3, Name = "Migration_003_AddTagFtsTriggers.sql" }
            };

            foreach (var migration in migrations)
            {
                if (migration.Version > currentVersion)
                {
                    ApplyMigration(connection, migration);
                }
            }

            _logger.Info($"[MigrationRunner] Database migrations complete. Final version: {GetCurrentVersion(connection)}");
        }

        private int GetCurrentVersion(IDbConnection connection)
        {
            try
            {
                // Check if schema_version table exists
                var tableExists = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'");

                if (tableExists == 0)
                {
                    _logger.Warning("[MigrationRunner] schema_version table does not exist. Assuming version 1.");
                    return 1;
                }

                // Get latest version
                var version = connection.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 1;
                return version;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MigrationRunner] Error getting current version");
                return 1; // Assume version 1 if error
            }
        }

        private void ApplyMigration(IDbConnection connection, Migration migration)
        {
            try
            {
                _logger.Info($"[MigrationRunner] Applying migration {migration.Version}: {migration.Name}");

                // Read migration SQL from embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Persistence.Migrations.{migration.Name}";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    throw new FileNotFoundException($"Migration file not found: {resourceName}");
                }

                using var reader = new StreamReader(stream);
                var sql = reader.ReadToEnd();

                // Execute migration (already wrapped in transaction in SQL file)
                connection.Execute(sql);

                _logger.Info($"[MigrationRunner] ✅ Migration {migration.Version} applied successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[MigrationRunner] ❌ Failed to apply migration {migration.Version}: {migration.Name}");
                throw new InvalidOperationException($"Migration {migration.Version} failed. See inner exception.", ex);
            }
        }

        private class Migration
        {
            public int Version { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}

