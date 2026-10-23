namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnniversaryYizanPlayPrefix = "anniversary-yizan-pair.";
    [ProgramActionSource(ProgramActionSourcePhase.Cardless, 44600)]
    private IEnumerable<LegalAction> AnniversaryYizanPairActions(CharacterState owner)
    {
        if (!owner.IsAlive || _winner != Winner.None || _currentSeat != owner.Seat || _phase != TurnPhase.Play ||
            !HasRuntimeSkill(owner, AnniversaryZhaoTongZhaoGuangComposition.Yizan) || AnniversaryYizanModified(owner.Seat)) yield break;
        foreach (var selection in AnniversaryZhaoTongZhaoGuangComposition.BasicKinds.Where(k => k != CardKind.Dodge)
                     .SelectMany(k => GetProgramMultiCardViewAsSelections(owner, k, false)).Where(IsAnniversaryYizanSelection))
        {
            var kind = selection.OutputKind; var targets = AnniversaryYizanPlayTargets(owner, selection);
            if (kind == CardKind.Alcohol ? !AnniversaryYizanAlcoholAllowed(owner, selection) : targets.Count == 0) continue;
            var ids = Array.AsReadOnly(selection.Cards.Select(c => c.Id).Order().ToArray()); var targetCount = kind == CardKind.Alcohol ? 0 : 1;
            yield return new(LegalActionKind.UseProgramSkill, null, null, DescribeConversion(selection.Source, $"将两张牌当【{CardCatalog.Get(kind).DisplayName}】使用"), kind,
                MinCardCount: 2, MaxCardCount: 2, MinTargetCount: targetCount, MaxTargetCount: targetCount)
            {
                ProgramSkillId = selection.Source.SkillId,
                ProgramActivationId = AnniversaryYizanPlayPrefix + selection.Source.BindingId + "." + string.Join('-', ids),
                ConversionSource = selection.Source, SelectableCardIds = ids, SelectableTargetSeats = Array.AsReadOnly(targets.ToArray()),
                ProgramAiHint = new(0, kind == CardKind.Peach ? 1 : 0, 0, 0, 0, IsSlashCard(kind) ? 1 : 0, false, false) { ValueAdjustment = kind == CardKind.Alcohol ? 10 : 0 }
            };
        }
    }
    private IReadOnlyList<int> AnniversaryYizanPlayTargets(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        var kind = selection.OutputKind; var ids = selection.Cards.Select(c => c.Id).ToArray();
        var suit = ProgramMultiCardSuit(owner, selection); var color = ProgramMultiCardColor(owner, selection);
        if (IsSlashCard(kind)) return _players.Where(p => CanUseVirtualSlashTarget(owner, p, kind, physicalSuit: suit, effectiveColor: color,
            physicalCardIds: ids, conversionSource: selection.Source) && !IsDirectedCardTargetProhibited(owner.Seat, p.Seat, kind) &&
            !IsCardTargetProhibited(p, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, p.Seat, suit)).Select(p => p.Seat).ToArray();
        return kind == CardKind.Peach && IsRuleWounded(owner) && !HasSelfCardTargetProhibition(owner.Seat) &&
            !IsCardTargetProhibited(owner, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, owner.Seat, suit) ? [owner.Seat] : [];
    }
    private bool AnniversaryYizanAlcoholAllowed(CharacterState owner, ProgramMultiCardViewAsSelection s) =>
        !owner.HasAlcoholEffect && !HasSelfCardTargetProhibition(owner.Seat) &&
        (CanUseJianjiOrdinaryAlcohol(owner, null) || HasTargetCardQuotaAllowance(owner.Seat, owner.Seat) || HasNextUnlimitedCard(owner) ||
            HasMouWujingAllowance(owner) || HasFriendQihuiAllowance(owner) || HasCardPolicy(owner, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol) ||
            HasRoundGainedBasicBonus(owner, CardKind.Alcohol, s.Cards.Select(c => c.Id).ToArray())) &&
        !IsCardTargetProhibited(owner, CardKind.Alcohol, ProgramMultiCardSuit(owner, s), ProgramMultiCardColor(owner, s)) &&
        !HasBeneficiarySuitShield(owner.Seat, owner.Seat, ProgramMultiCardSuit(owner, s));
    private CommandError? ValidateAnniversaryYizanPlay(CharacterState owner, LegalAction action, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (ValidateProgramSelection(action, ids, targets) is { } invalid) return invalid;
        if (action.ConversionSource is not { } source || action.PlayedCardKind is not { } kind || source.SkillId != AnniversaryZhaoTongZhaoGuangComposition.Yizan ||
            FindProgramMultiCardViewAsSelection(owner, ids, kind, false, source) is not { } selection)
            return new(CommandErrorCode.InvalidCard, "翊赞的原生牌名、两张实际材料或基本牌条件已变化。");
        if (kind == CardKind.Alcohol ? targets.Count != 0 || !AnniversaryYizanAlcoholAllowed(owner, selection) : targets.Count != 1 || !AnniversaryYizanPlayTargets(owner, selection).Contains(targets[0]))
            return new(CommandErrorCode.InvalidTarget, "翊赞使用的实际目标、距离或本回合次数不合法。");
        return null;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Submit, 44600)]
    private bool SubmitAnniversaryYizanPlay(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!; if (!command.ActivationId.StartsWith(AnniversaryYizanPlayPrefix, StringComparison.Ordinal)) return false;
        var invalid = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction);
        if (invalid is not null) { result = Reject(invalid.Code, invalid.Message); return true; }
        var owner = _players[command.ActorSeat];
        var action = AnniversaryYizanPairActions(owner).SingleOrDefault(a => a.ProgramSkillId == command.SkillId && a.ProgramActivationId == command.ActivationId);
        if (command.SkillOwnerSeat is not null || action is null) { result = Reject(CommandErrorCode.IllegalAction, "翊赞的双牌使用菜单已失效。"); return true; }
        if (ValidateAnniversaryYizanPlay(owner, action, command.CardIds, command.TargetSeats) is { } error) { result = Reject(error.Code, error.Message); return true; }
        result = Accept(() => { ClearPendingDecision(); ExecuteAnniversaryYizanPlay(owner, action, command.CardIds, command.TargetSeats); AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); });
        return true;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Execute, 44600)]
    private bool ExecuteAnniversaryYizanPlay(CharacterState owner, LegalAction action, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (action.ProgramActivationId?.StartsWith(AnniversaryYizanPlayPrefix, StringComparison.Ordinal) != true || action.ConversionSource is not { SkillId: AnniversaryZhaoTongZhaoGuangComposition.Yizan } source) return false;
        if (ValidateAnniversaryYizanPlay(owner, action, ids, targets) is { } invalid) throw new InvalidOperationException(invalid.Message);
        var kind = action.PlayedCardKind!.Value; var selection = FindProgramMultiCardViewAsSelection(owner, ids, kind, false, source)!;
        if (IsSlashCard(kind)) ResolveSlashCore(owner, _players[targets[0]], selection.Cards[0], kind, owner.Seat, physicalCards: selection.Cards, conversionSource: source);
        else if (kind == CardKind.Peach) ResolveRecoveryCard(owner, owner, selection.Cards[0], "桃", kind, conversionSource: source, physicalCards: selection.Cards);
        else
        {
            var id = BeginCardUse(selection.Cards[0], owner.Seat, [], kind, physicalCardIds: selection.Cards.Select(c => c.Id).ToArray(), conversionSource: source);
            MoveProgramCardsFromMultipleSources(selection.Cards.Select(c => c.Id).ToArray(), CardLocation.Processing, CardMoveReasons.Use);
            if (!HasIssuedRoundGainedBasicBonus(id, owner.Seat)) RecordMouZhaJiangAwareAlcoholDebit(id, owner);
            BeginSimpleCardUse(id, new(selection.Cards[0].Id, SimpleCardUseEffect.Alcohol));
        }
        return true;
    }
}
