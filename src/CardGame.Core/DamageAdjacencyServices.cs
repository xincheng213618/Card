namespace CardGame.Core;

internal interface IDamageAdjacencyFacts : IProgramSelectionQuery
{
    int FrameCount { get; }
    ResolutionFrame FrameAt(int index);
    int FrameIndex(long id);
    DamageTriggerWindowFrame? ActiveDamageTrigger { get; }
    DyingFrame? ActiveDying { get; }
    Winner Winner { get; }
    SkillProgram? Program(string skillId);
}

// These are neighboring mechanism proofs; this module owns the traversal and
// their short-circuit order, while each proof keeps its original paid receipt.
internal interface IDamageAdjacencyProtocols : IProgramRuntimeOperations
{
    bool RequestedDeckBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool DrawFundedDistinctBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool ChainedStateBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool ProvenanceClaimTurnedOverEdge(ResolutionFrame child, ResolutionFrame parent);
    bool HulaoActivityFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool ShenwuTrickFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool HellSoulFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool WenheChaosFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool RecoveryReplacementFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool PileEquipmentFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool RandomEquipmentFrameRidesOn(ResolutionFrame child, ResolutionFrame parent);
    bool RecoveryReplacementDamageObserverRidesOn(int index);
    bool CharacterTurnedOverDamageObserverRidesOn(int index);
    bool IsWenhePoisonHealthChild(ResolutionFrame parent, HpChangedTriggerWindowFrame child);
    bool IsWenhePoisonDyingChild(ResolutionFrame parent, DyingFrame child);
    bool IsPaidCardUseMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame movement);
    bool IsWenyiDamageHealthReturn(DamageFrame damage, HpChangedTriggerWindowFrame health);
    bool IsAmountSupportHealthChild(ResolutionFrame parent, HpChangedTriggerWindowFrame health);
    TypedRecoveryDamageCursorProjection ProjectTypedRecoveryDamageCursor(long windowId);
    ProgramSkillFrame? MouLixianDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId);
    ProgramSkillFrame? AlcoholSlashReplacementDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId);
    ProgramSkillFrame? RandomDamageTargetSkillDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId);
    ProgramSkillFrame? DamageTargetSkillRoundDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId);
    bool MatchesObserver(ProgramObserverProofPhase phase, long windowId);
    bool HasPaidFactionRequestCostRecovery(PaidFactionRequestCostRecovery receipt);
}

internal interface IDamageWindowOperations : IProgramRuntimeOperations
{
    DamageTriggerWindowFrame SetWindowCursor(long windowId, int candidateIndex);
    void SetWindowStep(long windowId, ResolutionFrameStep step);
    IDamageTriggerCapturedAttack CaptureAttack(DamageTriggerWindowFrame window);
}

// Ephemeral views preserve the original attack lookup before SetStep and the
// target lookup after it. They expose only this completion, with no player or
// attack state object available to the independent runtime or its fields.
internal interface IDamageTriggerCapturedAttack : IProgramRuntimeOperations
{
    IDamageTriggerCompletion BindTarget();
}

internal interface IDamageTriggerCompletion : IProgramRuntimeOperations
{
    int Hp { get; }
    bool IsAlive { get; }
    int SourceSeat { get; }
    int TargetSeat { get; }
    int DamageAmount { get; }
    DamageNature Nature { get; }
    bool IsSourceLess { get; }
    bool HasResolvedDying(long damageFrameId);
    void BeginDying(long damageFrameId);
    bool TryBeginAfterDamage(long damageFrameId);
    void CompleteAttack();
}

public sealed partial class GameEngine
{
    private DamageAdjacencyRuntime DamageAdjacency => GetRuntimeModule<DamageAdjacencyRuntime>();

    [ProgramCapability]
    private sealed class DamageAdjacencyFacts(GameEngine engine) : IDamageAdjacencyFacts
    {
        public int FrameCount => engine._resolutionStack.Count;
        public ResolutionFrame FrameAt(int index) => engine._resolutionStack[index];
        public int FrameIndex(long id) => engine._resolutionStack.FindIndex(frame => frame.Id == id);
        public DamageTriggerWindowFrame? ActiveDamageTrigger => engine.ActiveDamageTrigger;
        public DyingFrame? ActiveDying => engine.ActiveDying;
        public Winner Winner => engine._winner;
        public SkillProgram? Program(string skillId) => engine._contentRegistry.Skills.TryGetValue(skillId, out var skill)
            ? skill.Program : null;
    }

