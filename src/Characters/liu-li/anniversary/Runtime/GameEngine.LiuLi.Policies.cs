namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool LiuLiChosenProof(AnniversaryLiuLiNameChosenEvent e) => e.Source.SkillId == AnniversaryLiuLiComposition.Dehua && e.Source.BindingId == "round-damage-name" &&
        _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash && ProgramBasicCardName(e.Name) == e.Name && IsDamageCardKind(e.Name) &&
        ProgramEventHistory<AnniversaryLiuLiStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && s.Hash == e.Hash) == 1 &&
        ProgramEventHistory<AnniversaryLiuLiNativeIssuedEvent>().Count(s => s.FrameId == e.FrameId && s.UseId == e.UseId && s.ActionId == e.ActionId && ProgramBasicCardName(s.Kind) == e.Name) == 1 &&
        ProgramEventHistory<CardUseFinishedEvent>().Count(s => s.ResolutionId == e.UseId && s.CardId == 0 && ProgramBasicCardName(s.CardKind) == e.Name) == 1;
    private bool LiuLiHandNameForbidden(int owner, CardKind kind, IEnumerable<int> ids) => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua) &&
        ids.Any(id => id > 0 && _cardZones.GetLocation(id) == CardLocation.Hand(owner)) && LiuLiNameChosen(owner, kind);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 43300)] private bool LiuLiProjectedUseForbidden(ProgramLegalActionQuery q) =>
        q.EffectiveKind is { } kind && q.Action.CardId is { } id && LiuLiHandNameForbidden(q.Actor.Seat, kind, [id]);
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 43300)] private bool LiuLiMaterialsForbidden(ProgramCardMaterialQuery q) =>
        !q.IsResponse && LiuLiHandNameForbidden(q.ActorSeat, q.Kind, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 43300)] private void ValidateLiuLiSubmittedUse(ProgramSubmittedUseQuery q)
    { if (LiuLiHandNameForbidden(q.ActorSeat, q.Kind, q.CardIds)) throw new InvalidOperationException("Dehua forbids using Hand materials as an already chosen damage-card name."); }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 43300)] private void ValidateLiuLiNativeHandUse(IGameEvent payload)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua) || payload is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId)?.Action is not { Type: CardActionType.Use } a) return;
        if (a.PhysicalCards.Any(c => c.From == CardLocation.Hand(a.ActorSeat)) && LiuLiNameChosen(a.ActorSeat, a.EffectiveKind))
            throw new InvalidOperationException("Dehua rejects an actual native Use of an already chosen Hand name; pure Played remains allowed.");
    }
    private IEnumerable<RuleQueryContribution> AnniversaryLiuLiHandLimitContributions(CharacterState p)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua) || !p.IsAlive || !HasRuntimeSkill(p, AnniversaryLiuLiComposition.Dehua)) yield break;
        var count = ProgramEventHistory<AnniversaryLiuLiNameChosenEvent>().Where(e => e.Source.OwnerSeat == p.Seat && LiuLiChosenProof(e)).Select(e => e.Name).Distinct().Count();
        if (count > 0) yield return new FiniteRuleQueryContribution($"issued:{p.Seat}:anniversary:dehua:hand-limit", SkillRuleOperation.Add, count);
    }
    private bool IsAnniversaryLiuLiHandLimitExempt(int seat, Card card) => IsDamageCardKind(card.Kind) &&
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua) &&
        ProgramEventHistory<AnniversaryLiuLiDehuaCompletedEvent>().Any(e => e.OwnerSeat == seat && e.Names > 0 &&
            ProgramEventHistory<AnniversaryLiuLiNameChosenEvent>().Count(n => n.Source.OwnerSeat == seat && LiuLiChosenProof(n)) == e.Names &&
            ProgramEventHistory<AnniversaryLiuLiPoolNameEvent>().Where(n => n.OwnerSeat == seat).All(n => LiuLiNameChosen(seat, n.Name)));
    private bool LiuLiRangeActive(AnniversaryLiuLiRangeIssuedEvent e) => IsValidPlayerSeat(e.Source.OwnerSeat) && IsValidPlayerSeat(e.TargetSeat) &&
        e.Source.SkillId == AnniversaryLiuLiComposition.Fuli && e.Source.BindingId == "reveal-discard-draw" && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash &&
        ProgramEventHistory<TurnStartedEvent>().Count(t => t.ActorSeat == e.Source.OwnerSeat) == e.OwnerTurnCount &&
        ProgramEventHistory<AnniversaryLiuLiStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && s.Hash == e.Hash) == 1 &&
        ProgramEventHistory<AnniversaryLiuLiDiscardPaidEvent>().Count(p => p.FrameId == e.FrameId && p.DamageDiscarded == e.SetZero && p.Count > 0) == 1;
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeProjection, 43300)] private RuleQueryEvaluation LiuLiRange(CharacterState p, RuleQueryEvaluation ordinary)
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli)) return ordinary;
        var effects = ProgramEventHistory<AnniversaryLiuLiRangeIssuedEvent>().Where(e => e.TargetSeat == p.Seat && LiuLiRangeActive(e)).ToArray();
        if (effects.Length == 0 || ordinary.IsUnlimited && !effects.Any(e => e.SetZero)) return ordinary;
        var value = effects.Any(e => e.SetZero) ? 0 : Math.Max(0, ((FiniteRuleQueryValue)ordinary.Value).Value - effects.Length);
        var additions = effects.Select(e => (RuleQueryContribution)new FiniteRuleQueryContribution($"issued:{e.FrameId}:anniversary:fuli:range", e.SetZero ? SkillRuleOperation.Set : SkillRuleOperation.Add, e.SetZero ? 0 : -1)).ToArray();
        return new(new FiniteRuleQueryValue(value, Array.AsReadOnly(ordinary.Value.Contributions.Concat(additions).ToArray())), ordinary.BaseTerms, ordinary.Stages);
    }
}
