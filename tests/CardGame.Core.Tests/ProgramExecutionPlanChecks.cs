using CardGame.Core;

internal static class ProgramExecutionPlanChecks
{
    public static void ActiveAndTriggerUseStableInstructionPlans()
    {
        var resolver = new ProgramInstructionResolver();
        var program = Load();
        var active = resolver.Resolve(program, ProgramInstructionSourceKind.Activation, "draw");
        var triggered = resolver.Resolve(program, ProgramInstructionSourceKind.Trigger, "draw");
        Require(active.GetInstruction(0).Effect.Op == SkillProgramEffectOp.Draw &&
                triggered.GetInstruction(0).Effect.Op == SkillProgramEffectOp.Draw &&
                active.GetInstruction(0).Identity != triggered.GetInstruction(0).Identity,
            "The same binding name must not conflate active and trigger instructions.");
        Require(ReferenceEquals(active, resolver.Resolve(program, ProgramInstructionSourceKind.Activation, "draw")) &&
                !ReferenceEquals(active, resolver.Resolve(Load(), ProgramInstructionSourceKind.Activation, "draw")),
            "Plans must reuse the immutable definition reference without sharing state across equal definitions.");

        var frame = Frame(program);
        Require(ReferenceEquals(active, resolver.Resolve(frame, program)), "Active frame resolved another plan.");
        Require(ReferenceEquals(triggered, resolver.Resolve(frame with { TriggerId = "draw" }, program)),
            "A trigger frame must resolve its trigger even when its activation field is populated.");
        Require(active.GetPausedInstruction(1) == active.GetInstruction(0) &&
                active.GetPausedInstruction(2) == active.GetInstruction(1),
            "A committed cursor must locate the instruction that suspended without repeating its predecessor.");
        RequireThrows<NotSupportedException>(() =>
            ((IList<SkillProgramEffect>)active.Instructions)[0] = active.Instructions[1]);
        RequireThrows<InvalidOperationException>(() => active.GetInstruction(-1));
        RequireThrows<InvalidOperationException>(() => active.GetInstruction(2));
        RequireThrows<InvalidOperationException>(() => active.GetPausedInstruction(0));
        RequireThrows<InvalidOperationException>(() => active.GetPausedInstruction(3));
        RequireThrows<InvalidOperationException>(() => resolver.Resolve(frame with { GameplayHash = "changed" }, program));
        RequireThrows<InvalidOperationException>(() => resolver.Resolve(frame with { SkillId = "another" }, program));
        RequireThrows<ArgumentException>(() => resolver.Resolve(frame with { TriggerId = " " }, program));
        RequireThrows<InvalidOperationException>(() =>
            resolver.Resolve(program, ProgramInstructionSourceKind.Activation, "missing"));
        RequireThrows<InvalidOperationException>(() =>
            resolver.Resolve(program, ProgramInstructionSourceKind.Trigger, "legacy"));
    }

    public static void PlansFreezeInstructionsAndRejectAmbiguousBindings()
    {
        var loaded = Load();
        var effects = loaded.Activations.Single().Effects.ToArray();
        var first = effects[0];
        var plan = new ProgramExecutionPlan(loaded.Id, loaded.GameplayHash,
            ProgramInstructionSourceKind.Activation, "mutable-input", effects);
        effects[0] = effects[1];
        Require(ReferenceEquals(plan.GetInstruction(0).Effect, first),
            "Mutating the construction array must not rewrite a cached instruction.");

        var duplicate = new SkillProgram(loaded.Id, loaded.Revision, loaded.GameplayHash,
            loaded.RuntimeVersion, loaded.MinimumRulesVersion, loaded.Modifiers, loaded.ViewAs,
            [loaded.Activations.Single(), loaded.Activations.Single()], loaded.Triggers,
            loaded.Contributions, loaded.CardIdentities);
        RequireThrows<InvalidOperationException>(() =>
            new ProgramInstructionResolver().Resolve(duplicate, ProgramInstructionSourceKind.Activation, "draw"));
    }

    private static ProgramSkillFrame Frame(SkillProgram program) =>
        new(1, 0, program.Id, "draw", program.GameplayHash, 1, [], []);

    private static SkillProgram Load() => SkillProgramCatalog.Load(
        """
        {"schemaVersion":19,"skills":[{"id":"fixture:plan","revision":1,"minimumRulesVersion":124,
          "activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
            "targetKind":"otherLiving","usesPerTurn":1,"effects":[
              {"op":"draw","target":"owner","amount":1},
              {"op":"recover","target":"owner","amount":1}]}],
          "triggers":[
            {"id":"draw","window":"turnEnding","subject":"owner","optional":true,
             "effects":[{"op":"draw","target":"owner","amount":1}]},
            {"id":"legacy","window":"judgmentFinalized","subject":"owner",
             "suits":["club"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],
             "optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}
          ]}]}
        """,
        """{"schemaVersion":1,"skills":{"fixture:plan":{"name":"Plan","description":"Plan fixture"}}}""")
        .Programs["fixture:plan"];

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
