using System.Text.Json.Serialization;

namespace CardGame.Core;

internal static class MantianGuohaiGiftContract
{
    internal static bool Matches(IReadOnlyList<SkillProgramEffect> effects) => effects is
        [{ Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner, Amount: 1,
            ResultBind: "given", Zones: [CardZoneKind.Hand], MinimumCards: 0, MaximumCards: 0,
            Condition.Kind: SkillProgramConditionKind.Always },
         { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: "given",
            ExceptBind: null, Destination: SkillProgramCardDestination.SelectedTargetHand, AwaitMovementTriggers: true,
            Condition.Kind: SkillProgramConditionKind.Always }];
}

public sealed record MantianGuohaiGiftState
{
    private readonly IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> GiftTargets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public int GiftIndex { get; init; }
    public int EffectRound { get; init; }
    public long? ProgramFrameId { get; init; }
    public bool Complete { get; init; }
}
public sealed record MantianGuohaiGiftReturn(long CardUseFrameId, long ActionId, int GiftIndex, int RecipientSeat);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MantianGuohaiGiftState? MantianGuohaiGift { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MantianGuohaiGiftReturn? MantianGuohaiReturn { get; init; }
}

public sealed partial class GameEngine
{
    private const string MantianGuohaiGiftSkill = "mobile:mantian-guohai-gift";
    private static string MantianGuohaiGiftGrant(long useId, int index) => $"card-use:mantian-guohai:{useId}:{index}";
    private string MantianGuohaiGiftSource(long useId, int index) =>
        $"turn:{_turnNumber}:{_currentSeat}:{MantianGuohaiGiftGrant(useId, index)}";

    private IReadOnlyList<int> CreateMobileShiMantianGuohaiCards(int seat)
    {
        var ids = new List<int>();
        foreach (var suit in new[] { Suit.Heart, Suit.Diamond })
        {
            var id = checked(CreateCardZoneDiagnostics().Select(c => c.CardId).DefaultIfEmpty().Max() + 1);
            var card = new Card(id, CardKind.MantianGuohai, suit, 5);
            _cardZones.AddGeneratedCard(card);
            _generatedPhysicalCardIds.Add(id);
            MoveCard(card, CardLocation.OutsideGame, CardLocation.Hand(seat), new("skill-program.mobile-shi-dong-zhao.opening"));
            ids.Add(id);
        }
        return Array.AsReadOnly(ids.ToArray());
    }

