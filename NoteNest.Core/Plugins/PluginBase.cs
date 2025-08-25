using System;
using System.Threading.Tasks;
using System.Windows.Input;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Plugins
{
	/// <summary>
	/// Base class for plugins with common functionality
	/// </summary>
	public abstract class PluginBase : IPlugin
	{
		private bool _isEnabled;
		private bool _isInitialized;
		private ICommand _activateCommand;
		
		public abstract string Id { get; }
		public abstract string Name { get; }
		public abstract string Icon { get; }
		public abstract Version Version { get; }
		public abstract string Description { get; }
		
		public bool IsEnabled
		{
			get => _isEnabled;
			set
			{
				if (_isEnabled != value)
				{
					_isEnabled = value;
					if (_isEnabled && !_isInitialized)
					{
						_ = InitializeAsync();
					}
					else if (!_isEnabled && _isInitialized)
					{
						_ = ShutdownAsync();
					}
				}
			}
		}
		
		public ICommand ActivateCommand => 
			_activateCommand ??= new RelayCommand(OnActivate);
		
		public virtual async Task<bool> InitializeAsync()
		{
			if (_isInitialized)
				return true;
				
			try
			{
				await OnInitializeAsync();
				_isInitialized = true;
				return true;
			}
			catch (Exception ex)
			{
				AppLogger.Instance?.Error(ex, $"Failed to initialize plugin: {Name}");
				return false;
			}
		}
		
		public virtual async Task ShutdownAsync()
		{
			if (!_isInitialized)
				return;
				
			try
			{
				await OnShutdownAsync();
				GetPanel()?.OnPanelClosed();
			}
			finally
			{
				_isInitialized = false;
			}
		}
		
		protected abstract Task OnInitializeAsync();
		protected abstract Task OnShutdownAsync();
		protected abstract void OnActivate();
		
		public abstract IPluginPanel GetPanel();
		public abstract IPluginSettings GetSettings();
		
		public virtual void Dispose()
		{
			if (_isInitialized)
			{
				ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
			}
		}
	}
}


