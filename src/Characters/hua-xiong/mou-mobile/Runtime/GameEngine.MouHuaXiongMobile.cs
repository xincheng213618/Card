using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouYaowu = MouHuaXiongComposition.Yaowu, MouYangwei = MouHuaXiongComposition.Yangwei;
    private static CardConversionSource MouHuaXiongSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_hua_xiong_mou_mobile_MouHuaXiongMobile.MouHuaXiongSource(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29100)]
    private bool KeepPaidMouHuaXiong(ProgramSkillFrame f)
    {
        if (f.MouHuaXiongYangwei is not null) return ValidMouYangweiReceipt(f);
        return f.MouHuaXiongYaowu is { Benefit: not null } && ValidMouYaowuReceipt(f);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29100)]
    private void AssertMouHuaXiong(ProgramSkillFrame f)
    {
        if (f.MouHuaXiongYangwei is not null && !ValidMouYangweiReceipt(f) || f.MouHuaXiongYaowu is not null && !ValidMouYaowuReceipt(f))
            throw new InvalidOperationException("Mobile Hua Xiong lost its exact issued owner, source, damage window or paid phase receipt.");
        if (f.MouHuaXiongYaowu is { Benefit: null, SourceSeat: { } source } && _resolutionStack.LastOrDefault()?.Id == f.Id &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != source ||
                !AssistedChoicesEqual(prompt.Choices, MouYaowuChoices(f))))
            throw new InvalidOperationException("Yaowu's unanswered benefit must retain its real source's private choice.");
    }
    private bool MouHuaXiongPaidRoot(ProgramSkillFrame f) => f.MouHuaXiongYangwei is not null ? ValidMouYangweiReceipt(f) :
        f.MouHuaXiongYaowu is { Benefit: not null } && ValidMouYaowuReceipt(f);
    private bool MouHuaXiongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouHuaXiongPaidRoot(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f) || RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id;
        if (child is not CardsMovedTriggerWindowFrame moved || moved.Batch.ParentFrameId != f.Id ||
            moved.ResumeProgramFrameId != f.Id && moved.Batch.AwaitingProgramFrameId != f.Id || moved.Batch.Movements.Count == 0) return false;
        var recipient = f.MouHuaXiongYangwei is not null ? f.OwnerSeat : f.MouHuaXiongYaowu!.Benefit == "source-draw" ? f.MouHuaXiongYaowu.SourceSeat : f.OwnerSeat;
        return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(recipient!.Value) && m.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.Draw}");
    }
    private bool MouHuaXiongObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) =>
        g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) || g.IsYanPuDyingExitEdge(child) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame f && g.MouHuaXiongFirstChild(f, g._resolutionStack[child]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? MouHuaXiongObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouHuaXiongFirstChild(f, child),
        static (g, index) => g.MouHuaXiongObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1270)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1380)]
    private bool HasMouHuaXiongDamageObserver(long id) => MouHuaXiongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1410)]
    private bool HasMouHuaXiongBeforeDamageObserver(long id) => MouHuaXiongObserverRoot() is { } root &&
        (root.MouHuaXiongYaowu?.WindowId == id ||
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1240)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 800)]
    private bool IsMouHuaXiongDying() => ActiveDying is { } dying && MouHuaXiongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 60)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1830)]
    private bool AllowsMouHuaXiongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouHuaXiongObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 570)]
    private bool IsMouHuaXiongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!MouHuaXiongPaidRoot(f) || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouHuaXiongFirstChild(f, _resolutionStack[parent + 1]) &&
            MouHuaXiongObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29100)]
    private bool AdvanceMouHuaXiongChildren() => _pendingDecision is null && MouHuaXiongObserverRoot() is not null && TryAdvanceObserverSubtree(true);

    [ProgramCapability]
    private sealed class MouHuaXiongCapability(GameEngine engine) : IMouHuaXiongHost
    {
        public SkillProgramStepOutcome Yangwei(ProgramSkillFrame f) => engine.ResolveMouYangwei(f);
        public SkillProgramStepOutcome Yaowu(ProgramSkillFrame f) => engine.ResolveMouYaowu(f);
    }

}
