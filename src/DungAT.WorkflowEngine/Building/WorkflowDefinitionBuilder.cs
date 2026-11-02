using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;


namespace DungAT.WorkflowEngine.Building;

/// <summary>Fluent builder: <c>Define(...).State(...).On("A").Command("X").GoTo("B")</c>.</summary>
public sealed class WorkflowDefinitionBuilder
{
    private readonly string _code;
    private readonly string _name;
    private string? _description;
    private string? _entityType;
    private readonly List<WorkflowState> _states = [];
    private readonly Dictionary<string, WorkflowCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TransitionBuilder> _transitions = [];
    private readonly Dictionary<string, List<string>> _enterActions = new(StringComparer.OrdinalIgnoreCase);

    internal List<Type> ServiceTypes { get; } = [];

    private WorkflowDefinitionBuilder(string code, string name) => (_code, _name) = (code, name);

    public static WorkflowDefinitionBuilder Define(string code, string name) => new(code, name);

    public WorkflowDefinitionBuilder Describe(string description) { _description = description; return this; }
    public WorkflowDefinitionBuilder ForEntity(string entityType) { _entityType = entityType; return this; }

    public WorkflowDefinitionBuilder State(string code, WorkflowStateType type = WorkflowStateType.Intermediate, string? name = null)
    {
        _states.Add(new WorkflowState(code, name ?? code, type, _states.Count));
        return this;
    }

    public WorkflowDefinitionBuilder Command(string code, string? name = null, string? description = null)
    {
        _commands[code] = new WorkflowCommand(code, name ?? code, description);
        return this;
    }

    public TransitionFromBuilder On(string stateCode) => new(this, stateCode);

    /// <summary>Post actions that run whenever an instance enters <paramref name="stateCode"/>, whichever command got it there.</summary>
    public StateEnterBuilder OnEnter(string stateCode) => new(this, stateCode);

    public WorkflowDefinition Build()
    {
        foreach (var t in _transitions)
            if (!_commands.ContainsKey(t.CommandCode)) _commands[t.CommandCode] = new WorkflowCommand(t.CommandCode, t.CommandCode);

        var definition = new WorkflowDefinition(_code, _name)
        {
            Description = _description,
            EntityType = _entityType,
            States = _states.Select(s => _enterActions.TryGetValue(s.Code, out var names) ? s with { PostActionNames = names.ToList() } : s).ToList(),
            Commands = _commands.Values.ToList(),
            Transitions = _transitions.Select(t => t.ToTransition()).ToList(),
        };
        var errors = WorkflowDefinitionValidator.Validate(definition).ToList();
        errors.AddRange(_enterActions.Keys.Where(k => definition.FindState(k) is null).Select(k => $"OnEnter references unknown state '{k}'."));
        if (errors.Count > 0) throw new WorkflowDefinitionException(errors);
        return definition;
    }

    public sealed class StateEnterBuilder
    {
        private readonly WorkflowDefinitionBuilder _owner;
        private readonly string _state;
        internal StateEnterBuilder(WorkflowDefinitionBuilder owner, string state) => (_owner, _state) = (owner, state);
        internal WorkflowDefinitionBuilder Owner => _owner;

        public StateEnterBuilder PostAction(string name)
        {
            if (!_owner._enterActions.TryGetValue(_state, out var list)) _owner._enterActions[_state] = list = [];
            list.Add(name);
            return this;
        }

        public StateEnterBuilder PostAction<T>() where T : class, IWorkflowPostAction
        {
            _owner.ServiceTypes.Add(typeof(T));
            return PostAction(typeof(T).Name);
        }

        // Continue the chain on the owning definition.
        public TransitionFromBuilder On(string stateCode) => _owner.On(stateCode);
        public StateEnterBuilder OnEnter(string stateCode) => _owner.OnEnter(stateCode);
        public WorkflowDefinitionBuilder State(string code, WorkflowStateType type = WorkflowStateType.Intermediate, string? name = null) =>
            _owner.State(code, type, name);
        public WorkflowDefinition Build() => _owner.Build();
    }

    public sealed class TransitionFromBuilder
    {
        private readonly WorkflowDefinitionBuilder _owner;
        private readonly string _from;
        internal TransitionFromBuilder(WorkflowDefinitionBuilder owner, string from) => (_owner, _from) = (owner, from);

        public TransitionToBuilder Command(string commandCode) => new(_owner, _from, commandCode);
    }

    public sealed class TransitionToBuilder
    {
        private readonly WorkflowDefinitionBuilder _owner;
        private readonly string _from;
        private readonly string _command;
        internal TransitionToBuilder(WorkflowDefinitionBuilder owner, string from, string command) =>
            (_owner, _from, _command) = (owner, from, command);

        public TransitionBuilder GoTo(string toStateCode)
        {
            var t = new TransitionBuilder(_owner, _from, _command, toStateCode);
            _owner._transitions.Add(t);
            return t;
        }
    }

    public sealed class TransitionBuilder
    {
        private readonly WorkflowDefinitionBuilder _owner;
        private readonly string _from;
        private readonly string _to;
        private readonly List<string> _roles = [];
        private readonly List<string> _guards = [];
        private readonly List<string> _effects = [];
        private readonly List<string> _postActions = [];
        private bool _requireComment;

        internal string CommandCode { get; }
        internal WorkflowDefinitionBuilder Owner => _owner;

        internal TransitionBuilder(WorkflowDefinitionBuilder owner, string from, string command, string to) =>
            (_owner, _from, CommandCode, _to) = (owner, from, command, to);

        public TransitionBuilder AllowRoles(params string[] roles) { _roles.AddRange(roles); return this; }
        public TransitionBuilder RequireComment() { _requireComment = true; return this; }
        public TransitionBuilder Guard(string name) { _guards.Add(name); return this; }
        public TransitionBuilder Effect(string name) { _effects.Add(name); return this; }

        /// <summary>Runs after this transition is saved; cannot undo it. See <c>IWorkflowPostAction</c>.</summary>
        public TransitionBuilder PostAction(string name) { _postActions.Add(name); return this; }

        public TransitionBuilder PostAction<T>() where T : class, IWorkflowPostAction
        {
            _owner.ServiceTypes.Add(typeof(T));
            return PostAction(typeof(T).Name);
        }

        public TransitionBuilder Guard<T>() where T : class, IWorkflowGuard
        {
            _owner.ServiceTypes.Add(typeof(T));
            return Guard(typeof(T).Name);
        }

        public TransitionBuilder Effect<T>() where T : class, IWorkflowEffect
        {
            _owner.ServiceTypes.Add(typeof(T));
            return Effect(typeof(T).Name);
        }

        // Continue the chain on the owning definition.
        public TransitionFromBuilder On(string stateCode) => _owner.On(stateCode);
        public StateEnterBuilder OnEnter(string stateCode) => _owner.OnEnter(stateCode);
        public WorkflowDefinitionBuilder State(string code, WorkflowStateType type = WorkflowStateType.Intermediate, string? name = null) =>
            _owner.State(code, type, name);
        public WorkflowDefinition Build() => _owner.Build();

        internal WorkflowTransition ToTransition() => new(_from, CommandCode, _to)
        {
            AllowedRoles = _roles.ToList(),
            GuardNames = _guards.ToList(),
            EffectNames = _effects.ToList(),
            PostActionNames = _postActions.ToList(),
            RequireComment = _requireComment,
        };
    }
}
