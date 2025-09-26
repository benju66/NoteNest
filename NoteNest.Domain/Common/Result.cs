namespace NoteNest.Domain.Common
{
    public class Result
    {
        public bool Success { get; }
        public string Error { get; }
        public bool IsFailure => !Success;

        protected Result(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public static Result Ok() => new(true, null);
        public static Result Fail(string error) => new(false, error);
        public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
        public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
    }

    public class Result<T> : Result
    {
        public T Value { get; }

        protected Result(bool success, string error, T value) : base(success, error)
        {
            Value = value;
        }

        public static new Result<T> Ok(T value) => new(true, null, value);
        public static new Result<T> Fail(string error) => new(false, error, default);
    }
}
