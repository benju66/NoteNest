using System.Collections.Generic;

namespace NoteNest.Core.Plugins
{
	/// <summary>
	/// Plugin settings interface
	/// </summary>
	public interface IPluginSettings
	{
		Dictionary<string, object> ToDictionary();
		void FromDictionary(Dictionary<string, object> settings);
		void ResetToDefaults();
		bool Validate(out string errorMessage);
	}
}


