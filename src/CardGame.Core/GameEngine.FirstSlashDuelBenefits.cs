using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string FirstSlashDuelDrawReason = "skill-program.first-slash-duel.draw";
    private bool HasFirstSlashDuelBenefitCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit);
    private static bool IsFirstSlashDuelKind(CardKind kind) => IsSlashCard(kind) || kind == CardKind.Duel;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 260)]
    private void ObserveFirstSlashDuelDeclaration(IGameEvent payload)
    {
        if (!HasFirstSlashDuelBenefitCapability || payload is not CardUseDeclaredEvent declared ||
            !HasOngoingActualUseTurn || declared.SourceSeat != _currentSeat || !IsFirstSlashDuelKind(declared.CardKind)) return;
        var use = LifecycleCardUse(declared.ResolutionId);
        if (use is null || use.SourceSeat != declared.SourceSeat || use.CardKind != declared.CardKind || use.CardId != declared.CardId ||
            use.Action is { Type: not CardActionType.Use } ||
            use.Action is { } action && (action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind))
            throw new InvalidOperationException("The first Slash/Duel ledger lost its actual declaration.");
        if (use.Action is null && !IsActualTurnLegacyVirtualUse(use)) return;
        var old = CompleteProgramEventHistory().OfType<FirstSlashDuelUseRecordedEvent>().Where(e =>
            e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat && e.ActorSeat == use.SourceSeat).ToArray();
        if (old.Any(e => e.CardUseFrameId == use.Id)) return;
        AdvanceEventRulesAndQueueFact(new FirstSlashDuelUseRecordedEvent(_turnNumber, _currentSeat,
            use.SourceSeat, use.Id, use.Action?.ActionId, use.CardKind, checked(old.Length + 1)));
    }
    private bool IsFirstSlashDuelAction(int owner, CardActionContext action, CardUseFrame use) =>
        HasOngoingActualUseTurn && owner == _currentSeat && action.Type == CardActionType.Use && action.ActorSeat == owner &&
        IsFirstSlashDuelKind(action.EffectiveKind) && use.SourceSeat == owner && use.CardKind == action.EffectiveKind &&
        use.Action?.ActionId == action.ActionId && use.TargetSeats is [var target] && IsValidPlayerSeat(target) && _players[target].IsAlive &&
        CompleteProgramEventHistory().OfType<FirstSlashDuelUseRecordedEvent>().Any(e =>
            e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat && e.ActorSeat == owner && e.CardUseFrameId == use.Id &&
            e.ActionId == action.ActionId && FirstSlashDuelDeclaredKindMatches(use, e.EffectiveKind) && e.Ordinal == 1);
    private bool FirstSlashDuelDeclaredKindMatches(CardUseFrame use, CardKind declaredKind)
    {
        if (declaredKind == use.CardKind) return true;
        if (use.CurrentSlashFirePolicy is not { Converted: true } policy || policy.OriginalKind != declaredKind ||
            !IsSlashCard(declaredKind) || use.CardKind != CardKind.FireSlash) return false;
        // Declaration order remains frozen. An exact existing committed kind
        // change belongs to the same use, rather than a second first-use slot.
        AssertCurrentSlashFirePolicy(use); return true;
    }
    private bool CanCollectFirstSlashDuelCandidate(int owner, SkillProgramTrigger trigger, CardActionContext action)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit)) return true;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Action?.ActionId == action.ActionId);
        return use is not null && IsFirstSlashDuelAction(owner, action, use);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 300)]
    private bool CanOfferFirstSlashDuelBenefit(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit)) return true;
        if (_resolutionStack.OfType<FirstSlashDuelLegacyWindowFrame>().Any(w => w.Id == context.ParentFrameId))
            return CanRunFirstLegacyBenefit(candidate, context);
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId);
        var use = window is null ? null : LifecycleCardUse(window.ParentFrameId);
        return context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && context.CardUse?.CardActionId == window?.Action.ActionId &&
            window is not null && use is not null && IsFirstSlashDuelAction(candidate.OwnerSeat, window.Action, use) &&
            !CompleteProgramEventHistory().OfType<FirstSlashDuelBenefitIssuedEvent>().Any(e => e.Receipt.CardUseFrameId == use.Id &&
                e.Receipt.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId));
    }
    private CardConversionSource FirstSlashDuelSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome BeginFirstSlashDuelBenefit(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.FirstSlashDuelBenefit is not null || f.WindowContext is not { } c ||
            !CanOfferFirstSlashDuelBenefit(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), GetProgramTrigger(f), c))
            throw new InvalidOperationException("The first Slash/Duel benefit requires its exact unpaid own-turn finalized use.");
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId);
        var legacy = _resolutionStack.OfType<FirstSlashDuelLegacyWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId);
        var use = LifecycleCardUse(window?.ParentFrameId ?? legacy!.ParentFrameId)!; var target = use.TargetSeats.Single();
        // The count is trusted pending state. Neither choice labels nor public
        // facts expose the target's suits before the owner selects an effect.
        var x = GetHand(_players[target]).Count(card => EffectiveSuit(_players[target], card) == Suit.Diamond);
        ReplaceRuntimeTop(f = f with { FirstSlashDuelBenefit = new(f.InstructionIndex, c.ParentFrameId, use.Id, use.Action?.ActionId,
            _turnNumber, _currentSeat, target, x, FirstSlashDuelSource(f), f.GameplayHash, LegacyProducerFrameId: legacy?.ProducerFrameId), Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择一项。", [], [target], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = FirstSlashDuelChoices(f),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> FirstSlashDuelChoices(ProgramSkillFrame f) => Array.AsReadOnly(new[] {
        FirstSlashDuelChoice(f, "draw", "摸X张牌"), FirstSlashDuelChoice(f, "damage", "令此牌伤害+X") });
    private static PromptChoice FirstSlashDuelChoice(ProgramSkillFrame f, string branch, string label) => new(
        new($"first-slash-duel.{f.Id}.{branch}"), label, [], [], new Dictionary<string, string> {
            ["program-action"] = "first-slash-duel-benefit", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    [ProgramAnswer("first-slash-duel-benefit")]
    private void ResolveFirstSlashDuelChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The first-use choice lost its owning frame.");
        RequireFirstSlashDuelBenefit(f); var r = f.FirstSlashDuelBenefit!;
        if (r.SelectedBranch is not null || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], FirstSlashDuelChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("The first-use choice is stale or already settled.");
        ClearPendingDecision(); var branch = selected.Parameters["branch"];
        if (branch == "damage")
        {
            var use = LifecycleCardUse(r.CardUseFrameId)!;
            UpdateLifecycleCardUse(use.Id, u => u with { FirstSlashDuelDamageBonus = checked((u.FirstSlashDuelDamageBonus ?? 0) + r.FrozenDiamondCount) });
            r = r with { SelectedBranch = branch };
            AdvanceEventRulesAndQueueFact(new FirstSlashDuelBenefitIssuedEvent(f.Id, r));
            FinishFirstSlashDuelBenefit(f); return;
        }
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { FirstSlashDuelBenefit = r with { SelectedBranch = branch, MovementBefore = before, MovementAfter = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        var drawn = DrawCards(_players[f.OwnerSeat], r.FrozenDiamondCount, true, new(FirstSlashDuelDrawReason));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { FirstSlashDuelBenefit = f.FirstSlashDuelBenefit! with { DrawCount = drawn.Count, MovementAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new FirstSlashDuelBenefitIssuedEvent(f.Id, f.FirstSlashDuelBenefit!));
        if (!DrainFirstSlashDuelChildren(f)) ReturnFirstSlashDuelMovement(f);
    }
    private bool DrainFirstSlashDuelChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void FinishFirstSlashDuelBenefit(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { FirstSlashDuelBenefit = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21100)]
    private bool ResumeFirstSlashDuelBenefit(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FirstSlashDuelBenefit: { SelectedBranch: "draw" } } f || f.Id != id) return false;
        RequireFirstSlashDuelBenefit(f);
        if (!DrainFirstSlashDuelChildren(f)) FinishFirstSlashDuelBenefit(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 21100)]
    private bool ReturnFirstSlashDuelMovement(ProgramSkillFrame f)
    {
        if (f.FirstSlashDuelBenefit?.SelectedBranch != "draw") return false;
        RequireFirstSlashDuelBenefit(f);
        if (!DrainFirstSlashDuelChildren(f)) FinishFirstSlashDuelBenefit(f); return true;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 400)]
    private bool IsFirstSlashDuelMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        f.FirstSlashDuelBenefit is { SelectedBranch: "draw" } r && e?.Op == SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit &&
        r.InstructionIndex == f.InstructionIndex && pending.SubjectSeat == f.OwnerSeat && pending.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 390)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 390)]
    private bool CanContinueFirstSlashDuelBenefit(ProgramSkillFrame f) => f.FirstSlashDuelBenefit?.SelectedBranch == "draw" && ValidFirstSlashDuelBenefit(f);
    [ProgramAiChoice(SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit)]
    private PromptChoice SelectAiFirstSlashDuelBenefit(PendingDecision decision, ProgramSkillFrame f) =>
        decision.Choices.Single(c => c.Parameters["branch"] == (GetHand(_players[f.OwnerSeat]).Count < 3 ? "draw" : "damage"));
    private int FirstSlashDuelDamage(IDamageAttempt attack)
    {
        if (attack.IsChainPropagation || !IsFirstSlashDuelKind(attack.EffectiveCardKind ?? CardKind.Dodge) ||
            LifecycleCardUse(attack.ResolutionId) is not { FirstSlashDuelDamageBonus: { } bonus } use) return 0;
        var facts = CompleteProgramEventHistory().OfType<FirstSlashDuelBenefitIssuedEvent>().Where(e => e.Receipt.CardUseFrameId == use.Id &&
            e.Receipt.ActionId == use.Action?.ActionId && e.Receipt.SelectedBranch == "damage").ToArray();
        if (bonus < 0 || facts.Sum(e => e.Receipt.FrozenDiamondCount) != bonus)
            throw new InvalidOperationException("A same-use first Slash/Duel damage benefit lost its actual issued facts.");
        return bonus;
    }
    [ProgramCapability]
    private sealed class FirstSlashDuelBenefitCapability(GameEngine engine) : IFirstSlashDuelBenefitHost
    { public SkillProgramStepOutcome ChooseFirstSlashDuelDiamondBenefit(ProgramSkillFrame frame) => engine.BeginFirstSlashDuelBenefit(frame); }
}
