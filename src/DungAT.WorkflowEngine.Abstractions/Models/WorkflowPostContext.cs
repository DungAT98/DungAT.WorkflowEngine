namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>
/// What a post action sees, after the transition has been saved. <c>Instance</c> is the updated instance
/// and <c>Entity</c> already shows the new state. <c>Transition</c> is null when the action runs because
/// an instance was just started (state-entry actions of the initial state).
/// </summary>
public sealed record WorkflowPostContext(
    WorkflowDefinition Definition,
    WorkflowInstance Instance,
    WorkflowTransition? Transition,
    string? FromStateCode,
    string ToStateCode,
    WorkflowUser User,
    string? Comment,
    string? Payload,
    WorkflowHistoryEntry History,
    IServiceProvider Services,
    object? Entity,
    Func<string, string?, WorkflowUser?, Task<WorkflowResult<WorkflowInstance>>> FollowUp)
{
    public T GetEntity<T>() where T : class => EntityAccess.Get<T>(Entity, Instance.EntityType);

    public bool TryGetEntity<T>(out T entity) where T : class
    {
        if (Entity is T typed) { entity = typed; return true; }
        entity = null!;
        return false;
    }

    /// <summary>
    /// Runs another command on the same instance, e.g. auto-approving a small report right after it was submitted.
    /// Runs as <paramref name="user"/>, or as <see cref="WorkflowUser.System"/> by default. Guards still apply, and the
    /// chain is limited (<c>MaxChainDepth</c>) so actions cannot trigger each other forever; past the limit the
    /// result has status <see cref="WorkflowResultStatus.ChainDepthExceeded"/>. Note that <see cref="Instance"/>
    /// in this context is not refreshed after a follow-up.
    /// </summary>
    public Task<WorkflowResult<WorkflowInstance>> FollowUpAsync(string commandCode, string? comment = null, WorkflowUser? user = null) =>
        FollowUp(commandCode, comment, user);
}