    [ProgramCapability]
    private sealed class DamageAdjacencyProtocols(GameEngine engine) : IDamageAdjacencyProtocols
    {
        public bool RequestedDeckBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.RequestedDeckBasicFrameRidesOn(child, parent);
        public bool DrawFundedDistinctBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.DrawFundedDistinctBasicFrameRidesOn(child, parent);
        public bool ChainedStateBasicFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.ChainedStateBasicFrameRidesOn(child, parent);
        public bool ProvenanceClaimTurnedOverEdge(ResolutionFrame child, ResolutionFrame parent) =>
            engine.ProvenanceClaimTurnedOverEdge(child, parent);
        public bool HulaoActivityFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.HulaoActivityFrameRidesOn(child, parent);
        public bool ShenwuTrickFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.ShenwuTrickFrameRidesOn(child, parent);
        public bool HellSoulFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.HellSoulFrameRidesOn(child, parent);
        public bool WenheChaosFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.WenheChaosFrameRidesOn(child, parent);
        public bool RecoveryReplacementFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.RecoveryReplacementFrameRidesOn(child, parent);
        public bool PileEquipmentFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.PileEquipmentFrameRidesOn(child, parent);
        public bool RandomEquipmentFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
            engine.RandomEquipmentFrameRidesOn(child, parent);
        public bool RecoveryReplacementDamageObserverRidesOn(int index) =>
            engine.RecoveryReplacementDamageObserverRidesOn(index);
        public bool CharacterTurnedOverDamageObserverRidesOn(int index) =>
            engine.CharacterTurnedOverDamageObserverRidesOn(index);
        public bool IsWenhePoisonHealthChild(ResolutionFrame parent, HpChangedTriggerWindowFrame child) =>
            engine.IsWenhePoisonHealthChild(parent, child);
        public bool IsWenhePoisonDyingChild(ResolutionFrame parent, DyingFrame child) =>
            engine.IsWenhePoisonDyingChild(parent, child);
        public bool IsPaidCardUseMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame movement) =>
            engine.IsPaidCardUseMovementReturn(use, movement);
        public bool IsWenyiDamageHealthReturn(DamageFrame damage, HpChangedTriggerWindowFrame health) =>
            engine.IsWenyiDamageHealthReturn(damage, health);
        public bool IsAmountSupportHealthChild(ResolutionFrame parent, HpChangedTriggerWindowFrame health) =>
            engine.IsAmountSupportHealthChild(parent, health);
        public TypedRecoveryDamageCursorProjection ProjectTypedRecoveryDamageCursor(long windowId) =>
            engine.ProjectTypedRecoveryDamageCursor(windowId);
        public ProgramSkillFrame? MouLixianDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId) =>
            engine.MouLixianDamageCursorRoot(window, out dyingId);
        public ProgramSkillFrame? AlcoholSlashReplacementDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId) =>
            engine.AlcoholSlashReplacementDamageCursorRoot(window, out dyingId);
        public ProgramSkillFrame? RandomDamageTargetSkillDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId) =>
            engine.RandomDamageTargetSkillDamageCursorRoot(window, out dyingId);
        public ProgramSkillFrame? DamageTargetSkillRoundDamageCursorRoot(DamageTriggerWindowFrame window, out long? dyingId) =>
            engine.DamageTargetSkillRoundDamageCursorRoot(window, out dyingId);
        public bool MatchesObserver(ProgramObserverProofPhase phase, long windowId) =>
            engine.RuntimeHooks.MatchesObserver(phase, windowId);
        public bool HasPaidFactionRequestCostRecovery(PaidFactionRequestCostRecovery receipt) =>
            engine.ActiveFactionCardRequest is { } request && PaidFactionRequestMatches(request, receipt) &&
            engine.HasPaidFactionRequestCostRecovery(request);
    }

    [ProgramCapability]
    private sealed class DamageWindowOperations(GameEngine engine) : IDamageWindowOperations
    {
        public DamageTriggerWindowFrame SetWindowCursor(long windowId, int candidateIndex) =>
            engine.SetDamageTriggerWindowCursor(windowId, candidateIndex);
        public void SetWindowStep(long windowId, ResolutionFrameStep step) =>
            engine.SetDamageTriggerWindowStep(windowId, step);
        public IDamageTriggerCapturedAttack CaptureAttack(DamageTriggerWindowFrame window) =>
            new DamageTriggerCapturedAttack(engine, engine.GetDamageTriggerAttack(window));
    }

    private sealed class DamageTriggerCapturedAttack(GameEngine engine, IDamageAttempt attack) : IDamageTriggerCapturedAttack
    {
        public IDamageTriggerCompletion BindTarget() =>
            new DamageTriggerCompletion(engine, attack, engine._players[attack.TargetSeat]);
    }

    private sealed class DamageTriggerCompletion(GameEngine engine, IDamageAttempt attack, CharacterState target) : IDamageTriggerCompletion
    {
        public int Hp => target.Hp;
        public bool IsAlive => target.IsAlive;
        public int SourceSeat => attack.SourceSeat;
        public int TargetSeat => attack.TargetSeat;
        public int DamageAmount => attack.DamageAmount;
        public DamageNature Nature => engine.GetDamageNature(attack);
        public bool IsSourceLess => attack.IsSourceLess;
        public bool HasResolvedDying(long damageFrameId) => attack.HasResolvedDyingForDamage(damageFrameId);
        public void BeginDying(long damageFrameId) =>
            engine.BeginDying(attack, damageFrameId, target, engine._players[attack.SourceSeat]);
        public bool TryBeginAfterDamage(long damageFrameId) =>
            engine.TryBeginDamageTriggerWindow(attack, damageFrameId, SkillProgramTriggerWindow.AfterDamageApplied);
        public void CompleteAttack() => engine.CompleteDamageAttack(attack);
    }
}
