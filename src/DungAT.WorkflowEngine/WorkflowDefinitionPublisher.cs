using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine;

/// <summary>
/// Publishes registered code definitions to the store. Call once at startup; a new version is only
/// created when the definition's structure differs from the latest stored version.
/// </summary>
public sealed class WorkflowDefinitionPublisher
{
    private readonly IEnumerable<CodeDefinition> _definitions;
    private readonly IWorkflowDefinitionStore _store;
    private readonly IWorkflowComponentRegistry _components;

    internal WorkflowDefinitionPublisher(
        IEnumerable<CodeDefinition> definitions, IWorkflowDefinitionStore store, IWorkflowComponentRegistry components) =>
        (_definitions, _store, _components) = (definitions, store, components);

    /// <exception cref="WorkflowDefinitionException">
    /// A definition references an unregistered guard/effect, or one meant for a different entity.
    /// Checked up front so a mistake fails at startup rather than on the first transition.
    /// </exception>
    public async Task PublishAllAsync(CancellationToken ct = default)
    {
        var errors = _definitions
            .SelectMany(d => WorkflowComponentValidator.Validate(d.Definition, _components).Select(e => $"{d.Definition.Code}: {e}"))
            .ToList();
        if (errors.Count > 0) throw new WorkflowDefinitionException(errors);

        foreach (var code in _definitions)
        {
            var latest = await _store.GetAsync(code.Definition.Code, null, ct);
            if (latest is null || Fingerprint(latest) != Fingerprint(code.Definition))
                await _store.PublishAsync(code.Definition, ct);
        }
    }

    private static string Fingerprint(WorkflowDefinition d) => JsonSerializer.Serialize(d with { Version = 0 });
}
