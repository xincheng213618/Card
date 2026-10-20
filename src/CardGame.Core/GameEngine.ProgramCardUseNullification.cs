namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class WholeCardUseNullificationCapability(GameEngine engine) : IWholeCardUseNullificationHost
    {
        public void NullifyCurrentCardUse(ProgramSkillFrame frame) => engine.NullifyCurrentProgramCardUse(frame);
    }

    private void NullifyCurrentProgramCardUse(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not
            { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects, CardUse: { } context } windowContext ||
            windowContext.TargetSeat != active.OwnerSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is not { } window ||
            window.Id != windowContext.ParentFrameId || window.ParentFrameId != context.ParentCardUseFrameId ||
            window.Action.Type != CardActionType.Use || window.Action.ActionId != context.CardActionId ||
            LifecycleCardUse(window.ParentFrameId) is not { Action: { } action } use ||
            action.ActionId != context.CardActionId || action.ActorSeat != context.ActorSeat ||
            action.EffectiveKind != context.EffectiveKind || !use.TargetSeats.Contains(active.OwnerSeat) ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            window.Candidates[window.CandidateIndex] is not { } candidate ||
            candidate.OwnerSeat != active.OwnerSeat || candidate.SkillId != active.SkillId ||
            candidate.SkillInstanceId != active.SkillInstanceId || candidate.TriggerId != GetProgramBindingId(active) ||
            candidate.GameplayHash != active.GameplayHash)
            throw new InvalidOperationException("Whole-use nullification lost its exact target-owner use window.");

        // Ineffectiveness is a state, not a return or a replacement of the
        // remaining candidates. A second owner may still commit a conversion.
        if (use.ProgramUseNullification is not null)
        {
            AssertProgramUseNullification(use);
            return;
        }
        var proof = new ProgramCardUseNullifiedEvent(active.Id, use.Id, window.Id, action.ActionId,
            active.SkillId, active.SkillInstanceId, GetProgramBindingId(active), active.GameplayHash,
            active.InstructionIndex, active.OwnerSeat, action.ActorSeat, action.EffectiveKind);
        UpdateLifecycleCardUse(use.Id, current => current with { ProgramUseNullification = proof });
        AdvanceEventRulesAndQueueFact(proof);
        AddLog("SkillTriggered", $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，" +
            $"令本次使用的【{CardCatalog.Get(action.EffectiveKind).DisplayName}】无效。", active.OwnerSeat, action.ActorSeat);
    }

    private bool IsProgramUseIneffective(CardUseFrame use)
    {
        if (use.ProgramUseNullification is null) return false;
        AssertProgramUseNullification(use);
        return true;
    }

    private void AssertProgramUseNullification(CardUseFrame use)
    {
        if (use.ProgramUseNullification is not { } proof) return;
        var history = CompleteProgramEventHistory().ToArray();
        if (use.Action is not { Type: CardActionType.Use } action || proof.ParentUseFrameId != use.Id ||
            proof.ActionId != action.ActionId || proof.ActorSeat != action.ActorSeat ||
            proof.EffectiveKind != action.EffectiveKind || !use.TargetSeats.Contains(proof.OwnerSeat) ||
            proof.WindowFrameId <= use.Id || proof.ProgramFrameId <= proof.WindowFrameId ||
            _contentRegistry.Skills.GetValueOrDefault(proof.SkillId)?.Program is not { } program ||
            program.GameplayHash != proof.GameplayHash ||
            ProgramInstructionResolver.Default.FindTrigger(program, proof.BindingId) is not
                { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                  OwnerRelation: SkillProgramCardActionOwnerRelation.Target } trigger ||
            proof.InstructionIndex < 1 || proof.InstructionIndex > trigger.Effects.Count ||
            trigger.Effects[proof.InstructionIndex - 1].Op != SkillProgramEffectOp.NullifyCurrentCardUse ||
            history.OfType<ProgramCardUseNullifiedEvent>().Count(fact => fact == proof) != 1 ||
            history.OfType<ProgramBindingStartedEvent>().Count(fact =>
                fact.FrameId == proof.ProgramFrameId && fact.SkillId == proof.SkillId &&
                fact.BindingId == proof.BindingId && fact.SkillInstanceId == proof.SkillInstanceId &&
                fact.OwnerSeat == proof.OwnerSeat && fact.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects) != 1)
            throw new InvalidOperationException("A whole-use ineffective state lost its exact issued program proof.");
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == proof.ProgramFrameId) is { } active &&
            (active.WindowContext?.ParentFrameId != proof.WindowFrameId ||
             active.WindowContext.CardUse?.ParentCardUseFrameId != use.Id ||
             active.SkillId != proof.SkillId || active.SkillInstanceId != proof.SkillInstanceId ||
             active.GameplayHash != proof.GameplayHash || GetProgramBindingId(active) != proof.BindingId))
            throw new InvalidOperationException("A whole-use ineffective state changed its active issuing program.");
        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(frame => frame.Id == proof.WindowFrameId) is { } window)
        {
            if (window.ParentFrameId != use.Id || window.Action.ActionId != action.ActionId ||
                GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseBeforeTargetEffects ||
                window.Candidates.Count(candidate => candidate.OwnerSeat == proof.OwnerSeat &&
                    candidate.SkillId == proof.SkillId && candidate.SkillInstanceId == proof.SkillInstanceId &&
                    candidate.TriggerId == proof.BindingId && candidate.GameplayHash == proof.GameplayHash &&
                    candidate.FrozenContext is { TargetSeat: { } target, CardUse: { } cardUse } context &&
                    target == proof.OwnerSeat && context.ParentFrameId == window.Id &&
                    cardUse.ParentCardUseFrameId == use.Id && cardUse.CardActionId == action.ActionId) != 1)
                throw new InvalidOperationException("A whole-use ineffective state changed its frozen target candidate.");
        }
        else if (history.OfType<ProgramCardTriggerResolvedEvent>().Count(fact =>
            fact.FrameId == proof.WindowFrameId && fact.SkillId == proof.SkillId && fact.TriggerId == proof.BindingId &&
            fact.OwnerSeat == proof.OwnerSeat && fact.Activated) != 1)
            throw new InvalidOperationException("A whole-use ineffective state lost its completed target window.");
    }

    private bool HasBeforeTargetCardProgram(CardKind kind)
    {
        var category = CardCatalog.Get(kind).CategoryName switch
        {
            "基本牌" => SkillProgramCardCategory.Basic,
            "装备牌" => SkillProgramCardCategory.Equipment,
            _ => SkillProgramCardCategory.Trick
        };
        return _contentRegistry.ProgramDependencies.HasBeforeTargetCardTrigger(kind, category);
    }

    private bool TryBeginSimpleBeforeTargetPrograms(long useId, ProgramSimpleCardContinuation continuation)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("A simple target window lost its use.");
        if (continuation.Effect is not (SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol) ||
            use.BeforeSimpleTargetEffectsStarted || !HasBeforeTargetCardProgram(use.CardKind)) return false;
        var action = use.Action ?? throw new InvalidOperationException("A simple target window lost its accepted action.");
        if (action.Type != CardActionType.Use || !action.TargetSeats.SequenceEqual(use.TargetSeats))
            throw new InvalidOperationException("A simple target window changed its actual use targets.");
        UpdateLifecycleCardUse(useId, current => current with { BeforeSimpleTargetEffectsStarted = true });
        return TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
            use.TargetSeats, ProgramCardContinuation.BeforeSimpleTargetEffects, simpleContinuation: continuation);
    }

    private bool IsExactSimpleBeforeTargetWindow(ProgramCardTriggerWindowFrame window) =>
        window.Continuation == ProgramCardContinuation.BeforeSimpleTargetEffects &&
        LifecycleCardUse(window.ParentFrameId) is { BeforeSimpleTargetEffectsStarted: true, Action: { } action } use &&
        action.Type == CardActionType.Use && action.ActionId == window.Action.ActionId &&
        action.TargetSeats.SequenceEqual(use.TargetSeats) &&
        window.SimpleContinuation is { } continuation && continuation.CardId == use.CardId &&
        continuation.Effect is SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol &&
        HasBeforeTargetCardProgram(use.CardKind);

    private double GetWholeCardUseNullificationAiScore(int ownerSeat, ProgramCardTriggerWindowFrame window)
    {
        var use = LifecycleCardUse(window.ParentFrameId) ??
            throw new InvalidOperationException("A use-nullification estimate lost its actual use.");
        if (IsProgramUseIneffective(use)) return 0d;
        var kind = window.Action.EffectiveKind;
        // This is public utility of the card's effects, not an assumption about
        // the actor's private skill or the value of changing conversion state.
        var equivalent = kind switch
        {
            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.BorrowedSword => CardKind.Duel,
            CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning => CardKind.Dismantlement,
            CardKind.Peach => CardKind.PeachGarden,
            CardKind.Alcohol or CardKind.DrawTwo => CardKind.FiveGrains,
            _ => kind
        };
        var view = CreateSnapshot(ownerSeat);
        // Humans have no controlling AI in the engine. Their public hint uses
        // an ephemeral scorer only; it neither chooses nor consumes randomness.
        var brain = _aiBrains.GetValueOrDefault(ownerSeat) ?? new SimpleAiBrain(ownerSeat,
            unchecked(_options.Seed * 397) ^ (ownerSeat + 1), _options.AiPolicyVersion);
        var targets = window.Action.TargetSeats.Count > 0 ? window.Action.TargetSeats : [window.Action.ActorSeat];
        return targets.Distinct().Where(seat => _players[seat].IsAlive)
            .Sum(seat => brain.ScoreCardEffectIntervention(view, equivalent, seat)) *
            (kind == CardKind.DrawTwo ? 2d : 1d);
    }
}
