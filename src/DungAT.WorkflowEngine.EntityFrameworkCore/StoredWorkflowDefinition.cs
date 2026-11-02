using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

/// <summary>Row holding one published definition version as JSON.</summary>
public sealed class StoredWorkflowDefinition
{
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public string Json { get; set; } = "";
}
