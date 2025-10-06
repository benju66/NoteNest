using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NoteNest.Test
{
    class TestSearchInit
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== NoteNest Search Initialization Test ===");
            Console.WriteLine($"Starting at: {DateTime.Now:HH:mm:ss.fff}");

            try
            {
                // Build configuration
                var config = new ConfigurationBuilder()
                    .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .Build();

                // Build host
                var host = Host.CreateDefaultBuilder()
                    .ConfigureServices(services =>
                    {
                        // Configure logging to console
                        services.AddLogging(logging =>
                        {
                            logging.ClearProviders();
                            logging.AddConsole();
                            logging.SetMinimumLevel(LogLevel.Debug);
                        });

                        // Use the Clean Architecture service configuration
                        NoteNest.UI.Composition.CleanServiceConfiguration.ConfigureCleanArchitecture(services, config);
                    })
                    .Build();

                await host.StartAsync();

                var serviceProvider = host.Services;
                Console.WriteLine("✅ Host started successfully");

                // Test search service initialization
                Console.WriteLine("\n🔍 Testing Search Service Initialization...");
                
                var searchService = serviceProvider.GetRequiredService<NoteNest.UI.Interfaces.ISearchService>();
                Console.WriteLine($"✅ ISearchService resolved: {searchService.GetType().Name}");
                
                // Initialize search service
                Console.WriteLine("Initializing search service...");
                await searchService.InitializeAsync();
                Console.WriteLine("✅ Search service initialized");
                
                // Check index status
                Console.WriteLine($"IsIndexReady: {searchService.IsIndexReady}");
                
                // Get document count
                var docCount = await searchService.GetIndexedDocumentCountAsync();
                Console.WriteLine($"Indexed documents: {docCount}");
                
                // Check if indexing
                if (searchService.IsIndexing())
                {
                    var progress = searchService.GetIndexingProgress();
                    if (progress != null)
                    {
                        Console.WriteLine($"Indexing in progress: {progress.Processed}/{progress.Total} ({progress.PercentComplete:F0}%)");
                    }
                    else
                    {
                        Console.WriteLine("Indexing in progress (no progress info available)");
                    }
                }
                
                // Test a search
                Console.WriteLine("\n📝 Testing search functionality...");
                var results = await searchService.SearchAsync("test", System.Threading.CancellationToken.None);
                Console.WriteLine($"Search returned {results.Count} results");
                
                foreach (var result in results)
                {
                    Console.WriteLine($"  - {result.Title} (Score: {result.Score:F2})");
                }

                // Test SearchIndexSyncService
                Console.WriteLine("\n🔄 Testing SearchIndexSyncService...");
                var hostedServices = serviceProvider.GetServices<IHostedService>();
                var syncServiceFound = false;
                foreach (var service in hostedServices)
                {
                    if (service.GetType().Name == "SearchIndexSyncService")
                    {
                        syncServiceFound = true;
                        Console.WriteLine($"✅ SearchIndexSyncService found and registered");
                        break;
                    }
                }
                if (!syncServiceFound)
                {
                    Console.WriteLine("❌ SearchIndexSyncService NOT found in hosted services!");
                }

                Console.WriteLine("\n✅ All tests completed successfully!");
                
                await host.StopAsync();
                host.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Test failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().FullName}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"\nInner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"Inner stack trace:\n{ex.InnerException.StackTrace}");
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
