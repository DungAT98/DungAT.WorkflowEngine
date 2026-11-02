namespace DungAT.WorkflowEngine.Options;

/// <summary>Engine-wide settings. Change them with <c>wf.Configure(o => ...)</c>.</summary>
public sealed class WorkflowEngineOptions
{
    /// <summary>
    /// How many follow-up commands a chain of post actions may run (an action that approves, whose own action
    /// then does something else, and so on). Stops runaway loops. Default 5.
    /// </summary>
    public int MaxChainDepth { get; set; } = 5;
}
