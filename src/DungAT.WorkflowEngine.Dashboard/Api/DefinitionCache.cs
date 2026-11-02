using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Dashboard.Api;

/// <summary>Per-request cache of definition versions, so lists of instances resolve each version once.</summary>
internal sealed class DefinitionCache(IWorkflowDefinitionStore store)
{
    private readonly Dictionary<(string, int), WorkflowDefinition?> _items = [];

    public async Task<WorkflowDefinition?> GetAsync(string code, int version, CancellationToken ct)
    {
        var key = (code.ToUpperInvariant(), version);
        if (!_items.TryGetValue(key, out var definition))
            _items[key] = definition = await store.GetAsync(code, version, ct);
        return definition;
    }
}
