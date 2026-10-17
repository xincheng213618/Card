namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LeiluanPlayPrefix = "leiluan-native.";
    [ProgramActionSource(ProgramActionSourcePhase.Cardless, 27550)]
    private IEnumerable<LegalAction> BuildLeiluanPlayActions(CharacterState owner)
    {
        if (!HasLeiluanCapability || !owner.IsAlive || _winner != Winner.None || (_phase != TurnPhase.Play || owner.Seat != _currentSeat) && !IsKeBiNengPingduanBasicQuery(owner)) yield break;
        foreach (var selection in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Peach, CardKind.Alcohol }.SelectMany(k => LeiluanSelections(owner, k, false)).DistinctBy(s => (s.Source, s.OutputKind, Cards: string.Join('-', s.Cards.Select(c => c.Id)))))
        {
            var kind = selection.OutputKind; var targets = LeiluanPlayTargets(owner, selection);
            if (IsSlashCard(kind) && targets.Count == 0 || kind == CardKind.Peach && targets.Count == 0 || kind == CardKind.Alcohol && !LeiluanAlcoholAllowed(owner, selection.Cards)) continue;
            var ids = Array.AsReadOnly(selection.Cards.Select(c => c.Id).Order().ToArray()); var targetCount = kind == CardKind.Alcohol ? 0 : 1;
            yield return new(LegalActionKind.UseProgramSkill, null, null, DescribeConversion(selection.Source, $"将{ids.Count}张牌当【{CardCatalog.Get(kind).DisplayName}】使用"), kind,
                MinCardCount: ids.Count, MaxCardCount: ids.Count, MinTargetCount: targetCount, MaxTargetCount: targetCount)
            { ProgramSkillId = selection.Source.SkillId, ProgramActivationId = LeiluanPlayPrefix + selection.Source.BindingId + "." + string.Join('-', ids), ConversionSource = selection.Source,
                SelectableCardIds = ids, SelectableTargetSeats = Array.AsReadOnly(targets.ToArray()), ProgramAiHint = new(0, kind == CardKind.Peach ? 1 : 0, 0, 0, 0, IsSlashCard(kind) ? 1 : 0, false, false) { ValueAdjustment = kind == CardKind.Alcohol ? 10 : 0 } };
        }
    }
    private IReadOnlyList<int> LeiluanPlayTargets(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        var kind = selection.OutputKind; var suit = PhysicalGroupSuit(owner, selection.Cards); var color = PhysicalGroupColor(owner, selection.Cards);
        if (IsSlashCard(kind)) return _players.Where(p => CanUseVirtualSlashTarget(owner, p, kind, suit, effectiveColor: color, physicalCardIds: selection.Cards.Select(c => c.Id).ToArray())).Select(p => p.Seat).ToArray();
        return kind == CardKind.Peach && IsRuleWounded(owner) && !HasSelfCardTargetProhibition(owner.Seat) && !IsCardTargetProhibited(owner, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, owner.Seat, suit) ? [owner.Seat] : [];
    }
    private bool LeiluanAlcoholAllowed(CharacterState owner, IReadOnlyList<Card> cards) => CanUseRoundDistinctBasicAlcohol(owner, cards);
    private CommandError? ValidateLeiluanPlay(CharacterState owner, LegalAction action, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (ValidateProgramSelection(action, ids, targets) is { } error) return error;
        if (action.ConversionSource is not { } source || action.PlayedCardKind is not { } kind || !IsLeiluanSource(source) || LeiluanSelections(owner, kind, false).SingleOrDefault(s => s.Source == source && s.Cards.Select(c => c.Id).Order().SequenceEqual(ids.Order())) is not { } selection)
            return new(CommandErrorCode.InvalidCard, "累卵的真实连环人数、材料、轮牌名或技能资格已变化。");
        if (IsSlashCard(kind) || kind == CardKind.Peach)
        { if (targets.Count != 1 || !LeiluanPlayTargets(owner, selection).Contains(targets[0])) return new(CommandErrorCode.InvalidTarget, "累卵目标或原生杀次数不合法。"); }
        else if (kind != CardKind.Alcohol || targets.Count != 0 || !LeiluanAlcoholAllowed(owner, selection.Cards)) return new(CommandErrorCode.InvalidTarget, "累卵原生酒不合法。");
        return null;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Submit, 27550)]
    private bool TrySubmitLeiluanPlay(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!; if (!command.ActivationId.StartsWith(LeiluanPlayPrefix, StringComparison.Ordinal)) return false;
        var error = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction);
        if (error is not null) { result = Reject(error.Code, error.Message); return true; }
        var actor = _players[command.ActorSeat]; var action = BuildLeiluanPlayActions(actor).SingleOrDefault(a => a.ProgramSkillId == command.SkillId && a.ProgramActivationId == command.ActivationId);
        if (command.SkillOwnerSeat is not null || action is null) { result = Reject(CommandErrorCode.IllegalAction, "累卵真实使用菜单已失效。"); return true; }
        if (ValidateLeiluanPlay(actor, action, command.CardIds, command.TargetSeats) is { } invalid) { result = Reject(invalid.Code, invalid.Message); return true; }
        result = Accept(() => { ClearPendingDecision(); TryExecuteLeiluanPlay(actor, action, command.CardIds, command.TargetSeats); AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); }); return true;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Execute, 27550)]
    private bool TryExecuteLeiluanPlay(CharacterState actor, LegalAction selected, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (selected.ProgramActivationId?.StartsWith(LeiluanPlayPrefix, StringComparison.Ordinal) != true || selected.ConversionSource is not { } source || !IsLeiluanSource(source)) return false;
        if (ValidateLeiluanPlay(actor, selected, ids, targets) is { } invalid) throw new InvalidOperationException(invalid.Message);
        var kind = selected.PlayedCardKind!.Value; var selection = LeiluanSelections(actor, kind, false).Single(s => s.Source == source && s.Cards.Select(c => c.Id).Order().SequenceEqual(ids.Order()));
        if (IsSlashCard(kind)) ResolveSlashCore(actor, _players[targets[0]], selection.Cards[0], kind, actor.Seat, physicalCards: selection.Cards, conversionSource: source);
        else if (kind == CardKind.Peach) ResolveRecoveryCard(actor, actor, selection.Cards[0], "桃", kind, conversionSource: source, physicalCards: selection.Cards);
        else
        {
            var use = BeginCardUse(selection.Cards[0], actor.Seat, [], kind, physicalCardIds: selection.Cards.Select(c => c.Id).ToArray(), conversionSource: source);
            MoveProgramCardsFromMultipleSources(selection.Cards.Select(c => c.Id).ToArray(), CardLocation.Processing, CardMoveReasons.Use);
            if (!HasIssuedRoundGainedBasicBonus(use, actor.Seat)) actor.UsedPlayPhaseAlcoholThisTurn = true;
            BeginSimpleCardUse(use, new(selection.Cards[0].Id, SimpleCardUseEffect.Alcohol));
        }
        return true;
    }
    private bool TryResolveLeiluanDyingResponse(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        if (selection.OutputKind != CardKind.Alcohol || !IsLeiluanSelection(selection)) return false;
        if (!LeiluanSelections(owner, CardKind.Alcohol, true).Any(s => s.Source == selection.Source && s.Cards.Select(c => c.Id).Order().SequenceEqual(selection.Cards.Select(c => c.Id).Order())) || ActiveDying is not { } dying || dying.VictimSeat != owner.Seat)
            throw new InvalidOperationException("累卵酒必须保留真实自救与未使用牌名资格。");
        ClearPendingDecision(); ResolveRecoveryCard(owner, owner, selection.Cards[0], "酒", CardKind.Alcohol, conversionSource: selection.Source, dyingResponse: new(dying.FrameId, owner.Seat, false, null, true, selection.Cards[0].Id), physicalCards: selection.Cards); return true;
    }
    private bool TryFinishLeiluanAlcoholCost(CardUseFrame use, Card card)
    {
        if (use.CardKind != CardKind.Alcohol || use.LeiluanPayments?.Any(p => p.ActionId == use.Action?.ActionId && p.Paid && !p.ResponseUse) != true) return false;
        AssertLeiluanPayments(use);
        foreach (var id in use.PhysicalCardIds ?? []) if (_cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(c => c.Id == id) is { } cost) MoveCard(cost, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
        return true;
    }
}