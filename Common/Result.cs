namespace tienda.Common;

public class Result
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];

    public static Result Ok(string message = "")
    {
        return new Result
        {
            Success = true,
            Message = message
        };
    }

    public static Result Fail(string message, params string[] errors)
    {
        return new Result
        {
            Success = false,
            Message = message,
            Errors = [.. errors]
        };
    }
}
