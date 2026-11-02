namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Number of instances currently in one state of one definition version.</summary>
public sealed record WorkflowStateCount(string DefinitionCode, int DefinitionVersion, string StateCode, int Count);
