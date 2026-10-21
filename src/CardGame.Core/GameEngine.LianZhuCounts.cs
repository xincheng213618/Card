namespace CardGame.Core;

internal interface ILianZhuGiftSelectionQueries : IProgramSelectionQuery
{
    bool CanDeliver(int ownerSeat, int recipientSeat, int cardId);
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 22400, SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)]
internal sealed class RecipientHpGiftSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        if (context.Features.First(SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)?.NumberExpression != SkillProgramNumberExpression.RecipientCurrentHp)
            return true;
        var participants = queries.Get<IProgramSelectionSourceQuery>().Participants();
        if (!participants.Any(p => p.Seat == context.OwnerSeat && p.IsAlive && p.HasHpValue)) return false;
        var delivery = queries.Get<ILianZhuGiftSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => participants.Any(p => p.Seat == seat && p.IsAlive && p.HasHpValue && p.Hp > 0) &&
            candidates.Cards.Any(id => delivery.CanDeliver(context.OwnerSeat, seat, id))).ToArray();
        candidates.Cards = candidates.Cards.Where(id => candidates.Targets.Any(seat => delivery.CanDeliver(context.OwnerSeat, seat, id))).ToArray();
        return candidates.Targets.Length > 0 && candidates.Cards.Length > 0;
    }
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 22400, SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)]
internal sealed class RecipientHpGiftSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        if (request.Context.Features.First(SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)?.NumberExpression != SkillProgramNumberExpression.RecipientCurrentHp)
            return null;
        var participants = queries.Get<IProgramSelectionSourceQuery>().Participants();
        return request.Cards is [var cardId] && request.Targets is [var recipient] &&
            participants.Any(p => p.Seat == request.Context.OwnerSeat && p.IsAlive && p.HasHpValue) &&
            participants.Any(p => p.Seat == recipient && p.IsAlive && p.HasHpValue && p.Hp > 0) &&
            queries.Get<ILianZhuGiftSelectionQueries>().CanDeliver(request.Context.OwnerSeat, recipient, cardId)
            ? null : new(CommandErrorCode.IllegalAction, "动态赠牌须有真实体力值，且材料必须能实际交入受赠者手中。");
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 22400, SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)]
internal sealed class RecipientHpGiftSelectionAi : IProgramSelectionAiDecorator
{
    public SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint, IProgramSelectionQueries queries)
    {
        if (context.Features.First(SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)?.NumberExpression != SkillProgramNumberExpression.RecipientCurrentHp)
            return hint;
        var source = queries.Get<IProgramSelectionSourceQuery>();
        var delivery = queries.Get<ILianZhuGiftSelectionQueries>();
        var targets = source.Participants().Where(p => p.Seat != context.OwnerSeat && p.IsAlive && p.HasHpValue && p.Hp > 0).ToArray();
        var ownCards = source.Hand(context.OwnerSeat).Concat(source.Equipment(context.OwnerSeat))
            .Where(card => context.Activation.SourceZones.Contains(source.Location(card.Id).Zone) &&
                targets.Any(target => delivery.CanDeliver(context.OwnerSeat, target.Seat, card.Id))).ToArray();
        // An opportunity prior over the giver's own possible materials. Only
        // public target HP enters; no recipient card faces or deck order do.
        var blackPrior = ownCards.Length == 0 ? 0d : ownCards.Count(card => source.EffectiveSuit(context.OwnerSeat, card) is Suit.Spade or Suit.Club) / (double)ownCards.Length;
        var expected = targets.Select(target => target.Hp).DefaultIfEmpty(0).Max() * blackPrior;
        return hint with { OwnerDraw = checked(hint.OwnerDraw + (int)Math.Round(expected, MidpointRounding.AwayFromZero)), GivesSelected = true };
    }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class LianZhuGiftSelectionQueries(GameEngine engine) : ILianZhuGiftSelectionQueries
    {
        public bool CanDeliver(int ownerSeat, int recipientSeat, int cardId) => engine.CanDeliverLianZhuGift(ownerSeat, recipientSeat, cardId);
    }

    private bool CanDeliverLianZhuGift(int ownerSeat, int recipientSeat, int cardId)
    {
        if (!IsValidPlayerSeat(ownerSeat) || !IsValidPlayerSeat(recipientSeat) || ownerSeat == recipientSeat) return false;
        // Inspect only the giver's own published source regions; a forged ID
        // must not turn this legality query into a foreign-zone probe.
        var card = GetHand(_players[ownerSeat]).Concat(GetEquipment(ownerSeat)).SingleOrDefault(c => c.Id == cardId);
        if (card is null) return false;
        var from = _cardZones.GetLocation(cardId);
        var to = CardLocation.Hand(recipientSeat);
        return !(card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment) &&
            !IsGuanduThunderChariotDeparture(card, from, to) && NormalizeEphemeralCardDestination(card, from, to) == to;
    }

    private SkillProgramEffect LianZhuPausedEffect(ProgramSkillFrame frame) => ProgramInstructionResolver.Default
        .Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
    private bool UsesRecipientHpLianZhu(ProgramSkillFrame frame) => LianZhuPausedEffect(frame).NumberExpression == SkillProgramNumberExpression.RecipientCurrentHp;
    private static int LianZhuChoiceAmount(LianZhuReceipt receipt) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_LianZhuCounts.LianZhuChoiceAmount(receipt);

    private ProgramSkillFrame FreezeLianZhuChoiceAmount(ProgramSkillFrame frame)
    {
        if (!UsesRecipientHpLianZhu(frame)) return frame;
        var r = frame.LianZhu!;
        if (r.CountPolicy is not null) throw new InvalidOperationException("A gift recipient amount can be frozen only once after gift children.");
        var recipient = _players[r.RecipientSeat];
        if (!recipient.IsAlive || !recipient.HasHpValue || recipient.Hp <= 0)
            throw new InvalidOperationException("The gift recipient amount requires a living character's actual positive HP.");
        var count = new LianZhuChoiceAmountReceipt(recipient.Hp, recipient.Hp);
        var current = frame with { LianZhu = r with { CountPolicy = count }, PendingMovementContinuation = null };
        ReplaceRuntimeTop(current);
        AdvanceEventRulesAndQueueFact(new LianZhuChoiceAmountCapturedEvent(frame.Id, r.Source, r.GameplayHash,
            r.RecipientSeat, count.RecipientHp, count.Amount, r.GiftAfter));
        return current;
    }

    private void RequireLianZhuChoiceAmount(ProgramSkillFrame frame, LianZhuReceipt r)
    {
        var dynamic = UsesRecipientHpLianZhu(frame);
        if (!dynamic)
        {
            if (r.GiftFrom is not null || r.CountPolicy is not null)
                throw new InvalidOperationException("A fixed gift choice cannot acquire dynamic amount metadata.");
            return;
        }
        var activation = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Activation!;
        if (r.GiftFrom is not { OwnerSeat: var owner, Zone: var zone } || owner != frame.OwnerSeat ||
            !activation.SourceZones.Contains(zone) || zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("A dynamic gift lost its exact original HE source.");
        var counts = CompleteProgramEventHistory().OfType<LianZhuChoiceAmountCapturedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (r.CountPolicy is not { } count)
        {
            if (r.Stage != LianZhuStage.GiftChildren || counts.Length != 0)
                throw new InvalidOperationException("A dynamic recipient choice must retain its issued amount.");
            return;
        }
        if (r.Stage == LianZhuStage.GiftChildren || count.Amount <= 0 || count.Amount != count.RecipientHp ||
            counts is not [var captured] || captured.Source != r.Source || captured.GameplayHash != r.GameplayHash ||
            captured.RecipientSeat != r.RecipientSeat || captured.GiftAfter != r.GiftAfter ||
            captured.RecipientHp != count.RecipientHp || captured.Amount != count.Amount)
            throw new InvalidOperationException("A dynamic recipient choice changed its once-issued HP amount.");
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 22401)]
    private void RequireNativeLianZhuChoiceAmount(LianZhuChoiceAmountCapturedEvent captured)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LianZhu: { CountPolicy: { } count } r } frame ||
            frame.Id != captured.FrameId || frame.GameplayHash != captured.GameplayHash || r.Source != captured.Source ||
            !UsesRecipientHpLianZhu(frame) || r.Stage != LianZhuStage.RecipientChoice ||
            frame.PendingMovementContinuation is not null || frame.PendingRecoveryAttempts is { Count: > 0 } ||
            r.RecipientSeat != captured.RecipientSeat || r.GiftAfter != captured.GiftAfter ||
            count.RecipientHp != captured.RecipientHp || count.Amount != captured.Amount || count.Amount <= 0 ||
            !_players[r.RecipientSeat].IsAlive || !_players[r.RecipientSeat].HasHpValue ||
            _players[r.RecipientSeat].Hp != count.RecipientHp || count.Amount != count.RecipientHp ||
            _pendingHpChanges.Any(change => change.ParentFrameId == frame.Id) ||
            _pendingCardsMovedBatches.Any(batch => batch.ParentFrameId == frame.Id))
            throw new InvalidOperationException("A recipient HP amount must be issued at its exact drained native gift boundary.");
    }

    private PromptChoice ChooseLianZhuAiChoice(ProgramSkillFrame frame)
    {
        var choices = LianZhuChoices(frame);
        if (frame.LianZhu is not { CountPolicy: { } count } r) return choices.First();
        var view = CreateSnapshot(r.RecipientSeat);
        var recipient = view.Players.Single(p => p.Seat == r.RecipientSeat);
        var owned = recipient.Hand.Concat(recipient.Equipment).ToDictionary(card => card.Id);
        if (r.Stage == LianZhuStage.SelectingDiscard)
            return choices.OrderBy(choice => choice.Cards.Sum(id => CardCatalog.Get(owned[id].Kind).HandKeepValue))
                .ThenBy(choice => choice.Cards.Single()).First();
        var draw = choices.Single(choice => choice.Parameters["branch"] == "draw");
        var discard = choices.SingleOrDefault(choice => choice.Parameters["branch"] == "discard");
        if (discard is null || !_players[frame.OwnerSeat].IsAlive) return draw;
        var brain = _aiBrains.GetValueOrDefault(r.RecipientSeat) ?? new SimpleAiBrain(r.RecipientSeat,
            unchecked(_options.Seed * 397) ^ (r.RecipientSeat + 1), _options.AiPolicyVersion);
        var feedCost = brain.ScoreCardEffectIntervention(view, CardKind.FiveGrains, frame.OwnerSeat) * count.Amount;
        var discardCost = LianZhuDiscardOptions(frame).Select(choice => CardCatalog.Get(owned[choice.Cards.Single()].Kind).HandKeepValue)
            .Order().Take(count.Amount).Sum();
        return feedCost > discardCost ? discard : draw;
    }
}
