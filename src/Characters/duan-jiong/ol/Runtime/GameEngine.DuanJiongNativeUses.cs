namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? SaoguDiscardQueryOwner(int actor) => _resolutionStack.LastOrDefault() is ProgramSkillFrame
        { DuanJiongSaogu: { Stage: DuanJiongSaoguStage.UseChoice or DuanJiongSaoguStage.NativeUse, ActorSeat: var seat } } f && seat == actor ? f : null;
    private bool SaoguUnspentDiscard(ProgramSkillFrame f, int id) => f.DuanJiongSaogu is { } r && !r.UsedCardIds.Contains(id) &&
        r.Payments.Any(c => c.CardId == id && IsSlashCard(c.Kind)) && _cardZones.GetLocation(id) == CardLocation.DiscardPile &&
        CompleteProgramEventHistory().OfType<DuanJiongSaoguEntityPaidEvent>().SingleOrDefault(e => e.FrameId == f.Id && !e.DelegateFee && e.CardId == id) is { } paid &&
        !_cardMovements.Any(m => m.CardId == id && m.Sequence > paid.Sequence && m.From == CardLocation.DiscardPile);
    private IReadOnlyList<Card> AppendDuanJiongSaoguDiscardCards(CharacterState actor, IReadOnlyList<Card> held)
    {
        if (SaoguDiscardQueryOwner(actor.Seat) is not { } f) return held;
        var r = f.DuanJiongSaogu!;
        return Array.AsReadOnly(held.Concat(_cardZones.CardsAt(CardLocation.DiscardPile).Where(c => r.Stage == DuanJiongSaoguStage.UseChoice ?
            SaoguUnspentDiscard(f, c.Id) : r.SelectedUseCardId == c.Id)).DistinctBy(c => c.Id).ToArray());
    }
    private Card? FindDuanJiongSaoguDiscardCard(CharacterState actor, int id) => IsDuanJiongSaoguSelectedDiscard(actor.Seat, id) ? _cardZones.CardsAt(CardLocation.DiscardPile).Single(c => c.Id == id) : null;
    private bool IsDuanJiongSaoguSelectedDiscard(int actor, int id)
    {
        if (id <= 0 || !_cardZones.Contains(CardLocation.DiscardPile, id)) return false;
        if (SaoguDiscardQueryOwner(actor) is { } f) return f.DuanJiongSaogu!.Stage == DuanJiongSaoguStage.UseChoice ? SaoguUnspentDiscard(f, id) : f.DuanJiongSaogu.SelectedUseCardId == id;
        return _resolutionStack.LastOrDefault() is CardUseFrame { DuanJiongSaoguUse: { } origin } use && origin.CardId == id && HasIssuedDuanJiongSaoguUnlimitedUse(use.Id, actor);
    }
    private bool IsDuanJiongSaoguDiscardQuery(CharacterState actor, int? id) => id is > 0 && SaoguDiscardQueryOwner(actor.Seat) is not null && IsDuanJiongSaoguSelectedDiscard(actor.Seat, id.Value);
    private IReadOnlyList<LegalAction> SaoguNativeActions(ProgramSkillFrame f) => Array.AsReadOnly(f.DuanJiongSaogu!.Payments.Where(c => SaoguUnspentDiscard(f, c.CardId))
        .SelectMany(c => BuildLegalActions(_players[f.DuanJiongSaogu.ActorSeat], false, c.CardId))
        .Where(a => a.Kind == LegalActionKind.Slash && a.CardId is > 0 && a.ConversionSource is null && (a.AdditionalConversionSources?.Count ?? 0) == 0).ToArray());
    private void IssueSaoguUse(ProgramSkillFrame f, PromptChoice choice)
    {
        var r = f.DuanJiongSaogu!;
        var index = int.Parse(choice.Parameters["index"], System.Globalization.CultureInfo.InvariantCulture); var actions = SaoguNativeActions(f);
        if (index < 0 || index >= actions.Count || choice.Cards is not [var id] || actions[index].CardId != id || !SaoguUnspentDiscard(f, id))
            throw new InvalidOperationException("Saogu requires its still-available original paid Slash and current native target legality.");
        ReplaceRuntimeTop(f with { DuanJiongSaogu = r with { Stage = DuanJiongSaoguStage.NativeUse, SelectedUseCardId = id, ReservedUseId = _resolutionSequence + 1,
            UsedCardIds = r.UsedCardIds.Append(id).ToArray(), NativeUse = null, NativeCompleted = false } });
        ExecuteAction(_players[r.ActorSeat], actions[index]);
    }
    private void BindDuanJiongSaoguNativeUse(CardUseFrame use)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { DuanJiongSaogu: { Stage: DuanJiongSaoguStage.NativeUse } r } f || r.ReservedUseId != use.Id) return;
        if (!ValidSaogu(f) || r.NativeUse is not null || r.SelectedUseCardId != use.CardId || use.SourceSeat != r.ActorSeat || !IsSlashCard(use.CardKind) || use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != r.ActorSeat || action.ProviderSeat != r.ActorSeat || action.EffectiveKind != use.CardKind || action.ConversionChain.Count != 0 ||
            use.PhysicalCardIds is not [var material] || material != use.CardId || action.PhysicalCards is not [{ From: var from, CardId: var paid }] || paid != material || from != CardLocation.DiscardPile ||
            _cardZones.GetLocation(material) != CardLocation.DiscardPile || !r.Payments.Any(c => c.CardId == material && IsSlashCard(c.Kind)))
            throw new InvalidOperationException("Saogu must issue its exact original discarded Slash through a real native card use.");
        var origin = new DuanJiongSaoguUseOrigin(f.Id, use.Id, action.ActionId, material, use.CardKind, r.ActorSeat);
        ReplaceRuntimeFrame(f.Id, f with { DuanJiongSaogu = r with { NativeUse = origin } }); UpdateLifecycleCardUse(use.Id, u => u with { DuanJiongSaoguUse = origin, UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new DuanJiongSaoguNativeIssuedEvent(origin));
    }
    private bool HasIssuedDuanJiongSaoguUnlimitedUse(long id, int actor) => LifecycleCardUse(id) is { DuanJiongSaoguUse: { } o, Action: { } action } use &&
        o.CardUseFrameId == id && (o.ActorSeat == actor || use.SourceSeat == actor) && o.ActionId == action.ActionId && o.CardId == use.CardId && IsSlashCard(use.CardKind) &&
        ShownEntityUseActorMatches(use, o.ActorSeat, o.ActorSeat) && action.PhysicalCards is [{ From: var from, CardId: var material }] && from == CardLocation.DiscardPile && material == o.CardId &&
        _resolutionStack.SingleOrDefault(f => f.Id == o.ProgramFrameId) is ProgramSkillFrame { DuanJiongSaogu: { Stage: DuanJiongSaoguStage.NativeUse, NativeCompleted: false, NativeUse: { } stored } } parent &&
        stored == o && ValidSaogu(parent) && CompleteProgramEventHistory().OfType<DuanJiongSaoguNativeIssuedEvent>().Count(e => e.Origin == o) == 1;
    [NativeUseValidation(27060)]
    private void AssertSaoguNativeUse(CardUseFrame use)
    { if (use.DuanJiongSaoguUse is { } o && (use.Step != ResolutionFrameStep.Completed || !HasIssuedDuanJiongSaoguUnlimitedUse(use.Id, o.ActorSeat))) throw new InvalidOperationException("Saogu lost its exact whole native Slash completion."); }
    [NativeUseReturn(27060)]
    private void ReturnSaoguNativeUse(CardUseFrame use)
    {
        if (use.DuanJiongSaoguUse is not { } o) return;
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.DuanJiongSaogu!;
        if (r.NativeUse != o || r.Stage != DuanJiongSaoguStage.NativeUse || r.NativeCompleted || _resolutionStack.LastOrDefault()?.Id != f.Id || _resolutionStack.Any(x => x.Id == use.Id))
            throw new InvalidOperationException("Saogu native return cannot escape its original paid instruction or return twice.");
        ReplaceRuntimeTop(f with { DuanJiongSaogu = r with { NativeCompleted = true } });
        if (use.CardAttack is not { Active: true }) SettleSaoguNativeUse(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 27060)]
    private bool ReturnSaoguNativeAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DuanJiongSaogu: { Stage: DuanJiongSaoguStage.NativeUse, NativeCompleted: true, NativeUse: { } o } } || o.CardUseFrameId != completion.ResolutionId) return false;
        SettleSaoguNativeUse(o); return true;
    }
    private void SettleSaoguNativeUse(DuanJiongSaoguUseOrigin o)
    {
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.DuanJiongSaogu!;
        if (r.NativeUse != o || !r.NativeCompleted || LifecycleCardUse(o.CardUseFrameId) is not null ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == o.CardUseFrameId) != 1 ||
            CompleteProgramEventHistory().OfType<DuanJiongSaoguNativeReturnedEvent>().Any(e => e.Origin == o)) throw new InvalidOperationException("Saogu must return after one complete native use.");
        ReplaceRuntimeTop(f with { DuanJiongSaogu = r with { Stage = DuanJiongSaoguStage.NativeChildren } }); AdvanceEventRulesAndQueueFact(new DuanJiongSaoguNativeReturnedEvent(o));
    }
}
