namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ExecuteDrawFundedDistinctBasicPaid(DrawFundedDistinctBasicFrame frame)
    {
        var p = frame.Payment; var actor = _players[p.Source.OwnerSeat]; var kind = p.EffectiveKind; var card = new Card(0, kind, Suit.None, 0);
        if (p.Intent == DrawFundedDistinctBasicIntent.OwnSlashDodge) { ResolveDrawFundedDistinctBasicDodge(p); return; }
        if (p.Intent == DrawFundedDistinctBasicIntent.Dying)
        {
            var dying = ActiveDying!; var victim = _players[dying.VictimSeat];
            SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.ResolvingEffect);
            var policies = kind == CardKind.Peach && actor.Seat != victim.Seat
                ? CardPolicies(victim, SkillProgramCardPolicyKind.RescueRecoveryBonus, CardKind.Peach)
                    .Where(policy => string.Equals(GetEffectiveFactionId(actor), policy.Policy.FactionId, StringComparison.Ordinal)).ToArray() : [];
            ResolveRecoveryCard(actor, victim, card, kind == CardKind.Peach ? "桃" : "酒", kind,
                1 + policies.Sum(policy => policy.Policy.Value), recoveryPolicySources: policies.Select(policy => (policy.Source.SkillId, policy.Policy.Id)).ToArray(),
                conversionSource: p.Source, dyingResponse: new(dying.FrameId, actor.Seat, kind == CardKind.Peach, kind == CardKind.Peach ? 0 : null,
                    kind == CardKind.Alcohol, kind == CardKind.Alcohol ? 0 : null), physicalCards: [], drawFundedPayment: p); return;
        }
        if (p.Intent == DrawFundedDistinctBasicIntent.Play)
        {
            // Payment resume has revalidated these exact targets against the
            // current native menu. Reuse the existing adjusted simple-use loop.
            if (kind is CardKind.Peach or CardKind.Alcohol && frame.TargetSeats.Count > 1)
                _selectedNextCardTargetSeats = frame.TargetSeats;
            if (kind == CardKind.Peach)
            { ResolveRecoveryCard(actor, actor, card, "桃", kind, conversionSource: p.Source, physicalCards: [], drawFundedPayment: p); return; }
            if (kind == CardKind.Alcohol)
            {
                var id = BeginCardUse(card, actor.Seat, [], kind, physicalCardIds: [], conversionSource: p.Source, drawFundedPayment: p);
                actor.UsedPlayPhaseAlcoholThisTurn = true; BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return;
            }
            BeginDrawFundedDistinctBasicSlash(actor, frame.TargetSeats, p, null, true, null); return;
        }
        if (p.Intent == DrawFundedDistinctBasicIntent.BorrowedSword)
        {
            var borrowed = ActiveBorrowedSword!;
            PopResponseWindow(borrowed.ResolutionId); SetCardUseStep(borrowed.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            borrowed.AwaitingSlashChoice = false; borrowed.SlashCardId = 0; borrowed.EffectiveSlashKind = kind;
            BeginDrawFundedDistinctBasicSlash(actor, frame.TargetSeats, p, borrowed, true, null); return;
        }
        if (p.Intent == DrawFundedDistinctBasicIntent.Qinglong)
        {
            var original = ActiveQinglongCrescentBlade!.Attack; ActiveQinglongCrescentBlade = null;
            SetCardUseStep(original.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(original.ResolutionId, actor.Seat, p.TargetSeat!.Value,
                Used: true, SlashCardIds: [], EffectiveSlashKind: kind));
            CompleteAttack(original); SuspendContinuationForQinglongFollowup(original.ResolutionId);
            BeginDrawFundedDistinctBasicSlash(actor, frame.TargetSeats, p, null, false, null); return;
        }
        var parent = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A paid program Slash lost its original request.");
        if (parent.Id != p.ParentFrameId || parent.InstructionIndex != p.Cursor)
            throw new InvalidOperationException("A paid program Slash moved its original instruction cursor.");
        var effect = ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!).GetPausedInstruction(parent.InstructionIndex).Effect;
        if (p.Intent == DrawFundedDistinctBasicIntent.ProgramSlash)
            CommitProgramChoiceResult(parent.Id, effect.ResultBind!, RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption, actor.Seat, "摸牌后视为使用【杀】。");
        else if (p.Intent == DrawFundedDistinctBasicIntent.AssistedSlash)
        {
            ReplaceRuntimeTop(parent with { AssistedSlashRequest = null });
            CommitProgramChoiceResult(parent.Id, effect.ResultBind!, "used-slash", actor.Seat, "摸牌后视为使用【杀】。");
        }
        else if (p.Intent == DrawFundedDistinctBasicIntent.NearestLegalSlash)
            ReplaceRuntimeTop(parent with { NearestLegalSlashRequest = null });
        else if (p.Intent != DrawFundedDistinctBasicIntent.ProgramNearestSlash)
            throw new InvalidOperationException("A paid Slash has an unknown typed native return.");
        BeginDrawFundedDistinctBasicSlash(actor, frame.TargetSeats, p, null, false, parent.Id);
    }

    private void BeginDrawFundedDistinctBasicSlash(CharacterState actor, IReadOnlyList<int> targets, DrawFundedDistinctBasicPayment p,
        BorrowedSwordHandle? borrowed, bool countsTowardLimit, long? programParent)
    {
        if (!IsSlashCard(p.EffectiveKind) || targets.Count == 0) throw new InvalidOperationException("A paid zero-material Slash requires its accepted ordered targets.");
        var kind = p.EffectiveKind; var card = new Card(0, kind, Suit.None, 0);
        var id = BeginCardUse(card, actor.Seat, targets, kind, ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], kind),
            physicalCardIds: [], conversionSource: p.Source, drawFundedPayment: p);
        var actualTargets = LifecycleCardUse(id)!.TargetSeats;
        var attack = new CardAttackHandle(this, id, actor.Seat, actualTargets[0], card: null, damageAmount: actor.HasAlcoholEffect ? 2 : 1,
            playedCardKind: kind, ignoresArmor: HasCardArmorBypass(actor, _players[actualTargets[0]], kind), programSkillCardUseFrameId: programParent);
        if (borrowed is not null) borrowed.ActiveAttack = attack;
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
        if (countsTowardLimit && _phase == TurnPhase.Play && actor.Seat == _currentSeat && LifecycleCardUse(id)?.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, p.Source))
            RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actor.Seat, actualTargets[0], IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, _players[actualTargets[0]]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted,
            actualTargets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }

    private CardActionContext PrepareDrawFundedDistinctBasicAction(CardActionContext action, DrawFundedDistinctBasicPayment? payment) => payment is null ? action :
        CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat,
            action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards,
            action.ConversionChain, action.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: false));

    private void IssueDrawFundedDistinctBasicUse(CardUseFrame use, DrawFundedDistinctBasicPayment? payment)
    {
        if (payment is not { } p) return;
        if (!ValidDrawFundedDistinctBasicPayment(p) || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } action || use.CardId != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || action.ActorSeat != p.Source.OwnerSeat || action.ProviderSeat != p.Source.OwnerSeat ||
            action.RequesterSeat is not null || action.ResponderSeat is not null || action.EffectiveKind != p.EffectiveKind || use.CardKind != p.EffectiveKind ||
            action.ConversionChain is not [var source] || source != p.Source || action.EffectiveSuit != Suit.None || action.EffectiveRank != 0 ||
            action.EffectiveIsRed != false || p.Intent == DrawFundedDistinctBasicIntent.OwnSlashDodge ||
            !DrawFundedDistinctBasicNameAvailable(p.Source.OwnerSeat, p.MethodLedgerId, p.EffectiveKind))
            throw new InvalidOperationException("A draw-funded Use lost its unique paid source, neutral zero materials or actual native issuance.");
        var receipt = new DrawFundedDistinctBasicUseReceipt(p, use.Id, action.ActionId);
        ReplaceRuntimeFrame(use.Id, use with { DrawFundedDistinctBasicUse = receipt }); EmitDrawFundedDistinctBasicIssued(receipt);
    }
    private void EmitDrawFundedDistinctBasicIssued(DrawFundedDistinctBasicUseReceipt receipt)
    {
        var p = receipt.Payment;
        if (CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicIssuedEvent>().Any(e => e.PaymentFrameId == p.PaymentFrameId))
            throw new InvalidOperationException("A single paid draw attempt cannot issue two basic Uses.");
        AdvanceEventRulesAndQueueFact(new DrawFundedDistinctBasicIssuedEvent(p.PaymentFrameId, receipt.OwnerFrameId, receipt.CardActionId,
            p.Source.OwnerSeat, p.MethodLedgerId, p.ActualTurnNumber, p.ActualTurnOwnerSeat, p.NormalizedName, p.EffectiveKind, p.Intent, p.Source, p.GameplayHash));
    }
    private bool IsDrawFundedDistinctBasicIssued(DrawFundedDistinctBasicUseReceipt receipt) => ValidDrawFundedDistinctBasicPayment(receipt.Payment) &&
        CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicIssuedEvent>().Where(fact => fact.PaymentFrameId == receipt.Payment.PaymentFrameId).ToArray() is [var e] &&
        e.OwnerFrameId == receipt.OwnerFrameId && e.CardActionId == receipt.CardActionId && e.ActorSeat == receipt.Payment.Source.OwnerSeat &&
        e.MethodLedgerId == receipt.Payment.MethodLedgerId && e.ActualTurnNumber == receipt.Payment.ActualTurnNumber &&
        e.ActualTurnOwnerSeat == receipt.Payment.ActualTurnOwnerSeat && e.NormalizedName == receipt.Payment.NormalizedName && e.EffectiveKind == receipt.Payment.EffectiveKind &&
        e.Intent == receipt.Payment.Intent && e.Source == receipt.Payment.Source && e.GameplayHash == receipt.Payment.GameplayHash;

    private bool IsIssuedDrawFundedDistinctBasicUse(CardUseFrame use)
    {
        if (use.DrawFundedDistinctBasicUse is not { } receipt || receipt.OwnerFrameId != use.Id || use.CardId != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || !IsDrawFundedDistinctBasicIssued(receipt) || use.Action is not { Type: CardActionType.Use } current) return false;
        var action = use.CurrentSlashFirePolicy is { } fire ? fire.OriginalAction : current;
        if (use.CurrentSlashFirePolicy is not null) AssertCurrentSlashFirePolicy(use);
        var p = receipt.Payment;
        return action.ActionId == receipt.CardActionId && action.EffectiveKind == p.EffectiveKind &&
            (use.CardKind == p.EffectiveKind || use.CurrentSlashFirePolicy is not null && IsSlashCard(p.EffectiveKind)) &&
            action.ProviderSeat == p.Source.OwnerSeat && action.RequesterSeat is null && action.ResponderSeat is null && action.PhysicalCards.Count == 0 &&
            action.ConversionChain is [var source] && source == p.Source && action.EffectiveSuit == Suit.None && action.EffectiveRank == 0 && action.EffectiveIsRed == false &&
            (action.ActorSeat == p.Source.OwnerSeat && use.SourceSeat == p.Source.OwnerSeat || MatchesDeclaredActualDamageUse(use, action.ActorSeat, p.Source.OwnerSeat));
    }
    private bool IsDrawFundedDistinctBasicUse(long frameId) => LifecycleCardUse(frameId) is { } use && IsIssuedDrawFundedDistinctBasicUse(use);
    private Card DrawFundedDistinctBasicRepresentation(long frameId) => LifecycleCardUse(frameId) is { } use && IsIssuedDrawFundedDistinctBasicUse(use)
        ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("A logical basic card lost its unique paid issuance.");

    private bool ContinueDrawFundedDistinctBasicSimpleUse(long frameId, ProgramSimpleCardContinuation continuation)
    {
        if (continuation.CardId != 0 || !IsDrawFundedDistinctBasicUse(frameId)) return false;
        var use = LifecycleCardUse(frameId)!; var actor = _players[use.SourceSeat]; var card = DrawFundedDistinctBasicRepresentation(frameId);
        SetCardUseStep(frameId, ResolutionFrameStep.ResolvingEffect);
        if (!actor.IsAlive || _winner != Winner.None) { FinishCardUse(frameId, card, use.CardKind); return true; }
        if (continuation.Effect == SimpleCardUseEffect.Recovery && use.CardKind is CardKind.Peach or CardKind.Alcohol)
            CompleteRecoveryCardUse(actor, _players[use.TargetSeats[use.TargetIndex]], card, frameId, use.CardKind, continuation.RecoveryAmount, continuation.RecoveryPolicySources ?? []);
        else if (continuation.Effect == SimpleCardUseEffect.Alcohol && use.CardKind == CardKind.Alcohol) CompleteAlcoholUse(actor, card, frameId);
        else throw new InvalidOperationException("A paid basic card changed its accepted native simple effect.");
        return true;
    }

    private bool FinishDrawFundedDistinctBasicAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsDrawFundedDistinctBasicUse(attack.ResolutionId) || !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Dodge)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats,
            ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
}
