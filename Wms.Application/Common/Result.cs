// Wms.Application/Common/Result.cs

namespace Wms.Application.Common;

public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<ResultError>? errors = null)
    {
        IsSuccess = isSuccess;
        Errors = errors ?? [];
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<ResultError> Errors { get; }
    public ResultError? FirstError => Errors.Count == 0 ? null : Errors[0];
    public string Error => FirstError?.Message ?? string.Empty;
    public string ErrorCode => FirstError?.Code ?? string.Empty;
    public bool IsRetryable => Errors.Any(error => error.IsRetryable);

    public Result<T> ToFailure<T>()
    {
        if (IsSuccess)
        {
            throw new InvalidOperationException("A successful result cannot be converted to a failure.");
        }

        return Result.Failure<T>(Errors);
    }

    public static Result Success()
    {
        return new Result(true);
    }

    public static Result Failure(string error)
    {
        return Failure(WmsErrors.BusinessRule("business.rule", error));
    }

    public static Result Failure(ResultError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, [error]);
    }

    public static Result Failure(IEnumerable<ResultError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var materializedErrors = errors.ToArray();
        return materializedErrors.Length == 0
            ? Failure(WmsErrors.Unexpected("error.empty", "The operation could not be completed."))
            : new Result(false, materializedErrors);
    }

    public static Result<T> Success<T>(T value)
    {
        return new Result<T>(value, true);
    }

    public static Result<T> Failure<T>(string error)
    {
        return Failure<T>(WmsErrors.BusinessRule("business.rule", error));
    }

    public static Result<T> Failure<T>(ResultError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default!, false, [error]);
    }

    public static Result<T> Failure<T>(IEnumerable<ResultError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var materializedErrors = errors.ToArray();
        return materializedErrors.Length == 0
            ? Failure<T>(WmsErrors.Unexpected("error.empty", "The operation could not be completed."))
            : new Result<T>(default!, false, materializedErrors);
    }
}

public class Result<T> : Result
{
    internal Result(T value, bool isSuccess, IReadOnlyList<ResultError>? errors = null) : base(isSuccess, errors)
    {
        Value = value;
    }

    public T Value { get; private set; }
}
