namespace VMASharp;

public class ValidationFailedException : ApplicationException
{
    public ValidationFailedException() : base("Validation of Allocator structures found a bug!") { }
}