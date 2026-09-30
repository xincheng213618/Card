namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<long, int> _foreignPublicPileSlashBaseDamage = [];
    private int GetForeignPublicPileSlashBaseDamage(long resolutionId) => _foreignPublicPileSlashBaseDamage[resolutionId];
    private bool IsForeignPublicPileSlashUse(long resolutionId) => _resolutionStack.OfType<CardUseFrame>()
        .Any(frame => frame.Id == resolutionId && frame.CardId == 0 && frame.Action is { } action &&
            action.PhysicalCards.Count == 2 && action.PhysicalCards.All(cost => cost.From.Zone == CardZoneKind.Authority) &&
            action.PhysicalCards.Select(cost => cost.From.OwnerSeat).Distinct().Count() == 1);
    private IEnumerable<LegalAction> BuildForeignPublicPileSlashActions(CharacterState actor)
    {
        if (!actor.IsAlive || actor.Seat != _currentSeat || _phase != TurnPhase.Play) yield break;
        foreach (var owner in _players.Where(player => player.IsAlive && player.Seat != actor.Seat))
        {
            var pile = _cardZones.CardsAt(CardLocation.Authority(owner.Seat));
            if (pile.Count < 2 || !CanUseVirtualSlashTarget(actor, owner) ||
                IsDirectedCardTargetProhibited(actor.Seat, owner.Seat, CardKind.Slash)) continue;
            foreach (var binding in CardPolicies(owner, SkillProgramCardPolicyKind.ForeignPublicPileSlash))
            {
                var baseAction = new LegalAction(LegalActionKind.UseProgramSkill, null, owner.Seat,
                    $"移去 {owner.Name} 的两张公开牌堆牌，视为对其使用【杀】", CardKind.Slash,
                    MinCardCount: 2, MaxCardCount: 2, MinTargetCount: 1, MaxTargetCount: 1)
                {
                    ProgramSkillId = binding.Source.SkillId, ProgramActivationId = binding.Policy.Id,
                    ProgramSkillOwnerSeat = owner.Seat,
                    SelectableCardIds = pile.Select(card => card.Id).ToArray(), SelectableTargetSeats = [owner.Seat],
                    ProgramAiHint = new(0, 0, 1, 0, 0, 0, true, false)
                };
                yield return baseAction;
                if (HasNextCardTargetAdjustment(actor))
                    foreach (var extra in _players.Where(player => player.IsAlive && player.Seat != actor.Seat && player.Seat != owner.Seat &&
                        !IsSlashProhibited(player) && !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, CardKind.Slash) &&
                        !IsCardTargetProhibited(player, CardKind.Slash, Suit.None)))
                        yield return baseAction with
                        {
                            Description = baseAction.Description + $"（下一张牌增加目标：{extra.Name}）",
                            TargetSeats = new[] { owner.Seat, extra.Seat },
                            SelectableTargetSeats = new[] { owner.Seat, extra.Seat }, MinTargetCount = 2, MaxTargetCount = 2
                        };
            }
        }
    }

    private bool TrySubmitForeignPublicPileSlash(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!;
        if (command.SkillOwnerSeat is not { } ownerSeat || !IsValidPlayerSeat(ownerSeat) ||
            !CardPolicies(_players[ownerSeat], SkillProgramCardPolicyKind.ForeignPublicPileSlash)
                .Any(binding => binding.Source.SkillId == command.SkillId && binding.Policy.Id == command.ActivationId)) return false;
        var error = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction);
        if (error is not null) { result = Reject(error.Code, error.Message); return true; }
        var actor = _players[command.ActorSeat];
        var action = BuildForeignPublicPileSlashActions(actor).SingleOrDefault(item => item.ProgramSkillOwnerSeat == ownerSeat &&
            item.ProgramSkillId == command.SkillId && item.ProgramActivationId == command.ActivationId && item.TargetSeats.SequenceEqual(command.TargetSeats));
        if (action is null) { result = Reject(CommandErrorCode.IllegalAction, "The public pile Slash is currently unavailable."); return true; }
        error = ValidateProgramSelection(action, command.CardIds, command.TargetSeats);
        if (error is not null) { result = Reject(error.Code, error.Message); return true; }
        result = Accept(() =>
        {
            ClearPendingDecision();
            TryExecuteForeignPublicPileSlash(actor, action, command.CardIds, command.TargetSeats);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
        return true;
    }

    private bool TryExecuteForeignPublicPileSlash(CharacterState actor, LegalAction selected,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (selected.ProgramSkillOwnerSeat is not { } ownerSeat ||
            !CardPolicies(_players[ownerSeat], SkillProgramCardPolicyKind.ForeignPublicPileSlash).Any(binding =>
                binding.Source.SkillId == selected.ProgramSkillId && binding.Policy.Id == selected.ProgramActivationId)) return false;
        var action = BuildForeignPublicPileSlashActions(actor).SingleOrDefault(item =>
            item.ProgramSkillOwnerSeat == ownerSeat && item.ProgramSkillId == selected.ProgramSkillId && item.ProgramActivationId == selected.ProgramActivationId && item.TargetSeats.SequenceEqual(targets))
            ?? throw new InvalidOperationException("The public pile Slash became unavailable.");
        if (ValidateProgramSelection(action, cards, targets) is { } error) throw new InvalidOperationException(error.Message);
        var from = CardLocation.Authority(ownerSeat);
        var payments = cards.Select(id => _cardZones.CardsAt(from).Single(card => card.Id == id)).ToArray();
        var binding = CardPolicies(_players[ownerSeat], SkillProgramCardPolicyKind.ForeignPublicPileSlash).Single(item =>
            item.Source.SkillId == selected.ProgramSkillId && item.Policy.Id == selected.ProgramActivationId);
        var conversion = new CardConversionSource(binding.Source.SkillId, binding.Policy.Id, ownerSeat, binding.Source.SkillInstanceId);
        var resolutionId = ++_resolutionSequence;
        var context = new CardActionContext(++_cardActionSequence, null, CardActionType.Use, actor.Seat, actor.Seat,
            null, null, null, CardKind.Slash, targets, payments.Select(card => new CardActionCost(card.Id, card.Kind, from)).ToArray(),
            [conversion], effectiveSuit: Suit.None, effectiveRank: 0);
        MoveCards(payments, from, CardLocation.DiscardPile, new CardMoveReason("program.public-pile.slash-payment"));
        _resolutionStack.Add(new CardUseFrame(resolutionId, actor.Seat, 0, CardKind.Slash, targets, PhysicalCardIds: []) { Action = context });
        if (TracksPlayCardHistory) QueueGameEvent(new CardUseAppearanceCapturedEvent(context));
        QueueGameEvent(new CardUseDeclaredEvent(resolutionId, 0, CardKind.Slash, actor.Seat));
        QueueGameEvent(new TargetsConfirmedEvent(resolutionId, targets));
        if (!_unlimitedCardUses.Contains(resolutionId)) RecordSlashUseDebit(resolutionId, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        _foreignPublicPileSlashBaseDamage[resolutionId] = actor.HasAlcoholEffect ? 2 : 1;
        var attack = new AttackResolution(resolutionId, actor.Seat, ownerSeat, card: null,
            damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(actor, _players[ownerSeat], CardKind.Slash));
        actor.HasAlcoholEffect = false;
        _pendingAttack = attack;
        QueueGameEvent(new CardUsedEvent(0, CardKind.Slash, actor.Seat, ownerSeat));
        _committedProgramUses.Add(resolutionId);
        if (!TryBeginProgramCardWindow(attack, context, SkillProgramTriggerWindow.CardUseCommitted, context.TargetSeats,
            ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
        return true;
    }
}
