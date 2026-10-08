namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record AssistedFactionSlashCard(Card Card, CardKind Kind, CardConversionSource? Source);

    private IReadOnlyList<AssistedFactionSlashCard> GetAssistedFactionSlashCards(FactionCardRequestHandle pending, CharacterState provider)
    {
        var variants = new List<AssistedFactionSlashCard>();
        foreach (var card in GetPlayableCards(provider).Concat(GetEquipment(provider)).Concat(GetLastZoneJudgmentConversionCards(provider, forResponse: false)).DistinctBy(card => card.Id))
        {
            if (IsTurnHandCardRestricted(provider, card)) continue;
            var hasIdentity = HasProgramCardIdentity(provider, card);
            foreach (var kind in SlashKinds)
            {
                if (!CanSupplyAssistedFactionSlash(pending, kind, [card])) continue;
                if (!hasIdentity && !IsLastZoneJudgmentCard(provider, card) && card.Kind == kind) variants.Add(new(card, kind, null));
                var sources = hasIdentity
                    ? GetProgramCardIdentitySources(provider, card, kind, forResponse: false)
                    : GetProgramViewAsConversions(provider, card, kind, forResponse: false);
                variants.AddRange(sources.Select(source => new AssistedFactionSlashCard(card, kind, source)));
            }
        }
        return variants;
    }

    private IReadOnlyList<PromptChoice> CreateAssistedFactionSlashChoices(CharacterState provider, FactionCardRequestHandle pending)
    {
        var targets = new[] { pending.TargetSeat!.Value };
        Dictionary<string, string> Parameters(string response, CardKind kind) => new()
        {
            ["response"] = response, ["response-card-kind"] = kind.ToString(), ["skill"] = GetFactionRequestSkillId(pending)
        };
        var choices = new List<PromptChoice>();
        foreach (var variant in GetAssistedFactionSlashCards(pending, provider))
        {
            var parameters = Parameters("faction-slash-slash", variant.Kind);
            if (variant.Source is { } source) AddConversionParameters(parameters, source);
            var sourceId = variant.Source is { } conversion ? $"{conversion.SkillId}.{conversion.BindingId}.{conversion.SkillInstanceId}" : "native";
            choices.Add(new(new ChoiceId($"assisted-faction.{variant.Kind}.{variant.Card.Id}.{sourceId}"),
                $"打出【{CardCatalog.Get(variant.Kind).DisplayName}】，响应【{GetFactionRequestDisplayName(pending)}】。", [variant.Card.Id], targets, parameters));
        }
        foreach (var pair in GetFactionRequestZhangbaPairs(pending, provider))
        {
            var parameters = Parameters("zhangba-slash", CardKind.Slash);
            parameters["equipment"] = CardKind.ZhangbaSerpentSpear.ToString();
            choices.Add(new(new ChoiceId($"assisted-faction.zhangba.{pair[0].Id}.{pair[1].Id}"),
                $"发动丈八蛇矛，打出两张手牌响应【{GetFactionRequestDisplayName(pending)}】。", pair.Select(card => card.Id).ToArray(), targets, parameters));
        }
        foreach (var selection in GetFactionRequestMultiCardSelections(pending, provider))
        {
            var parameters = Parameters("program-view-as-slash", selection.OutputKind);
            AddConversionParameters(parameters, selection.Source);
            var ids = selection.Cards.Select(card => card.Id).ToArray();
            choices.Add(new(new ChoiceId($"assisted-faction.multi.{selection.OutputKind}.{selection.Source.SkillId}.{selection.Source.BindingId}.{string.Join('-', ids)}"),
                $"发动【{ProgramConversionName(selection.Source)}】，打出【{CardCatalog.Get(selection.OutputKind).DisplayName}】响应【{GetFactionRequestDisplayName(pending)}】。", ids, targets, parameters));
        }
        choices.Add(new(new ChoiceId("faction-slash.decline"), $"不响应【{GetFactionRequestDisplayName(pending)}】。", [], targets,
            new Dictionary<string, string> { ["response"] = "faction-slash-decline", ["skill"] = GetFactionRequestSkillId(pending) }));
        return choices;
    }

    private void ResolveAssistedFactionSlashChoice(PromptChoice selected, bool advanceToHumanBoundary)
    {
        var pending = ActiveFactionCardRequest;
        if (pending is not { IsAssistedProgramUse: true, AwaitingProviders: true } ||
            _pendingDecision is not { Kind: DecisionKind.RespondSlash } prompt || prompt.PlayerSeat != pending.CurrentCandidateSeat)
            throw new InvalidOperationException("The assisted faction response lost its provider continuation.");
        var provider = _players[pending.CurrentCandidateSeat];
        var canonical = CreateAssistedFactionSlashChoices(provider, pending).SingleOrDefault(choice => choice.Id == selected.Id);
        if (canonical is null || !AssistedChoicesEqual([selected], [canonical]))
            throw new InvalidOperationException("The assisted faction response changed its cards, kind or conversion source.");
        var response = canonical.Parameters["response"];
        if (response == "faction-slash-decline")
            ResolveFactionSlashCandidateResponse(pending, false, null);
        else if (response == "zhangba-slash")
            ResolveFactionSlashZhangbaCandidateResponse(pending, provider, FindZhangbaSlashPair(provider, canonical.Cards)!);
        else
        {
            var kind = ReadResponseCardKind(canonical) ?? throw new InvalidOperationException("The assisted faction response lost its Slash kind.");
            IReadOnlyList<Card> cards;
            CardConversionSource? conversion;
            if (response == "program-view-as-slash")
            {
                conversion = RequireConversionSource(canonical);
                cards = GetFactionRequestMultiCardSelections(pending, provider).Single(selection => selection.OutputKind == kind &&
                    selection.Source == conversion && selection.Cards.Select(card => card.Id).SequenceEqual(canonical.Cards)).Cards;
            }
            else
            {
                CaptureSelectedResponseConversion(canonical);
                var variant = GetAssistedFactionSlashCards(pending, provider).Single(item => item.Card.Id == canonical.Cards.Single() && item.Kind == kind &&
                    item.Source == (TryReadConversionSource(canonical.Parameters, out var source) ? source : null));
                cards = [variant.Card];
                if (TryBeginFactionSlashZhuqueFanChoice(pending, provider, cards, kind))
                {
                    AdvanceRulesAndPublishState();
                    if (advanceToHumanBoundary) AdvanceToHumanBoundary();
                    return;
                }
                conversion = variant.Source;
            }
            ClearPendingDecision();
            BeginProvidedFactionSlashSlash(pending, provider, cards, kind, usesZhuqueFan: false, conversionSource: conversion);
        }
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private bool CanSupplyAssistedFactionSlash(FactionCardRequestHandle pending, CardKind kind, IReadOnlyList<Card> cards, bool isTrueZhangba = false) =>
        IsRedSlashProviderPaymentLegal(pending,kind,cards,isTrueZhangba) &&
        (!IsFactionSlashUse(pending) || !IsTurnPhysicalUseForbidden(pending.OwnerSeat,cards.Select(c=>c.Id).ToArray())) &&
        (!pending.IsAssistedProgramUse || HasRankSlashRange(_players[pending.OwnerSeat],kind) || pending.TargetSeat is { } target &&
        (IsAssistedProvidedSlashTarget(pending.OwnerSeat, target, kind) ||
         kind == CardKind.Slash && HasZhuqueFan(_players[pending.OwnerSeat]) &&
         (cards.Count > 1 || cards is [var primary] && primary.Kind == CardKind.Slash) &&
         IsAssistedProvidedSlashTarget(pending.OwnerSeat, target, CardKind.FireSlash)));

    private IReadOnlyList<IReadOnlyList<Card>> GetFactionRequestZhangbaPairs(FactionCardRequestHandle pending, CharacterState provider) =>
        GetZhangbaSlashPairs(provider).Where(pair => CanSupplyAssistedFactionSlash(pending, CardKind.Slash, pair, isTrueZhangba:true)).ToArray();

    private IReadOnlyList<ProgramMultiCardViewAsSelection> GetFactionRequestMultiCardSelections(FactionCardRequestHandle pending, CharacterState provider) =>
        (pending.IsAssistedProgramUse
            ? SlashKinds.SelectMany(kind => GetProgramMultiCardViewAsSelections(provider, kind, false,ignoreSuitUseProhibition:true)
                .Where(item => ViewAsRule(item.Source)?.RoundDistinctBasicUse is null || item.OutputKind == kind))
            : GetProgramMultiCardViewAsSelections(provider, pending.RequiredKind, !IsFactionSlashUse(pending),ignoreSuitUseProhibition:true))
            .Where(selection => ViewAsRule(selection.Source)?.RoundDistinctBasicUse is null &&
                CanSupplyAssistedFactionSlash(pending, selection.OutputKind, selection.Cards)).ToArray();

    private bool CanRequestAssistedProgramFactionSlash(int actorSeat, int targetSeat) =>
        IsValidPlayerSeat(actorSeat) && IsValidPlayerSeat(targetSeat) &&
        GetFactionResponsePolicy(_players[actorSeat], CardKind.Slash) is not null &&
        SlashKinds.Any(kind => IsAssistedProvidedSlashTarget(actorSeat, targetSeat, kind));

    private bool IsAssistedProvidedSlashTarget(int actorSeat, int targetSeat, CardKind kind, int? effectiveRank = null, bool allowPotentialRank = true) =>
        _players[actorSeat].IsAlive && _players[targetSeat].IsAlive && actorSeat != targetSeat &&
        (ActiveFactionCardRequest is { IsAssistedProgramUse: true, ProgramSkillFrameId: { } fixedFrameId } &&
         _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == fixedFrameId) is { FixedTargetSlash: { } fixedDraft } fixedFrame &&
         fixedFrame.OwnerSeat == actorSeat && fixedDraft.TargetSeat == targetSeat
            ? IsFixedTargetFactionSlashLegal(fixedFrame, kind, effectiveRank, allowPotentialRank)
            : ActiveFactionCardRequest is { IsAssistedProgramUse: true, ProgramSkillFrameId: { } giftFrameId } &&
         _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == giftFrameId) is { CompletedCardGiftDraft.RecipientSeat: { } recipient } giftFrame && recipient == actorSeat
            ? CompletedGiftSlashTargets(giftFrame, actorSeat).Contains(targetSeat)
            : IsWithinSpecificSlashRange(_players[actorSeat],_players[targetSeat],kind,effectiveRank) || allowPotentialRank && effectiveRank is null && HasPotentialRankSlashRange(_players[actorSeat],_players[targetSeat],kind)) &&
        CanSpendSlashUse(_players[actorSeat], _players[targetSeat], ignoresCount: true, kind) &&
        !IsSlashProhibited(_players[targetSeat]);

    private void BeginAssistedProgramFactionSlashRequest(ProgramSkillFrame frame, int actorSeat, int targetSeat, string resultBind)
    {
        if (ActiveFactionCardRequest is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            current.SelectedTargetSeats is not [var selected] || selected != actorSeat || current.OwnerSeat == actorSeat ||
            !(current.CompletedCardGiftDraft is { RecipientSeat: { } recipient, RequestTargetSeat: { } giftTarget } && recipient == actorSeat && giftTarget == targetSeat
                ? CompletedGiftSlashTargets(current, actorSeat).Contains(targetSeat) && GetFactionResponsePolicy(_players[actorSeat], CardKind.Slash) is not null
                : CanRequestAssistedProgramFactionSlash(actorSeat, targetSeat)))
            throw new InvalidOperationException("An assisted faction Slash lost its program parent, actor or target.");
        var instruction = ProgramInstructionResolver.Default.Resolve(current, _contentRegistry.GetSkill(current.SkillId).Program!).GetPausedInstruction(current.InstructionIndex).Effect;
        if (!(instruction.Op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget && instruction.ResultBind == resultBind ||
              instruction.Op == SkillProgramEffectOp.OfferCompletedCardGift && resultBind == CompletedGiftFactionResultBind && current.CompletedCardGiftDraft is not null || IsNearestLegalFactionOrigin(current, actorSeat, targetSeat, resultBind)) ||
            current.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("An assisted faction Slash must answer its paused card request exactly once.");
        var policy = GetFactionResponsePolicy(_players[actorSeat], CardKind.Slash)!;
        var candidates = GetFactionProviderSeats(actorSeat, policy.FactionId);
        ActiveFactionCardRequest = new FactionCardRequestHandle(this, frame.Id, FactionCardRequestPurpose.AssistedProgramUse,
            actorSeat, candidates, policy.FactionId, targetSeat: targetSeat, programSkillFrameId: frame.Id,
            policySource: policy, assistedResultBind: resultBind);
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(frame.Id, actorSeat, candidates, policy.SkillId, IsActiveUse: true, targetSeat));
        AdvanceFactionSlashCandidate();
    }

    private void ValidateAssistedFactionSlashParent(FactionCardRequestHandle pending, ProgramSkillFrame frame)
    {
        if (ValidateFixedTargetFactionSlashParent(pending, frame)) return;
        if (ValidateNearestLegalFactionParent(pending, frame)) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var answered = frame.ChoiceBindings.SingleOrDefault(binding => binding.Name == pending.AssistedResultBind);
        if (!(effect.Op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget && effect.ResultBind == pending.AssistedResultBind ||
              effect.Op == SkillProgramEffectOp.OfferCompletedCardGift && pending.AssistedResultBind == CompletedGiftFactionResultBind && frame.CompletedCardGiftDraft is { } gift &&
              gift.RecipientSeat == pending.OwnerSeat && gift.RequestTargetSeat == pending.TargetSeat) ||
            frame.SelectedTargetSeats is not [var actor] || actor != pending.OwnerSeat || frame.OwnerSeat == actor ||
            pending.TargetSeat is not { } target || !IsValidPlayerSeat(target) || pending.PolicySource is null ||
            (pending.ActiveAttack is null ? answered is not null : answered?.OptionId != "used-slash"))
            throw new InvalidOperationException("An assisted faction Slash changed its instruction, participants or result.");
    }
}
