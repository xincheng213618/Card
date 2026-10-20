using System.Reflection;
using CardGame.Core;

internal static class ProgramModuleContractChecks
{
    public static void CatalogValidationKeepsOrderAndFailureIdentity()
    {
        var a = Program("a");
        var b = Program("b");
        IReadOnlyDictionary<string, SkillProgram> programs = new Dictionary<string, SkillProgram> { [b.Id] = b, [a.Id] = a };
        var catalog = ProgramCatalogValidations.Discover(Methods(typeof(Validators)).Reverse());
        Validators.Expected = programs;
        Validators.Trace.Clear();
        catalog.Validate(programs);
        Require(Validators.Trace.SequenceEqual(["catalog", "first:b", "first:a", "last:b", "last:a"]),
            "Catalog validators must retain validator-first order and the caller's program order.");
        Require(catalog.Registrations is IList<ProgramCatalogValidationRegistration> { IsReadOnly: true },
            "Shared validation metadata must be frozen.");

        Validators.Trace.Clear();
        var failure = new InvalidOperationException("module contract failure");
        Validators.Failure = failure;
        try { catalog.Validate(programs); throw new InvalidOperationException("Expected the module failure."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
        finally { Validators.Failure = null; Validators.Expected = null; }
        Require(Validators.Trace.SequenceEqual(["catalog", "first:b"]),
            "The original failure must propagate without invoking later programs or contracts.");

        Reject(typeof(Duplicate), "Duplicate");
        Reject(typeof(Invalid), "signature");
        Reject(typeof(Instance), "signature");
        Reject(typeof(Optional), "signature");
        static void Reject(Type type, string fragment)
        {
            try { _ = ProgramCatalogValidations.Discover(Methods(type)); }
            catch (InvalidOperationException exception) when (exception.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException($"Invalid module validation {type.Name} was accepted.");
        }
    }

    public static void DeadTargetPolicyKeepsReceiptAndHostBoundaries()
    {
        var frame = new ProgramSkillFrame(1, 0, "fixture", "run", "hash", 0, [], []);
        var host = new PermissionHost();
        bool Allows(SkillProgramEffectOp op, ProgramSkillFrame? current = null, bool paid = false)
        {
            var effect = new SkillProgramEffect(op, SkillProgramEffectTarget.Owner, 0, new(SkillProgramConditionKind.Always, 0, []));
            return ProgramOperationCatalog.Default.Resolve(op).AllowsDeadTarget(new(effect, current ?? frame, host, paid));
        }
        Require(!Allows(SkillProgramEffectOp.Draw) && !Allows(SkillProgramEffectOp.ResolveMouZhouYuYingzi) && host.Queries == 0,
            "Ordinary and unrelated sibling operations must retain living-target requirements.");
        var legacy = new SkillProgramEffect(SkillProgramEffectOp.UseVirtualDuel, SkillProgramEffectTarget.Owner, 0, new(SkillProgramConditionKind.Always, 0, []));
        Require(!ProgramOperationCatalog.Default.AllowsDeadTarget(new(legacy, frame, host, false)),
            "An unregistered legacy operation must retain its denied dead-target permission.");
        Require(Allows(SkillProgramEffectOp.SelectTarget) && host.Queries == 0,
            "The existing death-flow target selector does not require a second host permission query.");
        Require(!Allows(SkillProgramEffectOp.ResolveMouZhouYuFanjian) && host.Queries == 1,
            "A paid continuation operation must still ask its current host.");
        host.Allow = true;
        Require(Allows(SkillProgramEffectOp.ResolveMouZhouYuFanjian) && host.Queries == 2 && ReferenceEquals(host.LastFrame, frame),
            "The current frame and per-match permission must reach the owning operation.");
        Require(!Allows(SkillProgramEffectOp.ClearLiushiEffect) && !Allows(SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw, paid: true),
            "Host approval and a previously accepted return cannot replace an absent payment receipt.");
        Require(!Allows(SkillProgramEffectOp.DiscardDeathSourceCards) &&
                Allows(SkillProgramEffectOp.DiscardDeathSourceCards, frame with { WindowContext = new(SkillProgramTriggerWindow.OwnerDied, 9, 0) }),
            "A death-source operation must retain its original owner-death window restriction.");
        host.Failure = new InvalidOperationException("permission failure");
        try { _ = Allows(SkillProgramEffectOp.ResolveMouZhouYuFanjian); throw new InvalidOperationException("Expected host failure."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, host.Failure)) { }
    }

    private static SkillProgram Program(string id) => new(id, 1, "fixture", "fixture", 0, [], [], [], [], [], []);
    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static class Validators
    {
        internal static readonly List<string> Trace = [];
        internal static IReadOnlyDictionary<string, SkillProgram>? Expected;
        internal static Exception? Failure;
        [ProgramCatalogValidation(10)]
        private static void Catalog(IReadOnlyDictionary<string, SkillProgram> programs)
        { Require(ReferenceEquals(programs, Expected), "Catalog identity changed."); Trace.Add("catalog"); }
        [ProgramCatalogValidation(20)]
        private static void First(string path, SkillProgram program)
        { Require(path == program.Id, "Program path changed."); Trace.Add("first:" + path); if (Failure is { } failure) throw failure; }
        [ProgramCatalogValidation(30)]
        private static void Last(string path, SkillProgram program) => Trace.Add("last:" + path);
    }
    private static class Duplicate
    {
        [ProgramCatalogValidation(1)] private static void A(string path, SkillProgram program) { }
        [ProgramCatalogValidation(1)] private static void B(string path, SkillProgram program) { }
    }
    private static class Invalid { [ProgramCatalogValidation(0)] private static void A(string path, SkillProgram program) { } }
    private sealed class Instance { [ProgramCatalogValidation(1)] private void A(string path, SkillProgram program) { } }
    private static class Optional { [ProgramCatalogValidation(1)] private static void A(string path, SkillProgram? program = null) { } }
    private sealed class PermissionHost : ISkillProgramExecutionHost
    {
        internal bool Allow;
        internal int Queries;
        internal ProgramSkillFrame? LastFrame;
        internal Exception? Failure;
        public bool CanContinueAfterOwnerDeath(ProgramSkillFrame frame)
        { Queries++; LastFrame = frame; if (Failure is { } failure) throw failure; return Allow; }
        public bool IsGameOver => false;
        public ProgramSkillFrame? GetActiveFrame(long id) => throw new NotSupportedException();
        public SkillProgram GetProgram(string id) => throw new NotSupportedException();
        public SkillProgramActorState GetActor(int seat) => throw new NotSupportedException();
        public bool OwnsSkillInstance(int seat, string skill, string instance) => throw new NotSupportedException();
        public bool OwnsCards(int seat, IReadOnlyList<int> cards, IReadOnlyList<CardZoneKind> zones) => throw new NotSupportedException();
        public bool EvaluateCondition(ProgramSkillFrame frame, SkillProgramCondition condition, PlayerSkillContext context) => throw new NotSupportedException();
        public void UpdateFrame(ProgramSkillFrame frame) => throw new NotSupportedException();
        public void Complete(ProgramSkillFrame frame, bool completed, string? reason = null) => throw new NotSupportedException();
    }
}
