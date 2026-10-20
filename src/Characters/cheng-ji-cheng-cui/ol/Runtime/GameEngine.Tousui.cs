namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasChengJiChengCui => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveTousuiDodgeRequirement) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXumingDamagePromise);
    private static CardConversionSource ChengJiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool IsTousuiSource(CardConversionSource? source) => source is { SkillId: ChengJiChengCuiComposition.Tousui, BindingId: "bottom-cost-slash" } && ViewAsRule(source) is not null;
    private IReadOnlyList<Card> TousuiPhysicalCards(CharacterState actor) => Array.AsReadOnly(GetPlayableCards(actor)
        .Concat(GetEquipment(actor)).DistinctBy(c => c.Id).Where(c => _cardZones.GetLocation(c.Id) is { OwnerSeat: var seat, Zone: var zone } &&
            seat == actor.Seat && zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain).ToArray());
    private bool IsTousuiVariableSource(CharacterState actor, SkillProgramViewAs rule, Card card, CardLocation from) =>
        rule.CostDestination == SkillProgramCardDestination.DrawPileBottom && rule.VariableInputCount && from.OwnerSeat == actor.Seat &&
        TousuiPhysicalCards(actor).Any(c => c.Id == card.Id);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 32200)]
    private void CaptureTousuiUse(IGameEvent fact)
    {
        if (!HasChengJiChengCui || fact is not CardUseDeclaredEvent e || LifecycleCardUse(e.ResolutionId) is not
            { Action: { Type: CardActionType.Use } action, Tousui: null } use || !IsSlashCard(use.CardKind)) return;
        var source = action.ConversionChain.LastOrDefault(IsTousuiSource);
        if (source is null) return;
        if (action.PhysicalCards.Count < 1 || source.OwnerSeat != action.ProviderSeat ||
            !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(use.PhysicalCardIds ?? []) ||
            action.PhysicalCards.Any(c => c.From.OwnerSeat != action.ProviderSeat ||
                c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)))
            throw new InvalidOperationException("Tousui requires nonempty genuine native HE/usable-Ox material costs.");
        var hash = _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash;
        UpdateLifecycleCardUse(use.Id, u => u with { Tousui = new(use.Id, action.ActionId, source, hash,
            action.ActorSeat, action.ProviderSeat, action.PhysicalCards.Count) });
        AdvanceEventRulesAndQueueFact(new TousuiUseIssuedEvent(use.Id, action.ActionId, source, hash,
            action.ActorSeat, action.ProviderSeat, action.PhysicalCards.Count));
        for (var index = 0; index < action.PhysicalCards.Count; index++)
            AdvanceEventRulesAndQueueFact(new TousuiMaterialFrozenEvent(use.Id, index, action.PhysicalCards[index]));
    }
    private bool TousuiUseProof(CardUseFrame use) => use.Tousui is { } r && use.Action is { Type: CardActionType.Use } a &&
        r.UseId == use.Id && r.ActionId == a.ActionId && r.Source.OwnerSeat == r.ProviderSeat && a.ProviderSeat == r.ProviderSeat &&
        r.MaterialCount > 0 && a.PhysicalCards.Count == r.MaterialCount && IsTousuiSource(r.Source) &&
        a.ConversionChain.Contains(r.Source) && a.EffectiveKind == use.CardKind && IsSlashCard(use.CardKind) &&
        ProgramEventHistory<TousuiUseIssuedEvent>().Count(e => e == new TousuiUseIssuedEvent(use.Id, a.ActionId,
            r.Source, r.GameplayHash, r.ActorSeat, r.ProviderSeat, r.MaterialCount)) == 1 &&
        ProgramEventHistory<TousuiMaterialFrozenEvent>().Where(e => e.UseId == use.Id).OrderBy(e => e.Index)
            .Select(e => e.Cost).SequenceEqual(a.PhysicalCards);

    // The existing atomic native Slash payment owns all movement children. This
    // callback only fixes the native bottom order and signs that same payment.
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 32200)]
    private void SignTousuiBottomPayment(CardMovementBatchContext batch)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { Tousui: { } r } use || batch.ParentFrameId != use.Id) return;
        if (!TousuiUseProof(use) || r.BottomPaid || batch.ParentFrameId != use.Id || batch.Movements.Count != r.MaterialCount ||
            !use.Action!.PhysicalCards.All(cost => batch.Movements.Count(m => m.CardId == cost.CardId &&
                m.CardKind == cost.CardKind && m.From == cost.From && m.Reason == CardMoveReasons.Use &&
                m.To.Zone is CardZoneKind.DrawPile or CardZoneKind.OutsideGame) == 1))
            throw new InvalidOperationException("Tousui lost its single native real bottom-payment batch.");
        var bottom = use.Action.PhysicalCards.Select(c => c.CardId).Where(id => _cardZones.GetLocation(id) == CardLocation.DrawPile).ToArray();
        if (bottom.Length != 0) _cardZones.PlaceDrawPileCardsAtBottom(bottom);
        var before = batch.Movements[0].Sequence - 1; var after = batch.Movements[^1].Sequence;
        UpdateLifecycleCardUse(use.Id, u => u with { Tousui = r with { BottomPaid = true, Before = before, After = after } });
        AdvanceEventRulesAndQueueFact(new TousuiBottomPaidEvent(use.Id, r.ActionId, r.MaterialCount, before, after));
    }
    private int TousuiRequiredDodge(long useId, int ordinary) => LifecycleCardUse(useId) is { Tousui: { BottomPaid: true } r } use &&
        TousuiUseProof(use) ? Math.Max(ordinary, r.MaterialCount) : ordinary;
    private SkillProgramStepOutcome ResolveTousuiRequirement(ProgramSkillFrame f)
    {
        if (f.WindowContext?.CardUse is not { } c || LifecycleCardUse(c.ParentCardUseFrameId) is not { Tousui: { BottomPaid: true } r } use ||
            !TousuiUseProof(use) || r.Source.SkillId != f.SkillId || r.Source.SkillInstanceId != f.SkillInstanceId ||
            c.CardActionId != r.ActionId)
            throw new InvalidOperationException("Tousui requires its exact paid native Slash, never a pure Played action.");
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramCapability]
    private sealed class ChengJiChengCuiCapability(GameEngine g) : IChengJiChengCuiHost, ITousuiSelectionQuery
    {
        public IReadOnlyList<Card> Cards(int owner) => g.TousuiSources(g._players[owner]).SelectMany(g.TousuiRequestCards).DistinctBy(c => c.Id).ToArray();
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveTousuiDodgeRequirement => g.ResolveTousuiRequirement(f),
            SkillProgramEffectOp.ResolveXumingDamagePromise => g.ResolveXumingDamage(f),
            SkillProgramEffectOp.ResolveXumingIssuedTurnEndUses => g.BeginXumingEnding(f),
            _ => throw new InvalidOperationException("Unknown Cheng Ji Cheng Cui operation.")
        };
    }
}
