using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum DragonBoatCardEffectStage { Drawing, DrawingChildren, AwaitingDamage, Ranking, ChoosingDiscard, DiscardChildren, FinishingAttack }
public sealed record DragonBoatCardEffectReceipt(long ActionId, long NullificationFrameId, CardKind Kind,
    int TargetSeat, int TargetIndex, DragonBoatCardEffectStage Stage)
{
    private readonly IReadOnlyList<int> _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    public int RecipientIndex { get; init; }
    public int RequestedCount { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public long? AwaitingProgramFrameId { get; init; }
    public bool MostTreasures { get; init; }
    public bool LeastTreasures { get; init; }
    public int? DiscardCardId { get; init; }
    public CardLocation? DiscardFrom { get; init; }
    public int DiscardSequence { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DragonBoatCardEffectReceipt? DragonBoatCardEffect { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeDragonBoatCardEffectFrameId { get; init; }
}
public sealed record DragonBoatCardEffectStartedEvent(long UseFrameId, long ActionId, long NullificationFrameId,
    CardKind Kind, int TargetSeat, int TargetIndex, int RecipientCount, int RequestedCount) : IGameEvent;
public sealed record DragonBoatCardDrawAppliedEvent(long UseFrameId, long NullificationFrameId, int RecipientIndex,
    int RecipientSeat, int RequestedCount, int ActualDrawCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record DragonBoatAgainstCurrentRankingEvent(long UseFrameId, long NullificationFrameId,
    int TeamTreasures, bool Most, bool Least) : IGameEvent;

public sealed partial class GameEngine
{
    private static readonly IReadOnlyList<CardKind> DragonBoatOrdinaryTrickKinds = Array.AsReadOnly(new[]
        { CardKind.SameBoat, CardKind.StriveUpstream, CardKind.AgainstCurrent });
    private static bool IsDragonBoatCard(CardKind kind) => kind is CardKind.Zong or CardKind.RealgarWine or
        CardKind.SameBoat or CardKind.StriveUpstream or CardKind.AgainstCurrent;
    private static bool IsDragonBoatTrick(CardKind kind) => DragonBoatOrdinaryTrickKinds.Contains(kind);
    private static LegalActionKind DragonBoatCardAction(CardKind kind) => kind switch
    {
        CardKind.Zong => LegalActionKind.Zong, CardKind.RealgarWine => LegalActionKind.RealgarWine,
        CardKind.SameBoat => LegalActionKind.SameBoat, CardKind.StriveUpstream => LegalActionKind.StriveUpstream,
        CardKind.AgainstCurrent => LegalActionKind.AgainstCurrent,
        _ => throw new InvalidOperationException("This is not a Dragon Boat card.")
    };
    private bool DragonBoatCardAllies(int first, int second) => _players[first].TeamId is { } team && team == _players[second].TeamId;
    private bool IsDragonBoatRealgarWineQuotaUse(CardUseFrame use, CardActionContext action) =>
        IsDragonBoatActivity && use.CardKind == CardKind.RealgarWine && action.EffectiveKind == CardKind.RealgarWine &&
        action.Type == CardActionType.Use && action.ActorSeat == use.SourceSeat && action.ProviderSeat == use.SourceSeat &&
        use.TargetSeats is [var target] && target == use.SourceSeat && action.TargetSeats.SequenceEqual(use.TargetSeats);
    private int DragonBoatCardTeamScore(int seat) => DragonBoatTeamScore(_players[seat].TeamId ??
        throw new InvalidOperationException("A Dragon Boat card requires its actual public team."));
    private (bool Most, bool Least) DragonBoatCardRank(int seat)
    {
        var scores = _players.Select(p => p.TeamId).Where(id => id is not null).Distinct(StringComparer.Ordinal)
            .Select(id => DragonBoatTeamScore(id!)).ToArray();
        var score = DragonBoatCardTeamScore(seat);
        return (score == scores.Max(), score == scores.Min());
    }
    private bool CanBeDragonBoatAdditionalTarget(CardUseFrame use, CharacterState target) =>
        target.IsAlive && (use.CardKind switch
        {
            CardKind.SameBoat => DragonBoatCardAllies(use.SourceSeat, target.Seat),
            CardKind.AgainstCurrent => !DragonBoatCardAllies(use.SourceSeat, target.Seat),
            CardKind.Zong => DragonBoatCardAllies(use.SourceSeat, target.Seat) && IsRuleWounded(target),
            _ => false
        });
    private IReadOnlyList<int> DragonBoatGlobalTargets(CharacterState actor, Card card) => ApplyTurnCardGroupTargetRestrictions(actor,
        Enumerable.Range(0, _playerCount).Select(offset => _players[(actor.Seat + offset) % _playerCount])
            .Where(target => target.IsAlive && !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, CardKind.StriveUpstream) &&
                !IsCardTargetProhibited(target, CardKind.StriveUpstream, card.Suit, SuitColor(EffectiveSuit(actor, card))) &&
                !IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, CardKind.StriveUpstream) &&
                !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, CardKind.StriveUpstream) &&
                !HasBeneficiarySuitShield(actor.Seat, target.Seat, EffectiveSuit(actor, card)))
            .Select(target => target.Seat).ToArray());
    private IReadOnlyList<int>? DragonBoatExpectedNativeTargets(CharacterState actor, Card card, LegalAction action) =>
        (action.PlayedCardKind ?? card.Kind) switch
        {
            CardKind.StriveUpstream => DragonBoatGlobalTargets(actor, card),
            CardKind.RealgarWine => [actor.Seat],
            CardKind.Zong or CardKind.SameBoat or CardKind.AgainstCurrent => action.TargetSeats,
            _ => null
        };

    private void AddDragonBoatCardActions(List<LegalAction> actions, CharacterState actor, IEnumerable<Card> cards)
    {
        foreach (var card in cards.Where(card => IsDragonBoatCard(card.Kind)))
            AddDragonBoatPhysicalOrConvertedActions(actions, actor, card, card.Kind, null);
    }
    private void AddDragonBoatPhysicalOrConvertedActions(List<LegalAction> actions, CharacterState actor, Card card,
        CardKind kind, CardConversionSource? conversion)
    {
        if (IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return;
        if (kind == CardKind.StriveUpstream)
        {
            var targets = DragonBoatGlobalTargets(actor, card);
            if (targets.Count > 0) actions.Add(new(DragonBoatCardAction(kind), card.Id, null,
                "使用【力争上游】", conversion is null ? null : kind, TargetSeats: targets) { ConversionSource = conversion });
            return;
        }
        if (kind == CardKind.RealgarWine && (actor.HasAlcoholEffect ||
            !(HasWuLuKangDixianMaterialPrivilege(actor, CardKind.Alcohol, [card.Id], conversion) || SiruiIgnoresAlcoholLimit(actor, conversion) || CanUseJianjiOrdinaryAlcohol(actor, card.Id) || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) ||
                HasNextUnlimitedCard(actor) || HasMouWujingAllowance(actor) || HasFriendQihuiAllowance(actor) ||
                HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol)))) return;
        foreach (var target in _players.Where(p => p.IsAlive && (kind switch
        {
            CardKind.Zong => DragonBoatCardAllies(actor.Seat, p.Seat) && IsRuleWounded(p),
            CardKind.RealgarWine => p.Seat == actor.Seat,
            CardKind.SameBoat => DragonBoatCardAllies(actor.Seat, p.Seat),
            CardKind.AgainstCurrent => !DragonBoatCardAllies(actor.Seat, p.Seat),
            _ => false
        })))
        {
            if (IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) ||
                IsCardTargetProhibited(target, kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) ||
                IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, kind) ||
                HasBeneficiarySuitShield(actor.Seat, target.Seat, EffectiveSuit(actor, card))) continue;
            actions.Add(new(DragonBoatCardAction(kind), card.Id, kind == CardKind.RealgarWine ? null : target.Seat,
                DescribeConversion(conversion, $"对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】"),
                conversion is null ? null : kind) { ConversionSource = conversion });
        }
    }

    private void ResolveDragonBoatCard(CharacterState actor, Card card, LegalAction action)
    {
        var kind = action.PlayedCardKind ?? card.Kind;
        if (!IsDragonBoatCard(kind)) throw new InvalidOperationException("A Dragon Boat action lost its effective card name.");
        if (kind == CardKind.Zong)
        {
            var target = _players[action.TargetSeat ?? throw new InvalidOperationException("Zong needs its selected allied target.")];
            if (!DragonBoatCardAllies(actor.Seat, target.Seat) || !IsRuleWounded(target))
                throw new InvalidOperationException("Zong lost its living wounded public ally.");
            ResolveRecoveryCard(actor, target, card, "粽", CardKind.Zong, conversionSource: action.ConversionSource,
                physicalCards: card.Id == 0 ? [] : null);
            return;
        }
        var targets = kind == CardKind.StriveUpstream ? DragonBoatGlobalTargets(actor, card) : kind == CardKind.RealgarWine
            ? Array.AsReadOnly(new[] { actor.Seat }) : Array.AsReadOnly(new[] { action.TargetSeat ??
                throw new InvalidOperationException("A Dragon Boat trick needs its selected target.") });
        var id = BeginCardUse(card, actor.Seat, targets, kind, conversionSource: action.ConversionSource,
            physicalCardIds: card.Id == 0 ? [] : null);
        if (card.Id != 0) MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use,
            disclosure: CardMovementDisclosure.FaceUp);
        if (kind == CardKind.RealgarWine)
        {
            if (!HasIssuedFuRongUnlimitedUse(id, actor.Seat) && !HasIssuedRoundGainedBasicBonus(id, actor.Seat) && !HasIssuedSiruiUse(id, actor.Seat))
                RecordMouZhaJiangAwareAlcoholDebit(id, actor);
            BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Alcohol)); return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, targets, DragonBoatCardAction(kind), playedCardKind: kind);
    }

    private void ResolveDragonBoatTrickEffect(NullificationWindowFrame pending)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != pending.ParentFrameId ||
            !IsDragonBoatTrick(use.CardKind) || pending.EffectCardKind != use.CardKind ||
            pending.ActionKind != DragonBoatCardAction(use.CardKind) || use.DragonBoatCardEffect is not null ||
            use.Action is not { Type: CardActionType.Use } action || use.SourceSeat != pending.SourceSeat ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count ||
            GetNullificationTargetSeat(pending) is not { } target || use.TargetSeats[use.TargetIndex] != target)
            throw new InvalidOperationException("A Dragon Boat trick lost its exact native effect and target cursor.");
        if (!_players[target].IsAlive) { FinishCardUse(use.Id, GetNullificationEffectCard(pending), use.CardKind); return; }
        var rank = DragonBoatCardRank(use.SourceSeat);
        var recipients = use.CardKind == CardKind.SameBoat
            ? _players.Where(p => p.IsAlive && DragonBoatCardAllies(target, p.Seat)).OrderBy(p => (p.Seat - target + _playerCount) % _playerCount).Select(p => p.Seat).ToArray()
            : use.CardKind == CardKind.StriveUpstream ? [target] : Array.Empty<int>();
        var count = use.CardKind == CardKind.SameBoat ? rank.Most ? 2 : 1 :
            use.CardKind == CardKind.StriveUpstream ? target == use.SourceSeat ? Math.Min(5, DragonBoatCardTeamScore(use.SourceSeat)) : 1 : 0;
        var receipt = new DragonBoatCardEffectReceipt(action.ActionId, pending.Id, use.CardKind, target,
            use.TargetIndex, use.CardKind == CardKind.AgainstCurrent ? DragonBoatCardEffectStage.AwaitingDamage : DragonBoatCardEffectStage.Drawing)
        { Recipients = recipients, RequestedCount = count, AwaitingProgramFrameId = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is { } parent && IsAwaitingProgramMovement(parent) ? parent.Id : null };
        ReplaceRuntimeTop(use with { Step = ResolutionFrameStep.ResolvingEffect, DragonBoatCardEffect = receipt });
        AdvanceEventRulesAndQueueFact(new DragonBoatCardEffectStartedEvent(use.Id, action.ActionId, pending.Id,
            use.CardKind, target, use.TargetIndex, recipients.Length, count));
        if (use.CardKind == CardKind.AgainstCurrent)
        {
            var effectCard = GetNullificationEffectCard(pending);
            var attack = new CardAttackHandle(this, use.Id, use.SourceSeat, target, effectCard.Id == 0 ? null : effectCard,
                playedCardKind: use.CardKind, damageNatureOverride: DamageNature.Normal, physicalCards: GetCardUsePhysicalCards(use.Id));
            ActiveCardAttack = attack;
            if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
            return;
        }
        ContinueDragonBoatCardEffect(use.Id);
    }

    private bool TryCompleteDragonBoatAgainstCurrentAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { DragonBoatCardEffect: { Kind: CardKind.AgainstCurrent } r } use) return false;
        if (use.Action?.ActionId != r.ActionId || attack.EffectiveCardKind != CardKind.AgainstCurrent ||
            attack.CardUserSeat != use.SourceSeat || use.TargetIndex != r.TargetIndex || ActiveDying is not null)
            throw new InvalidOperationException("Against Current lost its exact completed native damage subtree.");
        if (r.Stage == DragonBoatCardEffectStage.FinishingAttack)
        { UpdateLifecycleCardUse(use.Id, current => current with { DragonBoatCardEffect = null }); return false; }
        if (r.Stage != DragonBoatCardEffectStage.AwaitingDamage || _resolutionStack.LastOrDefault()?.Id != use.Id)
            throw new InvalidOperationException("Against Current repeated its already returned native damage.");
        ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision();
        // ActiveCardAttack is a frame projection: clearing it replaced the
        // stored CardAttackState. Preserve that new inactive state instead of
        // restoring the captured pre-return frame while its tail owns prompts.
        use = LifecycleCardUse(use.Id)!;
        var rank = DragonBoatCardRank(use.SourceSeat);
        ReplaceRuntimeTop(use with { DragonBoatCardEffect = r with { Stage = DragonBoatCardEffectStage.Ranking,
            MostTreasures = rank.Most, LeastTreasures = rank.Least } });
        AdvanceEventRulesAndQueueFact(new DragonBoatAgainstCurrentRankingEvent(use.Id, r.NullificationFrameId,
            DragonBoatCardTeamScore(use.SourceSeat), rank.Most, rank.Least));
        ContinueDragonBoatCardEffect(use.Id); return true;
    }
    private bool IsDragonBoatZeroAgainstCurrentAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        attack.Card is null && attack.EffectiveCardKind == CardKind.AgainstCurrent && attack.PhysicalCards.Count == 0 &&
        LifecycleCardUse(attack.ResolutionId) is { CardKind: CardKind.AgainstCurrent, CardId: 0, PhysicalCardIds.Count: 0,
            Action: { Type: CardActionType.Use, EffectiveKind: CardKind.AgainstCurrent, PhysicalCards.Count: 0 } action } use &&
        IsIssuedTieredRoundUse(use, true) && action.ActorSeat == use.SourceSeat && attack.CardUserSeat == use.SourceSeat &&
        MatchesDeclaredActualDamageUse(use, action.ActorSeat, use.TieredRoundConversionUse!.ActorSeat) &&
        (use.TargetSeats.Contains(attack.TargetSeat) || attack.IsChainPropagation) && processing.Count == 0;

    private IReadOnlyList<Card> DragonBoatDiscardableCards(int seat) => GetHand(_players[seat]).Concat(GetEquipment(_players[seat]))
        .Where(card => !IsSelfHandCategoryDiscardForbidden(seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private void BeginDragonBoatDiscard(CardUseFrame use)
    {
        var choices = DragonBoatDiscardableCards(use.SourceSeat).Select(card => new PromptChoice(
            new ChoiceId($"dragon-boat.against-current.{use.Id}.{use.TargetIndex}.discard-{card.Id}"),
            $"弃置【{card.DisplayName}】", [card.Id], [], new Dictionary<string, string>
            { ["program-action"] = "dragon-boat-discard", ["frame-id"] = use.Id.ToString(CultureInfo.InvariantCulture) })).ToArray();
        _pendingDecision = new(DecisionKind.ProgramTrigger, use.SourceSeat, "【逆水行舟】己队龙船至宝并列最多，须弃置一张牌。",
            choices.SelectMany(c => c.Cards).ToArray(), [], use.SourceSeat, CardKind.AgainstCurrent)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices) };
        _status = _players[use.SourceSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private bool HasDragonBoatDiscardPrompt() => _resolutionStack.LastOrDefault() is CardUseFrame
        { DragonBoatCardEffect.Stage: DragonBoatCardEffectStage.ChoosingDiscard } use &&
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IncomingCard: CardKind.AgainstCurrent } p && p.PlayerSeat == use.SourceSeat;
    [ProgramAnswer("dragon-boat-discard")]
    private void ResolveDragonBoatDiscard(PromptChoice selected)
    {
        if (!HasDragonBoatDiscardPrompt() || _resolutionStack.LastOrDefault() is not CardUseFrame { DragonBoatCardEffect: { } r } use ||
            selected.Cards is not [var cardId] || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != use.Id.ToString(CultureInfo.InvariantCulture) ||
            !_pendingDecision!.Choices.Any(choice => choice.Id == selected.Id) ||
            DragonBoatDiscardableCards(use.SourceSeat).SingleOrDefault(card => card.Id == cardId) is not { } card)
            throw new InvalidOperationException("Against Current changed its exact mandatory self-discard choice.");
        var from = _cardZones.GetLocation(card.Id); ClearPendingDecision();
        ReplaceRuntimeTop(use with { DragonBoatCardEffect = r with { Stage = DragonBoatCardEffectStage.DiscardChildren,
            DiscardCardId = card.Id, DiscardFrom = from } });
        MoveLogicalDiscard(use.SourceSeat, use.SourceSeat, card, from, new("card.effect.dragon-boat.against-current"),
            movement => UpdateLifecycleCardUse(use.Id, current => current with
            { DragonBoatCardEffect = current.DragonBoatCardEffect! with { DiscardSequence = movement.Sequence } }));
        ContinueDragonBoatCardEffect(use.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 43000)]
    private bool TryAdvanceDragonBoatDiscardAi()
    {
        if (!HasDragonBoatDiscardPrompt() || _players[_pendingDecision!.PlayerSeat].IsHuman) return false;
        ResolveDragonBoatDiscard(_pendingDecision.Choices.OrderBy(choice => CardCatalog.Get(GetAdvancedCard(choice.Cards.Single()).Kind).HandKeepValue)
            .ThenBy(choice => choice.Cards.Single()).First());
        AdvanceRulesAndPublishState(); return true;
    }

    private void ContinueDragonBoatCardEffect(long id)
    {
        while (_resolutionStack.LastOrDefault() is CardUseFrame { DragonBoatCardEffect: { } r } use && use.Id == id)
        {
            if (_pendingDecision is not null) return;
            if (r.Stage is DragonBoatCardEffectStage.AwaitingDamage or DragonBoatCardEffectStage.FinishingAttack) return;
            if (r.Stage == DragonBoatCardEffectStage.Ranking)
            {
                if (_players[use.SourceSeat].IsAlive && r.MostTreasures && DragonBoatDiscardableCards(use.SourceSeat).Count > 0)
                { ReplaceRuntimeTop(use = use with { DragonBoatCardEffect = r with { Stage = DragonBoatCardEffectStage.ChoosingDiscard } }); BeginDragonBoatDiscard(use); return; }
                ReplaceRuntimeTop(use with { DragonBoatCardEffect = r with { Stage = DragonBoatCardEffectStage.DiscardChildren } }); continue;
            }
            if (r.Stage == DragonBoatCardEffectStage.ChoosingDiscard) { BeginDragonBoatDiscard(use); return; }
            if (r.Stage is DragonBoatCardEffectStage.DiscardChildren or DragonBoatCardEffectStage.DrawingChildren)
            {
                if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.DragonBoatCardEffect) ||
                    TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.DragonBoatCardEffect) ||
                    TryBeginHpChangedProgramWindow(id, PostEventContinuation.DragonBoatCardEffect) || TryBeginCardsMovedProgramWindow(id)) return;
                use = LifecycleCardUse(id)!; r = use.DragonBoatCardEffect!;
                ReplaceRuntimeTop(use with { DragonBoatCardEffect = r.Stage == DragonBoatCardEffectStage.DiscardChildren
                    ? r with { Stage = DragonBoatCardEffectStage.Drawing, Recipients = r.LeastTreasures ? [use.SourceSeat] : [], RequestedCount = 1 }
                    : r with { Stage = DragonBoatCardEffectStage.Drawing, RecipientIndex = r.RecipientIndex + 1, DrawOutcome = null } });
                continue;
            }
            if (r.Stage != DragonBoatCardEffectStage.Drawing) throw new InvalidOperationException("A Dragon Boat card lost its native effect stage.");
            if (r.RecipientIndex >= r.Recipients.Count)
            {
                if (use.CardKind == CardKind.AgainstCurrent)
                {
                    ReplaceRuntimeTop(use with { DragonBoatCardEffect = r with { Stage = DragonBoatCardEffectStage.FinishingAttack } });
                    var attack = new CardAttackHandle(this, id); ActiveCardAttack = attack; CompleteAttack(attack); return;
                }
                ReplaceRuntimeTop(use with { DragonBoatCardEffect = null, NativeDraw = null });
                FinishCardUse(id, GetTrickRepresentation(id, use.CardId, requireProcessing: true), use.CardKind); return;
            }
            var recipient = r.Recipients[r.RecipientIndex];
            if (use.NativeDraw is null)
                ReplaceRuntimeTop(use = use with { NativeDraw = new NativeDrawProducerReceipt(0, r.RequestedCount,
                    null, SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw)
                { Recipients = [recipient], ReturnKind = NativeDrawAttemptReturnKind.DragonBoatCardEffect } });
            var paid = use.NativeDraw!;
            if (paid.ActiveAttemptId is not null && !paid.AttemptReturned) throw new InvalidOperationException("A Dragon Boat draw resumed before its request returned.");
            if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(use)) return;
            var before = OutsidePhaseMovementSequence;
            var ids = paid.Replacement is null && _players[recipient].IsAlive && _winner == Winner.None
                ? DrawCards(_players[recipient], r.RequestedCount, true, CardMoveReasons.Draw) : Array.AsReadOnly(Array.Empty<int>());
            var outcome = new NativeDrawOutcome(recipient, r.RequestedCount, paid.ActiveAttemptId, before,
                OutsidePhaseMovementSequence, paid.Replacement) { ActualDrawIds = ids };
            ReplaceRuntimeTop(LifecycleCardUse(id)! with { NativeDraw = null, DragonBoatCardEffect = r with
                { Stage = DragonBoatCardEffectStage.DrawingChildren, DrawOutcome = outcome } });
            AdvanceEventRulesAndQueueFact(new DragonBoatCardDrawAppliedEvent(id, r.NullificationFrameId, r.RecipientIndex,
                recipient, r.RequestedCount, ids.Count, before, outcome.SequenceAfter));
        }
    }

    private bool IsDragonBoatCardEffectMovement(CardUseFrame use, CardMovementBatchContext batch) =>
        use.DragonBoatCardEffect is { } r && batch.ParentFrameId == use.Id && batch.AwaitingProgramFrameId == r.AwaitingProgramFrameId &&
        batch.Movements.Count > 0 && (r.Stage == DragonBoatCardEffectStage.DrawingChildren && r.DrawOutcome is { } outcome &&
            batch.Movements.All(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                (outcome.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(outcome.RecipientSeat) && m.Reason == CardMoveReasons.Draw ||
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle)) ||
            r.Stage == DragonBoatCardEffectStage.DiscardChildren && r.DiscardSequence > 0 && batch.Movements is [var move] &&
            move.Sequence == r.DiscardSequence && move.CardId == r.DiscardCardId && move.From == r.DiscardFrom && move.To == CardLocation.DiscardPile &&
            move.Reason == new CardMoveReason("card.effect.dragon-boat.against-current"));
    private bool IsDragonBoatCardEffectMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame frame) =>
        frame.ResumeDragonBoatCardEffectFrameId == use.Id && frame.Id == frame.Batch.Id && frame.ResumeProgramFrameId is null &&
        !HasAlternateProgramMovementReturn(frame with { ResumeDragonBoatCardEffectFrameId = null }) && IsDragonBoatCardEffectMovement(use, frame.Batch);
    private bool IsDragonBoatCardEffectStateReturn(CardUseFrame use, ProgramLifecycleTriggerWindowFrame frame) =>
        use.DragonBoatCardEffect is { Stage: DragonBoatCardEffectStage.DrawingChildren or DragonBoatCardEffectStage.DiscardChildren } &&
        frame.ResumeProgramFrameId == use.Id && frame.CharacterStateContinuation == CharacterStateContinuation.DragonBoatCardEffect &&
        frame.ResumeCardId is null && frame.ResumeCardKind is null &&
        ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == frame.Id && e.Change.ParentFrameId == use.Id) == 1;
    private bool IsDragonBoatCardEffectHpReturn(CardUseFrame use, HpChangedTriggerWindowFrame hp) =>
        use.DragonBoatCardEffect is { Stage: DragonBoatCardEffectStage.DrawingChildren or DragonBoatCardEffectStage.DiscardChildren } &&
        hp.Continuation == PostEventContinuation.DragonBoatCardEffect && hp.ResumeFrameId == use.Id &&
        hp.Change.ParentFrameId == use.Id && hp.CardId is null && hp.CardKind is null;
    private bool IsDragonBoatCardEffectRecoveryReturn(CardUseFrame use, RecoveryReplacementFrame recovery) =>
        recovery.ParentFrameId == use.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.DragonBoatCardEffect, use.Id) &&
        IsDragonBoatCardRecoveryAttempt(use, recovery.Attempt);
    private bool IsDragonBoatCardRecoveryAttempt(CardUseFrame use, RecoveryAttempt attempt) =>
        use.DragonBoatCardEffect is { Stage: DragonBoatCardEffectStage.DiscardChildren, DiscardCardId: { } card,
            DiscardFrom: { Zone: CardZoneKind.Equipment } from, DiscardSequence: > 0 } r &&
        from.OwnerSeat == use.SourceSeat && attempt.SourceSeat == use.SourceSeat && attempt.TargetSeat == use.SourceSeat &&
        attempt.Amount == 1 && attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.SilverLion,
            MoveReason: new("card.effect.dragon-boat.against-current")) &&
        _cardMovements.Any(m => m.Sequence == r.DiscardSequence && m.CardId == card && m.CardKind == CardKind.SilverLion &&
            m.From == from && m.To == CardLocation.DiscardPile && m.Reason == new CardMoveReason("card.effect.dragon-boat.against-current"));
    private bool HasDragonBoatCardEffectProcessing(IReadOnlyList<Card> processing)
    {
        var uses = _resolutionStack.OfType<CardUseFrame>().ToArray();
        if (!uses.Any(use => use.DragonBoatCardEffect is not null)) return false;
        foreach (var use in uses) AssertDragonBoatCardReceipt(use);
        var materials = uses.SelectMany(use => use.Action?.PhysicalCards.Select(card => card.CardId) ?? []).ToHashSet();
        return processing.All(card => materials.Contains(card.Id));
    }
    private void AssertDragonBoatCardReceipt(CardUseFrame use)
    {
        if (use.DragonBoatCardEffect is not { } r) return;
        if (!IsDragonBoatTrick(use.CardKind) || r.Kind != use.CardKind || use.Action?.ActionId != r.ActionId ||
            r.TargetIndex != use.TargetIndex || r.TargetIndex < 0 || r.TargetIndex >= use.TargetSeats.Count ||
            use.TargetSeats[r.TargetIndex] != r.TargetSeat || !Enum.IsDefined(r.Stage) ||
            r.Recipients is not System.Collections.IList { IsReadOnly: true } || r.Recipients.Any(seat => !IsValidPlayerSeat(seat)) ||
            r.Recipients.Distinct().Count() != r.Recipients.Count || r.RecipientIndex < 0 || r.RecipientIndex > r.Recipients.Count ||
            r.RequestedCount is < 0 or > 5 || ProgramEventHistory<DragonBoatCardEffectStartedEvent>().Count(e => e.UseFrameId == use.Id &&
                e.ActionId == r.ActionId && e.NullificationFrameId == r.NullificationFrameId && e.Kind == use.CardKind &&
                e.TargetSeat == r.TargetSeat && e.TargetIndex == r.TargetIndex) != 1)
            throw new InvalidOperationException("A Dragon Boat effect lost its exact action, resolved target or immutable recipient cursor.");
        if (use.NativeDraw is { } paid && (r.Stage != DragonBoatCardEffectStage.Drawing ||
            paid.ReturnKind != NativeDrawAttemptReturnKind.DragonBoatCardEffect || paid.RequestedCount != r.RequestedCount ||
            paid.Recipients is not [var seat] || r.RecipientIndex >= r.Recipients.Count || r.Recipients[r.RecipientIndex] != seat ||
            paid.RecipientIndex != 0 || paid.InstructionIndex != 0 || paid.ResultBind is not null || paid.Operation is not null ||
            paid.Reason != CardMoveReasons.Draw || paid.ActiveAttemptId is null && paid.AttemptReturned))
            throw new InvalidOperationException("A Dragon Boat card changed its already issued native draw request.");
        if (r.Stage == DragonBoatCardEffectStage.DrawingChildren)
        {
            if (r.DrawOutcome is not { } result || use.NativeDraw is not null || r.RecipientIndex >= r.Recipients.Count ||
                result.RecipientSeat != r.Recipients[r.RecipientIndex] || result.RequestedCount != r.RequestedCount ||
                result.SequenceBefore > result.SequenceAfter || result.SequenceAfter > OutsidePhaseMovementSequence ||
                result.ActualDrawIds.Count > r.RequestedCount || result.ActualDrawIds.Distinct().Count() != result.ActualDrawIds.Count ||
                result.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } ||
                result.Replacement is not null && result.ActualDrawIds.Count != 0 ||
                !_cardMovements.Where(m => m.Sequence > result.SequenceBefore && m.Sequence <= result.SequenceAfter &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(result.RecipientSeat) && m.Reason == CardMoveReasons.Draw)
                    .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(result.ActualDrawIds) ||
                ProgramEventHistory<DragonBoatCardDrawAppliedEvent>().Count(e => e.UseFrameId == use.Id && e.NullificationFrameId == r.NullificationFrameId &&
                    e.RecipientIndex == r.RecipientIndex && e.RecipientSeat == result.RecipientSeat && e.RequestedCount == r.RequestedCount &&
                    e.ActualDrawCount == result.ActualDrawIds.Count && e.SequenceBefore == result.SequenceBefore && e.SequenceAfter == result.SequenceAfter) != 1 ||
                result.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt &&
                    e.ParentFrameId == use.Id && e.RecipientSeat == result.RecipientSeat && e.RequestedCount == r.RequestedCount && e.Replaced == (result.Replacement is not null)) != 1)
                throw new InvalidOperationException("A Dragon Boat card lost its actual draw materials or exact replacement return.");
        }
        if (r.DiscardCardId is { } discarded && (r.DiscardFrom is not { } from || r.DiscardSequence <= 0 ||
            !_cardMovements.Any(m => m.Sequence == r.DiscardSequence && m.CardId == discarded && m.From == from && m.To == CardLocation.DiscardPile &&
                m.Reason == new CardMoveReason("card.effect.dragon-boat.against-current"))))
            throw new InvalidOperationException("Against Current lost its genuine once-paid self-discard movement.");
    }
    private void AssertDragonBoatCardState()
    { foreach (var use in _resolutionStack.OfType<CardUseFrame>()) AssertDragonBoatCardReceipt(use); }
}
