using CardGame.Core;

internal static class ProgramExecutionPlanChecks
{

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

        var trigger = loaded.Triggers[0];
        var duplicateTrigger = CopyTrigger(trigger.Id, [null!]);
        var duplicateError = RequireThrows<InvalidOperationException>(() =>
            new ProgramInstructionResolver().Resolve(WithTriggers(trigger, duplicateTrigger),
                ProgramInstructionSourceKind.Trigger, trigger.Id));
        Require(duplicateError.Message == $"Skill program '{loaded.Id}' contains duplicate binding '{trigger.Id}'.",
            "A duplicate trigger must fail before its malformed instructions are compiled.");
        var nullIdError = RequireThrows<ArgumentNullException>(() =>
            new ProgramInstructionResolver().Resolve(WithTriggers(CopyTrigger(null, trigger.Effects)),
                ProgramInstructionSourceKind.Trigger, trigger.Id));
        Require(nullIdError.ParamName == "bindingId",
            "A null trigger ID must retain the execution plan's original argument boundary.");

        SkillProgram WithTriggers(params SkillProgramTrigger[] triggers) => new(loaded.Id, loaded.Revision,
            loaded.GameplayHash, loaded.RuntimeVersion, loaded.MinimumRulesVersion, loaded.Modifiers,
            loaded.ViewAs, loaded.Activations, triggers, loaded.Contributions, loaded.CardIdentities);

        SkillProgramTrigger CopyTrigger(string? id, IReadOnlyList<SkillProgramEffect> instructions) => new(id!,
            trigger.Window, trigger.SourceSkillId, trigger.SourceViewAsId, trigger.Subject, trigger.Suits,
            trigger.MinimumRank, trigger.MaximumRank, trigger.ExcludedReasons, trigger.JudgmentReasons,
            trigger.JudgmentSource, trigger.CardKinds, trigger.SourceZones, trigger.MovementOccurrence,
            trigger.DamageOccurrence, trigger.DrawPhaseMode, trigger.Optional, trigger.Condition, instructions);
    }

    private static ProgramSkillFrame Frame(SkillProgram program) =>
        new(1, 0, program.Id, "draw", program.GameplayHash, 1, [], []);

    private static SkillProgram Load() => SkillProgramCatalog.Load(
        """
        {"schemaVersion":62,"skills":[{"id":"fixture:plan","revision":1,"minimumRulesVersion": 171,
          "activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
            "targetKind":"otherLiving","usesPerTurn":1,"effects":[
              {"op":"draw","target":"owner","amount":1},
              {"op":"recover","target":"owner","amount":1}]}],
          "triggers":[
            {"id":"draw","window":"turnEnding","subject":"owner","optional":true,
             "effects":[{"op":"draw","target":"owner","amount":1}]},
            {"id":"judgment-draw","window":"judgmentFinalized","subject":"owner",
             "suits":["club"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],
             "optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}
          ]}]}
        """,
        """{"schemaVersion":3,"skills":{"fixture:plan":{"name":"Plan","description":"Plan fixture"}}}""")
        .Programs["fixture:plan"];

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static TException RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
