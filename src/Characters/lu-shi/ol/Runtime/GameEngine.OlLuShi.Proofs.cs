namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasOlLuShiJudgmentProof(ProgramSkillFrame f) => f.OlLuShi is { Operation: SkillProgramEffectOp.ResolveOlLuShiLeijie, Item: "slashes" } r &&
        f.JudgmentOutcomeBindings is [var outcome] && outcome is { Name: "leijie-result", Suit: Suit.Spade, Rank: >= 2 and <= 9 } &&
        outcome.TargetSeat == r.TargetSeat && IsValidProgramJudgmentOutcome(f, outcome);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 42700)] private void RequireOlLuShi(ProgramSkillFrame f)
    {
        if (f.OlLuShi is not { } r) return;
        if (r.Source != OlLuShiSource(f) || r.Hash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.Parent != (f.WindowContext?.ParentFrameId ?? 0) ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<OlLuShiStartedEvent>().Count(e => e == new OlLuShiStartedEvent(f.Id, r.Source, r.Hash, r.InstructionIndex, r.Parent, r.Operation)) != 1)
            throw new InvalidOperationException("Lu Shi lost the original content, instruction, parent or issued receipt.");
        if (r.Operation == SkillProgramEffectOp.ResolveOlLuShiZhuyan && r.Item is not null)
        {
            if (r.Item is not ("hp" or "hand") || !IsValidPlayerSeat(r.TargetSeat) || r.Snapshot is not { } snapshot || snapshot.Seat != r.TargetSeat ||
                !ProgramEventHistory<OlLuShiSnapshotEvent>().Contains(snapshot) ||
                ProgramEventHistory<OlLuShiAdjustmentIssuedEvent>().Count(e => e == new OlLuShiAdjustmentIssuedEvent(f.Id, r.Source, r.TargetSeat, r.Item, snapshot)) != 1 ||
                ProgramEventHistory<OlLuShiAdjustmentIssuedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.TargetSeat == r.TargetSeat && e.Item == r.Item) != 1)
                throw new InvalidOperationException("Zhuyan changed its actual historical checkpoint or lifetime participant/item budget.");
            if (r.Pool is { } pool && ProgramEventHistory<OlLuShiPoolAcceptedEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat && e.Pool == pool && e.TargetHp == r.TargetValue) != 1)
                throw new InvalidOperationException("Zhuyan lost the actual target's once-selected historical/current sister.");
        }
        if (r.Operation == SkillProgramEffectOp.ResolveOlLuShiLeijie)
        {
            var outcome = f.JudgmentOutcomeBindings?.SingleOrDefault(o => o.Name == "leijie-result");
            if (outcome is null || !IsValidProgramJudgmentOutcome(f, outcome) || outcome.TargetSeat != r.TargetSeat ||
                (r.Item == "slashes") != (outcome is { Suit: Suit.Spade, Rank: >= 2 and <= 9 }) || r.SlashCount is < 0 or > 2)
                throw new InvalidOperationException("Leijie changed its final judgment or native two-use budget.");
            var issued = ProgramEventHistory<OlLuShiSlashIssuedEvent>().Select(e => e.Origin).Where(o => o.FrameId == f.Id).ToArray();
            var returned = ProgramEventHistory<OlLuShiSlashReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (issued.Length != r.SlashCount || !issued.Select(o => o.Ordinal).SequenceEqual(Enumerable.Range(1, r.SlashCount)) ||
                issued.Any(o => o.Source != r.Source || o.Hash != r.Hash || o.TargetSeat != r.TargetSeat) || returned.Select(e => e.Ordinal).Distinct().Count() != returned.Length ||
                returned.Any(e => !issued.Any(o => o.Ordinal == e.Ordinal && o.UseId == e.UseId && o.ActionId == e.ActionId)) ||
                r.Stage == OlLuShiStage.NativeUse && returned.Length != r.SlashCount - 1 || r.Stage is OlLuShiStage.Ready or OlLuShiStage.Children && returned.Length != r.SlashCount)
                throw new InvalidOperationException("Leijie lost the ordered native issuance/whole-use return ledger.");
        }
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 42700)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 42700)]
    private bool OlLuShiMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.OlLuShi is not { Stage: OlLuShiStage.Children } r || effect?.Op != r.Operation || pending != f.PendingMovementContinuation || pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        RequireOlLuShi(f);
        if (ProgramEventHistory<OlLuShiMovementEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] || paid.Before != r.Before || paid.After != r.After || paid.TargetSeat != r.TargetSeat) return false;
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray();
        return paid.Draw ? r.Draw is { } draw && draw.ActualDrawIds.Count == paid.Count && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After &&
            draw.ActualDrawIds.All(id => moves.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.TargetSeat) && m.Reason.Value == "skill.ol.lushi.draw") == 1) :
            r.Cards.Count == r.RequiredCount && paid.Count == r.Cards.Count && r.Cards.Distinct().Count() == r.Cards.Count &&
            r.Cards.All(id => moves.Count(m => m.CardId == id && (m.From == CardLocation.Hand(r.TargetSeat) || GetProgramDiscardSource(m) == CardLocation.Hand(r.TargetSeat)) &&
                (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && IsEphemeralCardDestroyedMaterial(m)) && m.Reason.Value == "skill.ol.zhuyan.discard") == 1);
    }
    private bool OlLuShiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.OlLuShi is not { } r) return false; RequireOlLuShi(f);
        if (child is CardUseFrame use) return r.Stage == OlLuShiStage.NativeUse && use.OlLuShiLeijie is not null && IsOlLuShiLeijieNativeSlash(use);
        if (child is HappyQiaoHealthChoiceFrame health) return HappyQiaoHealthChoiceRidesOn(health, f);
        if (child is IdentityAppearanceFrame appearance) return r.Item == "hp" && r.Stage is OlLuShiStage.Loss or OlLuShiStage.Children &&
            appearance.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && appearance.Receipt.ParentFrameId == f.Id && appearance.Receipt.OwnerSeat == r.TargetSeat &&
            appearance.Receipt.RequestedDeduction == r.Amount && appearance.Receipt.OriginalInstructionIndex == f.InstructionIndex;
        if (r.Stage != OlLuShiStage.Children) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } pending && OlLuShiMovementProof(f, GetPausedPrivateOfferEffect(f), pending) &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return r.Item == "hp" && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == r.TargetSeat &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.Program;
        if (child is HpChangedTriggerWindowFrame hp) return r.Item == "hp" && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Change.TargetSeat == r.TargetSeat && hp.Continuation == PostEventContinuation.Program && hp.Change.Kind is HpChangeKind.Loss or HpChangeKind.Recovery;
        if (child is DyingFrame dying) return r.Item == "hp" && dying.ParentFrameId == f.Id && dying.VictimSeat == r.TargetSeat && dying.Continuation == DyingContinuationKind.ProgramSkill &&
            ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.TargetSeat == r.TargetSeat && e.Amount == r.Amount && e.RemainingHp <= 0);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? OlLuShiObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.OlLuShiFirstChild(f, c), static (e, i) => e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 42700)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 42700)]
    private bool OlLuShiDamageObserver(long id) => OlLuShiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 42700)] private bool OlLuShiBeforeDamage(long id) => OlLuShiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 42700)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 42700)]
    private bool OlLuShiDying() => ActiveDying is { } dying && OlLuShiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 42700)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 42700)]
    private bool OlLuShiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.OlLuShi is null) return false;
        var root = _resolutionStack.FindIndex(p => p.Id == f.Id); var child = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return root >= 0 && child > root && OlLuShiFirstChild(f, _resolutionStack[root + 1]) && (child == root + 1 || Enumerable.Range(root + 2, child - root - 1).All(i => SameNameHandObserverSuffix(i))) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 42700)] private bool AdvanceOlLuShiChildren() => _pendingDecision is null && OlLuShiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
