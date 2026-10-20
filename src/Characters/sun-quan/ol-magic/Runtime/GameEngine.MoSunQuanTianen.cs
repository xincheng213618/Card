namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 10)]
    private bool MoTianenNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasMoTianenCapability;


    private const string MoTianenDiscardReason = "skill-program.mo-sun-quan.tianen.discard";
    private const string MoTianenObtainReason = "skill-program.mo-sun-quan.tianen.obtain";
    private bool HasMoTianenCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse);
    private static CardConversionSource MoTianenSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool MoTianenBranchSpent(int owner, int turn, int turnOwner, MoTianenBranch branch) =>
        ProgramEventHistory<MoTianenBranchIssuedEvent>().Any(e => e.Source.OwnerSeat == owner &&
            e.ActualTurn == turn && e.TurnOwner == turnOwner && e.Branch == branch);

    private bool CanOfferMoTianen(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (!HasMoTianenCapability || candidate.SkillId != "ol:mo-tianen" || candidate.BindingId != "unique-target-branches" ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || !_players[candidate.OwnerSeat].IsAlive ||
            !TryGetMoQuanyuSingletonUse(candidate.OwnerSeat, context, out _, out _, out var target)) return false;
        var own = MoQuanyuCurrentChoice(candidate.OwnerSeat, candidate.OwnerSeat);
        var other = MoQuanyuCurrentChoice(candidate.OwnerSeat, target);
        return own is >= MoQuanyuEffect.Baihong and <= MoQuanyuEffect.Liuxing &&
            other is >= MoQuanyuEffect.Baihong and <= MoQuanyuEffect.Liuxing &&
            !MoTianenBranchSpent(candidate.OwnerSeat, _turnNumber, _turnProgression.OwnerSeat,
                own == other ? MoTianenBranch.Same : MoTianenBranch.Different);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28920,
        SkillProgramEffectOp.ResolveMoSunQuanTianenUniqueUse)]
    private bool CanOfferMoTianenGate(ProgramTriggerLegalityContext gate) => CanOfferMoTianen(gate.Candidate, gate.Context);

    [ProgramCapability]
    private sealed class MoSunQuanTianenCapability(GameEngine engine) : IMoSunQuanTianenHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginMoTianen(f); }

    private SkillProgramStepOutcome BeginMoTianen(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MoTianen is not null) { ContinueMoTianen(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != "ol:mo-tianen" || f.TriggerId != "unique-target-branches" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context ||
            !CanOfferMoTianen(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash,
                GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) ||
            !TryGetMoQuanyuSingletonUse(f.OwnerSeat, context, out var use, out var window, out var target))
            throw new InvalidOperationException("Tianen needs its canonical true sole-target use and one unspent actual-turn branch.");
        var own = MoQuanyuCurrentChoice(f.OwnerSeat, f.OwnerSeat);
        var other = MoQuanyuCurrentChoice(f.OwnerSeat, target);
        var branch = own == other ? MoTianenBranch.Same : MoTianenBranch.Different;
        var r = new MoTianenReceipt(MoTianenSource(f), f.GameplayHash, use.Id, use.Action!.ActionId,
            window.Id, target, _roundNumber, _turnNumber, _turnProgression.OwnerSeat, own, other, branch)
        { Stage = branch == MoTianenBranch.Same ? MoTianenStage.ObtainChildren : MoTianenStage.DiscardChildren,
            Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MoTianen = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MoTianenBranchIssuedEvent(f.Id, r.Source, r.GameplayHash,
            r.UseId, r.ActionId, r.WindowId, r.TargetSeat, r.RoundNumber, r.ActualTurn, r.TurnOwner, own, other, branch));
        if (branch == MoTianenBranch.Same) PayMoTianenObtain(f); else PayMoTianenDiscard(f);
        ContinueMoTianen(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private void PayMoTianenObtain(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.MoTianen!;
        var card = _players[f.OwnerSeat].IsAlive
            ? _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault(c => IsSlashCard(c.Kind)) : null;
        var material = card is null ? null : new MoTianenMaterial(card.Id, card.Kind, CardLocation.DrawPile);
        ReplaceRuntimeTop(f = f with { MoTianen = r = r with { Material = material } });
        AdvanceEventRulesAndQueueFact(new MoTianenSlashObtainIssuedEvent(f.Id, material?.CardId,
            material?.PrintedKind, material?.From, r.Before));
        if (card is not null)
            MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(MoTianenObtainReason),
                move => AdvanceEventRulesAndQueueFact(new MoTianenSlashExemptionIssuedEvent(f.Id,
                    r.Source, r.GameplayHash, move.CardId, move.CardKind, move.Sequence)));
        SealMoTianenPayment(f.Id);
    }

    private void PayMoTianenDiscard(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.MoTianen!;
        var pool = _players[r.TargetSeat].IsAlive
            ? _cardZones.CardsAt(CardLocation.Hand(r.TargetSeat)).Concat(_cardZones.CardsAt(CardLocation.Equipment(r.TargetSeat)))
                .Where(c => !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard))
                .OrderBy(c => c.Id).ToArray() : [];
        var beforeRandom = _random.State;
        var index = pool.Length == 0 ? -1 : _random.Next(pool.Length);
        var card = index < 0 ? null : pool[index];
        var material = card is null ? null : new MoTianenMaterial(card.Id, card.Kind, _cardZones.GetLocation(card.Id));
        var grains = material is { PrintedKind: CardKind.WoodenOx, From.Zone: CardZoneKind.Equipment }
            ? _cardZones.CardsAt(CardLocation.WoodenOxGrain(r.TargetSeat))
                .Select(c => new MoTianenMaterial(c.Id, c.Kind, CardLocation.WoodenOxGrain(r.TargetSeat))).ToArray() : [];
        ReplaceRuntimeTop(f = f with { MoTianen = r = r with { PoolCount = pool.Length, RandomIndex = index,
            RandomBefore = beforeRandom, RandomAfter = _random.State, Material = material, GrainMaterials = grains } });
        AdvanceEventRulesAndQueueFact(new MoTianenRandomDiscardIssuedEvent(f.Id, r.PoolCount, r.RandomIndex,
            r.RandomBefore, r.RandomAfter, material?.CardId, material?.PrintedKind, material?.From, r.Before));
        for (var i = 0; i < grains.Length; i++) AdvanceEventRulesAndQueueFact(new MoTianenSpillMaterialFrozenEvent(
            f.Id, i, grains[i].CardId, grains[i].PrintedKind, grains[i].From));
        if (card is not null) MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, material!.From,
            new(MoTianenDiscardReason), parentFrameId: f.Id);
        SealMoTianenPayment(f.Id);
    }

    private void SealMoTianenPayment(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MoTianen!;
        if (r.PaymentSealed) throw new InvalidOperationException("Tianen cannot repeat its branch payment.");
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= _movementSequence &&
            (r.Material is { } p && m.CardId == p.CardId && m.From == p.From &&
                m.Reason.Value == (r.Branch == MoTianenBranch.Same ? MoTianenObtainReason : MoTianenDiscardReason) ||
             r.GrainMaterials.Any(g => m.CardId == g.CardId && m.CardKind == g.PrintedKind && m.From == g.From) &&
                m.Reason == CardMoveReasons.WoodenOxGrainDiscard))
            .Select(m => new MoTianenPaidMovement(m.CardId, m.CardKind, m.From, m.To, m.Reason.Value, m.Sequence)).ToArray();
        ReplaceRuntimeTop(f with { MoTianen = r = r with { PaymentSealed = true, After = _movementSequence, Movements = moves } });
        AdvanceEventRulesAndQueueFact(new MoTianenPaymentSealedEvent(id, r.Branch, r.Before, r.After, moves.Length, r.Material?.CardId));
        for (var i = 0; i < moves.Length; i++) { var m = moves[i]; AdvanceEventRulesAndQueueFact(new MoTianenMaterialPaidEvent(
            id, i, m.CardId, m.PrintedKind, m.From, m.To, m.Reason, m.Sequence)); }
    }

    private bool DrainMoTianen(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueMoTianen(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoTianen: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertMoTianen(f);
        if (r.Stage == MoTianenStage.QuanyuChild) return;
        if (r.Stage is MoTianenStage.DiscardChildren or MoTianenStage.ObtainChildren)
        {
            if (DrainMoTianen(f)) return;
            if (r.Stage == MoTianenStage.DiscardChildren && _winner == Winner.None && _players[r.TargetSeat].IsAlive)
            {
                var program = _contentRegistry.GetSkill("ol:mo-quanyu").Program ??
                    throw new InvalidOperationException("Tianen's explicit derived Quanyu requires its registered native program.");
                var source = new CardConversionSource("ol:mo-quanyu", "target-only-choices", f.OwnerSeat, "issued:tianen:" + f.Id);
                var request = new MoQuanyuTargetRequest(f.Id, f.InstructionIndex, r.Source, r.GameplayHash,
                    r.UseId, r.ActionId, r.TargetSeat, r.RoundNumber, r.ActualTurn, r.TurnOwner,
                    source, program.GameplayHash, r.Before, r.After);
                ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null,
                    MoTianen = r = r with { Stage = MoTianenStage.QuanyuChild, QuanyuRequest = request } });
                AdvanceEventRulesAndQueueFact(new MoTianenQuanyuRequestedEvent(id, request));
                BeginMoQuanyuTargetChoice(f, request); return;
            }
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null,
                MoTianen = r = r with { Stage = MoTianenStage.Completing } });
        }
        if (r.Stage != MoTianenStage.Completing) throw new InvalidOperationException("Tianen lost its paid child return stage.");
        AdvanceEventRulesAndQueueFact(new MoTianenCompletedEvent(id, r.Branch)); FinishProgramSkill(f, true);
    }

    private void ReturnMoSunQuanTianenQuanyu(ProgramSkillFrame parent, MoQuanyuTargetChoiceReturn result)
    {
        var f = GetActiveProgramFrame(parent.Id);
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || f.MoTianen is not { Stage: MoTianenStage.QuanyuChild, QuanyuChildFrameId: null } r ||
            !IsMoSunQuanTianenQuanyuRequest(f, result.Request) || result.ChildFrameId <= f.Id ||
            _resolutionStack.Any(x => x.Id == result.ChildFrameId) ||
            ProgramEventHistory<MoQuanyuTargetRequestIssuedEvent>().Count(e => e.ChildFrameId == result.ChildFrameId && e.Request == result.Request) != 1 ||
            ProgramEventHistory<MoQuanyuChoicesCompletedEvent>().Count(e => e.FrameId == result.ChildFrameId && e.Kind == MoQuanyuInvocationKind.TargetOnly && e.DrawCount == result.DrawCount) != 1 ||
            ProgramEventHistory<MoQuanyuTargetChoiceReturnedEvent>().Count(e => e.Result == result) != 1 ||
            !Enum.IsDefined(result.ChosenEffect))
            throw new InvalidOperationException("Tianen accepts only its exact once-returned derived Quanyu child.");
        AdvanceEventRulesAndQueueFact(new MoTianenQuanyuReturnedEvent(f.Id, result.ChildFrameId, result.Request, result.ChosenEffect, result.DrawCount));
        ReplaceRuntimeTop(f = f with { MoTianen = r with { Stage = MoTianenStage.Completing, QuanyuChildFrameId = result.ChildFrameId } });
        ContinueMoTianen(f.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 28920)]
    private bool ResumeMoTianen(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoTianen: not null } f || f.Id != id) return false; ContinueMoTianen(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28920)]
    private bool ReturnMoTianenMovement(ProgramSkillFrame f)
    { if (f.MoTianen is null) return false; ContinueMoTianen(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28920)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28920)]
    private bool CanContinuePaidMoTianen(ProgramSkillFrame f) => f.MoTianen is not null && ValidMoTianen(f);
}
