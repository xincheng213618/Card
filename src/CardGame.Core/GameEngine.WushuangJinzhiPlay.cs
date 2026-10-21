namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WushuangJinzhiPlayPrefix = "wushuang-jinzhi-native.";
    [ProgramActionSource(ProgramActionSourcePhase.Cardless, 61020)]
    private IEnumerable<LegalAction> BuildWushuangJinzhiPlayActions(CharacterState owner)
    {
        if (!_contentRegistry.Skills.ContainsKey(WushuangJinzhiSkill) || !owner.IsAlive || _winner != Winner.None || (_phase != TurnPhase.Play || owner.Seat != _currentSeat) && !IsKeBiNengPingduanBasicQuery(owner)) yield break;
        foreach (var selection in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Peach, CardKind.Alcohol }.SelectMany(k => WushuangJinzhiSelections(owner, k, false)).DistinctBy(s => (s.Source, s.OutputKind, Cards: string.Join('-', s.Cards.Select(c => c.Id)))))
        {
            var kind = selection.OutputKind; var targets = WushuangJinzhiPlayTargets(owner, selection);
            if (IsSlashCard(kind) && targets.Count == 0 || kind == CardKind.Peach && targets.Count == 0 || kind == CardKind.Alcohol && !WushuangJinzhiAlcoholAllowed(owner, selection.Cards)) continue;
            var ids = Array.AsReadOnly(selection.Cards.Select(c => c.Id).Order().ToArray()); var targetCount = kind == CardKind.Alcohol ? 0 : 1;
            yield return new(LegalActionKind.UseProgramSkill, null, null, DescribeConversion(selection.Source, $"将{ids.Count}张牌当【{CardCatalog.Get(kind).DisplayName}】使用"), kind,
                MinCardCount: ids.Count, MaxCardCount: ids.Count, MinTargetCount: targetCount, MaxTargetCount: targetCount)
            { ProgramSkillId = selection.Source.SkillId, ProgramActivationId = WushuangJinzhiPlayPrefix + selection.Source.BindingId + "." + string.Join('-', ids), ConversionSource = selection.Source,
                SelectableCardIds = ids, SelectableTargetSeats = Array.AsReadOnly(targets.ToArray()), ProgramAiHint = new(0, kind == CardKind.Peach ? 1 : 0, 0, 0, 0, IsSlashCard(kind) ? 1 : 0, false, false) { ValueAdjustment = kind == CardKind.Alcohol ? 10 : 0 } };
        }
    }
    private IReadOnlyList<int> WushuangJinzhiPlayTargets(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        var kind = selection.OutputKind; var suit = Suit.None; bool? color = null;
        if (IsSlashCard(kind)) return _players.Where(p => CanUseVirtualSlashTarget(owner, p, kind, suit, effectiveColor: color,
            physicalCardIds: selection.Cards.Select(c => c.Id).ToArray(), conversionSource: selection.Source)).Select(p => p.Seat).ToArray();
        return kind == CardKind.Peach && IsRuleWounded(owner) && !HasSelfCardTargetProhibition(owner.Seat) && !IsCardTargetProhibited(owner, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, owner.Seat, suit) ? [owner.Seat] : [];
    }
    private bool WushuangJinzhiAlcoholAllowed(CharacterState owner, IReadOnlyList<Card> cards) =>
        !owner.HasAlcoholEffect && !HasSelfCardTargetProhibition(owner.Seat) &&
        (CanUseJianjiOrdinaryAlcohol(owner, null) || HasTargetCardQuotaAllowance(owner.Seat, owner.Seat) ||
            HasNextUnlimitedCard(owner) || HasMouWujingAllowance(owner) || HasFriendQihuiAllowance(owner) ||
            HasCardPolicy(owner, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol) ||
            HasRoundGainedBasicBonus(owner, CardKind.Alcohol, cards.Select(c => c.Id).ToArray())) &&
        !IsCardTargetProhibited(owner, CardKind.Alcohol, Suit.None, null) &&
        !HasBeneficiarySuitShield(owner.Seat, owner.Seat, Suit.None);
    private CommandError? ValidateWushuangJinzhiPlay(CharacterState owner, LegalAction action, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (ValidateProgramSelection(action, ids, targets) is { } error) return error;
        if (action.ConversionSource is not { } source || action.PlayedCardKind is not { } kind || !IsWushuangJinzhiSource(source) || WushuangJinzhiSelections(owner, kind, false).SingleOrDefault(s => s.Source == source && s.Cards.Select(c => c.Id).Order().SequenceEqual(ids.Order())) is not { } selection)
            return new(CommandErrorCode.InvalidCard, "锦织的真实同色材料、本轮次数或技能资格已变化。");
        if (IsSlashCard(kind) || kind == CardKind.Peach)
        { if (targets.Count != 1 || !WushuangJinzhiPlayTargets(owner, selection).Contains(targets[0])) return new(CommandErrorCode.InvalidTarget, "锦织目标或原生杀次数不合法。"); }
        else if (kind != CardKind.Alcohol || targets.Count != 0 || !WushuangJinzhiAlcoholAllowed(owner, selection.Cards)) return new(CommandErrorCode.InvalidTarget, "锦织原生酒不合法。");
        return null;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Submit, 61020)]
    private bool TrySubmitWushuangJinzhiPlay(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!; if (!command.ActivationId.StartsWith(WushuangJinzhiPlayPrefix, StringComparison.Ordinal)) return false;
        var error = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction);
        if (error is not null) { result = Reject(error.Code, error.Message); return true; }
        var actor = _players[command.ActorSeat]; var action = BuildWushuangJinzhiPlayActions(actor).SingleOrDefault(a => a.ProgramSkillId == command.SkillId && a.ProgramActivationId == command.ActivationId);
        if (command.SkillOwnerSeat is not null || action is null) { result = Reject(CommandErrorCode.IllegalAction, "锦织真实使用菜单已失效。"); return true; }
        if (ValidateWushuangJinzhiPlay(actor, action, command.CardIds, command.TargetSeats) is { } invalid) { result = Reject(invalid.Code, invalid.Message); return true; }
        result = Accept(() => { ClearPendingDecision(); TryExecuteWushuangJinzhiPlay(actor, action, command.CardIds, command.TargetSeats); AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); }); return true;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Execute, 61020)]
    private bool TryExecuteWushuangJinzhiPlay(CharacterState actor, LegalAction selected, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (selected.ProgramActivationId?.StartsWith(WushuangJinzhiPlayPrefix, StringComparison.Ordinal) != true || selected.ConversionSource is not { } source || !IsWushuangJinzhiSource(source)) return false;
        if (ValidateWushuangJinzhiPlay(actor, selected, ids, targets) is { } invalid) throw new InvalidOperationException(invalid.Message);
        var kind = selected.PlayedCardKind!.Value; var selection = WushuangJinzhiSelections(actor, kind, false).Single(s => s.Source == source && s.Cards.Select(c => c.Id).Order().SequenceEqual(ids.Order()));
        var appearance = ApplyProgramUseAppearance(actor, selection.Cards[0], source);
        if (IsSlashCard(kind)) ResolveSlashCore(actor, _players[targets[0]], appearance, kind, actor.Seat, physicalCards: selection.Cards, conversionSource: source);
        else if (kind == CardKind.Peach) ResolveRecoveryCard(actor, actor, appearance, "桃", kind, conversionSource: source, physicalCards: selection.Cards);
        else
        {
            var use = BeginCardUse(appearance, actor.Seat, [], kind, physicalCardIds: selection.Cards.Select(c => c.Id).ToArray(), conversionSource: source);
            MoveProgramCardsFromMultipleSources(selection.Cards.Select(c => c.Id).ToArray(), CardLocation.Processing, CardMoveReasons.Use);
            if (!HasIssuedRoundGainedBasicBonus(use, actor.Seat)) RecordMouZhaJiangAwareAlcoholDebit(use, actor);
            BeginSimpleCardUse(use, new(selection.Cards[0].Id, SimpleCardUseEffect.Alcohol));
        }
        return true;
    }
}

