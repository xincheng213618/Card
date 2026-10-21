namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouChengYu => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouChengYuLidan) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouChengYuDanchi);
    private MouLidanRuntime MouLidan => GetRuntimeModule<MouLidanRuntime>();
    private MouDanchiRuntime MouDanchi => GetRuntimeModule<MouDanchiRuntime>();

    [ProgramCapability]
    private sealed class MouChengYuHost(GameEngine engine) : IMouChengYuHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolveMouChengYuLidan
            ? engine.MouLidan.Resolve(f) : engine.MouDanchi.Resolve(f);
    }
    [ProgramCapability]
    private sealed class MouChengYuFacts(GameEngine engine) : IMouChengYuFacts
    {
        public int Turn => engine._turnNumber;
        public bool GameOver => engine._winner != Winner.None;
        public bool Alive(int seat) => engine.IsValidPlayerSeat(seat) && engine._players[seat].IsAlive;
        public bool Human(int seat) => engine._players[seat].IsHuman;
        public IReadOnlyList<T> History<T>() where T : IGameEvent => Array.AsReadOnly(engine.ProgramEventHistory<T>().ToArray());
        public CardActionContext Action(ProgramSkillFrame f) => engine.MouChengYuAction(f);
        public CardUseFrame? Use(long id) => engine.LifecycleCardUse(id);
        public CardUseFrame? TargetUse(ProgramSkillFrame f) => f.SkillId == MouChengYuComposition.Lidan &&
            f.TriggerId == "next-matching-targets" && NativeParentMatches(f) && f.WindowContext?.CardUse is { } c
            ? engine.LifecycleCardUse(c.ParentCardUseFrameId) : null;
        public MouDanchiDamage? Damage(ProgramSkillFrame f) => engine.MouChengYuDamage(f.OwnerSeat, f.WindowContext);
        public bool NativeParentMatches(ProgramSkillFrame f) => engine.MouChengYuParentMatches(f);
        public IReadOnlyList<MouChengYuTargetOption> TargetOptions(long id, IReadOnlyList<int> selected)
        {
            if (engine.LifecycleCardUse(id) is not { Action: { } a } use || !Alive(use.SourceSeat)) return [];
            // An entity-backed delayed placement has exactly one native Judgment
            // destination. Extra targeting must not clone its physical entity.
            if (IsDelayedCard(use.CardKind)) return [];
            var original = use.TargetSeats.Count == 0 && use.CardKind == CardKind.DrawTwo ? new[] { use.SourceSeat } : use.TargetSeats;
            var targets = Array.AsReadOnly(original.Concat(selected).ToArray());
            var preview = use with { TargetSeats = targets, Action = engine.CloneDesignatedExtraTargetAction(a, targets) };
            return Array.AsReadOnly(engine.DesignatedExtraTargetCandidates(preview).Select(seats => new MouChengYuTargetOption(
                string.Join("、", seats.Select(s => engine._players[s].Name))) { Seats = seats }).ToArray());
        }
        public IReadOnlyList<int> SlashTargets(int actor) => Array.AsReadOnly(engine._players.Where(t => engine.CanMouDanchiSlash(actor, t.Seat))
            .Select(t => t.Seat).ToArray());
    }
    [ProgramCapability]
    private sealed class MouChengYuNative(GameEngine engine) : IMouChengYuNative
    {
        public void FreezeLidan(CardUseFrame use, MouLidanUseReceipt receipt) => engine.UpdateLifecycleCardUse(use.Id, u => u with { MouLidan = receipt });
        public void ApplyTargets(ProgramSkillFrame f, MouLidanSelection selection) => engine.ApplyMouLidanTargets(f, selection);
        public bool IssueReward(ProgramSkillFrame f, CardKind kind, int target) => engine.IssueMouDanchiReward(f, kind, target);
        public bool DrainChildren(ProgramSkillFrame f) => engine.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            engine.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            engine.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            engine.TryBeginCardsMovedProgramWindow(f.Id) || engine.TryBeginAdvancedSkillsChanged(f.Id);
        public void Finish(ProgramSkillFrame f) { engine.RestoreMouDanchiAttack(engine.GetActiveProgramFrame(f.Id)); engine.ReplaceRuntimeTop(f); engine.FinishProgramSkill(f, true); }
        public void PublishState() => engine.AdvanceRulesAndPublishState();
    }
    private CardActionContext MouChengYuAction(ProgramSkillFrame f) => f.WindowContext?.CardUse is { } c &&
        _resolutionStack.SingleOrDefault(w => w.Id == f.WindowContext.ParentFrameId) is ProgramCardTriggerWindowFrame window &&
        window.Action.ActionId == c.CardActionId ? window.Action : throw new InvalidOperationException("Mou Cheng Yu needs its exact native card window.");
    private bool MouChengYuParentMatches(ProgramSkillFrame f)
    {
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { } context ||
            _resolutionStack[i - 1].Id != context.ParentFrameId ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        if (_resolutionStack[i - 1] is ProgramCardTriggerWindowFrame window)
            return context.CardUse is { } c && c.CardActionId == window.Action.ActionId &&
                window.Candidates.Any(n => n.OwnerSeat == f.OwnerSeat && n.SkillId == f.SkillId && n.SkillInstanceId == f.SkillInstanceId &&
                    n.TriggerId == f.TriggerId && n.GameplayHash == f.GameplayHash) && MouDanchiSuspensionMatches(f);
        return _resolutionStack[i - 1] is DamageTriggerWindowFrame damage && context.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
            context.DamageFrameId == damage.ParentFrameId && damage.Candidates.Any(n => MountObserverCandidateMatches(f, n.ToProgramCandidate()));
    }
    private MouDanchiDamage? MouChengYuDamage(int owner, ProgramSkillWindowContext? c)
    {
        if (c is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, SourceSeat: { } source, TargetSeat: { } victim } ||
            !IsValidPlayerSeat(owner) || !_players[owner].IsAlive || !IsValidPlayerSeat(source) || !IsValidPlayerSeat(victim) ||
            !_players[victim].IsAlive || GetCombatDistance(owner, victim) > 1 ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not DamageTriggerWindowFrame window ||
            _resolutionStack.SingleOrDefault(x => x.Id == window.ParentFrameId) is not DamageFrame damage || damage.Id != c.DamageFrameId ||
            GetDamageTriggerAttack(window).IsSourceLess || damage.SourceSeat != source || damage.TargetSeat != victim || damage.Amount != c.Amount) return null;
        var original = LifecycleCardUse(damage.ParentFrameId);
        return new(c.ParentFrameId, damage.Id, damage.ParentFrameId, original?.Action?.ActionId, source, victim, c.Amount, _turnNumber);
    }
    private bool IsMouChengYuTrueUse(CardActionContext action) => action.Type == CardActionType.Use ||
        IsCompletedResponseUse(action, ProgramCardContinuation.NullificationResponse) ||
        IsCompletedResponseUse(action, ProgramCardContinuation.Dodge) && ActiveCardAttack is { } attack &&
            attack.EffectiveCardKind is { } incoming && IsSlashCard(incoming) && attack.TargetSeat == action.ActorSeat &&
            action.ParentActionId == LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId && action.OpponentSeat == attack.SourceSeat;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 34100)]
    private void ObserveMouChengYuUse(IGameEvent fact)
    {
        if (!HasMouChengYu) return;
        var use = fact is CardUseDeclaredEvent d ? LifecycleCardUse(d.ResolutionId) : null;
        var action = use?.Action ?? (fact is CardActionAcceptedEvent accepted ? accepted.Action : null);
        if (action is null || use is null && action.Type != CardActionType.Response || !IsMouChengYuTrueUse(action)) return;
        if (use is not null) MouLidan.AcceptUse(use);
        else if (action.EffectiveKind == CardKind.Nullification)
            ConsumeMouLidanCounterspell(action);
        MouDanchi.AcceptUse(use?.Id ?? 0, action);
    }
    private void ConsumeMouLidanCounterspell(CardActionContext action)
    {
        foreach (var permit in ProgramEventHistory<MouLidanPermitEvent>().Where(p => p.Source.OwnerSeat == action.ActorSeat && !p.ForSlash &&
            !ProgramEventHistory<MouLidanConsumedEvent>().Any(c => c.PermitFrameId == p.FrameId && c.PermitActionId == p.ActionId)).ToArray())
            AdvanceEventRulesAndQueueFact(new MouLidanConsumedEvent(permit.FrameId, permit.ActionId, permit.Source,
                action.ParentActionId ?? 0, action.ActionId, action.ActorSeat));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 34100, SkillProgramEffectOp.ResolveMouChengYuLidan, SkillProgramEffectOp.ResolveMouChengYuDanchi)]
    private bool QualifyMouChengYu(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == MouChengYuComposition.Danchi && trigger.Id == "nearby-damage-type")
            return MouChengYuDamage(candidate.OwnerSeat, context) is not null && MouDanchi.DamageAvailable(candidate.OwnerSeat);
        if (context.CardUse is not { } card || _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            window.Action.ActionId != card.CardActionId || !IsMouChengYuTrueUse(window.Action)) return false;
        return candidate.SkillId == MouChengYuComposition.Lidan && trigger.Id == "alternating-use-permit"
            ? window.Action.ActorSeat == candidate.OwnerSeat && (IsSlashCard(window.Action.EffectiveKind) || MouLidanRuntime.Trick(window.Action.EffectiveKind)) &&
                !ProgramEventHistory<MouLidanPermitEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.ActionId == card.CardActionId)
            : IsIssuedMouChengYu(candidate, context);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 34100)]
    private bool IsIssuedMouChengYu(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!HasMouChengYu || context.CardUse is not { } card || !IsValidPlayerSeat(c.OwnerSeat) || !_players[c.OwnerSeat].IsAlive ||
            _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash != c.GameplayHash ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window || window.Action.ActionId != card.CardActionId) return false;
        if (c.SkillId == MouChengYuComposition.Lidan && c.BindingId == "next-matching-targets")
            return context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && LifecycleCardUse(card.ParentCardUseFrameId)?.MouLidan is { Offered: false } r &&
                r.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && r.GameplayHash == c.GameplayHash && r.ActionId == card.CardActionId;
        return c.SkillId == MouChengYuComposition.Danchi && c.BindingId == "source-next-use" && context.Window == SkillProgramTriggerWindow.CardUseCompleted &&
            MouDanchi.RewardPromise(c.OwnerSeat, card.CardActionId) is { } p && p.Source.SkillInstanceId == c.SkillInstanceId && p.GameplayHash == c.GameplayHash;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 34100)]
    private void CollectMouChengYuIssued(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasMouChengYu || !IsMouChengYuTrueUse(action) || _winner != Winner.None) return;
        var sources = new List<(CardConversionSource Source, string Hash)>();
        if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized && _resolutionStack.LastOrDefault() is CardUseFrame { MouLidan: { Offered: false } r })
            sources.Add((r.Source, r.GameplayHash));
        if (window == SkillProgramTriggerWindow.CardUseCompleted)
            foreach (var p in ProgramEventHistory<MouDanchiPromiseEvent>().Where(p => _players[p.Source.OwnerSeat].IsAlive &&
                MouDanchi.RewardPromise(p.Source.OwnerSeat, action.ActionId)?.FrameId == p.FrameId))
                sources.Add((p.Source with { BindingId = "source-next-use" }, p.GameplayHash));
        foreach (var (source, hash) in sources)
        {
            if (!_players[source.OwnerSeat].IsAlive || result.Any(c => c.OwnerSeat == source.OwnerSeat && c.SkillId == source.SkillId && c.TriggerId == source.BindingId && c.SkillInstanceId == source.SkillInstanceId)) continue;
            var t = _contentRegistry.GetSkill(source.SkillId).Program!.Triggers.Single(t => t.Id == source.BindingId);
            result.Add(new(source.OwnerSeat, action.ActorSeat, source.SkillId, t.Id, hash, source.SkillInstanceId, t.Priority,
                CreateCardActionProgramContext(action, window, 0, source.OwnerSeat, action.ActorSeat, CaptureProgramTriggerFacts(_players[source.OwnerSeat], action))));
        }
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ObservesResponseUseCompletion, 34100)]
    private bool ObserveMouChengYuResponse(CardActionContext action) => HasMouChengYu && IsMouChengYuTrueUse(action) &&
        ProgramEventHistory<MouDanchiPromiseEvent>().Any(p => MouDanchi.RewardPromise(p.Source.OwnerSeat, action.ActionId) is not null);
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 34100)]
    private bool MouChengYuFinalizedTrick(ProgramTargetCardWindowQuery query) => HasMouChengYu;
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 34100)]
    private bool MouChengYuDrawTwoTargets(ProgramTargetCardWindowQuery query) => HasMouChengYu;

    private void ApplyMouLidanTargets(ProgramSkillFrame f, MouLidanSelection selection)
    {
        if (!MouLidan.Valid(f) || LifecycleCardUse(selection.UseId) is not { MouLidan: { } r, Action: { } original } use)
            throw new InvalidOperationException("Lidan cannot apply targets outside its original paid native use.");
        var before = use.TargetSeats.Count == 0 && use.CardKind == CardKind.DrawTwo ? Array.AsReadOnly(new[] { use.SourceSeat }) : use.TargetSeats;
        var targets = Array.AsReadOnly(before.Concat(selection.Selected).ToArray());
        var action = CloneDesignatedExtraTargetAction(original, targets);
        UpdateProgramRoleCardUse(use with { Action = action, TargetSeats = targets, TargetsAdjusted = use.TargetsAdjusted || selection.Selected.Count > 0,
            MouLidan = r with { Offered = true, Original = before, Added = selection.Selected },
            Enhancements = selection.Selected.Count > 0 ? use.Enhancements | CurrentCardEnhancement.ExtraTarget : use.Enhancements }, action);
        AdvanceEventRulesAndQueueFact(new MouLidanTargetsAppliedEvent(f.Id, use.Id, action.ActionId, r.Source, r.GameplayHash, before.Count, selection.Selected.Count));
        for (var i = 0; i < before.Count; i++) AdvanceEventRulesAndQueueFact(new MouLidanTargetSlotEvent(f.Id, i, before[i], false));
        for (var i = 0; i < selection.Selected.Count; i++)
        {
            AdvanceEventRulesAndQueueFact(new MouLidanTargetSlotEvent(f.Id, i, selection.Selected[i], true));
            if (use.CardKind != CardKind.BorrowedSword || i % 2 == 0)
                AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id, f.SkillId, f.OwnerSeat, use.Id, selection.Selected[i]));
        }
        if (selection.Selected.Count > 0) RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!,
            use.CardKind == CardKind.BorrowedSword ? selection.Selected.Where((_, i) => i % 2 == 0).ToArray() : selection.Selected);
    }
    private bool HasMouLidanTargetTail(CardUseFrame use) => use.MouLidan is { Offered: true, Added.Count: > 0 } r && use.Action is { } a &&
        r.UseId == use.Id && r.ActionId == a.ActionId && r.ProviderSeat == a.ProviderSeat && ShownEntityUseActorMatches(use, r.ActorSeat, r.ProviderSeat) &&
        a.TargetSeats.SequenceEqual(r.Original.Concat(r.Added)) && use.TargetSeats.Count == a.TargetSeats.Count &&
        ProgramEventHistory<MouLidanUseFrozenEvent>().Count(e => e.UseId == use.Id && e.ActionId == a.ActionId && e.Maximum == r.Maximum && e.Source == r.Source && e.GameplayHash == r.GameplayHash) == 1 &&
        ProgramEventHistory<MouLidanConsumedEvent>().Count(e => e.UseId == use.Id && e.ActionId == a.ActionId) == r.Maximum &&
        ProgramEventHistory<MouLidanTargetsAppliedEvent>().SingleOrDefault(e => e.UseId == use.Id && e.ActionId == a.ActionId) is { } applied &&
        applied.Source == r.Source && applied.GameplayHash == r.GameplayHash && applied.OriginalCount == r.Original.Count && applied.AddedCount == r.Added.Count &&
        ProgramEventHistory<MouLidanTargetSlotEvent>().Where(e => e.FrameId == applied.FrameId && !e.Added).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.Original) &&
        ProgramEventHistory<MouLidanTargetSlotEvent>().Where(e => e.FrameId == applied.FrameId && e.Added).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.Added);

    private bool IsMouLidanAppliedTargetFact(CardUseFrame use, MouLidanTargetsAppliedEvent fact,
        IReadOnlyList<int> precedingTargets, out IReadOnlyList<int> resultTargets)
    {
        resultTargets = [];
        if (use.MouLidan is not { Offered: true } r || use.Action is not { } action || fact.UseId != use.Id ||
            fact.ActionId != action.ActionId || fact.Source != r.Source || fact.GameplayHash != r.GameplayHash ||
            r.Source.SkillId != MouChengYuComposition.Lidan || r.Source.BindingId != "next-matching-targets" ||
            r.Source.OwnerSeat != r.ActorSeat || r.UseId != use.Id || r.ActionId != action.ActionId ||
            r.ProviderSeat != action.ProviderSeat || !precedingTargets.SequenceEqual(r.Original) ||
            fact.OriginalCount != r.Original.Count || fact.AddedCount != r.Added.Count || fact.AddedCount < 1 ||
            r.Added.Count % (use.CardKind == CardKind.BorrowedSword ? 2 : 1) != 0 ||
            r.Added.Count / (use.CardKind == CardKind.BorrowedSword ? 2 : 1) > r.Maximum ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
            ProgramEventHistory<MouLidanTargetsAppliedEvent>().Count(e => e.FrameId == fact.FrameId && e == fact) != 1 ||
            ProgramEventHistory<MouLidanUseFrozenEvent>().Count(e => e.UseId == use.Id && e.ActionId == action.ActionId &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Maximum == r.Maximum &&
                e.ActorSeat == r.ActorSeat && e.ProviderSeat == r.ProviderSeat) != 1 ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.FrameId &&
                e.SkillId == r.Source.SkillId && e.BindingId == r.Source.BindingId && e.SkillInstanceId == r.Source.SkillInstanceId &&
                e.OwnerSeat == r.Source.OwnerSeat && e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) != 1)
            return false;
        var slots = ProgramEventHistory<MouLidanTargetSlotEvent>().Where(e => e.FrameId == fact.FrameId).ToArray();
        var original = slots.Where(e => !e.Added).OrderBy(e => e.Index).ToArray();
        var added = slots.Where(e => e.Added).OrderBy(e => e.Index).ToArray();
        var consumed = ProgramEventHistory<MouLidanConsumedEvent>().Where(e => e.UseId == use.Id && e.ActionId == action.ActionId).ToArray();
        if (original.Length != r.Original.Count || added.Length != r.Added.Count ||
            !original.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, original.Length)) ||
            !added.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, added.Length)) ||
            !original.Select(e => e.Seat).SequenceEqual(r.Original) || !added.Select(e => e.Seat).SequenceEqual(r.Added) ||
            slots.Any(e => !IsValidPlayerSeat(e.Seat)) || consumed.Length != r.Maximum ||
            consumed.Any(c => c.ActorSeat != r.ActorSeat || !ProgramEventHistory<MouLidanPermitEvent>().Any(p =>
                p.FrameId == c.PermitFrameId && p.ActionId == c.PermitActionId && p.Source == c.Source &&
                p.Source.OwnerSeat == r.ActorSeat && p.ForSlash == IsSlashCard(use.CardKind)))) return false;
        resultTargets = Array.AsReadOnly(r.Original.Concat(r.Added).ToArray());
        return true;
    }
    private bool IsMouLidanTargetSummary(CardUseFrame use, ProgramCardUseTargetAddedEvent summary) =>
        use.MouLidan is { Offered: true } r && summary.SkillId == r.Source.SkillId && summary.OwnerSeat == r.Source.OwnerSeat &&
        ProgramEventHistory<MouLidanTargetsAppliedEvent>().SingleOrDefault(e => e.FrameId == summary.FrameId && e.UseId == use.Id) is { } fact &&
        IsMouLidanAppliedTargetFact(use, fact, r.Original, out _) &&
        r.Added.Where((_, i) => use.CardKind != CardKind.BorrowedSword || i % 2 == 0).Contains(summary.TargetSeat) &&
        ProgramEventHistory<ProgramCardUseTargetAddedEvent>().Count(e => e == summary) == 1;
}
