using CardGame.Core;

internal static class ProgramFactRequirementChecks
{
    public static void CatalogDemandPreservesEntrySourcesAndIsolation()
    {
        var empty = Dependencies();
        Require(empty.RequiredFacts == ProgramTrackedFact.None &&
                !empty.RequiresFact(ProgramTrackedFact.NativeLogicalDiscard),
            "An unrelated catalog must not collect extra native facts.");

        var triggerOnly = Program(SkillProgramEffectOp.InitializeGuYinInitialEntities, activation: false);
        var activationOnly = Program(SkillProgramEffectOp.ResolveMouXiahouDunGanglie, activation: true);
        var phaseOnly = Program(SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse, activation: false);
        Require(Dependencies(triggerOnly).RequiredFacts ==
                (ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry),
            "A catalog skill without any general owner must retain its trigger fact requirements.");
        Require(Dependencies(activationOnly).RequiredFacts ==
                (ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase |
                 ProgramTrackedFact.NativeTrueUseProcessingEntry),
            "Activation-only catalog requirements must include each requested native fact.");
        Require(Dependencies(phaseOnly).RequiredFacts == ProgramTrackedFact.ActualDiscardRecoveryPhase,
            "A phase demand must not silently enable discard invoices or native processing facts.");
        Require(Dependencies(Program(SkillProgramEffectOp.InitializeGuYinInitialEntities, activation: true))
                    .RequiredFacts == ProgramTrackedFact.None &&
                Dependencies(Program(SkillProgramEffectOp.ResolveMouXiahouDunGanglie, activation: false))
                    .RequiredFacts == ProgramTrackedFact.None,
            "Moving an operation to another entry source must preserve the historical opt-in boundary.");

        var wholeUses = Dependencies(Program(SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao, activation: false));
        var wrongEntryWholeUses = Dependencies(Program(SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao, activation: true));
        Require(wholeUses.RequiredFacts == ProgramTrackedFact.WholeActualUseDeclaration &&
                wholeUses.TracksWholeActualTurnUseOrdinal && wholeUses.TracksCurrentTurnUseKinds &&
                wrongEntryWholeUses.RequiredFacts == ProgramTrackedFact.None && !wrongEntryWholeUses.TracksWholeActualTurnUseOrdinal &&
                !empty.TracksWholeActualTurnUseOrdinal && !empty.TracksCurrentTurnUseKinds,
            "A triggered whole-use declaration demand enables the existing native ledger, while another entry source or catalog remains isolated.");

        var passive = new SkillProgramDependencies([new ContentSkillDefinition("mou-ol:yangwei", "扬威", "Passive native facts")]);
        var unrelatedPassive = new SkillProgramDependencies([new ContentSkillDefinition("fixture:unrelated-yangwei", "扬威", "No registered demand")]);
        Require(passive.RequiredFacts == (ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.NativeLogicalDiscard |
                    ProgramTrackedFact.ActualDiscardRecoveryPhase) && unrelatedPassive.RequiredFacts == ProgramTrackedFact.None &&
                empty.RequiredFacts == ProgramTrackedFact.None,
            "A loaded passive skill has its own native fact demand without an operation, while another identity or catalog stays isolated.");

        var combined = Dependencies(triggerOnly, activationOnly, phaseOnly);
        Require(combined.RequiredFacts == Dependencies(activationOnly).RequiredFacts &&
                empty.RequiredFacts == ProgramTrackedFact.None,
            "Requirements must union across the entire catalog without contaminating other catalogs.");
        var effect = activationOnly.Activations[0].Effects[0];
        var features = new ProgramInstructionFeatures([effect, effect]);
        Require(features.FactRequirements == ProgramOperationCatalog.Default.Resolve(effect.Op).FactRequirements,
            "Repeated instructions must retain immutable source-specific demand without multiplying facts.");

        Require(ProgramOperationCatalog.Default.FactRequirementsFor(SkillProgramEffectOp.UseVirtualDuel) == default,
            "Legacy operations without a descriptor must not acquire native fact requirements.");
        var legacyRejected = false;
        try { _ = ProgramOperationCatalog.Default.Resolve(SkillProgramEffectOp.UseVirtualDuel); }
        catch (InvalidOperationException) { legacyRejected = true; }
        Require(legacyRejected,
            "A safe fact metadata query must not make a legacy operation a supported JSON node.");

        foreach (var invalid in new[]
                 {
                     new ProgramFactRequirements(Triggered: (ProgramTrackedFact)32),
                     new ProgramFactRequirements(Activated: (ProgramTrackedFact)64)
                 })
        {
            var rejected = false;
            try { _ = new ProgramOperationCatalog([new InvalidDemandDescriptor(invalid)]); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Unknown native fact flags must be rejected during descriptor discovery.");
        }
    }

    private static SkillProgramDependencies Dependencies(params SkillProgram[] programs) =>
        new(programs.Select((program, index) =>
            new ContentSkillDefinition($"fixture:demand:{index}", "Demand", "Unowned fact demand") { Program = program }));

    // Construct definition metadata directly so this check can exercise an
    // operation at the opposite entry source without weakening JSON contracts.
    private static SkillProgram Program(SkillProgramEffectOp operation, bool activation)
    {
        var condition = new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []);
        var effect = new SkillProgramEffect(operation, SkillProgramEffectTarget.Owner, 0, condition);
        IReadOnlyList<SkillProgramActivation> activations = activation
            ? [new("activate", 0, 0, 0, 0, SkillProgramTargetKind.AnyLiving, null, condition, [effect])]
            : [];
        IReadOnlyList<SkillProgramTrigger> triggers = activation ? [] :
        [new("trigger", SkillProgramTriggerWindow.TurnEnding, null, null, SkillProgramTriggerSubject.Owner,
            [], 1, 13, [], [], null, [], [], null, null, SkillProgramDrawPhaseMode.Additive, false,
            new(SkillProgramTriggerConditionKind.Always, []), [effect])];
        return new("fixture:demand", 1, "fixture-hash", SkillProgramCatalog.RuntimeVersion, 0,
            [], [], activations, triggers, [], []);
    }

    private sealed class InvalidDemandDescriptor(ProgramFactRequirements requirements) : ProgramOperationDescriptorBase
    {
        private readonly DrawProgramOperationDescriptor _draw = new();
        public override SkillProgramEffectOp Op => _draw.Op;
        public override ISkillProgramEffectHandler Handler => _draw.Handler;
        public override ProgramOperationAiPolicy AiPolicy => _draw.AiPolicy;
        public override ProgramFactRequirements FactRequirements => requirements;
        public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => _draw.Parse(reader);
        public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => _draw.Resources(effect);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
