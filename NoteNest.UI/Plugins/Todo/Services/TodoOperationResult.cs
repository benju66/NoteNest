using System;

namespace NoteNest.UI.Plugins.Todo.Services
{
	public class TodoOperationResult<T>
	{
		public bool Success { get; }
		public T Data { get; }
		public string ErrorMessage { get; }

		private TodoOperationResult(bool success, T data, string error)
		{
			Success = success;
			Data = data;
			ErrorMessage = error;
		}

		public static TodoOperationResult<T> Ok(T data) => new TodoOperationResult<T>(true, data, null);
		public static TodoOperationResult<T> Fail(string error) => new TodoOperationResult<T>(false, default, error);
	}
}


