namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Where a definition was authored. Code-defined workflows are read-only in the dashboard.</summary>
public enum WorkflowDefinitionSource
{
    Code = 0,
    Dashboard = 1,
}
