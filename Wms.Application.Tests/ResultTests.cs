using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Common;
using Wms.Domain.Services;
using Xunit;

namespace Wms.Application.Tests;

public sealed class ResultTests
{
    [Fact]
    public void FailurePreservesTypedErrorAndCompatibilityMessage()
    {
        var result = Result.Failure<string>(WmsErrors.NotFound(
            "item.not_found",
            "The item was not found."));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("The item was not found.");
        result.ErrorCode.Should().Be("item.not_found");
        result.Errors.Should().ContainSingle()
            .Which.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void FailureCanCarryFieldErrorsAndRetryability()
    {
        var result = Result.Failure(WmsErrors.Concurrency(
            "stock.concurrent_update",
            "The stock record changed. Reload and try again."));

        result.IsRetryable.Should().BeTrue();
        result.FirstError!.IsExpected.Should().BeTrue();
        result.FirstError.IsRetryable.Should().BeTrue();

        var validation = WmsErrors.Validation(
            "request.invalid",
            "Correct the highlighted fields.",
            new Dictionary<string, string[]> { ["Quantity"] = ["Quantity must be positive."] });
        validation.FieldErrors!["Quantity"].Should().ContainSingle("Quantity must be positive.");
    }

    [Fact]
    public void CancellationIsNotRepresentedAsAnUnexpectedFailure()
    {
        var result = Result.Failure(WmsErrors.Cancelled());

        result.FirstError!.Type.Should().Be(ErrorType.Cancelled);
        result.FirstError.IsExpected.Should().BeTrue();
        result.Error.Should().NotContain("Exception");
    }

    [Fact]
    public void ConcurrencyExceptionMapsToRetryableTypedError()
    {
        var result = WmsErrors.FromException(
            new ConcurrencyConflictException("InventoryBalance", "Id=7"),
            "inventory.failed",
            "The inventory operation failed.");

        result.Type.Should().Be(ErrorType.Concurrency);
        result.Code.Should().Be("data.concurrency_conflict");
        result.IsRetryable.Should().BeTrue();
        result.Message.Should().NotContain("Id=7");
    }

    [Theory]
    [InlineData("23505", ErrorType.Conflict)]
    [InlineData("40001", ErrorType.Concurrency)]
    [InlineData("40P01", ErrorType.Concurrency)]
    public void PostgreSqlWriteConflictsMapToTypedRetryableErrors(string sqlState, ErrorType expectedType)
    {
        var exception = new DbUpdateException(
            "A database write failed.",
            new PostgreSqlExceptionStub(sqlState));

        var result = WmsErrors.FromException(
            exception,
            "inventory.failed",
            "The inventory operation failed.");

        result.Type.Should().Be(expectedType);
        result.IsRetryable.Should().Be(expectedType is ErrorType.Concurrency);
        result.Message.Should().NotContain(sqlState);
        result.Message.Should().NotContain("PostgreSqlExceptionStub");
    }

    [Fact]
    public void DatabaseUpdateFailureWithoutConflictStateRemainsDependencyError()
    {
        var result = WmsErrors.FromException(
            new DbUpdateException("A database write failed."),
            "inventory.failed",
            "The inventory operation failed.");

        result.Type.Should().Be(ErrorType.Dependency);
        result.Code.Should().Be("data.dependency_failure");
    }

    private sealed class PostgreSqlExceptionStub(string sqlState) : Exception("PostgreSQL write failed.")
    {
        public string SqlState { get; } = sqlState;
    }
}
