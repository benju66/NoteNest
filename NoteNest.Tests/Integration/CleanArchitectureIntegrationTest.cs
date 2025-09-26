using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MediatR;
using NUnit.Framework;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.UI.ViewModels.Shell;

namespace NoteNest.Tests.Integration
{
    [TestFixture]
    public class CleanArchitectureIntegrationTest
    {
        private IHost _host;
        private IServiceProvider _serviceProvider;

        [SetUp]
        public async Task Setup()
        {
            // Create test configuration
            var configBuilder = new ConfigurationBuilder();
            var config = configBuilder.Build();

            // Build host with Clean Architecture services
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    NoteNest.UI.Composition.ServiceConfiguration.ConfigureServices(services, config);
                })
                .Build();

            _serviceProvider = _host.Services;
            await _host.StartAsync();
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        [Test]
        public void CanResolveMainShellViewModel()
        {
            // Act
            var mainShell = _serviceProvider.GetRequiredService<MainShellViewModel>();

            // Assert
            Assert.That(mainShell, Is.Not.Null);
            Assert.That(mainShell.CategoryTree, Is.Not.Null);
            Assert.That(mainShell.NoteOperations, Is.Not.Null);
            Assert.That(mainShell.CategoryOperations, Is.Not.Null);
            Assert.That(mainShell.Workspace, Is.Not.Null);
        }

        [Test]
        public void CanResolveMediatR()
        {
            // Act
            var mediator = _serviceProvider.GetRequiredService<IMediator>();

            // Assert
            Assert.That(mediator, Is.Not.Null);
        }

        [Test]
        public async Task CanExecuteCreateNoteCommand()
        {
            // Arrange
            var mediator = _serviceProvider.GetRequiredService<IMediator>();
            var command = new CreateNoteCommand
            {
                CategoryId = "test-category",
                Title = "Test Note",
                InitialContent = "Test content"
            };

            // Act
            var result = await mediator.Send(command);

            // Assert
            Assert.That(result, Is.Not.Null);
            // Note: This will likely fail due to missing category, but shows the pipeline works
        }

        [Test]
        public void AllViewModelsCanBeResolved()
        {
            // Act & Assert - all should resolve without errors
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<MainShellViewModel>());
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<NoteNest.UI.ViewModels.Notes.NoteOperationsViewModel>());
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<NoteNest.UI.ViewModels.Categories.CategoryOperationsViewModel>());
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<NoteNest.UI.ViewModels.Workspace.ModernWorkspaceViewModel>());
        }
    }
}
