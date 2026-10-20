namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool WuHfMarkedFor(int owner, int id, WuHuangFuSongTurn turn) => HasWuHuangFuSongCapability && id > 0 &&
        ProgramEventHistory<WuHuangFuSongLianjieEntityEvent>().Any(e => e.OwnerSeat == owner && e.CardId == id && e.Turn == turn &&
            _cardMovements.Any(m => m.Sequence == e.DrawSequence && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner) && m.Reason.Value == WuHfDraw));
    private bool HasWuHfMaterialPrivilege(CharacterState actor, IReadOnlyList<int>? ids)
    {
        if (!HasWuHuangFuSongCapability || !actor.IsAlive || ids is not { Count: > 0 } || ids.Distinct().Count() != ids.Count || !WuHfTurnOpen(WuHfTurn())) return false;
        var paidUse = _resolutionStack.OfType<CardUseFrame>().Any(use => use.WuHuangFuSongUse is { LianjieMaterials: true } r && r.ActorSeat == actor.Seat && r.ProviderSeat == actor.Seat &&
            use.Action is { Type: CardActionType.Use } a && a.ActionId == r.ActionId && a.ActorSeat == actor.Seat && a.ProviderSeat == actor.Seat &&
            a.PhysicalCards.Count == ids.Count && a.PhysicalCards.All(c => ids.Contains(c.CardId)) && ProgramEventHistory<WuHuangFuSongUseAdmittedEvent>().Any(e => e.Receipt == r));
        return ids.All(id => WuHfMarkedFor(actor.Seat, id, WuHfTurn()) &&
            (_cardZones.GetLocation(id) is { OwnerSeat: var owner, Zone: CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain } && owner == actor.Seat ||
             _cardZones.GetLocation(id) == CardLocation.Processing && (paidUse || UnclaimedDeclarationPayment(actor.Seat, id) is { ActorSeat: var seat, ProviderSeat: var provider } && seat == actor.Seat && provider == seat)));
    }
    private bool HasWuHfPotential(int owner) => HasWuHuangFuSongCapability && IsValidPlayerSeat(owner) && GetPlayableCards(_players[owner]).Any(c => HasWuHfMaterialPrivilege(_players[owner], [c.Id]));
    private bool HasWuHfOrDixianMaterialPrivilege(CharacterState actor, CardKind kind, IReadOnlyList<int>? ids, CardConversionSource? source = null) =>
        HasWuHfMaterialPrivilege(actor, ids) || HasWuLuKangDixianMaterialPrivilege(actor, kind, ids, source);
    private bool HasWuHfOrWuyouMaterialPrivilege(CharacterState actor, CardKind kind, IReadOnlyList<int>? ids, long? useId = null) =>
        (useId is { } id ? HasIssuedWuHfUse(id, actor.Seat) : HasWuHfMaterialPrivilege(actor, ids)) || HasWuyouMaterialPrivilege(actor, kind, ids, useId);
    private CardActionContext CaptureWuHfUseAction(CardActionContext a) => !HasWuHuangFuSongCapability || a.EffectiveRank is not null ? a :
        new(a.ActionId, a.ParentActionId, a.Type, a.ActorSeat, a.ProviderSeat, a.RequesterSeat, a.ResponderSeat, a.OpponentSeat, a.EffectiveKind, a.TargetSeats,
            a.PhysicalCards, a.ConversionChain, a.DesignatedTargetSeats, a.EffectiveSuit, 0, a.EffectiveIsRed, a.FactionOrigin);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 10)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 20)]
    private bool HasWuHfOrDixianIssuedUse(long id, int actor) => HasIssuedWuHfUse(id, actor) || HasIssuedWuLuKangDixianUse(id, actor);
    private bool HasWuHfOrDixianPotential(int actor) => HasWuHfPotential(actor) || HasWuLuKangDixianGrant(actor);
    private bool HasIssuedWuHfUse(long id, int actor) => LifecycleCardUse(id) is { WuHuangFuSongUse: { LianjieMaterials: true } r, Action: { } a } &&
        r.UseId == id && r.ActionId == a.ActionId && r.ActorSeat == actor && r.ProviderSeat == actor && a.ActorSeat == actor && a.ProviderSeat == actor &&
        r.MaterialCount == a.PhysicalCards.Count && r.MaterialCount > 0 && WuHfTurnOpen(r.Turn) &&
        a.PhysicalCards.All(c => c.From.OwnerSeat == actor && c.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain && WuHfMarkedFor(actor, c.CardId, r.Turn)) &&
        ProgramEventHistory<WuHuangFuSongUseAdmittedEvent>().Any(e => e.Receipt == r);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 30001)]
    private void ObserveWuHfUse(IGameEvent fact)
    {
        if (!HasWuHuangFuSongCapability) return;
        var id = fact switch { CardUseDeclaredEvent d => d.ResolutionId, ProgramCardUseActorReplacedEvent reassigned => reassigned.CardUseFrameId, _ => 0 };
        if (id <= 0 || LifecycleCardUse(id) is not { Action: { Type: CardActionType.Use } a } use) return;
        var own = a.ActorSeat == a.ProviderSeat && a.PhysicalCards.Count > 0 && a.PhysicalCards.All(c => c.From.OwnerSeat == a.ActorSeat);
        var r = new WuHuangFuSongUseReceipt(id, a.ActionId, a.ActorSeat, a.ProviderSeat, a.EffectiveKind, a.EffectiveRank, a.PhysicalCards.Count,
            own && a.PhysicalCards.All(c => c.From == CardLocation.Hand(a.ActorSeat)),
            own && a.PhysicalCards.All(c => c.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain && WuHfMarkedFor(a.ActorSeat, c.CardId, WuHfTurn())), WuHfTurn());
        ReplaceRuntimeFrame(id, use with { WuHuangFuSongUse = r }); AdvanceEventRulesAndQueueFact(new WuHuangFuSongUseAdmittedEvent(r));
    }
    private WuHuangFuSongJiangxianArmedEvent? WuHfOutstandingArm(int owner) => !HasWuHuangFuSongCapability ? null :
        ProgramEventHistory<WuHuangFuSongJiangxianArmedEvent>().SingleOrDefault(e => e.Source.OwnerSeat == owner && e.Turn == WuHfTurn() &&
            !ProgramEventHistory<WuHuangFuSongJiangxianSettledEvent>().Any(s => s.OwnerSeat == owner && s.Turn == e.Turn));
    private int WuHfPriorDamageCount(int owner)
    {
        var h = CompleteProgramEventHistory().ToArray(); var start = Array.FindLastIndex(h, e => e is TurnStartedEvent t && t.TurnNumber == _turnNumber && t.ActorSeat == _currentSeat);
        return Math.Min(5, h.Skip(start + 1).OfType<DamageAppliedEvent>().Count(e => !e.SourceLess && e.SourceSeat == owner && e.Amount > 0));
    }
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 50)]
    private IEnumerable<(CardUseEffectSource Source, int Amount)> WuHfDamageBonuses(IDamageAttempt attack)
    {
        // Native elemental propagation already carries the finalized first
        // damage amount, including this bonus; never add the same bonus twice.
        if (attack.IsChainPropagation || attack.IsSourceLess || attack.SourceSeat != attack.CardUserSeat || !HasIssuedWuHfUse(attack.ResolutionId, attack.SourceSeat) ||
            WuHfOutstandingArm(attack.SourceSeat) is not { } arm) return [];
        var amount = WuHfPriorDamageCount(attack.SourceSeat);
        return amount == 0 ? [] : [(new(arm.Source.SkillId, arm.Source.BindingId, arm.Source.OwnerSeat, arm.Source.SkillInstanceId), amount)];
    }
    private IReadOnlyList<TurnEndingBoundaryItem> IncludeWuHfEndingItems(DeferredTurnEndFrame parent, IReadOnlyList<TurnEndingBoundaryItem> ordinary)
    {
        var arm = WuHfOutstandingArm(parent.OwnerSeat); if (arm is null || !_players[parent.OwnerSeat].IsAlive) return ordinary;
        var items = ordinary.Where(i => i.Candidate is not { SkillId: WuHuangFuSongComposition.Jiangxian, BindingId: "lose-after-turn" }).ToList();
        var c = new ProgramTriggerCandidate(parent.OwnerSeat, WuHuangFuSongComposition.Jiangxian, "lose-after-turn", arm.Source.SkillInstanceId, arm.GameplayHash, 0, 0);
        items.Add(new(TurnEndingBoundaryItemKind.Program, 0, $"wu-huangfu-song:{arm.FrameId}", c, CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
        return Array.AsReadOnly(items.ToArray());
    }
    private bool IsIssuedWuHfEnding(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => c.SkillId == WuHuangFuSongComposition.Jiangxian && c.BindingId == "lose-after-turn" &&
        context.Window == SkillProgramTriggerWindow.AfterTurnEnded && context.OwnerSeat == c.OwnerSeat && context.SourceSeat == c.OwnerSeat &&
        WuHfOutstandingArm(c.OwnerSeat) is { } arm && arm.Source.SkillInstanceId == c.SkillInstanceId && arm.GameplayHash == c.GameplayHash &&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is { } parent && IsActualAfterTurnEndedParent(parent) &&
        AfterTurnEndedCandidate(parent) == c && parent.OwnerSeat == c.OwnerSeat && parent.TurnNumber == arm.Turn.Number;
    private string? WuHfEntityMark(int cardId) => HasWuHuangFuSongCapability && WuHfTurnOpen(WuHfTurn()) &&
        ProgramEventHistory<WuHuangFuSongLianjieEntityEvent>().Any(e => e.CardId == cardId && e.Turn == WuHfTurn()) ? "连捷" : null;

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 10)]
    private bool DistanceCardUseExemption10HasWuHfOrDixianIssuedUse(ProgramCardDistanceQuery q) =>
        q.CardUseFrameId is { } dixianUse && HasWuHfOrDixianIssuedUse(dixianUse, q.Actor.Seat);


    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 100)]
    private bool DistancePhysicalSlash100HasWuHfOrWuyouMaterialPrivilege(ProgramSlashDistanceQuery q) =>
        HasWuHfOrWuyouMaterialPrivilege(q.Actor, q.Kind, q.PhysicalCardIds ?? (q.PhysicalCard!.Id > 0 ? [q.PhysicalCard!.Id] : []), q.ExistingUseFrameId);


    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 10)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 10)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 20)]
    private bool DistanceVirtualSlash10HasWuHfOrWuyouMaterialPrivilege(ProgramSlashDistanceQuery q) =>
        HasWuHfOrWuyouMaterialPrivilege(q.Actor, q.Kind, q.PhysicalCardIds);






    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 20)]
    private bool DistanceBorrowedPhysicalSlash20HasWuHfOrWuyouMaterialPrivilege(ProgramSlashDistanceQuery q) =>
        HasWuHfOrWuyouMaterialPrivilege(q.Actor, q.Kind, [q.PhysicalCard!.Id]);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 30)]
    private bool WuHfWuyouSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        HasWuHfOrWuyouMaterialPrivilege(query.Actor, query.Kind, query.PhysicalCardIds ?? (query.PhysicalCard is { Id: > 0 } ? [query.PhysicalCard.Id] : []));
}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 70)]
    private bool WuHfMaterialSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) =>
        HasWuHfMaterialPrivilege(query.Actor, query.PhysicalCardIds ?? (query.PhysicalCard is { Id: > 0 } ? [query.PhysicalCard.Id] : []));
}
