namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability] private sealed class ShenMaChaoHost(GameEngine engine) : IShenMaChaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginShenMaChao(f, op); }
    private static CardConversionSource ShenMcSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private CardActionContext? HengwuAction(ProgramSkillWindowContext? context) => context is null ? null :
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId)?.Action;
    private bool CanHengwu(int owner, CardActionContext? a) => a is { EffectiveSuit: { } suit } && suit != Suit.None && (a.ActorSeat == owner || a.ProviderSeat == owner) &&
        !GetHand(_players[owner]).Any(c => EffectiveSuit(_players[owner], c) == suit) &&
        !ProgramEventHistory<HengwuDrawIssuedEvent>().Any(e => e.OwnerSeat == owner && e.ActionId == a.ActionId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39000, SkillProgramEffectOp.DrawHengwuFieldSuitEquipment)]
    private bool CanTriggerHengwu(ProgramTriggerLegalityContext gate) => CanHengwu(gate.OwnerSeat, HengwuAction(gate.WindowContext));
    private SkillProgramStepOutcome BeginShenMaChao(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenMaChao is not null || f.InstructionIndex != 1 || f.WindowContext is not { } w || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Shen Ma Chao requires his original unpaid native instruction.");
        var r = new ShenMaChaoReceipt(ShenMcSource(f), f.GameplayHash, w.ParentFrameId, op, ShenMaChaoStage.StartingMount);
        if (op == SkillProgramEffectOp.DealShouliStartingMounts)
        {
            if (f.SkillId != ShenMaChaoComposition.Shouli || w.Window != SkillProgramTriggerWindow.GameStarting)
                throw new InvalidOperationException("Shouli's starting mounts require the actual game-start frame.");
            r = r with { Seats = Enumerable.Range(1, _playerCount).Select(offset => TableStepSeat(f.OwnerSeat, offset)).ToArray() };
        }
        else
        {
            if (op != SkillProgramEffectOp.DrawHengwuFieldSuitEquipment || f.SkillId != ShenMaChaoComposition.Hengwu || !CanHengwu(f.OwnerSeat, HengwuAction(w)))
                throw new InvalidOperationException("Hengwu requires an unpaid actual use or played response and no same-suit hand card.");
            var a = HengwuAction(w)!; var suit = a.EffectiveSuit!.Value;
            var count = _players.Where(p => p.IsAlive).Sum(p => GetEquipment(p).Count(c => EffectiveSuit(p, c) == suit));
            AdvanceEventRulesAndQueueFact(new HengwuDrawIssuedEvent(f.Id, f.OwnerSeat, a.ActionId, suit, count));
            if (count == 0) return SkillProgramStepOutcome.Continue;
            r = r with { ActionId = a.ActionId, Suit = suit, DrawCount = count, Stage = ShenMaChaoStage.DrawAttempt };
            ReplaceRuntimeTop(f = f with { ShenMaChao = r });
            BeginOperationProgramDraw(f, f.OwnerSeat, count, new("skill-program.anniversary:hengwu.draw"));
            return SkillProgramStepOutcome.AwaitChild;
        }
        ReplaceRuntimeTop(f = f with { ShenMaChao = r }); ContinueShenMaChao(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenMc(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 39000)] private bool ResumeShenMc(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenMaChao: not null } f || f.Id != id) return false; ContinueShenMaChao(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39000)] private bool ReturnShenMcMovement(ProgramSkillFrame f) => f.ShenMaChao is not null && ResumeShenMc(f.Id);
    private void ContinueShenMaChao(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenMaChao: { } r } f && f.Id == id)
        {
            RequireShenMaChao(f);
            if (r.Stage is ShenMaChaoStage.DrawAttempt or ShenMaChaoStage.EquipmentUse) return;
            if (DrainShenMc(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenMaChao!;
            if (_winner != Winner.None || r.Stage == ShenMaChaoStage.DrawChildren || r.Cursor >= r.Seats.Count)
            { ReplaceRuntimeTop(f with { ShenMaChao = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(id); return; }
            if (r.Stage == ShenMaChaoStage.EquipmentChildren)
            {
                ReplaceRuntimeTop(f with { ShenMaChao = r with { Stage = ShenMaChaoStage.StartingMount, Cursor = r.Cursor + 1, CardId = 0, UseId = 0 }, PendingMovementContinuation = null }); continue;
            }
            var seat = r.Seats[r.Cursor]; var target = _players[seat];
            var candidates = target.IsAlive ? _cardZones.CardsAt(CardLocation.DrawPile).Where(c => EquipmentCatalog.IsEquipment(c.Kind) &&
                EquipmentCatalog.Get(c.Kind).Slot is EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse &&
                target.EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot) > 0 && !IsCardUseForbidden(seat, c.Kind, CardActionType.Use) &&
                !IsSelfTargetForbiddenAction(target, c.Kind, [seat]) && !IsResponseEntityRestricted(seat, c.Id) && !IsPlayPhasePhysicalCardRestricted(target, c)).OrderBy(c => c.Id).ToArray() : [];
            if (candidates.Length == 0)
            { ReplaceRuntimeTop(f with { ShenMaChao = r with { Cursor = r.Cursor + 1 } }); continue; }
            var card = candidates[_random.Next(candidates.Length)];
            ReplaceRuntimeTop(f = f with { ShenMaChao = r with { CardId = card.Id, Stage = ShenMaChaoStage.EquipmentUse }, PendingMovementContinuation = new(seat, 0, null) });
            var useId = BeginCardUse(card, seat, [], conversionSource: r.Source);
            ReplaceRuntimeFrame(f.Id, f with { ShenMaChao = f.ShenMaChao! with { UseId = useId } });
            AdvanceEventRulesAndQueueFact(new ShouliStartingMountIssuedEvent(f.Id, r.Cursor, seat, card.Id, useId));
            MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, CardMoveReasons.EquipmentUse);
            if (!TryBeginEquipmentTargetPrograms(target, card, useId)) CompleteEquipmentUse(target, card, useId);
            return;
        }
    }
    [NativeUseReturn(39000)] private void ContinueShouliStartingMountReturn()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenMaChao: { Stage: ShenMaChaoStage.EquipmentUse } r } f) return;
        if (!ProgramEventHistory<CardUseFinishedEvent>().Any(e => e.ResolutionId == r.UseId && e.CardId == r.CardId))
            throw new InvalidOperationException("Shouli's starting mount did not return its exact native equipment use.");
        AdvanceEventRulesAndQueueFact(new ShouliStartingMountFinishedEvent(f.Id, r.Cursor, r.Seats[r.Cursor], r.CardId, r.UseId));
        ReplaceRuntimeTop(f with { ShenMaChao = r with { Stage = ShenMaChaoStage.EquipmentChildren } }); ContinueShenMaChao(f.Id);
    }
    private bool IsShouliStartingEquipmentRide(ResolutionFrame child, ResolutionFrame parent) =>
        parent is ProgramSkillFrame { ShenMaChao: { Operation: SkillProgramEffectOp.DealShouliStartingMounts, Stage: ShenMaChaoStage.EquipmentUse } r } f &&
        child is CardUseFrame use && use.Id == r.UseId && use.CardId == r.CardId && use.SourceSeat == r.Seats[r.Cursor] &&
        use.Action is { } a && a.ActorSeat == use.SourceSeat && a.ConversionChain.SequenceEqual([r.Source]) &&
        a.PhysicalCards is [{ CardId: var cardId, From: var from }] && cardId == r.CardId && from == CardLocation.DrawPile &&
        EquipmentCatalog.IsEquipment(use.CardKind) && EquipmentCatalog.Get(use.CardKind).Slot is EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse &&
        r.Source == ShenMcSource(f);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawHengwuFieldSuitEquipment)] private void ReturnHengwuDraw(ProgramSkillFrame supplied, NativeDrawOutcome o)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShenMaChao ?? throw new InvalidOperationException("Hengwu lost its original draw owner.");
        if (r.Stage != ShenMaChaoStage.DrawAttempt || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.DrawCount || o.SequenceAfter != _movementSequence ||
            ProgramEventHistory<HengwuDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Hengwu cannot change or repay its frozen native draw.");
        ReplaceRuntimeTop(f with { ShenMaChao = r with { Stage = ShenMaChaoStage.DrawChildren, Before = o.SequenceBefore, After = o.SequenceAfter } });
        AdvanceEventRulesAndQueueFact(new HengwuDrawReturnedEvent(f.Id, f.OwnerSeat, r.DrawCount, o.ActualDrawIds.Count, o.SequenceBefore, o.SequenceAfter)); ContinueShenMaChao(f.Id);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 39000)] private bool IsShenMcNativeMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.ShenMaChao is { Operation: SkillProgramEffectOp.DealShouliStartingMounts, Stage: ShenMaChaoStage.EquipmentChildren } r && e?.Op == r.Operation &&
        r.Cursor < r.Seats.Count && p.SubjectSeat == r.Seats[r.Cursor] && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        ProgramEventHistory<ShouliStartingMountFinishedEvent>().Count(x => x.FrameId == f.Id && x.Index == r.Cursor && x.UseId == r.UseId && x.CardId == r.CardId) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39000)] private void RequireShenMaChao(ProgramSkillFrame f)
    {
        if (f.ShenMaChao is not { } r) return;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (r.Source != ShenMcSource(f) || r.GameplayHash != f.GameplayHash || r.ParentId != f.WindowContext?.ParentFrameId || index < 1 ||
            _resolutionStack[index - 1].Id != r.ParentId || r.Seats is not System.Collections.IList { IsReadOnly: true } || r.Cursor < 0 || r.Cursor > r.Seats.Count ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence)
            throw new InvalidOperationException("Shen Ma Chao lost his immutable original native parent or paid cursor.");
        if (r.Operation == SkillProgramEffectOp.DealShouliStartingMounts)
        {
            if (f.SkillId != ShenMaChaoComposition.Shouli || f.WindowContext?.Window != SkillProgramTriggerWindow.GameStarting || r.Seats.Count != _playerCount ||
                r.Seats.Distinct().Count() != _playerCount || r.Seats.Any(s => !IsValidPlayerSeat(s)) ||
                r.Stage is ShenMaChaoStage.EquipmentUse or ShenMaChaoStage.EquipmentChildren && (r.Cursor >= r.Seats.Count || r.CardId <= 0 || r.UseId <= 0 ||
                    ProgramEventHistory<ShouliStartingMountIssuedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Cursor && e.RecipientSeat == r.Seats[r.Cursor] && e.CardId == r.CardId && e.UseId == r.UseId) != 1))
                throw new InvalidOperationException("Shouli lost the actual starting seat order or issued native mount.");
        }
        else if (r.Operation != SkillProgramEffectOp.DrawHengwuFieldSuitEquipment || f.SkillId != ShenMaChaoComposition.Hengwu ||
            r.Stage is not (ShenMaChaoStage.DrawAttempt or ShenMaChaoStage.DrawChildren) || r.DrawCount < 1 ||
            ProgramEventHistory<HengwuDrawIssuedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.ActionId == r.ActionId && e.Suit == r.Suit && e.Count == r.DrawCount) != 1)
            throw new InvalidOperationException("Hengwu lost its exact paid actual-action draw entitlement.");
    }
}
