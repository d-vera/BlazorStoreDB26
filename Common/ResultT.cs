namespace tienda.Common;

public class Result<T> : Result
{
    public T? Data { get; set; }

    public static Result<T> Ok(T data, string message = "")
    {
        return new Result<T>
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    public new static Result<T> Fail(string message, params string[] errors)
    {
        return new Result<T>
        {
            Success = false,
            Message = message,
            Errors = [.. errors]
        };
    }
}
