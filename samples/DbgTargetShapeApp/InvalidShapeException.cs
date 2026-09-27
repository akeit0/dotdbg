namespace DbgTargetShapeApp;

public class InvalidShapeException : Exception
{
    public object? Value { get; }

    public InvalidShapeException(string message, object? value)
        : base(message)
    {
        Value = value;
    }

    public InvalidShapeException(string message, object? value, Exception inner)
        : base(message, inner)
    {
        Value = value;
    }
}
