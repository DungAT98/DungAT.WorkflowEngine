
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>A command a user can run on an instance (e.g. SUBMIT, APPROVE).</summary>
public sealed record WorkflowCommand(string Code, string Name, string? Description = null);
