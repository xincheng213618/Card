namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RoundDistinctBasicPlayPrefix = "round-distinct-basic.";

    // The mature active-skill command already carries two cards. These are
    // conversion menus, not content activations or artificial ProgramSkillFrames.
    private IEnumerable<LegalAction> BuildRoundDistinctBasicPlayActions(CharacterState owner)
    {
        if (!_contentRegistry.ProgramDependencies.UsesRoundDistinctBasicUse || !owner.IsAlive ||
            _winner != Winner.None || _phase != TurnPhase.Play || owner.Seat != _currentSeat) yield break;
        var selections = new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Peach, CardKind.Alcohol }
            .SelectMany(kind => GetProgramMultiCardViewAsSelections(owner, kind, false))
            .Where(s => ViewAsRule(s.Source)?.RoundDistinctBasicUse is not null)
            .DistinctBy(s => (s.Source, s.OutputKind, Cards: string.Join('-', s.Cards.Select(c => c.Id).Order())));
        foreach (var selection in selections)
        {
            var kind = selection.OutputKind;
            var targets = RoundDistinctBasicPlayTargets(owner, selection);
            if (IsSlashCard(kind) && targets.Count == 0 || kind == CardKind.Peach && targets.Count == 0 ||
                kind == CardKind.Alcohol && !CanUseRoundDistinctBasicAlcohol(owner, selection.Cards)) continue;
            var ids = Array.AsReadOnly(selection.Cards.Select(c => c.Id).Order().ToArray());
            var targetCount = kind == CardKind.Alcohol ? 0 : 1;
            yield return new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                DescribeConversion(selection.Source, $"将【{selection.Cards[0].DisplayName}】和【{selection.Cards[1].DisplayName}】当【{CardCatalog.Get(kind).DisplayName}】使用"), kind,
                MinCardCount: 2, MaxCardCount: 2, MinTargetCount: targetCount, MaxTargetCount: targetCount)
            {
                ProgramSkillId = selection.Source.SkillId,
                ProgramActivationId = RoundDistinctBasicPlayPrefix + selection.Source.BindingId + "." + string.Join('-', ids),
                ConversionSource = selection.Source, SelectableCardIds = ids,
                SelectableTargetSeats = Array.AsReadOnly(targets.ToArray()),
                ProgramAiHint = new(0, kind == CardKind.Peach ? 1 : 0, 0, 0, 0, IsSlashCard(kind) ? 1 : 0, false, false)
                { ValueAdjustment = kind == CardKind.Alcohol ? 10 : 0 }
            };
        }
    }

    private IReadOnlyList<int> RoundDistinctBasicPlayTargets(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        var kind = selection.OutputKind;
        var suit = PhysicalGroupSuit(owner, selection.Cards);
        var color = PhysicalGroupColor(owner, selection.Cards);
        if (IsSlashCard(kind))
            return _players.Where(target => CanUseVirtualSlashTarget(owner, target, kind, suit,
                    effectiveColor: color, physicalCardIds: selection.Cards.Select(c => c.Id).ToArray()))
                .Select(target => target.Seat).Order().ToArray();
        if (kind == CardKind.Peach && owner.Hp < owner.MaxHp && !HasSelfCardTargetProhibition(owner.Seat) &&
            !IsCardTargetProhibited(owner, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, owner.Seat, suit))
            return [owner.Seat];
        return [];
    }

    private bool CanUseRoundDistinctBasicAlcohol(CharacterState owner, IReadOnlyList<Card> cards) =>
        !owner.HasAlcoholEffect && !HasSelfCardTargetProhibition(owner.Seat) &&
        (!owner.UsedPlayPhaseAlcoholThisTurn || HasTargetCardQuotaAllowance(owner.Seat, owner.Seat) ||
            HasNextUnlimitedCard(owner) || HasCardPolicy(owner, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol) ||
            HasRoundGainedBasicBonus(owner, CardKind.Alcohol, cards.Select(c => c.Id).ToArray())) &&
        !IsCardTargetProhibited(owner, CardKind.Alcohol, PhysicalGroupSuit(owner, cards), PhysicalGroupColor(owner, cards)) &&
        !HasBeneficiarySuitShield(owner.Seat, owner.Seat, PhysicalGroupSuit(owner, cards));

    private CommandError? ValidateRoundDistinctBasicPlaySelection(CharacterState owner, LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (ValidateProgramSelection(action, cards, targets) is { } error) return error;
        if (action.ConversionSource is not { } source || action.PlayedCardKind is not { } kind ||
            ViewAsRule(source)?.RoundDistinctBasicUse is null ||
            FindProgramMultiCardViewAsSelection(owner, cards, kind, false, source) is not { } selection)
            return new(CommandErrorCode.InvalidCard, "The pair conversion requires its exact currently legal same-color hand-like materials.");
        if (IsSlashCard(kind) || kind == CardKind.Peach)
        {
            if (targets.Count != 1 || !RoundDistinctBasicPlayTargets(owner, selection).Contains(targets[0]))
                return new(CommandErrorCode.InvalidTarget, "The pair conversion target is no longer legal.");
        }
        else if (kind != CardKind.Alcohol || targets.Count != 0 || !CanUseRoundDistinctBasicAlcohol(owner, selection.Cards))
            return new(CommandErrorCode.InvalidTarget, "The pair Alcohol use is no longer legal.");
        return null;
    }

    private bool TrySubmitRoundDistinctBasicPlay(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!;
        if (!command.ActivationId.StartsWith(RoundDistinctBasicPlayPrefix, StringComparison.Ordinal) ||
            _contentRegistry.Skills.GetValueOrDefault(command.SkillId)?.Program?.ViewAs.Any(r => r.RoundDistinctBasicUse is not null) != true)
            return false;
        var error = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction);
        if (error is not null) { result = Reject(error.Code, error.Message); return true; }
        var owner = _players[command.ActorSeat];
        var action = BuildRoundDistinctBasicPlayActions(owner).SingleOrDefault(a =>
            a.ProgramSkillId == command.SkillId && a.ProgramActivationId == command.ActivationId);
        if (command.SkillOwnerSeat is not null || action is null)
        { result = Reject(CommandErrorCode.IllegalAction, "The pair conversion menu is no longer available."); return true; }
        error = ValidateRoundDistinctBasicPlaySelection(owner, action, command.CardIds, command.TargetSeats);
        if (error is not null) { result = Reject(error.Code, error.Message); return true; }
        result = Accept(() =>
        {
            ClearPendingDecision();
            TryExecuteRoundDistinctBasicPlay(owner, action, command.CardIds, command.TargetSeats);
            AdvanceRulesAndPublishState();
            if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        });
        return true;
    }

    private bool TryExecuteRoundDistinctBasicPlay(CharacterState owner, LegalAction selected,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (selected.ProgramActivationId?.StartsWith(RoundDistinctBasicPlayPrefix, StringComparison.Ordinal) != true ||
            selected.ConversionSource is not { } source || ViewAsRule(source)?.RoundDistinctBasicUse is null) return false;
        var action = BuildRoundDistinctBasicPlayActions(owner).SingleOrDefault(a =>
            a.ProgramSkillId == selected.ProgramSkillId && a.ProgramActivationId == selected.ProgramActivationId &&
            a.ConversionSource == source) ?? throw new InvalidOperationException("The pair conversion menu lost its exact published source.");
        if (ValidateRoundDistinctBasicPlaySelection(owner, action, cards, targets) is { } error)
            throw new InvalidOperationException(error.Message);
        var kind = action.PlayedCardKind!.Value;
        var selection = FindProgramMultiCardViewAsSelection(owner, cards, kind, false, source)!;
        if (IsSlashCard(kind))
            ResolveSlashCore(owner, _players[targets[0]], selection.Cards[0], kind, owner.Seat,
                physicalCards: selection.Cards, conversionSource: source);
        else if (kind == CardKind.Peach)
            ResolveRecoveryCard(owner, owner, selection.Cards[0], "桃", CardKind.Peach,
                conversionSource: source, physicalCards: selection.Cards);
        else
        {
            var id = BeginCardUse(selection.Cards[0], owner.Seat, [], CardKind.Alcohol,
                physicalCardIds: selection.Cards.Select(c => c.Id).ToArray(), conversionSource: source);
            foreach (var cost in selection.Cards)
                MoveCard(cost, FindOwnedCardLocation(owner, cost), CardLocation.Processing, CardMoveReasons.Use);
            if (!HasIssuedRoundGainedBasicBonus(id, owner.Seat)) owner.UsedPlayPhaseAlcoholThisTurn = true;
            BeginSimpleCardUse(id, new(selection.Cards[0].Id, SimpleCardUseEffect.Alcohol));
        }
        return true;
    }

    // Hook before the existing extended response dispatcher. Only Alcohol is
    // new here; Peach continues through its mature native recovery producer.
    private bool TryResolveRoundDistinctBasicDyingResponse(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        if (selection.OutputKind != CardKind.Alcohol || ViewAsRule(selection.Source)?.RoundDistinctBasicUse is null) return false;
        if (ActiveDying is not { } dying || dying.ResponderSeat != owner.Seat || dying.VictimSeat != owner.Seat ||
            !owner.IsAlive || owner.Hp > 0 || HasSelfCardTargetProhibition(owner.Seat) ||
            IsCardUseForbidden(owner.Seat, CardKind.Alcohol, CardActionType.Use))
            throw new InvalidOperationException("A pair Alcohol use requires its own actual dying response.");
        ClearPendingDecision();
        ResolveRecoveryCard(owner, owner, selection.Cards[0], "酒", CardKind.Alcohol,
            conversionSource: selection.Source,
            dyingResponse: new(dying.FrameId, owner.Seat, false, null, true, selection.Cards[0].Id),
            physicalCards: selection.Cards);
        return true;
    }

    // The existing Alcohol finisher moves a single representative card. This
    // exact opt-in drains both original native costs once, without a new effect.
    private bool TryFinishRoundDistinctBasicAlcoholCost(CardUseFrame use, Card card)
    {
        if (use.CardKind != CardKind.Alcohol || use.RoundDistinctBasicUses?.Any(r =>
                !r.IsResponseUse && r.CardActionId == use.Action?.ActionId) != true) return false;
        AssertRoundDistinctBasicUses(use);
        if (use.CardId != card.Id || use.PhysicalCardIds is not { Count: 2 } ids)
            throw new InvalidOperationException("A pair Alcohol lost its exact native representative and two materials.");
        foreach (var id in ids)
            if (_cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(c => c.Id == id) is { } cost)
                MoveCard(cost, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
        return true;
    }
}
