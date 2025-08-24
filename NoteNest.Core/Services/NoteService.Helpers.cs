using System;

namespace NoteNest.Core.Services
{
	public partial class NoteService
	{
		private string SanitizeFileName(string fileName)
		{
			return PathService.SanitizeName(fileName);
		}
	}
}


