using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record HulaoAllianceFeastChoice(int TargetSeat, int TargetIndex, long ActionId);
public sealed record HulaoAllianceFeastBenefitEvent(long CardUseFrameId, int SourceSeat,
    int TargetSeat, string Benefit, int Amount) : IGameEvent;

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HulaoAllianceFeastChoice? AllianceFeastChoice { get; init; }
}

public sealed partial class GameEngine
{
    private void AddHulaoAllianceFeastActions(List<LegalAction> actions, CharacterState actor,
        IEnumerable<Card> playableCards)
    {
        if (IsCardUseForbidden(actor.Seat, CardKind.AllianceFeast, CardActionType.Use) ||
            _players.Any(target => target.IsAlive &&
                !IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, CardKind.AllianceFeast) &&
                IsDirectedCardTargetProhibited(actor.Seat, target.Seat, CardKind.AllianceFeast))) return;
        foreach (var card in playableCards.Where(card => card.Kind == CardKind.AllianceFeast))
            actions.Add(new LegalAction(LegalActionKind.AllianceFeast, card.Id, null,
                "使用【联军盛宴】：你摸存活角色数张牌，其他角色各选择回复1点体力或摸1张牌"));
    }

    private void ResolveAllianceFeast(CharacterState source, Card card)
    {
        if (!BuildLegalActions(source, selectedPhysicalCardId: card.Id).Any(action =>
                action.Kind == LegalActionKind.AllianceFeast && action.CardId == card.Id && action.TargetSeat is null))
            throw new InvalidOperationException("Alliance feast became illegal before native resolution.");
        var targets = ApplyTurnCardGroupTargetRestrictions(source, Enumerable.Range(0, _playerCount)
            .Select(offset => _players[TableStepSeat(source.Seat, offset)])
            .Where(target => target.IsAlive &&
                !IsShenwuRobeTargetProhibited(source.Seat, target.Seat, CardKind.AllianceFeast) &&
                !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, CardKind.AllianceFeast) &&
                !IsCardTargetProhibited(target, CardKind.AllianceFeast, card.Suit, SuitColor(EffectiveSuit(source, card))) &&
                !HasBeneficiarySuitShield(source.Seat, target.Seat, EffectiveSuit(source, card)))
            .Select(target => target.Seat).ToArray());
        var id = BeginCardUse(card, source.Seat, targets);
        MoveCard(card, FindOwnedCardLocation(source, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, source.Seat, targets, LegalActionKind.AllianceFeast);
    }

    private void ResolveAllianceFeastEffect(NullificationWindowFrame pending)
    {
        var use = LifecycleCardUse(pending.ParentFrameId) ??
            throw new InvalidOperationException("Alliance feast lost its native card use.");
        var targetSeat = GetNullificationTargetSeat(pending) ??
            throw new InvalidOperationException("Alliance feast requires one exact nullification target.");
        if (LeChengyanNativeEffectKind(use) != CardKind.AllianceFeast || use.Action is not { } action ||
            LeChengyanNativeTargets(use)[use.TargetIndex] != targetSeat || use.AllianceFeastChoice is not null)
            throw new InvalidOperationException("Alliance feast lost its native target cursor.");
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        if (!_players[targetSeat].IsAlive)
        { CompleteHulaoAllianceFeastTarget(use.Id); return; }
        if (targetSeat == use.SourceSeat)
        {
            var amount = _players.Count(player => player.IsAlive);
            DrawCards(_players[targetSeat], amount, log: true);
            AdvanceEventRulesAndQueueFact(new HulaoAllianceFeastBenefitEvent(use.Id, use.SourceSeat,
                targetSeat, "draw", amount));
            CompleteHulaoAllianceFeastTarget(use.Id);
            return;
        }

        UpdateLifecycleCardUse(use.Id, current => current with
        { AllianceFeastChoice = new(targetSeat, use.TargetIndex, action.ActionId) });
        var choices = new[] { "recover", "draw" }.Select(benefit => new PromptChoice(
            new ChoiceId($"hulao.alliance-feast.{use.Id}.{use.TargetIndex}.{benefit}"),
            benefit == "recover" ? "回复 1 点体力" : "摸 1 张牌", [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "hulao-alliance-feast",
                ["frame-id"] = use.Id.ToString(CultureInfo.InvariantCulture),
                ["target-index"] = use.TargetIndex.ToString(CultureInfo.InvariantCulture),
                ["benefit"] = benefit
            })).ToArray();
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, targetSeat,
            "【联军盛宴】请选择回复 1 点体力或摸 1 张牌。", [], [], use.SourceSeat, CardKind.AllianceFeast)
        { PromptId = CreatePromptId(), TargetSeat = targetSeat, IsPrivate = true,
            Choices = Array.AsReadOnly(choices) };
        _status = _players[targetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private bool HasHulaoAllianceFeastPrompt() =>
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IncomingCard: CardKind.AllianceFeast } decision &&
        _resolutionStack.LastOrDefault() is CardUseFrame { AllianceFeastChoice: { } choice } use &&
        LeChengyanNativeEffectKind(use) == CardKind.AllianceFeast && use.TargetIndex == choice.TargetIndex &&
        LeChengyanNativeTargets(use)[use.TargetIndex] == choice.TargetSeat && decision.PlayerSeat == choice.TargetSeat;

    [ProgramAnswer("hulao-alliance-feast")]
    private void ResolveHulaoAllianceFeastChoice(PromptChoice selected)
    {
        if (!HasHulaoAllianceFeastPrompt() ||
            _resolutionStack.LastOrDefault() is not CardUseFrame { AllianceFeastChoice: { } choice } use ||
            use.Action?.ActionId != choice.ActionId || selected.Cards.Count != 0 || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != use.Id.ToString(CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("target-index") != use.TargetIndex.ToString(CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("benefit") is not ("recover" or "draw") ||
            !_pendingDecision!.Choices.Any(option => option.Id == selected.Id))
            throw new InvalidOperationException("Alliance feast choice must name its exact native target and current prompt.");
        var benefit = selected.Parameters["benefit"];
        ClearPendingDecision();
        UpdateLifecycleCardUse(use.Id, current => current with { AllianceFeastChoice = null });
        var target = _players[choice.TargetSeat];
        if (target.IsAlive && benefit == "draw") DrawCards(target, 1, log: true);
        else if (target.IsAlive && target.HasHpValue && target.Hp < target.MaxHp && benefit == "recover")
        {
            var amount = ApplyIssuedCardRecoveryModifiers(use.Id, use.SourceSeat, target.Seat, 1);
            if (!TryQueueRecoveryReplacement(use.Id, use.SourceSeat, target.Seat, amount,
                new(RecoveryAttemptProducer.CardUse, CardId: use.CardId, CardKind: LeChengyanNativeEffectKind(use), Policies: [])))
            {
                var recoveryId = BeginRecovery(use.Id, use.SourceSeat, target.Seat, amount);
                try
                {
                    var actual = Math.Min(amount, target.MaxHp - target.Hp);
                    target.Hp += actual;
                    AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(use.SourceSeat, target.Seat,
                        actual, target.Hp));
                    AddLog("Recovered", $"{target.Name} 因【联军盛宴】回复 {actual} 点体力。",
                        use.SourceSeat, target.Seat);
                }
                finally { PopResolutionFrame(recoveryId, ResolutionFrameKind.Recovery); }
            }
        }
        AdvanceEventRulesAndQueueFact(new HulaoAllianceFeastBenefitEvent(use.Id, use.SourceSeat,
            target.Seat, benefit, 1));
        CompleteHulaoAllianceFeastTarget(use.Id);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 27500)]
    private bool TryAdvanceHulaoAllianceFeastAi()
    {
        if (!HasHulaoAllianceFeastPrompt() || _players[_pendingDecision!.PlayerSeat].IsHuman) return false;
        var target = _players[_pendingDecision.PlayerSeat];
        var benefit = target.HasHpValue && target.Hp < target.MaxHp ? "recover" : "draw";
        ResolveHulaoAllianceFeastChoice(_pendingDecision.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("benefit") == benefit));
        AdvanceRulesAndPublishState();
        return true;
    }

    private void CompleteHulaoAllianceFeastTarget(long useId)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("Alliance feast lost its owner.");
        var card = GetTrickRepresentation(useId, use.CardId, requireProcessing: true);
        // FinishCardUse drains native recovery replacements and HP observers before
        // its existing sequential target cursor advances. The physical trick stays
        // in Processing until every surviving target has resolved or nullified.
        FinishCardUse(useId, card, CardKind.AllianceFeast);
    }

    private void AssertHulaoCardState()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.AllianceFeastChoice is not null))
        {
            var choice = use.AllianceFeastChoice!;
            if (LeChengyanNativeEffectKind(use) != CardKind.AllianceFeast || use.Action?.ActionId != choice.ActionId ||
                use.TargetIndex != choice.TargetIndex || use.TargetIndex < 0 || use.TargetIndex >= LeChengyanNativeTargets(use).Count ||
                LeChengyanNativeTargets(use)[use.TargetIndex] != choice.TargetSeat || !HasHulaoAllianceFeastPrompt() ||
                _resolutionStack.LastOrDefault()?.Id != use.Id ||
                _pendingDecision!.Choices.Count != 2 || _pendingDecision.Choices.Any(option =>
                    option.Parameters.GetValueOrDefault("program-action") != "hulao-alliance-feast"))
                throw new InvalidOperationException("Alliance feast lost its exact native target choice.");
        }
        if (_pendingDecision?.IncomingCard == CardKind.AllianceFeast &&
            _pendingDecision.Kind == DecisionKind.ProgramTrigger && !HasHulaoAllianceFeastPrompt())
            throw new InvalidOperationException("Alliance feast has a choice without its owning card use.");
    }
}
