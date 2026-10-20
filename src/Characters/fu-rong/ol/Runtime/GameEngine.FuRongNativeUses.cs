using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? FuRongDiscardQueryOwner(int seat) => _resolutionStack.LastOrDefault() is ProgramSkillFrame
        { FuRongPair: { Stage: FuRongPairStage.UseChoice or FuRongPairStage.NativeUse } } f && f.OwnerSeat == seat ? f : null;
    private bool FuRongUnspentDiscard(ProgramSkillFrame f, int id) => f.FuRongPair is { } r &&
        !r.UsedCardIds.Contains(id) && (r.OwnerPayment?.CardId == id || r.TargetPayment?.CardId == id) &&
        _cardZones.GetLocation(id) == CardLocation.DiscardPile;
    private IReadOnlyList<Card> AppendFuRongDiscardCards(CharacterState actor, IReadOnlyList<Card> held)
    {
        var f = FuRongDiscardQueryOwner(actor.Seat);
        if (f is null) return held;
        var r = f.FuRongPair!;
        var cards = _cardZones.CardsAt(CardLocation.DiscardPile).Where(c =>
            r.Stage == FuRongPairStage.UseChoice ? FuRongUnspentDiscard(f, c.Id) : r.SelectedUseCardId == c.Id);
        return Array.AsReadOnly(held.Concat(cards).DistinctBy(c => c.Id).ToArray());
    }
    private Card? FindFuRongDiscardCard(CharacterState actor, int id) =>
        IsFuRongSelectedDiscard(actor.Seat, id) ? _cardZones.CardsAt(CardLocation.DiscardPile).Single(c => c.Id == id) : null;
    private bool IsFuRongSelectedDiscard(int seat, int id)
    {
        if (id <= 0 || !_cardZones.Contains(CardLocation.DiscardPile, id)) return false;
        if (FuRongDiscardQueryOwner(seat) is { } querying)
            return querying.FuRongPair!.Stage == FuRongPairStage.UseChoice ? FuRongUnspentDiscard(querying, id) : querying.FuRongPair.SelectedUseCardId == id;
        return _resolutionStack.LastOrDefault() is CardUseFrame { FuRongReturn: { } ret } use &&
            ret.CardId == id && use.CardId == id && HasIssuedFuRongDistance(use.Id, seat);
    }
    private bool IsFuRongDiscardQuery(CharacterState actor, int? id) => id is > 0 &&
        FuRongDiscardQueryOwner(actor.Seat) is not null && IsFuRongSelectedDiscard(actor.Seat, id.Value);
    private bool HasFuRongDiscardDistance(CharacterState actor, CardKind kind, IReadOnlyList<int>? ids) =>
        ids is [var id] && IsFuRongDiscardQuery(actor, id);
    private bool HasIssuedFuRongDistance(long? existingUseFrameId, int ownerSeat)
    {
        if (existingUseFrameId is not { } id || LifecycleCardUse(id) is not
            { FuRongReturn: { } ret, Action: { } action, PhysicalCardIds: [var material] } use ||
            use.SourceSeat != ownerSeat || ret.CardUseFrameId != id || ret.CardId != material || ret.CardId != use.CardId ||
            ret.ActionId != action.ActionId || action.PhysicalCards is not [var cost] || cost.CardId != material ||
            cost.From != CardLocation.DiscardPile) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == id);
        return index > 0 && _resolutionStack[index - 1] is ProgramSkillFrame { FuRongPair:
            { Stage: FuRongPairStage.NativeUse, NativeCompleted: false, NativeReturn: { } issued } r } parent &&
            parent.OwnerSeat == ownerSeat && parent.Id == ret.ProgramFrameId && issued == ret && r.ReservedUseId == id &&
            r.SelectedUseCardId == material && r.UsedCardIds.Contains(material) &&
            CompleteProgramEventHistory().OfType<FuRongNativeIssuedEvent>().Count(e => e.Return == ret) == 1;
    }
    private bool HasIssuedFuRongUnlimitedUse(long id, int seat) => HasIssuedFuRongDistance(id, seat);

    private IReadOnlyList<LegalAction> FuRongNativeActions(ProgramSkillFrame f) =>
        Array.AsReadOnly(new[] { f.FuRongPair!.OwnerPayment, f.FuRongPair.TargetPayment }.OfType<FuRongPayment>()
            .Where(p => FuRongUnspentDiscard(f, p.CardId))
            .SelectMany(p => BuildLegalActions(_players[f.OwnerSeat], includeProgramActions: false, selectedPhysicalCardId: p.CardId))
            .Where(a => a.CardId is > 0 && a.ConversionSource is null &&
                a.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral))
            .ToArray());
    private IReadOnlyList<PromptChoice> FuRongUseChoices(ProgramSkillFrame f)
    {
        var result = FuRongNativeActions(f).Select((a, i) =>
        {
            var args = FuRongArgs(f, "use"); args["action-index"] = i.ToString(CultureInfo.InvariantCulture);
            return new PromptChoice(new($"fu-rong-use.{f.Id}.{i}"), a.Description, [a.CardId!.Value],
                a.TargetSeats.Count > 0 ? a.TargetSeats : a.TargetSeat is { } seat ? [seat] : [], args);
        }).ToList();
        result.Add(new(new($"fu-rong-use.{f.Id}.finish"), "结束效死用牌。", [], [], FuRongArgs(f, "finish")));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishFuRongUse(ProgramSkillFrame f)
    {
        var choices = FuRongUseChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "可以任意顺序使用仍在弃牌堆的效死弃牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name + " · 使用弃牌", skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void AnswerFuRongUse(ProgramSkillFrame f, PromptChoice selected)
    {
        if (selected.Parameters.GetValueOrDefault("branch") == "finish")
        { ClearPendingDecision(); FinishFuRongPair(f); return; }
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("action-index"), out var index))
            throw new InvalidOperationException("Xiaosi lost its exact native action index.");
        var actions = FuRongNativeActions(f);
        if (index < 0 || index >= actions.Count || selected.Cards is not [var id] || actions[index].CardId != id || !FuRongUnspentDiscard(f, id))
            throw new InvalidOperationException("Xiaosi lost its unspent original discard entity or native legality.");
        var r = f.FuRongPair!; ClearPendingDecision();
        ReplaceRuntimeTop(f with { FuRongPair = r with { Stage = FuRongPairStage.NativeUse, SelectedUseCardId = id,
            ReservedUseId = _resolutionSequence + 1, UsedCardIds = r.UsedCardIds.Append(id).ToArray(), NativeCompleted = false } });
        ExecuteAction(_players[f.OwnerSeat], actions[index]);
    }
    private void BindFuRongNativeUse(CardUseFrame use)
    {
        var at = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { FuRongPair: { Stage: FuRongPairStage.NativeUse } r } f ||
            r.ReservedUseId != use.Id) return;
        if (r.NativeReturn is not null || r.SelectedUseCardId != use.CardId || use.SourceSeat != f.OwnerSeat || use.Action is not { } action ||
            use.PhysicalCardIds is not [var id] || id != use.CardId || !r.UsedCardIds.Contains(id) ||
            (r.OwnerPayment?.CardId != id && r.TargetPayment?.CardId != id) || _cardZones.GetLocation(id) != CardLocation.DiscardPile)
            throw new InvalidOperationException("Xiaosi must issue its one selected paid original discard entity through native Use.");
        var ret = new FuRongUseReturn(f.Id, use.Id, id, use.CardKind, action.ActionId);
        ReplaceRuntimeFrame(f.Id, f with { FuRongPair = r with { NativeReturn = ret } });
        UpdateLifecycleCardUse(use.Id, u => u with { FuRongReturn = ret, UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new FuRongNativeIssuedEvent(ret));
    }
    [NativeUseReturn(26310)]
    private void ReturnFuRongNativeUse(CardUseFrame use)
    {
        if (use.FuRongReturn is not { } ret) return;
        var f = GetActiveProgramFrame(ret.ProgramFrameId); var r = f.FuRongPair!;
        if (r.NativeReturn != ret || use.Id != ret.CardUseFrameId || use.CardId != ret.CardId || use.Action?.ActionId != ret.ActionId ||
            _resolutionStack.LastOrDefault()?.Id != f.Id || r.NativeCompleted)
            throw new InvalidOperationException("Xiaosi native use lost its exact original owning parent return.");
        ReplaceRuntimeTop(f with { FuRongPair = r with { NativeCompleted = true } });
        if (use.CardAttack is { Active: true }) return;
        SettleFuRongNativeReturn(ret);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 26310)]
    private bool ReturnFuRongNativeAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FuRongPair:
                { Stage: FuRongPairStage.NativeUse, NativeCompleted: true, NativeReturn: { } ret } } || ret.CardUseFrameId != completion.ResolutionId) return false;
        SettleFuRongNativeReturn(ret); return true;
    }
    private void SettleFuRongNativeReturn(FuRongUseReturn ret)
    {
        var f = GetActiveProgramFrame(ret.ProgramFrameId); var r = f.FuRongPair!;
        if (r.NativeReturn != ret || !r.NativeCompleted || r.Stage != FuRongPairStage.NativeUse || LifecycleCardUse(ret.CardUseFrameId) is not null ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == ret.CardUseFrameId) != 1 ||
            CompleteProgramEventHistory().OfType<FuRongNativeReturnedEvent>().Any(e => e.Return == ret))
            throw new InvalidOperationException("Xiaosi cannot return before its whole native use or twice.");
        ReplaceRuntimeTop(f with { FuRongPair = r with { Stage = FuRongPairStage.NativeChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new FuRongNativeReturnedEvent(ret));
        // The native producer must finish its tail before this parent issues a
        // second card. The normal runtime loop resumes the owning instruction.
    }

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 50)]
    private bool DistancePhysicalSlash50HasIssuedFuRongDistance(ProgramSlashDistanceQuery q) =>
        HasIssuedFuRongDistance(q.ExistingUseFrameId, q.Actor.Seat);


    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 60)]
    private bool DistancePhysicalSlash60HasFuRongDiscardDistance(ProgramSlashDistanceQuery q) =>
        HasFuRongDiscardDistance(q.Actor, q.Kind, q.PhysicalCardIds ?? (q.PhysicalCard!.Id > 0 ? [q.PhysicalCard!.Id] : []));


    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 20)]
    private bool DistanceVirtualSlash20HasFuRongDiscardDistance(ProgramSlashDistanceQuery q) =>
        HasFuRongDiscardDistance(q.Actor, q.Kind, q.PhysicalCardIds);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 10)]
    private bool FuRongSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) =>
        HasFuRongDiscardDistance(query.Actor, query.Kind, query.PhysicalCardIds ?? (query.PhysicalCard is { Id: > 0 } ? [query.PhysicalCard.Id] : []));
}