    private void AddMantianGuohaiActions(List<LegalAction> actions, CharacterState actor, IEnumerable<Card> playableCards)
    {
        foreach (var card in playableCards.Where(c => c.Kind == CardKind.MantianGuohai))
        {
            if (IsCardUseForbidden(actor.Seat, card.Kind, CardActionType.Use) ||
                !TargetRules.AllowsNativeUseMaterial(new(actor.Seat, card.Kind, [card.Id], null, null))) continue;
            var targets = _players.Where(target => target.IsAlive && target.Seat != actor.Seat &&
                GetHand(target).Count + GetEquipment(target).Count + GetJudgment(target).Count > 0 &&
                !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, card.Kind) &&
                !IsCardTargetProhibited(target, card.Kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) &&
                !HasBeneficiarySuitShield(actor.Seat, target.Seat, EffectiveSuit(actor, card))).Select(t => t.Seat).ToArray();
            foreach (var first in targets)
            {
                actions.Add(new(LegalActionKind.MantianGuohai, card.Id, first,
                    $"对 {_players[first].Name} 使用【瞒天过海】"));
                foreach (var second in targets.Where(t => t != first))
                    actions.Add(new(LegalActionKind.MantianGuohai, card.Id, first,
                        $"对 {_players[first].Name}、{_players[second].Name} 使用【瞒天过海】", TargetSeats: [first, second]));
            }
        }
    }

    private void ResolveMantianGuohai(CharacterState source, Card card, LegalAction action)
    {
        if (!BuildLegalActions(source, selectedPhysicalCardId: card.Id).Any(candidate =>
            candidate.Kind == LegalActionKind.MantianGuohai && candidate.CardId == card.Id &&
            candidate.TargetSeats.SequenceEqual(action.TargetSeats)))
            throw new InvalidOperationException("Mantian Guohai lost its legal material or ordered targets.");
        var id = BeginCardUse(card, source.Seat, action.TargetSeats);
        UpdateLifecycleCardUse(id, use => use with { MantianGuohaiGift = new() });
        MoveCard(card, FindOwnedCardLocation(source, card), CardLocation.Processing, CardMoveReasons.Use,
            disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, source.Seat, action.TargetSeats, LegalActionKind.MantianGuohai);
    }

    private bool IsMantianGuohaiUse(long id) => LifecycleCardUse(id) is { CardKind: CardKind.MantianGuohai,
        MantianGuohaiGift: not null, Action: { EffectiveKind: CardKind.MantianGuohai } };
    private bool KeepMantianGuohaiMaterial(long id) => LifecycleCardUse(id) is { MantianGuohaiGift: { Complete: false } };

    private void PrepareMantianGuohaiEffectRound(long id)
    {
        if (LifecycleCardUse(id) is not { MantianGuohaiGift: { Complete: true } state } use) return;
        if (!IsMantianGuohaiUse(id) || !IsExtraCardEffectRound(id) || use.TargetIndex != 0 ||
            state.ProgramFrameId is not null || state.GiftIndex != state.GiftTargets.Count ||
            _resolutionStack.LastOrDefault()?.Id != id)
            throw new InvalidOperationException("Mantian Guohai cannot repeat before its original gifts finish.");
        UpdateLifecycleCardUse(id, current => current with
        { MantianGuohaiGift = new() { EffectRound = checked(state.EffectRound + 1) } });
    }

    private void ResolveMantianGuohaiEffect(NullificationWindowFrame pending)
    {
        var use = LifecycleCardUse(pending.ParentFrameId) ?? throw new InvalidOperationException("Mantian Guohai lost its native use.");
        var target = GetNullificationTargetSeat(pending) ?? throw new InvalidOperationException("Mantian Guohai lost its target.");
        if (!IsMantianGuohaiUse(use.Id) || use.MantianGuohaiGift is not { Complete: false, ProgramFrameId: null } state ||
            state.GiftTargets.Contains(target) || use.Action is null || !use.Action.TargetSeats.Contains(target))
            throw new InvalidOperationException("Mantian Guohai changed its acquisition cursor.");
        UpdateLifecycleCardUse(use.Id, current => current with
        { MantianGuohaiGift = state with { GiftTargets = state.GiftTargets.Append(target).ToArray() } });
        ResolveTargetCardEffect(pending);
    }

    // All non-nullified acquisitions finish before the first gift. Each gift is
    // an ordinary owned-card selection and awaited movement on its own child.
    private bool TryContinueMantianGuohaiGifts(long id)
    {
        if (LifecycleCardUse(id) is not { MantianGuohaiGift: { Complete: false } state } use) return false;
        if (!IsMantianGuohaiUse(id) || state.ProgramFrameId is not null || _resolutionStack.LastOrDefault()?.Id != id)
            throw new InvalidOperationException("Mantian Guohai cannot advance above an unfinished child.");
        var next = state.GiftIndex;
        while (next < state.GiftTargets.Count && (!_players[state.GiftTargets[next]].IsAlive ||
            !_players[use.SourceSeat].IsAlive || GetHand(_players[use.SourceSeat]).Count == 0 || _winner != Winner.None)) next++;
        if (next == state.GiftTargets.Count)
        {
            UpdateLifecycleCardUse(id, current => current with
            { MantianGuohaiGift = state with { GiftIndex = next, Complete = true } });
            return false;
        }
        var program = _contentRegistry.GetSkill(MantianGuohaiGiftSkill).Program!;
        var grant = MantianGuohaiGiftGrant(id, next);
        _players[use.SourceSeat].SkillGrants.Grant(new(grant, MantianGuohaiGiftSkill, grant, MantianGuohaiGiftSource(id, next),
            TurnExpiry: new(_turnNumber, _currentSeat)));
        var frameId = ++_resolutionSequence;
        UpdateLifecycleCardUse(id, current => current with
        { Step = ResolutionFrameStep.ResolvingEffect, MantianGuohaiGift = state with { GiftIndex = next, ProgramFrameId = frameId } });
        var frame = new ProgramSkillFrame(frameId, use.SourceSeat, MantianGuohaiGiftSkill, "card-effect",
            program.GameplayHash, 0, [], Array.AsReadOnly(new[] { state.GiftTargets[next] }))
        {
            SkillInstanceId = grant,
            MantianGuohaiReturn = new(id, use.Action!.ActionId, next, state.GiftTargets[next])
        };
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId));
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 47700, SkillProgramEffectOp.SelectOwnedCards)]
    private bool AllowsMantianGuohaiGiftActivation(ProgramActivationLegalityContext gate) => gate.SkillId != MantianGuohaiGiftSkill;

    private bool IsMantianGuohaiGiftProgram(ProgramSkillFrame frame)
    {
        if (frame.MantianGuohaiReturn is not { } receipt || frame.SkillId != MantianGuohaiGiftSkill ||
            frame.TriggerId is not null || frame.ActivationId != "card-effect" || frame.SelectedCardIds.Count != 0 ||
            !frame.SelectedTargetSeats.SequenceEqual(new[] { receipt.RecipientSeat }) ||
            frame.SkillInstanceId != MantianGuohaiGiftGrant(receipt.CardUseFrameId, receipt.GiftIndex) ||
            LifecycleCardUse(receipt.CardUseFrameId) is not { MantianGuohaiGift: { Complete: false } state, Action: { } action } use ||
            !IsMantianGuohaiUse(use.Id) || state.ProgramFrameId != frame.Id || state.GiftIndex != receipt.GiftIndex ||
            action.ActionId != receipt.ActionId || frame.OwnerSeat != use.SourceSeat ||
            state.GiftIndex < 0 || state.GiftIndex >= state.GiftTargets.Count || state.GiftTargets[state.GiftIndex] != receipt.RecipientSeat)
            return false;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        return index > 0 && _resolutionStack[index - 1].Id == use.Id &&
            _players[frame.OwnerSeat].SkillGrants.Grants.Any(grant => grant.GrantId == frame.SkillInstanceId &&
                grant.SkillId == frame.SkillId && grant.SkillInstanceId == frame.SkillInstanceId &&
                grant.SourceId == MantianGuohaiGiftSource(receipt.CardUseFrameId, receipt.GiftIndex) &&
                grant.TurnExpiry == new SkillGrantTurnExpiry(_turnNumber, _currentSeat));
    }

    private bool TryCompleteMantianGuohaiGift(ProgramSkillFrame frame, bool completed)
    {
        if (frame.MantianGuohaiReturn is not { } receipt) return false;
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || !IsMantianGuohaiGiftProgram(frame))
            throw new InvalidOperationException("Mantian Guohai lost its issued gift return.");
        AdvanceEventRulesAndQueueFact(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        _players[frame.OwnerSeat].SkillGrants.RemoveGrant(frame.SkillInstanceId);
        UpdateLifecycleCardUse(receipt.CardUseFrameId, use => use with
        { MantianGuohaiGift = use.MantianGuohaiGift! with { GiftIndex = receipt.GiftIndex + 1, ProgramFrameId = null } });
        var owner = LifecycleCardUse(receipt.CardUseFrameId)!;
        FinishCardUse(owner.Id, GetTrickRepresentation(owner.Id, owner.CardId, requireProcessing: true), CardKind.MantianGuohai);
        return true;
    }

    private bool MantianGuohaiFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        child is ProgramSkillFrame { MantianGuohaiReturn: { } receipt } frame &&
        receipt.CardUseFrameId == parent.Id && IsMantianGuohaiGiftProgram(frame);
    private bool HasMantianGuohaiPrompt() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Any(frame => frame.MantianGuohaiReturn is not null && IsMantianGuohaiGiftProgram(frame));

    private void AssertMantianGuohaiState()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.MantianGuohaiReturn is not null))
            if (!IsMantianGuohaiGiftProgram(frame)) throw new InvalidOperationException("Mantian Guohai changed its gift parent.");
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.MantianGuohaiGift is not null))
        {
            var state = use.MantianGuohaiGift!;
            if (!IsMantianGuohaiUse(use.Id) || state.GiftTargets.Distinct().Count() != state.GiftTargets.Count ||
                state.GiftTargets.Any(t => !use.Action!.TargetSeats.Contains(t)) || state.GiftIndex < 0 || state.GiftIndex > state.GiftTargets.Count || state.EffectRound < 0 ||
                state.ProgramFrameId is { } program && !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == program && IsMantianGuohaiGiftProgram(f)))
                throw new InvalidOperationException("Mantian Guohai changed its frozen acquisition or gift cursor.");
        }
    }
}
