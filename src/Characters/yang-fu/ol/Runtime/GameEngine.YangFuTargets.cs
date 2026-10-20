namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 130)]
    private bool BingzhongNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBingzhongExtraTargets);


    private bool TryGetBingzhongUse(int owner, ProgramSkillWindowContext context, out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        if (TryGetDesignatedExtraTargetUse(owner, context, out use, out window)) return true;
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            !IsValidPlayerSeat(owner) || !_players[owner].IsAlive || _winner != Winner.None ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { Continuation: ProgramCardContinuation.Slash, CompletedResponseReturn: null } w ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } a } u || u.SourceSeat != owner || a.ActorSeat != owner ||
            !IsSlashCard(u.CardKind) || a.ActionId != identity.CardActionId || w.ParentFrameId != u.Id || w.Action.ActionId != a.ActionId || w.Action.ActorSeat != owner ||
            !w.Action.TargetSeats.SequenceEqual(u.TargetSeats) || !a.TargetSeats.SequenceEqual(u.TargetSeats) || u.DyingResponse is not null ||
            a.ResponderSeat is not null || a.RequesterSeat is not null || a.OpponentSeat is not null || a.PhysicalCards.Count == 0 ||
            !a.PhysicalCards.Select(c => c.CardId).SequenceEqual(u.PhysicalCardIds ?? [u.CardId]) ||
            !a.PhysicalCards.Any(c => IsKenshangRemovedMaterial(u.Id, c.CardId) || IsWuJingHejiRemovedMaterial(u.Id, c.CardId) || IsSelectedForeignCardSlashRemovedMaterial(u.Id, c.CardId) || IsZhangYanSujiRemovedMaterial(u.Id, c.CardId)) ||
            a.PhysicalCards.Any(c => _cardZones.GetLocation(c.CardId) != CardLocation.Processing && !IsKenshangRemovedMaterial(u.Id, c.CardId) && !IsWuJingHejiRemovedMaterial(u.Id, c.CardId) &&
                !IsSelectedForeignCardSlashRemovedMaterial(u.Id, c.CardId) && !IsZhangYanSujiRemovedMaterial(u.Id, c.CardId) && !IsCurrentUsePhysicalCardClaim(u.Id, c.CardId) && !IsExchangedCardClaim(a.ActionId, c.CardId))) return false;
        var at = _resolutionStack.FindIndex(f => f.Id == w.Id); if (at < 1 || _resolutionStack[at - 1].Id != u.Id) return false;
        use = u; window = w; return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27310)]
    private bool QualifyBingzhongTargets(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveBingzhongExtraTargets)) return true;
        return candidate.SkillId == "ol:bingzhong" && TryGetBingzhongUse(candidate.OwnerSeat, context, out var use, out var window) &&
            DesignatedExtraTargetCandidateMatches(candidate, context, window) && use.BingzhongTail is null &&
            CapturedWholeActualTurnActorUseOrdinal(use.Action!) is { } ordinal && ordinal <= BingzhongCurrentX();
    }
    private CardUseFrame RequireBingzhongUse(ProgramSkillFrame f, out ProgramCardTriggerWindowFrame window)
    {
        if (f.SkillId != "ol:bingzhong" || f.TriggerId != "first-dynamic-x-targets" || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !TryGetBingzhongUse(f.OwnerSeat, context, out var use, out window) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), context, window))
            throw new InvalidOperationException("Bingzhong lost its exact finalized actual actor use.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id)
            throw new InvalidOperationException("Bingzhong requires its immediate native target window.");
        return use;
    }
    private IReadOnlyList<IReadOnlyList<int>> BingzhongLegalTargets(CardUseFrame use)
    {
        var action = use.Action!; var actor = _players[action.ActorSeat]; var physical = action.PhysicalCards.Select(c => c.CardId).ToArray();
        var result = new List<IReadOnlyList<int>>();
        foreach (var target in _players.OrderBy(p => p.Seat))
        {
            if (use.CardKind == CardKind.UnexpectedAssault)
            {
                if (!target.IsAlive || target.Seat == actor.Seat || use.TargetSeats.Contains(target.Seat) || GetHand(target).Count == 0 ||
                    IsDirectedCardTargetProhibited(actor.Seat, target.Seat, use.CardKind) || IsCardTargetProhibited(target, use.CardKind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) ||
                    HasBeneficiarySuitShield(actor.Seat, target.Seat, action.EffectiveSuit) || IsSelfTargetForbiddenAction(actor, use.CardKind, [target.Seat])) continue;
            }
            else if (!CanBeDistanceFreeDesignatedTarget(use, target)) continue;
            if (IsSlashCard(use.CardKind) && !CanUseSlashTarget(actor, target, new(-1, use.CardKind, action.EffectiveSuit ?? Suit.None, action.EffectiveRank ?? 0),
                action.ConversionChain.FirstOrDefault(), use.CardKind, existingUseFrameId: use.Id, specificEffectiveRank: action.EffectiveRank, physicalCardIds: physical)) continue;
            if (use.CardKind == CardKind.Snatch && GetCombatDistance(actor.Seat, target.Seat) != 1 &&
                !HasCardDistanceExemption(actor, target, CardKind.Snatch, use.Id) && !HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, CardKind.Snatch) &&
                !HasIssuedFuRongDistance(use.Id, actor.Seat) && !HasIssuedProvenanceUseDistance(use.Id, actor.Seat) && !HasIssuedGrantedPhaseEntityDistance(use.Id, actor.Seat) &&
                !HasPhaseSuitAllowance(actor.Seat, action.EffectiveSuit) && !GuanFirstRedAvailable(actor, action.EffectiveSuit)) continue;
            if (use.CardKind == CardKind.BorrowedSword)
                foreach (var victim in _players.Where(v => IsLegalBorrowedSwordSlashTarget(target, v)).OrderBy(v => v.Seat)) result.Add(Array.AsReadOnly(new[] { target.Seat, victim.Seat }));
            else result.Add(Array.AsReadOnly(new[] { target.Seat }));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private SkillProgramStepOutcome BeginBingzhongTargets(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = RequireBingzhongUse(f, out _);
        var ordinal = CapturedWholeActualTurnActorUseOrdinal(use.Action!) ?? throw new InvalidOperationException("Bingzhong requires its frozen actual-use ordinal.");
        var x = BingzhongCurrentX();
        StartYangFu(f, NewYangFuReceipt(f, SkillProgramEffectOp.ResolveBingzhongExtraTargets, YangFuStage.Targets, f.OwnerSeat, x, ordinal, use));
        f = GetActiveProgramFrame(f.Id);
        if (ordinal > x || BingzhongChoices(f).Count == 1) CompleteBingzhongTargets(f);
        else PublishBingzhongChoices(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> BingzhongChoices(ProgramSkillFrame f)
    {
        var r = f.YangFu!; var use = RequireBingzhongUse(f, out _); var result = new List<PromptChoice>();
        if (r.AddedTargets.Count / (use.CardKind == CardKind.BorrowedSword ? 2 : 1) < r.X)
        {
            var targets = r.OriginalTargets.Concat(r.AddedTargets).ToArray();
            foreach (var seats in BingzhongLegalTargets(use with { TargetSeats = targets, Action = CloneDesignatedExtraTargetAction(use.Action!, targets) }))
                result.Add(new(new($"bingzhong.{f.Id}.{string.Join('.', seats)}"), "追加目标：" + string.Join("、", seats.Select(s => _players[s].Name)), [], seats,
                    new Dictionary<string, string> { ["program-action"] = "bingzhong-target", ["branch"] = "target" }));
        }
        result.Add(new(new($"bingzhong.{f.Id}.finish"), "完成额外目标选择", [], [], new Dictionary<string, string> { ["program-action"] = "bingzhong-target", ["branch"] = "finish" }));
        return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishBingzhongChoices(ProgramSkillFrame f)
    {
        var choices = BingzhongChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"屏忠：额外指定至多{f.YangFu!.X}个目标。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("bingzhong-target")]
    private void AnswerBingzhongTargets(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Bingzhong lost its public choice owner.");
        AssertYangFu(f); var r = f.YangFu!; var choices = BingzhongChoices(f);
        var choice = choices.SingleOrDefault(c => c.Id == selected.Id);
        if (r.Stage != YangFuStage.Targets || choice is null || !AssistedChoicesEqual([choice], [selected])) throw new InvalidOperationException("Bingzhong cannot change its normal-range frozen target choice.");
        ClearPendingDecision();
        if (choice.Targets.Count == 0) { CompleteBingzhongTargets(f); return; }
        for (var i = 0; i < choice.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new BingzhongTargetSelectedEvent(f.Id, r.AddedTargets.Count + i, choice.Targets[i]));
        ReplaceRuntimeTop(f = f with { YangFu = r with { AddedTargets = r.AddedTargets.Concat(choice.Targets).ToArray() } });
        if (BingzhongChoices(f).Count == 1) CompleteBingzhongTargets(f); else PublishBingzhongChoices(f);
    }
    private void CompleteBingzhongTargets(ProgramSkillFrame f)
    {
        var r = f.YangFu!; var use = RequireBingzhongUse(f, out var window); var old = use.Action!;
        var targets = r.OriginalTargets.Concat(r.AddedTargets).ToArray(); var action = CloneDesignatedExtraTargetAction(old, targets);
        var simple = use.AdjustedSimpleContinuation;
        if (r.AddedTargets.Count > 0 && window.Continuation == ProgramCardContinuation.FinalizedSimpleCard)
        {
            var continuation = window.SimpleContinuation ?? throw new InvalidOperationException("Bingzhong lost its original basic-card continuation.");
            if (continuation.Effect is not (SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol)) throw new InvalidOperationException("Bingzhong cannot invent a basic-card effect.");
            simple ??= continuation with { RecoveryPolicySources = continuation.RecoveryPolicySources is { } sources ? Array.AsReadOnly(sources.ToArray()) : null };
        }
        var tail = new BingzhongTargetTail(f.Id, use.Id, action.ActionId, r.Source, r.GameplayHash, r.X, r.Ordinal!.Value, old.ActorSeat, old.ProviderSeat, r.OriginalTargets, r.AddedTargets);
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, BingzhongTail = tail, AdjustedSimpleContinuation = simple,
            TargetsAdjusted = use.TargetsAdjusted || r.AddedTargets.Count > 0, Enhancements = r.AddedTargets.Count == 0 ? use.Enhancements : use.Enhancements | CurrentCardEnhancement.ExtraTarget }, action);
        AdvanceEventRulesAndQueueFact(new BingzhongTargetsAppliedEvent(f.Id, use.Id, action.ActionId, r.Source, r.GameplayHash, r.X, r.Ordinal.Value, old.ActorSeat, old.ProviderSeat, r.OriginalTargets.Count, r.AddedTargets.Count));
        FinishYangFu(GetActiveProgramFrame(f.Id));
    }
    private bool YangFuActorChain(CardUseFrame use, int original, int provider, long actionId)
    {
        if (use.Action is not { Type: CardActionType.Use } action || action.ActionId != actionId || action.ProviderSeat != provider) return false;
        var actor = original;
        foreach (var e in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == use.Id))
        {
            if (e.PreviousActorSeat != actor || e.OwnerSeat != actor || e.ProviderSeat != provider || !IsValidPlayerSeat(e.ActorSeat) ||
                !ProgramEventHistory<ProgramBindingStartedEvent>().Any(b => b.FrameId == e.FrameId && b.SkillId == e.SkillId && b.OwnerSeat == actor && b.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized)) return false;
            actor = e.ActorSeat;
        }
        return actor == action.ActorSeat && actor == use.SourceSeat;
    }
    private bool HasBingzhongTargetTail(CardUseFrame use) => use.Action is { } a && HasBingzhongTargetTail(use, a);
    private bool HasBingzhongTargetTail(CardUseFrame use, CardActionContext action)
    {
        if (use.BingzhongTail is not { AddedTargets.Count: > 0 } r || action.ActionId != r.ActionId || r.UseId != use.Id || r.Source.SkillId != "ol:bingzhong" || r.Source.BindingId != "first-dynamic-x-targets" ||
            r.X < 1 || r.Ordinal < 1 || r.Ordinal > r.X || r.AddedTargets.Count / (use.CardKind == CardKind.BorrowedSword ? 2 : 1) > r.X ||
            !action.TargetSeats.SequenceEqual(r.OriginalTargets.Concat(r.AddedTargets)) || r.Source.OwnerSeat != r.OriginalActor ||
            ProgramEventHistory<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { } declaration ||
            !YangFuActorChain(use, declaration.SourceSeat, r.ProviderSeat, r.ActionId) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
            ProgramEventHistory<BingzhongTargetsAppliedEvent>().Count(e => e == new BingzhongTargetsAppliedEvent(r.FrameId, r.UseId, r.ActionId, r.Source, r.GameplayHash, r.X, r.Ordinal, r.OriginalActor, r.ProviderSeat, r.OriginalTargets.Count, r.AddedTargets.Count)) != 1 ||
            !ProgramEventHistory<BingzhongOriginalTargetEvent>().Where(e => e.FrameId == r.FrameId).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.OriginalTargets) ||
            !ProgramEventHistory<BingzhongTargetSelectedEvent>().Where(e => e.FrameId == r.FrameId).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.AddedTargets)) return false;
        var actual = r.OriginalTargets.Concat(r.AddedTargets).ToArray();
        foreach (var e in ProgramEventHistory<BingzhongRedirectedEvent>().Where(e => e.UseId == use.Id))
        {
            if (!IsSlashCard(use.CardKind) || e.ActionId != r.ActionId || e.TargetIndex < 0 || e.TargetIndex >= actual.Length || actual[e.TargetIndex] != e.PreviousSeat || e.Source.OwnerSeat != e.PreviousSeat ||
                _contentRegistry.GetSkill(e.Source.SkillId).Program is not { } p || p.GameplayHash != e.GameplayHash ||
                ProgramInstructionResolver.Default.Find(p, ProgramInstructionSourceKind.Trigger, e.Source.BindingId) is not { } plan || plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting ||
                e.InstructionIndex < 1 || e.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(e.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(b => b.FrameId == e.FrameId && b.OwnerSeat == e.Source.OwnerSeat && b.SkillId == e.Source.SkillId && b.SkillInstanceId == e.Source.SkillInstanceId && b.BindingId == e.Source.BindingId) != 1) return false;
            actual[e.TargetIndex] = e.Seat;
        }
        return actual.SequenceEqual(use.TargetSeats);
    }
    private void RecordBingzhongRedirect(ProgramSkillFrame f, long useId, int previousSeat, int nextSeat)
    {
        if (LifecycleCardUse(useId) is not { BingzhongTail: { AddedTargets.Count: > 0 }, Action: { } a } use) return;
        if (GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.RedirectCurrentAttack || f.OwnerSeat != previousSeat ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } c } context || c.ParentCardUseFrameId != useId || c.CardActionId != a.ActionId ||
            context.TargetSeat != previousSeat || use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != nextSeat)
            throw new InvalidOperationException("Bingzhong cannot acknowledge an unrelated native redirect.");
        AdvanceEventRulesAndQueueFact(new BingzhongRedirectedEvent(f.Id, use.Id, a.ActionId, YangFuSource(f), f.GameplayHash, f.InstructionIndex, use.TargetIndex, previousSeat, nextSeat));
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBingzhongExtraTargets)]
    private PromptChoice ChooseAiBingzhong(PendingDecision decision, ProgramSkillFrame f)
    {
        var use = RequireBingzhongUse(f, out _); bool helpful = use.CardKind is CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains;
        return decision.Choices.FirstOrDefault(c => c.Targets.Count > 0 && (use.CardKind == CardKind.BorrowedSword ? !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[1]]) :
            (helpful || use.CardKind == CardKind.IronChain && _players[c.Targets[0]].IsChained) == AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]]))) ?? decision.Choices.Single(c => c.Targets.Count == 0);
    }
}
