using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Building;

namespace DungAT.WorkflowEngine.Sample;

public static class SampleWorkflows
{
    /// <summary>Bound to the Report table: its guards and effects receive the Report row.</summary>
    public static WorkflowDefinitionBuilder.StateEnterBuilder ReportApproval() =>
        WorkflowDefinitionBuilder.Define("REPORT_APPROVAL", "Report approval")
            .Describe("Expense reports are drafted, checked against budget and attachments, then approved by a manager.")
            .ForEntity("Report")
            .State("DRAFT", WorkflowStateType.Initial, "Draft")
            .State("SUBMITTED", name: "Submitted")
            .State("APPROVED", WorkflowStateType.Final, "Approved")
            .State("REJECTED", name: "Rejected")
            .Command("SUBMIT", "Submit for approval")
            .Command("APPROVE", "Approve")
            .Command("REJECT", "Reject")
            .Command("REVISE", "Revise")
            .Command("RECALL", "Recall")
            .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").Guard<ReportHasAttachmentsGuard>().Guard<ReportWithinBudgetGuard>().Effect<NotifyManagerEffect>()
                // After the transition is saved: tell the owner, send the receipt, and approve small reports automatically.
                .PostAction<NotifyOwnerPostAction>().PostAction<SendReceiptEmailPostAction>().PostAction<AutoApproveSmallReportsPostAction>()
            .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager")
            .On("SUBMITTED").Command("REJECT").GoTo("REJECTED").AllowRoles("Manager").RequireComment()
            .On("SUBMITTED").Command("RECALL").GoTo("DRAFT")
            .On("REJECTED").Command("REVISE").GoTo("DRAFT")
            .OnEnter("APPROVED").PostAction<ArchiveApprovedReportPostAction>();

    public static WorkflowDefinitionBuilder.TransitionBuilder LeaveRequest() =>
        WorkflowDefinitionBuilder.Define("LEAVE_REQUEST", "Leave request")
            .Describe("Two-step approval for time off: line manager, then HR. Not linked to a table, so it works with just an id.")
            .ForEntity("LeaveRequest")
            .State("OPEN", WorkflowStateType.Initial, "Open")
            .State("MANAGER_REVIEW", name: "Manager review")
            .State("HR_REVIEW", name: "HR review")
            .State("GRANTED", WorkflowStateType.Final, "Granted")
            .State("DECLINED", WorkflowStateType.Final, "Declined")
            .Command("REQUEST", "Request leave")
            .Command("ENDORSE", "Endorse")
            .Command("GRANT", "Grant")
            .Command("DECLINE", "Decline")
            .On("OPEN").Command("REQUEST").GoTo("MANAGER_REVIEW")
            .On("MANAGER_REVIEW").Command("ENDORSE").GoTo("HR_REVIEW").AllowRoles("Manager")
            .On("MANAGER_REVIEW").Command("DECLINE").GoTo("DECLINED").AllowRoles("Manager").RequireComment()
            .On("HR_REVIEW").Command("GRANT").GoTo("GRANTED").AllowRoles("HR")
            .On("HR_REVIEW").Command("DECLINE").GoTo("DECLINED").AllowRoles("HR").RequireComment();
}
