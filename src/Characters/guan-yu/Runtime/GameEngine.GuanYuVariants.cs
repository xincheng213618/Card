namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class GuanYuVariantCapability(GameEngine engine) : IGuanYuVariantHost
    {
        public SkillProgramStepOutcome ExecuteGuanYuVariant(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.ExecuteGuanYuVariant(frame, op);
    }

    private GuanPhaseFocusEvent? GuanPhaseFocus(int owner) => CompleteProgramEventHistory().OfType<GuanPhaseFocusEvent>()
        .LastOrDefault(e => e.OwnerSeat == owner && e.TurnNumber == _turnNumber && e.PhaseSerial == _cardUseDebitPhaseInstanceId && _phase == TurnPhase.Play && _currentSeat == owner);
    private int GuanFocusedSlashCount(GuanPhaseFocusEvent focus) => CompleteProgramEventHistory().OfType<GuanFocusedSlashEvent>()
        .Count(e => e.OwnerSeat == focus.OwnerSeat && e.TargetSeat == focus.TargetSeat && e.TurnNumber == focus.TurnNumber && e.PhaseSerial == focus.PhaseSerial);
    private bool GuanFocusAllowsSlash(int owner, int target) => GuanPhaseFocus(owner) is { } focus && focus.TargetSeat == target && GuanFocusedSlashCount(focus) < 3;
    private bool GuanFocusProhibitsSlash(int owner, int target, CardKind kind) => IsSlashCard(kind) && GuanPhaseFocus(owner) is { } focus && focus.TargetSeat == target && GuanFocusedSlashCount(focus) >= 3;
    private bool GuanSparedThisTurn(int owner, int target) => CompleteProgramEventHistory().OfType<GuanLethalSparedEvent>()
        .Any(e => e.OwnerSeat == owner && e.TargetSeat == target && e.TurnNumber == _turnNumber);
    private bool GuanOpeningUseExists(int owner, bool red) => CompleteProgramEventHistory().OfType<GuanOpeningUseEvent>()
        .Any(e => e.OwnerSeat == owner && e.TurnNumber == _turnNumber && (red ? e.IsRed : e.IsBasic));
    private bool GuanFirstBasicAvailable(CharacterState owner, CardKind kind) => HasRuntimeSkill(owner, "mou-ol:duoshou") &&
        CardUseCategoryCatalog.Get(kind) == CardUseCategories.Basic && !GuanOpeningUseExists(owner.Seat, false);
    private bool GuanFirstRedAvailable(CharacterState owner, Suit? suit) => suit is Suit.Heart or Suit.Diamond && HasRuntimeSkill(owner, "mou-ol:duoshou") && !GuanOpeningUseExists(owner.Seat, true);
    private bool GuanFirstRedDistance(CharacterState owner, Card card, long? useId = null) =>
        useId is { } id ? LifecycleCardUse(id)?.GuanFirstRedDistance == true : GuanFirstRedAvailable(owner, EffectiveSuit(owner, card));
    private bool GuanConversionAvailable(int owner, string skill) => skill != "mou-ol:weilin" || !EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent)
        .OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == owner && e.Action.Type == CardActionType.Use &&
            e.Action.ConversionChain.Any(s => s.OwnerSeat == owner && s.SkillId == skill && s.BindingId != "weilin-hand-slash"));
    private DemonRoundFocusEvent? DemonFocus(int target) => CompleteProgramEventHistory().OfType<DemonRoundFocusEvent>()
        .LastOrDefault(e => e.TargetSeat == target && e.RoundNumber == _roundNumber);
    private FactionResponsePolicySource? GetDemonDodgeRequest(CharacterState owner, CardKind kind) =>
        kind == CardKind.Dodge && DemonFocus(owner.Seat) is { } focus && GetFactionProviderSeats(owner.Seat, "magic-any").Count > 0
            ? new(focus.Source.SkillId, focus.Source.SkillInstanceId, "round-aid", "magic-any", kind, 0, 0) : null;
    private IReadOnlyList<int> ExclusiveSupportTargets(int recipient, string? skill = null) => CompleteProgramEventHistory().OfType<DemonSupportGrantedEvent>()
        .Where(e => e.RecipientSeat == recipient && (skill is null || e.SkillId == skill) && _players[e.OwnerSeat].IsAlive)
        .Select(e => e.OwnerSeat).Distinct().Order().ToArray();

    private CardActionContext GuanParentAction(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext?.CardUse ?? throw new InvalidOperationException("Guan rule requires its actual card-action context.");
        var parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == frame.WindowContext!.ParentFrameId);
        if (parent is null || parent.Action.ActionId != context.CardActionId || parent.Action.ActorSeat != context.ActorSeat)
            throw new InvalidOperationException("Guan rule lost its exact card-action parent.");
        return parent.Action;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 1000)]
    private bool CanOfferGuanVariant(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        var owner = candidate.OwnerSeat;
        if (candidate.SkillId == "magic:hanguo" && trigger.Id == "aid-reward")
        {
            if (context.CardUse is not { ActorSeat: var defender } || DemonFocus(defender)?.Source.OwnerSeat != owner) return false;
            var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId);
            return window is { Continuation: ProgramCardContinuation.FactionDefenseDodge } && window.Action.ProviderSeat != defender;
        }
        if (candidate.SkillId == "magic:longyin")
            return context.CardUse is { ActorSeat: var actor } && actor == _currentSeat && _phase == TurnPhase.Play &&
                ExclusiveSupportTargets(owner, candidate.SkillId).Contains(actor) && GetHand(_players[owner]).Count + GetEquipment(_players[owner]).Count > 0;
        var ops = trigger.Effects.Select(e => e.Op).ToArray();
        if (ops.Contains(SkillProgramEffectOp.HoldRoundFocusCards))
            return _roundTurnSeats.Count == 1 && !CompleteProgramEventHistory().OfType<DemonRoundFocusEvent>()
                .Any(e => e.Source.OwnerSeat == owner && e.RoundNumber == _roundNumber);
        if (ops.Contains(SkillProgramEffectOp.SpareFirstLethalTarget))
            return context.SourceSeat == owner && owner == _currentSeat && context.TargetSeat is { } target && target != owner &&
                context.Amount >= _players[target].Hp && !CompleteProgramEventHistory().OfType<GuanLethalSparedEvent>().Any(e => e.OwnerSeat == owner && e.TargetSeat == target);
        if (ops.Contains(SkillProgramEffectOp.RecordPhaseFocusedSlash))
            return GuanPhaseFocus(owner) is { } focus && context.CardUse?.DesignatedTargetSeats?.Contains(focus.TargetSeat) == true;
        if (ops.Contains(SkillProgramEffectOp.CancelSparedCardTargets))
            return context.CardUse?.DesignatedTargetSeats?.Any(t => GuanSparedThisTurn(owner, t)) == true;
        if (ops.Contains(SkillProgramEffectOp.RewriteWeilinTargetHand))
            return context.CardUse is { } use && CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e =>
                e.Action.ActionId == use.CardActionId && e.Action.ConversionChain.Any(s => s.SkillId == candidate.SkillId && s.OwnerSeat == owner && s.BindingId != "weilin-hand-slash"));
        if (ops.Contains(SkillProgramEffectOp.KillRoundFocusSlashVictim))
            return context.TargetSeat is { } victim && DemonFocus(victim) is { } focus && focus.Source.OwnerSeat == owner &&
                context.SourceSeat == owner && CurrentDamageAttempt is { IsChainPropagation: false, IsSourceLess: false, EffectiveCardKind: { } kind } && IsSlashCard(kind);
        return true;
    }

    private SkillProgramStepOutcome ExecuteGuanYuVariant(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.SkillId != supplied.SkillId || frame.GameplayHash != _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash)
            throw new InvalidOperationException("Guan rule lost its original content binding.");
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        switch (op)
        {
            case SkillProgramEffectOp.ArmPhaseSlashFocus:
                if (frame.SelectedTargetSeats is not [var target] || _phase != TurnPhase.Play || frame.OwnerSeat != _currentSeat || _players[target].Role == Role.Lord)
                    throw new InvalidOperationException("Slash focus requires its own actual Play phase and non-lord target.");
                AdvanceEventRulesAndQueueFact(new GuanPhaseFocusEvent(frame.Id, frame.OwnerSeat, frame.SkillId, target, _turnNumber, _cardUseDebitPhaseInstanceId));
                break;
            case SkillProgramEffectOp.RecordPhaseFocusedSlash:
                var focus = GuanPhaseFocus(frame.OwnerSeat) ?? throw new InvalidOperationException("Focused Slash lost its phase lease.");
                var slash = GuanParentAction(frame);
                if (slash.ActorSeat != frame.OwnerSeat || !IsSlashCard(slash.EffectiveKind) || !slash.EffectiveDesignatedTargetSeats.Contains(focus.TargetSeat))
                    throw new InvalidOperationException("Focused Slash requires the accepted designated target.");
                if (!CompleteProgramEventHistory().OfType<GuanFocusedSlashEvent>().Any(e => e.ActionId == slash.ActionId && e.OwnerSeat == frame.OwnerSeat))
                {
                    AdvanceEventRulesAndQueueFact(new GuanFocusedSlashEvent(slash.ActionId, frame.OwnerSeat, focus.TargetSeat, _turnNumber, _cardUseDebitPhaseInstanceId));
                    RefundProgramCardUseDebit(frame);
                    DrawProgramCards(frame.Id, frame.OwnerSeat, IsClassicIdentityMode ? 2 : 1, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.focused-slash.draw"));
                }
                break;
            case SkillProgramEffectOp.SpareFirstLethalTarget:
                if (frame.WindowContext?.TargetSeat is not { } victim || _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w => w.Id == frame.WindowContext.ParentFrameId) is not { } damage || damage.SourceSeat != frame.OwnerSeat || damage.Amount < _players[victim].Hp)
                    throw new InvalidOperationException("Lethal prevention lost its actual source damage window.");
                PreventProgramCurrentDamage(frame);
                AdvanceEventRulesAndQueueFact(new GuanLethalSparedEvent(frame.Id, frame.OwnerSeat, victim, _turnNumber));
                break;
            case SkillProgramEffectOp.CancelSparedCardTargets:
                var action = GuanParentAction(frame);
                var use = LifecycleCardUse(frame.WindowContext!.CardUse!.ParentCardUseFrameId) ?? throw new InvalidOperationException("Spared target lost its card use.");
                var canceled = action.EffectiveDesignatedTargetSeats.Where(t => GuanSparedThisTurn(frame.OwnerSeat, t));
                ReplaceRuntimeFrame(use.Id, use with { IneffectiveTargetSeats = Array.AsReadOnly((use.IneffectiveTargetSeats ?? []).Concat(canceled).Distinct().ToArray()) });
                break;
            case SkillProgramEffectOp.RecordOpeningTurnUse:
                var opening = GuanParentAction(frame);
                if (opening.ActorSeat != frame.OwnerSeat) throw new InvalidOperationException("Opening use changed actor.");
                var firstBasic = !GuanOpeningUseExists(frame.OwnerSeat, false) && CardUseCategoryCatalog.Get(opening.EffectiveKind) == CardUseCategories.Basic;
                var weilin = opening.ConversionChain.Any(s => s.SkillId == "mou-ol:weilin" && s.OwnerSeat == frame.OwnerSeat && s.BindingId != "weilin-hand-slash");
                if (!CompleteProgramEventHistory().OfType<GuanOpeningUseEvent>().Any(e => e.ActionId == opening.ActionId && e.OwnerSeat == frame.OwnerSeat))
                {
                    AdvanceEventRulesAndQueueFact(new GuanOpeningUseEvent(opening.ActionId, frame.OwnerSeat, _turnNumber, opening.EffectiveIsRed == true, CardUseCategoryCatalog.Get(opening.EffectiveKind) == CardUseCategories.Basic, weilin));
                    if (firstBasic)
                    {
                        RefundProgramCardUseDebit(frame);
                        if (opening.EffectiveKind == CardKind.Alcohol && _phase == TurnPhase.Play && _currentSeat == frame.OwnerSeat)
                            _players[frame.OwnerSeat].UsedPlayPhaseAlcoholThisTurn = false;
                    }
                }
                break;
            case SkillProgramEffectOp.RewriteWeilinTargetHand:
                var converted = GuanParentAction(frame);
                var appearance = converted.EffectiveIsRed;
                if (appearance is null || converted.EffectiveSuit == Suit.None) break;
                var conversionSource = converted.ConversionChain.Single(s => s.SkillId == frame.SkillId && s.OwnerSeat == frame.OwnerSeat);
                var recipients = converted.EffectiveKind == CardKind.Alcohol ? new[] { frame.OwnerSeat } : converted.EffectiveDesignatedTargetSeats;
                foreach (var seat in recipients.Distinct())
                    AdvanceEventRulesAndQueueFact(new WeilinHandColorEvent(converted.ActionId, conversionSource, seat, _turnNumber, appearance.Value));
                break;
            case SkillProgramEffectOp.HoldRoundFocusCards:
                if (frame.SelectedTargetSeats is not [var heldTarget] || _roundNumber <= 0 || heldTarget == frame.OwnerSeat)
                    throw new InvalidOperationException("Round hold requires one other living target.");
                AdvanceEventRulesAndQueueFact(new DemonRoundFocusEvent(frame.Id, source, heldTarget, _roundNumber));
                foreach (var zone in new[] { CardLocation.Hand(heldTarget), CardLocation.Equipment(heldTarget), CardLocation.Judgment(heldTarget) })
                    foreach (var card in _cardZones.CardsAt(zone).ToArray())
                    {
                        MoveCard(card, zone, CardLocation.RoundHold(heldTarget), new("skill-program.hanguo.hold"));
                    }
                break;
            case SkillProgramEffectOp.KillRoundFocusSlashVictim:
                if (frame.WindowContext?.TargetSeat is not { } killed || !_players[killed].IsAlive) break;
                BeginPlayerDeath(frame.Id, _players[killed], null, null, null, causingProgramSkillFrameId: frame.Id);
                return SkillProgramStepOutcome.AwaitChild;
            case SkillProgramEffectOp.GrantExclusiveRende:
            case SkillProgramEffectOp.GrantExclusiveQingnang:
            case SkillProgramEffectOp.GrantExclusiveLongyin:
                if (frame.SelectedTargetSeats is [var recipient])
                {
                    var id = op switch { SkillProgramEffectOp.GrantExclusiveRende => "magic:rende", SkillProgramEffectOp.GrantExclusiveQingnang => "magic:qingnang", _ => "magic:longyin" };
                    AcquireRuntimeSkills(_players[recipient], $"magic:weiwo:{frame.OwnerSeat}", [id]);
                    AdvanceEventRulesAndQueueFact(new DemonSupportGrantedEvent(frame.Id, frame.OwnerSeat, recipient, id));
                }
                ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { SelectedTargetSeats = [] });
                break;
            default: throw new InvalidOperationException("Unsupported Guan operation.");
        }
        return SkillProgramStepOutcome.Continue;
    }

    private ProgramCardIdentityMatch? WeilinIdentity(CharacterState owner, Card card)
    {
        if (_cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat)) return null;
        var suit = EffectiveSuit(owner, card);
        if (suit == Suit.None) return null;
        var policy = EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent).OfType<WeilinHandColorEvent>().LastOrDefault(e => e.TargetSeat == owner.Seat && e.TurnNumber == _turnNumber && e.IsRed == IsRedSuit(suit));
        return policy is null ? null : new(_contentRegistry.GetSkill(policy.Source.SkillId).Program!,
            new SkillProgramCardIdentity("weilin-hand-slash", [CardZoneKind.Hand], [], [], CardKind.Slash, new(SkillProgramConditionKind.Always, 0, [])),
            policy.Source with { BindingId = "weilin-hand-slash" });
    }

    [ProgramCardLegality(ProgramCardLegalityStage.NamedConversionBan, 20)]
    private bool GuanConversionUnavailable(ProgramConfiguredViewAsQuery query) =>
        !GuanConversionAvailable(query.Owner.Seat, query.Instance.SkillId);

    private void ReturnDemonRoundHolds()
    {
        foreach (var player in _players)
        {
            var held = _cardZones.CardsAt(CardLocation.RoundHold(player.Seat)).ToArray();
            if (held.Length > 0) MoveCards(held, CardLocation.RoundHold(player.Seat), player.IsAlive ? CardLocation.Hand(player.Seat) : CardLocation.DiscardPile, new("skill-program.hanguo.return"));
        }
    }
}

public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool GuanFirstRedDistance { get; init; }

}
