namespace DungAT.WorkflowEngine.Sample;

/// <summary>
/// A normal business table. It knows nothing about the workflow engine except that the engine keeps
/// <see cref="Status"/> in step with the workflow state (see SyncState in Program.cs).
/// </summary>
public class Report
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public int AttachmentCount { get; set; }
    public string OwnerId { get; set; } = "";
    public ReportStatus Status { get; set; }
}
