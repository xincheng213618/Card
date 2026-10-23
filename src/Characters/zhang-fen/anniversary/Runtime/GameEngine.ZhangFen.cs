using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource ZhangFenSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class ZhangFenHost(GameEngine e) : IAnniversaryZhangFenHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginZhangFen(f, op); }
    private bool ZhangFenSlashDamage(int owner, ProgramSkillWindowContext w) => w.SourceSeat == owner && CurrentDamageAttempt is CardAttackHandle a &&
        !a.IsSourceLess && a.SourceSeat == owner && w.TargetSeat == a.TargetSeat && a.DamageWasApplied && IsSlashCard(a.EffectiveCardKind ?? CardKind.Slash);
    private int ZhangFenDestroyedCount(int owner, ProgramSkillWindowContext w) => w.MovementBatch is { } batch ? batch.Movements
        .Where((m, index) => (w.MovementIndex is null || w.MovementIndex == index) && m.From == CardLocation.Equipment(owner) && IsDagongCheDestroyedMaterial(m))
        .Sum(m => ProgramEventHistory<DagongCheDestroyedEvent>().Single(e => e.CardId == m.CardId).Upgrades) : 0;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 43500, SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu,
        SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu, SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack, SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie)]
    private bool CanOfferZhangFen(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => t.Id switch
    {
        "preparation-cart-or-play" => HeldDagongChe(c.OwnerSeat) is not null || _players[c.OwnerSeat].EquipmentSlotCapacity(EquipmentSlot.Treasure) > 0,
        "slash-upgrade" => ZhangFenSlashDamage(c.OwnerSeat, w) && HeldDagongChe(c.OwnerSeat) is { } cart && DagongUpgrades(cart.Id).Length < 5,
        "destroyed-cart-draw" => ZhangFenDestroyedCount(c.OwnerSeat, w) > 0,
        "play-start-slash" => HeldDagongChe(c.OwnerSeat) is { } held && DagongGrant(held.Id) == c.SkillInstanceId && DagongLegalTargets(c.OwnerSeat, DagongUpgrades(held.Id).Any(e => e.Choice == "range")).Length > 0,
        "cart-slash-discard" => ZhangFenSlashDamage(c.OwnerSeat, w) && CurrentDamageAttempt is { } attack && LifecycleCardUse(attack.ResolutionId)?.DagongCheSlash is { } origin &&
            origin.Source.OwnerSeat == c.OwnerSeat && origin.Source.SkillInstanceId == c.SkillInstanceId && w.TargetSeat is { } target && _players[target].IsAlive && HasDiscardableHeBy(c.OwnerSeat, target),
        _ => false
    };
    private SkillProgramStepOutcome BeginZhangFen(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id); var w = f.WindowContext ?? throw new InvalidOperationException("Zhang Fen requires the original native trigger.");
        if (f.ZhangFen is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Zhang Fen requires his original unpaid instruction.");
        var cart = HeldDagongChe(f.OwnerSeat); var count = op == SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie ? ZhangFenDestroyedCount(f.OwnerSeat, w) : 0;
        var stage = op switch { SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu => cart is null ? ZhangFenStage.PlaceChildren : ZhangFenStage.ExtraPhase,
            SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu => ZhangFenStage.Upgrade,
            SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie => ZhangFenStage.Drawing,
            _ => w.Window == SkillProgramTriggerWindow.PlayPhaseStarting ? ZhangFenStage.Targets : ZhangFenStage.DiscardChoice };
        var source = ZhangFenSource(f);
        if (op == SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu && cart is null)
        {
            if (_players[f.OwnerSeat].EquipmentSlotCapacity(EquipmentSlot.Treasure) <= 0) return SkillProgramStepOutcome.Continue;
            var cardId = _cardZones.CreateDiagnostics().Select(c => c.CardId).DefaultIfEmpty().Max() + 1;
            cart = new(cardId, CardKind.DagongChe, Suit.Spade, 9); _cardZones.AddGeneratedCard(cart); _generatedPhysicalCardIds.Add(cardId);
            AdvanceEventRulesAndQueueFact(new DagongCheCreatedEvent(f.Id, source, f.GameplayHash, cart.Id, f.OwnerSeat));
        }
        var r = new ZhangFenReceipt(source, f.GameplayHash, op, w.ParentFrameId, _turnNumber, stage)
        { CartId = cart?.Id ?? 0, TargetSeat = w.TargetSeat ?? -1, Count = count, Before = _movementSequence, After = _movementSequence };
        if (stage == ZhangFenStage.Targets) r = r with { Count = 1 + DagongUpgrades(r.CartId).Count(e => e.Choice == "target"), IgnoreRangeArmor = DagongUpgrades(r.CartId).Any(e => e.Choice == "range") };
        if (stage == ZhangFenStage.DiscardChoice)
        {
            var origin = LifecycleCardUse(CurrentDamageAttempt!.ResolutionId)!.DagongCheSlash!;
            r = r with { CartId = origin.CartId, Count = 1 + DagongUpgrades(origin.CartId).Count(e => e.Choice == "discard") };
        }
        ReplaceRuntimeTop(f = f with { ZhangFen = r });
        AdvanceEventRulesAndQueueFact(new ZhangFenStartedEvent(f.Id, source, f.GameplayHash, op, r.ParentId, r.Turn, r.CartId, r.TargetSeat, r.Count));
        if (stage == ZhangFenStage.ExtraPhase) return ScheduleProgramPhase(f, TurnPhase.Play, SkillProgramPhaseContinuation.BeforeNormalPreparation);
        if (stage == ZhangFenStage.PlaceChildren)
        {
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            PlaceAdvancedEquipment(cart!, f.OwnerSeat);
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ZhangFen = f.ZhangFen! with { After = _movementSequence } });
            ContinueZhangFen(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (stage == ZhangFenStage.Drawing)
        {
            if (count == 0) { FinishZhangFen(f); return SkillProgramStepOutcome.AwaitChild; }
            BeginOperationProgramDraw(f, f.OwnerSeat, count, new("anniversary.chaixie.draw")); return SkillProgramStepOutcome.AwaitChild;
        }
        PublishZhangFen(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainZhangFen(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 43500)] private bool ContinueZhangFen(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangFen: { } r } f || f.Id != id) return false;
        RequireZhangFen(f);
        if (r.Stage == ZhangFenStage.NativeUse)
        {
            if (f.VirtualBasicDraft is not null) return true;
            if (LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Dagong Che cannot return an unfinished native Use.");
            if (ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardId == 0 && e.CardKind == CardKind.Slash) != 1)
                throw new InvalidOperationException("Dagong Che must retain its once-completed native Use before attack cleanup.");
            // PopFinishedCardUse retires the frame that owns ActiveCardAttack.
            // Its typed attack completion is still on the native call stack;
            // this parent must remain until that exact receipt returns.
            ReplaceRuntimeTop(f with { ZhangFen = r with { Stage = ZhangFenStage.NativeReturned } }); return true;
        }
        if (r.Stage == ZhangFenStage.Drawing) return true;
        if (r.Stage is ZhangFenStage.PlaceChildren or ZhangFenStage.DiscardChildren or ZhangFenStage.DrawChildren)
        {
            if (DrainZhangFen(f)) return true;
            f = GetActiveProgramFrame(id); r = f.ZhangFen!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage != ZhangFenStage.DiscardChildren || r.Paid >= r.Count || !_players[r.TargetSeat].IsAlive || !HasDiscardableHeBy(f.OwnerSeat, r.TargetSeat))
            { FinishZhangFen(f); return true; }
            ReplaceRuntimeTop(f = f with { ZhangFen = r with { Stage = ZhangFenStage.DiscardChoice } });
        }
        if (r.Stage == ZhangFenStage.ExtraPhase) { FinishZhangFen(f); return true; }
        PublishZhangFen(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 43500)] private bool ReturnZhangFenMovement(ProgramSkillFrame f) => f.ZhangFen is not null && ContinueZhangFen(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie)] private void ReturnZhangFenDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ZhangFen!;
        if (!ValidZhangFen(f) || r.Stage != ZhangFenStage.Drawing || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != r.Count || f.NativeDraw is not null)
            throw new InvalidOperationException("Chaixie lost its original destroyed-cart native draw.");
        ReplaceRuntimeTop(f = f with { ZhangFen = r with { Stage = ZhangFenStage.DrawChildren, Draw = draw, Before = draw.SequenceBefore, After = draw.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ZhangFenDrawReturnedEvent(f.Id, draw.RequestedCount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter)); ContinueZhangFen(f.Id);
    }
    private void FinishZhangFen(ProgramSkillFrame f) { ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, ZhangFen = f.ZhangFen! with { Stage = ZhangFenStage.Done } }); FinishProgramSkill(f, true); }
}
