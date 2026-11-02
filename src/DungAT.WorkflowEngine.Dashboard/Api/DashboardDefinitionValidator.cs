using System.Text.RegularExpressions;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;

namespace DungAT.WorkflowEngine.Dashboard.Api;

/// <summary>
/// Checks a definition posted from the editor: sanitizes missing values, runs the engine's structural
/// validation and verifies that referenced guards and effects are registered in the host.
/// </summary>
internal static partial class DashboardDefinitionValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+$")]
    private static partial Regex CodePattern();

    /// <summary>Replaces null collections and strings so the engine validator never sees null.</summary>
    public static WorkflowDefinition Normalize(WorkflowDefinition d) => d with
    {
        Code = d.Code?.Trim() ?? "",
        Name = d.Name?.Trim() ?? "",
        States = (d.States ?? []).Select((s, i) => s with { Code = s.Code?.Trim() ?? "", Name = s.Name?.Trim() ?? "", Order = i, PostActionNames = s.PostActionNames ?? [] }).ToList(),
        Commands = (d.Commands ?? []).Select(c => c with { Code = c.Code?.Trim() ?? "", Name = c.Name?.Trim() ?? "" }).ToList(),
        Transitions = (d.Transitions ?? []).Select(t => t with
        {
            FromStateCode = t.FromStateCode?.Trim() ?? "",
            CommandCode = t.CommandCode?.Trim() ?? "",
            ToStateCode = t.ToStateCode?.Trim() ?? "",
            AllowedRoles = (t.AllowedRoles ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList(),
            GuardNames = t.GuardNames ?? [],
            EffectNames = t.EffectNames ?? [],
            PostActionNames = t.PostActionNames ?? [],
        }).ToList(),
    };

    public static IReadOnlyList<string> Validate(WorkflowDefinition d, IWorkflowComponentRegistry registry)
    {
        var errors = new List<string>();
        void CheckCode(string kind, string code)
        {
            if (code.Length == 0) errors.Add($"A {kind} code is empty.");
            else if (!CodePattern().IsMatch(code)) errors.Add($"{kind} code '{code}' may only contain letters, digits, '_', '-' and '.'.");
        }

        CheckCode("Workflow", d.Code);
        if (d.Name.Length == 0) errors.Add("Workflow name is required.");
        foreach (var s in d.States) CheckCode("State", s.Code);
        foreach (var c in d.Commands) CheckCode("Command", c.Code);
        foreach (var t in d.Transitions)
        {
            if (t.FromStateCode.Length == 0 || t.CommandCode.Length == 0 || t.ToStateCode.Length == 0)
                errors.Add("Every transition needs a from-state, a command and a to-state.");
        }

        // The engine validator assumes non-empty codes, so only run it on a clean definition.
        if (errors.Count == 0)
        {
            errors.AddRange(WorkflowDefinitionValidator.Validate(d));
            // Unregistered components and guards meant for another entity (see ForEntityAttribute).
            errors.AddRange(WorkflowComponentValidator.Validate(d, registry));
        }
        return errors.Distinct().ToList();
    }
}
