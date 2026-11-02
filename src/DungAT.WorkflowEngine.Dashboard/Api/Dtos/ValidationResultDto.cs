namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record ValidationResultDto(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
