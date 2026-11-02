
namespace DungAT.WorkflowEngine.Abstractions.Models;

public enum WorkflowResultStatus
{
    Success,
    NotFound,
    AlreadyExists,
    InvalidTransition,
    EntityMismatch,
    ChainDepthExceeded,
    NotAllowed,
    GuardFailed,
    CommentRequired,
    ConcurrencyConflict,
}
