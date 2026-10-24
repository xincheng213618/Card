namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MobileShiWanglieSelectedEvent? ShiCdWanglieGrant(int seat) => _winner == Winner.None &&
        seat == _currentSeat && _phase == TurnPhase.Play ? ProgramEventHistory<MobileShiWanglieSelectedEvent>().LastOrDefault(e =>
            e.Source.OwnerSeat == seat && e.ActualTurn == _turnNumber && e.PhaseId == _cardUseDebitPhaseInstanceId) : null;
    private bool ShiCdWanglieMaterials(int seat, IEnumerable<int>? ids) => ShiCdWanglieGrant(seat) is { } grant && ids?.Contains(grant.CardId) == true;
    private bool ShiCdWanglieBan(int seat, long? existingUse = null) => ShiCdWanglieGrant(seat) is { } grant &&
        ProgramEventHistory<MobileShiWanglieCompletedEvent>().Any(e => e.SelectionId == grant.FrameId &&
            (existingUse is null || existingUse.Value > e.UseId));
    private bool IsMobileShiWanglieUnrespondable(long id) => LifecycleCardUse(id)?.Action is { } action &&
        ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.UseId == id && e.ActionId == action.ActionId && e.OwnerSeat == action.ActorSeat);
    private CardConversionSource? MobileShiWanglieCounterspellSource(CharacterState owner, IReadOnlyList<int> ids) =>
        ShiCdWanglieMaterials(owner.Seat, ids) ? ShiCdWanglieGrant(owner.Seat)?.Source : null;
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 47800)]
    private string? ShiCdWanglieCardLabel(int cardId) => ShiCdWanglieGrant(_currentSeat) is { } grant && grant.CardId == cardId &&
        _cardZones.GetLocation(cardId) == CardLocation.Hand(grant.Source.OwnerSeat) ? "往烈" : null;

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 47800)]
    private bool ShiCdWanglieCardDistance(ProgramCardDistanceQuery q) => q.CardUseFrameId is { } id && IsMobileShiWanglieUnrespondable(id) ||
        ShiCdWanglieMaterials(q.Actor.Seat, q.PhysicalCardIds);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 47800)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 47800)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 47800)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 47800)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 47800)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 47800)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 47800)]
    private bool ShiCdWanglieSlashDistance(ProgramSlashDistanceQuery q) => q.ExistingUseFrameId is { } id && IsMobileShiWanglieUnrespondable(id) ||
        ShiCdWanglieMaterials(q.Actor.Seat, q.PhysicalCardIds ?? (q.PhysicalCard is { } c ? [c.Id] : null));

    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 47800)]
    [ProgramTargetRule(ProgramTargetRulePoint.DeclaredGlobalProhibition, 47800)]
    private bool ShiCdWanglieOtherTargets(ProgramDirectedTargetQuery q) => q.TargetSeat != q.ActorSeat && ShiCdWanglieBan(q.ActorSeat, q.ExistingUseFrameId);
    [ProgramCardLegality(ProgramCardLegalityStage.LegalActionProjection, 47800)]
    private LegalAction? ShiCdWanglieProject(ProgramLegalActionQuery q)
    {
        if (!ShiCdWanglieBan(q.Actor.Seat) || q.Action.Kind is LegalActionKind.EndPlay or LegalActionKind.Recast or
            LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral) return q.Action;
        if (q.Action.CardId is null && q.Action.Kind == LegalActionKind.UseEquipmentEffect) return q.Action;
        return GetSelfProhibitionPolicyTargets(q.Actor, q.Action).Any(s => s != q.Actor.Seat) ? null : q.Action;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 47800)]
    private void CaptureShiCdUse(IGameEvent payload)
    {
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } a } use &&
            ShiCdWanglieMaterials(a.ActorSeat, a.PhysicalCards.Select(c => c.CardId)) && ShiCdWanglieGrant(a.ActorSeat) is { } grant &&
            !ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.ActionId == a.ActionId))
            AdvanceEventRulesAndQueueFact(new MobileShiWanglieUsedEvent(grant.FrameId, use.Id, a.ActionId, a.ActorSeat, _turnNumber, _cardUseDebitPhaseInstanceId));
        else if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted)
        {
            var response = accepted.Action;
            if (response.ActorSeat == response.ProviderSeat && response.RequesterSeat is null && (response.EffectiveKind == CardKind.Nullification || IsProgramResponseCardUse(_players[response.ActorSeat], response.EffectiveKind)) &&
                ShiCdWanglieMaterials(response.ActorSeat, response.PhysicalCards.Select(c => c.CardId)) && ShiCdWanglieGrant(response.ActorSeat) is { } responseGrant &&
                !ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.ActionId == response.ActionId))
                AdvanceEventRulesAndQueueFact(new MobileShiWanglieUsedEvent(responseGrant.FrameId, ActiveNullificationWindow?.Id ?? ActiveCardAttack?.ResolutionId ?? 0,
                    response.ActionId, response.ActorSeat, _turnNumber, _cardUseDebitPhaseInstanceId));
        }
        else if (payload is CardUseFinishedEvent finished && LifecycleCardUse(finished.ResolutionId)?.Action is { } completed)
            CompleteShiCdWanglie(completed.ActionId);
    }
    private void CompleteShiCdWanglie(long actionId)
    {
        if (ProgramEventHistory<MobileShiWanglieUsedEvent>().SingleOrDefault(e => e.ActionId == actionId) is not { } use ||
            use.ActualTurn != _turnNumber || use.PhaseId != _cardUseDebitPhaseInstanceId ||
            ProgramEventHistory<MobileShiWanglieCompletedEvent>().Any(e => e.ActionId == actionId)) return;
        AdvanceEventRulesAndQueueFact(new MobileShiWanglieCompletedEvent(use.SelectionId, use.UseId, use.ActionId, use.OwnerSeat, use.ActualTurn, use.PhaseId));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ObservesResponseUseCompletion, 47800)]
    private bool ObservesShiCdResponseUse(CardActionContext action) => ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.ActionId == action.ActionId);
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 47800)]
    private void CollectShiCdResponseUse(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCompleted || action.Type != CardActionType.Response ||
            ProgramEventHistory<MobileShiWanglieUsedEvent>().SingleOrDefault(e => e.ActionId == action.ActionId) is not { } use ||
            ProgramEventHistory<MobileShiWanglieSelectedEvent>().SingleOrDefault(e => e.FrameId == use.SelectionId) is not { } grant) return;
        result.Add(new(action.ActorSeat, action.ActorSeat, ShiCdWanglie, "wanglie-response-complete", grant.GameplayHash, grant.Source.SkillInstanceId));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 47801)]
    private bool IsIssuedShiCdResponse(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => c.SkillId == ShiCdWanglie &&
        c.BindingId == "wanglie-response-complete" && context is { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } card } &&
        ProgramEventHistory<MobileShiWanglieUsedEvent>().Any(e => e.ActionId == card.CardActionId && e.OwnerSeat == c.OwnerSeat &&
            ProgramEventHistory<MobileShiWanglieSelectedEvent>().Any(g => g.FrameId == e.SelectionId && g.Source.SkillInstanceId == c.SkillInstanceId && g.GameplayHash == c.GameplayHash));
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiChenDaoPublicStates(CharacterState owner, string skillId)
    {
        if (skillId is not (ShiCdWanglie or ShiCdHongyi)) return [];
        var states = new List<ProgramPublicRuleStateSnapshot>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId))
        {
            if (skillId == ShiCdHongyi)
            {
                states.Add(new(instance.SkillInstanceId, "hongyi-marks", ProgramPublicRuleStateKind.GameActivationProgress, ShiCdMarks(owner.Seat)));
                if (ShiCdPendingEnding(owner.Seat) is { } pending)
                {
                    states.Add(new(instance.SkillInstanceId, "hongyi-frozen-x", ProgramPublicRuleStateKind.GameActivationProgress, pending.FrozenCount));
                    states.Add(new(instance.SkillInstanceId, "hongyi-draw-first", ProgramPublicRuleStateKind.GameActivationProgress, pending.DrawFirst ? 1 : 0));
                }
            }
            else
                states.Add(new(instance.SkillInstanceId, "wanglie-phase", ProgramPublicRuleStateKind.GameActivationProgress,
                    ShiCdWanglieBan(owner.Seat) ? 2 : ShiCdWanglieGrant(owner.Seat) is not null ? 1 : 0));
        }
        return states.ToArray();
    }
}
