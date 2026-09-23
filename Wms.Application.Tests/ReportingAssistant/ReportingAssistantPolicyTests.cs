using System.Globalization;
using FluentAssertions;
using Wms.Application.Identity;
using Wms.Application.ReportingAssistant;

namespace Wms.Application.Tests.ReportingAssistant;

public sealed class ReportingAssistantPolicyTests
{
    [Fact]
    public void Creates_scoped_inventory_plan_without_retaining_the_question()
    {
        var result = ReportingAssistantPolicy.CreatePlan(
            new ReportingAssistantRequest(
                "Show available inventory for warehouse 7",
                WarehouseId: 7,
                AllowedWarehouseIds: new HashSet<int> { 7, 8 },
                PermissionCodes: new HashSet<string>
                {
                    WmsPermissions.ReportsRead,
                    WmsPermissions.InventoryRead
                }),
            DateTimeOffset.Parse("2026-09-22T10:00:00Z", CultureInfo.InvariantCulture));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ReportingAssistantPlanStatus.Ready);
        result.Value.QueryKind.Should().Be(ReportingAssistantQueryKind.InventoryBalance);
        result.Value.ToolName.Should().Be(ReportingAssistantPolicy.InventoryTool);
        result.Value.WarehouseId.Should().Be(7);
        result.Value.QuestionFingerprint.Should().NotContain("Show available inventory");
    }

    [Fact]
    public void Rejects_mutation_intent_before_tool_planning()
    {
        var result = ReportingAssistantPolicy.CreatePlan(
            new ReportingAssistantRequest(
                "Adjust inventory quantity for SKU-1",
                PermissionCodes: new HashSet<string>
                {
                    WmsPermissions.ReportsRead,
                    WmsPermissions.InventoryRead
                }),
            DateTimeOffset.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("reporting_assistant.read_only");
    }

    [Fact]
    public void Rejects_a_warehouse_outside_the_authenticated_scope()
    {
        var result = ReportingAssistantPolicy.CreatePlan(
            new ReportingAssistantRequest(
                "Show stock levels",
                WarehouseId: 9,
                AllowedWarehouseIds: new HashSet<int> { 1, 2 },
                PermissionCodes: new HashSet<string>
                {
                    WmsPermissions.ReportsRead,
                    WmsPermissions.InventoryRead
                }),
            DateTimeOffset.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("reporting_assistant.warehouse_forbidden");
    }

    [Fact]
    public void Returns_a_clarification_plan_for_ambiguous_read_questions()
    {
        var result = ReportingAssistantPolicy.CreatePlan(
            new ReportingAssistantRequest(
                "Show inventory movements",
                PermissionCodes: new HashSet<string> { WmsPermissions.ReportsRead, WmsPermissions.InventoryRead }),
            DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ReportingAssistantPlanStatus.NeedsClarification);
        result.Value.QueryKind.Should().BeNull();
        result.Value.ToolName.Should().Be("reports.clarification.required");
        result.Value.ClarificationQuestion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Enforces_permission_and_row_bounds()
    {
        var result = ReportingAssistantPolicy.Validate(
            new ReportingAssistantRequest(
                "Show movement history",
                MaximumRows: ReportingAssistantPolicy.MaximumRows + 1,
                PermissionCodes: new HashSet<string> { WmsPermissions.ReportsRead }));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("reporting_assistant.row_limit_invalid");
    }
}
