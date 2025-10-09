using System;

namespace NoteNest.UI.Plugins.TodoPlugin.Domain.Common
{
    public class Result
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string Error { get; }

        protected Result(bool isSuccess, string error)
        {
            if (isSuccess && error != null)
                throw new InvalidOperationException("Success result cannot have an error");
            if (!isSuccess && error == null)
                throw new InvalidOperationException("Failure result must have an error");

            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result Ok() => new Result(true, null);
        public static Result Fail(string error) => new Result(false, error);

        public static Result<T> Ok<T>(T value) => new Result<T>(value, true, null);
        public static Result<T> Fail<T>(string error) => new Result<T>(default, false, error);
    }

    public class Result<T> : Result
    {
        public T Value { get; }

        protected internal Result(T value, bool isSuccess, string error)
            : base(isSuccess, error)
        {
            Value = value;
        }
    }
}

