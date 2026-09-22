using FluentAssertions;
using Wms.Application.Support;

namespace Wms.Application.Tests.Support;

public sealed class SupportBundlePolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidRequestIsAcceptedWithinBoundedWindowAndExpiry()
    {
        var result = SupportBundlePolicy.Validate(
            new SupportBundleRequest(
                "Investigate delayed integration delivery",
                Now.AddHours(-2),
                Now,
                ExpiresAfter: TimeSpan.FromHours(2)),
            Now);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public void FutureOrOversizedRequestsAreRejected()
    {
        var future = SupportBundlePolicy.Validate(
            new SupportBundleRequest(
                "future",
                Now,
                Now.AddHours(1),
                ExpiresAfter: TimeSpan.FromHours(2)),
            Now);
        var oversized = SupportBundlePolicy.Validate(
            new SupportBundleRequest(
                "large",
                Now.AddHours(-1),
                Now,
                MaximumBytes: SupportBundlePolicy.MaximumBundleBytes + 1),
            Now);

        future.IsFailure.Should().BeTrue();
        oversized.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SensitiveBundleFieldsAreRedactedAndOtherValuesAreBounded()
    {
        var redacted = new List<string>();
        var values = SupportBundlePolicy.Redact(
            new Dictionary<string, string?>
            {
                ["connectionString"] = "Host=db;Password=hidden",
                ["payload"] = "{\"secret\":\"hidden\"}",
                ["correlationId"] = new string('x', 3_000)
            },
            redacted);

        values["connectionString"].Should().Be("[redacted]");
        values["payload"].Should().Be("[redacted]");
        values["correlationId"]!.Length.Should().Be(2_000);
        redacted.Should().Contain("connectionString");
    }
}
