using System.Reflection;
using CardGame.Core;

internal static class ProgramLegalityChecks
{
    public static void RegistrationAndDispatchPreserveBoundaries()
    {
        var methods = Methods(typeof(Host));
        var catalog = ProgramLegalityCatalog.Discover(methods);
        var reversed = ProgramLegalityCatalog.Discover(methods.Reverse());
        foreach (var phase in Enum.GetValues<ProgramActivationLegalityPhase>())
            Require(catalog.Activations(phase).Select(gate => gate.Method)
                .SequenceEqual(reversed.Activations(phase).Select(gate => gate.Method)),
                "Activation ordering must be independent of reflection enumeration.");
        foreach (var phase in Enum.GetValues<ProgramTriggerLegalityPhase>())
            Require(catalog.Triggers(phase).Select(gate => gate.Method)
                .SequenceEqual(reversed.Triggers(phase).Select(gate => gate.Method)),
                "Trigger ordering must be independent of reflection enumeration.");
        // Validate every production signature and precedence, including gates
        // whose content is absent from this small dispatch fixture.
        ProgramLegalityCatalog.Discover(Methods(typeof(GameEngine)));

        foreach (var names in new[]
                 {
                     new[] { "DuplicateA", "DuplicateB" }, new[] { "WrongReturn" },
                     new[] { "AmbiguousStrings" }, new[] { "UnknownOperation" },
                     new[] { "NegativeOrder" }, new[] { "UnknownPhase" },
                     new[] { "Generic" }, new[] { "WindowDuringCollection" }
                 })
            Reject(() => ProgramLegalityCatalog.Discover(names.Select(name =>
                typeof(InvalidHost).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!)));

        var program = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"fixture:legality","revision":1,
               "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},
               "activations":[{"id":"activate","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":1,
                 "targetKind":"otherLiving","effects":[{"op":"draw","target":"owner","amount":1}]}],
               "triggers":[{"id":"trigger","window":"turnEnding","subject":"owner","optional":true,
                 "effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """, $$"""
            {"schemaVersion":{{SkillProgramCatalog.PresentationSchemaVersion}},
             "skills":{
               "fixture:legality":{"name":"Legality","description":"Legality fixture"}
             }
            }
            """).Programs["fixture:legality"];
        var owner = new CharacterState
        {
            Seat = 2, Name = "fixture", IsHuman = true, Role = Role.Rebel,
            RoleRevealed = false, General = new("fixture", "Fixture", "fixture", []),
            GeneralSelected = true, GeneralRevealed = true, MaxHp = 4, Hp = 3
        };
        var playerContext = new PlayerSkillContext(owner.Seat, 3, 4, 2, TurnPhase.Play);
        var activationPlan = ProgramInstructionResolver.Default.Resolve(program,
            ProgramInstructionSourceKind.Activation, "activate");
        var activation = new ProgramActivationLegalityContext(owner, program, activationPlan, playerContext);
        var triggerPlan = ProgramInstructionResolver.Default.Resolve(program,
            ProgramInstructionSourceKind.Trigger, "trigger");
        var candidate = new ProgramTriggerCandidate(owner.Seat, program.Id, "trigger", "instance", program.GameplayHash, 0);
        var window = new ProgramSkillWindowContext(SkillProgramTriggerWindow.TurnEnding, 99, owner.Seat);
        var trigger = new ProgramTriggerLegalityContext(owner, program, triggerPlan, candidate, window);
        var host = new Host { ExpectedActivation = activation, ExpectedTrigger = trigger };
        var router = new ProgramLegalityRouter(catalog, host);

        Require(router.CanActivate(ProgramActivationLegalityPhase.BeforeOwnerFacts, activation) &&
                host.Calls.SequenceEqual(["activation:10", "activation:20"]),
            "Matching gates must run in explicit order and skip unrelated operations.");
        host.Calls.Clear();
        host.Stop = true;
        Require(!router.CanActivate(ProgramActivationLegalityPhase.BeforeOwnerFacts, activation) &&
                host.Calls.SequenceEqual(["activation:10"]),
            "An activation veto must short-circuit later handlers.");
        host.Stop = false;
        host.Calls.Clear();
        Require(router.CanTrigger(ProgramTriggerLegalityPhase.BeforeCondition, trigger) &&
                host.Calls.SequenceEqual(["trigger:10", "trigger:20"]),
            "Trigger adapters must preserve the original participants, window, and plan.");
        host.Calls.Clear();
        host.Stop = true;
        Require(!router.CanTrigger(ProgramTriggerLegalityPhase.BeforeCondition, trigger) &&
                host.Calls.SequenceEqual(["trigger:10"]),
            "A trigger veto must short-circuit later handlers.");
        host.Calls.Clear();
        Require(router.CanTrigger(ProgramTriggerLegalityPhase.Collection, trigger with { WindowContext = null }) &&
                host.Calls.SequenceEqual(["collection"]),
            "Early candidate collection must not require a running window.");
        try
        {
            router.CanTrigger(ProgramTriggerLegalityPhase.AfterCondition, trigger);
            throw new Exception("The original legality failure was swallowed.");
        }
        catch (InvalidOperationException error)
        {
            Require(ReferenceEquals(error, Host.Failure), "Legality failures must remain unwrapped.");
        }
        var otherHost = new Host { ExpectedActivation = activation, ExpectedTrigger = trigger };
        Require(new ProgramLegalityRouter(catalog, otherHost)
                    .CanActivate(ProgramActivationLegalityPhase.BeforeOwnerFacts, activation) &&
                otherHost.Calls.SequenceEqual(["activation:10", "activation:20"]) &&
                host.Calls.SequenceEqual(["collection"]),
            "Shared registration metadata must never capture a different game session.");
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid program legality registration was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Host
    {
        internal static readonly InvalidOperationException Failure = new("original legality failure");
        internal readonly List<string> Calls = [];
        internal bool Stop;
        internal ProgramActivationLegalityContext ExpectedActivation;
        internal ProgramTriggerLegalityContext ExpectedTrigger;

        [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 5, SkillProgramEffectOp.Recover)]
        private static bool UnrelatedActivation(ProgramActivationLegalityContext gate) =>
            throw new Exception("Unrelated activation inspected runtime state.");

        [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 20, SkillProgramEffectOp.Draw)]
        private bool ActivationSecond(CharacterState owner, SkillProgramActivation activation,
            ProgramExecutionPlan plan, SkillProgram program, ProgramInstructionFeatures features,
            PlayerSkillContext context, int seat, string skillId)
        {
            Require(ReferenceEquals(owner, ExpectedActivation.Owner) && ReferenceEquals(program, ExpectedActivation.Program) &&
                    ReferenceEquals(activation, plan.Activation) && ReferenceEquals(plan, ExpectedActivation.Plan) &&
                    ReferenceEquals(features, plan.Features) && ReferenceEquals(context, ExpectedActivation.Context) &&
                    seat == owner.Seat && skillId == program.Id, "Activation projection changed its inputs.");
            Calls.Add("activation:20");
            return true;
        }

        [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 10)]
        private bool ActivationFirst(ProgramActivationLegalityContext gate)
        { Require(gate == ExpectedActivation, "Activation context changed."); Calls.Add("activation:10"); return !Stop; }

        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 5, SkillProgramEffectOp.Recover)]
        private static bool UnrelatedTrigger(ProgramTriggerLegalityContext gate) =>
            throw new Exception("Unrelated trigger inspected runtime state.");

        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 10)]
        private bool TriggerFirst(ProgramTriggerLegalityContext gate)
        { Require(gate == ExpectedTrigger, "Trigger context changed."); Calls.Add("trigger:10"); return !Stop; }

        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 20, SkillProgramEffectOp.Draw)]
        private bool TriggerSecond(CharacterState owner, SkillProgramTrigger trigger, ProgramTriggerCandidate candidate,
            ProgramSkillWindowContext context, ProgramExecutionPlan plan, SkillProgram program,
            ProgramInstructionFeatures features, int seat, string skillId)
        {
            Require(ReferenceEquals(owner, ExpectedTrigger.Owner) && ReferenceEquals(trigger, plan.Trigger) &&
                    ReferenceEquals(candidate, ExpectedTrigger.Candidate) && ReferenceEquals(context, ExpectedTrigger.Context) &&
                    ReferenceEquals(program, ExpectedTrigger.Program) && ReferenceEquals(plan, ExpectedTrigger.Plan) &&
                    ReferenceEquals(features, plan.Features) && seat == candidate.OwnerSeat && skillId == candidate.SkillId,
                "Trigger projection changed its inputs.");
            Calls.Add("trigger:20");
            return true;
        }

        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 10)]
        private bool Collect(int seat, SkillProgramTrigger trigger)
        { Require(seat == ExpectedTrigger.Candidate.OwnerSeat && ReferenceEquals(trigger, ExpectedTrigger.Trigger),
            "Collection projection changed its inputs."); Calls.Add("collection"); return true; }

        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.AfterCondition, 10)]
        private static bool Fail(ProgramTriggerLegalityContext gate) => throw Failure;
    }

    private static class InvalidHost
    {
        [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 10)]
        private static bool DuplicateA(ProgramActivationLegalityContext context) => true;
        [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 10)]
        private static bool DuplicateB(ProgramActivationLegalityContext context) => true;
        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 10)]
        private static void WrongReturn(ProgramTriggerLegalityContext context) { }
        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 10)]
        private static bool AmbiguousStrings(string skillId, string skillInstanceId) => true;
        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 10, (SkillProgramEffectOp)int.MaxValue)]
        private static bool UnknownOperation(ProgramTriggerLegalityContext context) => true;
        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, -1)]
        private static bool NegativeOrder(ProgramTriggerLegalityContext context) => true;
        [ProgramTriggerLegality((ProgramTriggerLegalityPhase)int.MaxValue, 10)]
        private static bool UnknownPhase(ProgramTriggerLegalityContext context) => true;
        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 10)]
        private static bool Generic<T>(ProgramTriggerLegalityContext context) => true;
        [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 10)]
        private static bool WindowDuringCollection(ProgramSkillWindowContext context) => true;
    }
}
