using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 110)]
    private bool WeifuNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasWeifuCapability;


    private bool HasWeifuCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWeifuJudgment);
    private IReadOnlyList<WeifuGrantedEvent> ActiveWeifuGrants(int actor, CardKind kind)
    {
        if (!HasWeifuCapability || !IsValidPlayerSeat(actor) || !_players[actor].IsAlive) return [];
        var category = GetProgramCardCategory(kind);
        if (category == SkillProgramCardCategory.Equipment) return [];
        var consumed = ProgramEventHistory<WeifuGrantConsumedEvent>().Select(e => e.GrantFrameId).ToHashSet();
        var expired = ProgramEventHistory<WeifuGrantExpiredEvent>().Select(e => e.GrantFrameId).ToHashSet();
        return Array.AsReadOnly(ProgramEventHistory<WeifuGrantedEvent>().Where(e => e.Source.OwnerSeat == actor && e.ActualTurn == _turnNumber &&
            e.TurnOwnerSeat == _turnProgression.OwnerSeat && e.Category == category && !consumed.Contains(e.FrameId) && !expired.Contains(e.FrameId) &&
            _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash).ToArray());
    }
    private void IssueWeifuUse(long id, CardActionContext action)
    {
        if (action.Type != CardActionType.Use || ActiveWeifuGrants(action.ActorSeat, action.EffectiveKind) is not { Count: > 0 } grants) return;
        var use = LifecycleCardUse(id) ?? throw new InvalidOperationException("Weifu's matching actual use has no owning native frame.");
        if (use.WeifuUse is not null || use.Action?.ActionId != action.ActionId || use.SourceSeat != action.ActorSeat || use.CardKind != action.EffectiveKind)
            throw new InvalidOperationException("Weifu cannot duplicate or replace its actual effective-category use.");
        var category = GetProgramCardCategory(action.EffectiveKind);
        UpdateLifecycleCardUse(id, f => f with { WeifuUse = new(id, action.ActionId, action.ActorSeat, _turnNumber, _turnProgression.OwnerSeat, category)
        { Grants = grants, OriginalTargets = f.TargetSeats, ProviderSeat = action.ProviderSeat } });
        foreach (var grant in grants) AdvanceEventRulesAndQueueFact(new WeifuGrantConsumedEvent(grant.FrameId, id, action.ActionId, action.ActorSeat, category, false));
        AdvanceEventRulesAndQueueFact(new WeifuUseIssuedEvent(id, action.ActionId, action.ActorSeat, _turnNumber, _turnProgression.OwnerSeat, category, grants.Count, action.ProviderSeat));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26621)]
    private void ConsumeWeifuCounterspell(IGameEvent payload)
    {
        if (!HasWeifuCapability || payload is not CardActionAcceptedEvent { Action: { Type: CardActionType.Response, EffectiveKind: CardKind.Nullification } a } ||
            a.ActorSeat != a.ProviderSeat || a.ResponderSeat != a.ActorSeat || a.RequesterSeat is not null ||
            ActiveNullificationWindow is not { } window || LifecycleCardUse(window.ParentFrameId)?.Action is not { } parent ||
            a.ParentActionId != parent.ActionId || a.OpponentSeat != window.SourceSeat) return;
        foreach (var grant in ActiveWeifuGrants(a.ActorSeat, a.EffectiveKind))
            AdvanceEventRulesAndQueueFact(new WeifuGrantConsumedEvent(grant.FrameId, window.Id, a.ActionId, a.ActorSeat, SkillProgramCardCategory.Trick, true));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26623)]
    private void ConsumeWeifuActualDodge(IGameEvent payload)
    {
        if (!HasWeifuCapability || payload is not CardActionAcceptedEvent { Action: { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge } a } ||
            a.ActorSeat != a.ProviderSeat || a.ResponderSeat != a.ActorSeat || a.RequesterSeat is not null ||
            ActiveCardAttack is not { EffectiveCardKind: { } incoming } attack || !IsSlashCard(incoming) || attack.TargetSeat != a.ActorSeat ||
            !IsProgramResponseCardUse(_players[a.ActorSeat], CardKind.Dodge) || a.OpponentSeat != attack.SourceSeat ||
            LifecycleCardUse(attack.ResolutionId)?.Action is not { Type: CardActionType.Use } parent || parent.ActionId != a.ParentActionId ||
            parent.ActorSeat != attack.SourceSeat || parent.EffectiveKind != incoming) return;
        foreach (var grant in ActiveWeifuGrants(a.ActorSeat, CardKind.Dodge))
            AdvanceEventRulesAndQueueFact(new WeifuGrantConsumedEvent(grant.FrameId, attack.ResolutionId, a.ActionId, a.ActorSeat, SkillProgramCardCategory.Basic, false, CardKind.Dodge));
    }
    private void ExpireWeifuActualTurnGrants()
    {
        if (!HasWeifuCapability) return;
        var consumed = ProgramEventHistory<WeifuGrantConsumedEvent>().Select(e => e.GrantFrameId).ToHashSet();
        var expired = ProgramEventHistory<WeifuGrantExpiredEvent>().Select(e => e.GrantFrameId).ToHashSet();
        foreach (var grant in ProgramEventHistory<WeifuGrantedEvent>().Where(e => e.ActualTurn == _turnNumber && e.TurnOwnerSeat == _turnProgression.OwnerSeat &&
            !consumed.Contains(e.FrameId) && !expired.Contains(e.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new WeifuGrantExpiredEvent(grant.FrameId, grant.ActualTurn, grant.TurnOwnerSeat));
    }
    private bool HasWeifuUseDistance(CharacterState actor, CardKind kind, long? cardUseFrameId) => cardUseFrameId is { } id ?
        LifecycleCardUse(id) is { WeifuUse: { } r, Action: { } a } use && r.UseId == id && r.ActionId == a.ActionId &&
        a.ActorSeat == actor.Seat && use.SourceSeat == actor.Seat && use.CardKind == kind && r.Category == GetProgramCardCategory(kind) && WeifuUseProof(use)
        : ActiveWeifuGrants(actor.Seat, kind).Count > 0;
    private bool WeifuUseProof(CardUseFrame use) => use.WeifuUse is { Grants.Count: > 0 } r && use.Action is { } a &&
        r.UseId == use.Id && r.ActionId == a.ActionId && WeifuCurrentActorMatches(use, r) && a.ActorSeat == use.SourceSeat &&
        a.EffectiveKind == use.CardKind && r.Category == GetProgramCardCategory(use.CardKind) &&
        r.Grants.Select(g => g.FrameId).Distinct().Count() == r.Grants.Count &&
        ProgramEventHistory<WeifuUseIssuedEvent>().Count(e => e.UseId == use.Id && e.ActionId == a.ActionId && e.ActorSeat == r.ActorSeat && e.ProviderSeat == r.ProviderSeat &&
            e.ActualTurn == r.ActualTurn && e.TurnOwnerSeat == r.TurnOwnerSeat && e.Category == r.Category && e.GrantCount == r.Grants.Count) == 1 &&
        r.Grants.All(g => g.Source.OwnerSeat == r.ActorSeat && g.ActualTurn == r.ActualTurn && g.TurnOwnerSeat == r.TurnOwnerSeat && g.Category == r.Category &&
            ProgramEventHistory<WeifuGrantedEvent>().Count(e => e == g) == 1 && ProgramEventHistory<WeifuGrantConsumedEvent>().Count(e =>
                e.GrantFrameId == g.FrameId && e.UseId == use.Id && e.ActionId == a.ActionId && e.ActorSeat == r.ActorSeat && e.Category == r.Category && !e.Counterspell) == 1);
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 16)]
    private void CollectIssuedWeifuCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseTargetsFinalized || _resolutionStack.LastOrDefault() is not CardUseFrame { WeifuUse: { Offered: false } r } use ||
            use.Action?.ActionId != action.ActionId || !_players[action.ActorSeat].IsAlive || _winner != Winner.None ||
            !WeifuCanAddTargets(use) || !WeifuUseProof(use)) return;
        foreach (var grant in r.Grants.DistinctBy(g => g.Source.SkillInstanceId))
        {
            var p = _contentRegistry.GetSkill(grant.Source.SkillId).Program!;
            var t = p.Triggers.Single(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferWeifuCategoryTargets));
            if (result.Any(c => c.OwnerSeat == r.ActorSeat && c.SkillId == p.Id && c.SkillInstanceId == grant.Source.SkillInstanceId && c.TriggerId == t.Id)) continue;
            result.Add(new(r.ActorSeat, r.ActorSeat, p.Id, t.Id, p.GameplayHash, grant.Source.SkillInstanceId, t.Priority,
                CreateCardActionProgramContext(action, window, 0, r.ActorSeat, r.ActorSeat, CaptureProgramTriggerFacts(_players[r.ActorSeat], action))));
        }
    }
    private static bool WeifuCanAddTargets(CardUseFrame use) => GetProgramCardCategory(use.CardKind) == SkillProgramCardCategory.Basic ||
        IsOrdinaryTrick(use.CardKind);
    private bool HasIssuedWeifuCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.SkillId != "ol:weifu" || context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } card } ||
            LifecycleCardUse(card.ParentCardUseFrameId) is not { WeifuUse: { } r, Action: { } a } use || !WeifuUseProof(use) ||
            a.ActionId != card.CardActionId || r.ActorSeat != c.OwnerSeat || !_players[c.OwnerSeat].IsAlive ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.ParentFrameId != use.Id || window.Action.ActionId != a.ActionId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var candidate = window.Candidates[window.CandidateIndex];
        return candidate.OwnerSeat == c.OwnerSeat && candidate.SkillId == c.SkillId && candidate.TriggerId == c.BindingId && candidate.GameplayHash == c.GameplayHash &&
            candidate.SkillInstanceId == c.SkillInstanceId && CreateCardActionProgramContext(window, candidate) == context &&
            r.Grants.Any(g => g.Source.SkillId == c.SkillId && g.Source.SkillInstanceId == c.SkillInstanceId && g.GameplayHash == c.GameplayHash) &&
            _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Any(t => t.Id == c.BindingId && t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferWeifuCategoryTargets));
    }
    private CardUseFrame RequireWeifuTargetUse(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } context || !HasIssuedWeifuCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), context))
            throw new InvalidOperationException("Weifu's target child lost its original issued native use.");
        return LifecycleCardUse(context.CardUse!.ParentCardUseFrameId)!;
    }
    private SkillProgramStepOutcome BeginWeifuTargets(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = RequireWeifuTargetUse(f);
        if (f.WeifuTargets is not null || use.WeifuUse!.Offered || f.InstructionIndex != 1)
            throw new InvalidOperationException("Weifu's matching actual use can offer its category stack only once.");
        if (use.CardKind == CardKind.DrawTwo && use.TargetSeats.Count == 0)
        {
            var targets = Array.AsReadOnly(new[] { use.SourceSeat }); var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action); use = RequireWeifuTargetUse(f);
        }
        ReplaceRuntimeTop(f = f with { WeifuTargets = new(QianZhaoSource(f), f.GameplayHash, f.InstructionIndex, use.Id, use.Action!.ActionId, use.WeifuUse!.Grants.Count) });
        if (!WeifuCanAddTargets(use) || WeifuTargetChoices(f).Count == 1) CompleteWeifuTargets(f);
        else PublishQianZhaoChoice(f.OwnerSeat, f, WeifuTargetChoices(f), "威抚：选择额外目标，或完成选择。", false);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> WeifuTargetChoices(ProgramSkillFrame f)
    {
        var r = f.WeifuTargets!; var use = RequireWeifuTargetUse(f); var result = new List<PromptChoice>();
        var width = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        if (r.Selected.Count / width < r.Maximum)
        {
            var targets = Array.AsReadOnly(use.TargetSeats.Concat(r.Selected).ToArray());
            var preview = use with { TargetSeats = targets, Action = CloneDesignatedExtraTargetAction(use.Action!, targets) };
            foreach (var seats in DesignatedExtraTargetCandidates(preview)) result.Add(new(new($"weifu.{f.Id}.target.{string.Join('.', seats)}"),
                "增加目标：" + string.Join("、", seats.Select(s => _players[s].Name)), [], seats,
                new Dictionary<string, string> { ["program-action"] = "qian-zhao", ["branch"] = "weifu-target" }));
        }
        result.Add(new(new($"weifu.{f.Id}.finish"), "完成额外目标选择", [], [],
            new Dictionary<string, string> { ["program-action"] = "qian-zhao", ["branch"] = "weifu-finish" }));
        return Array.AsReadOnly(result.ToArray());
    }
    private void CompleteWeifuTargets(ProgramSkillFrame f)
    {
        var r = f.WeifuTargets!; var use = RequireWeifuTargetUse(f); var targets = Array.AsReadOnly(use.TargetSeats.Concat(r.Selected).ToArray());
        var action = CloneDesignatedExtraTargetAction(use.Action!, targets); var simple = use.AdjustedSimpleContinuation;
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == f.WindowContext!.ParentFrameId);
        if (r.Selected.Count > 0 && window.Continuation == ProgramCardContinuation.FinalizedSimpleCard)
        {
            var continuation = window.SimpleContinuation ?? throw new InvalidOperationException("Weifu's actual basic card lost its native effect.");
            if (continuation.Effect is not (SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol))
                throw new InvalidOperationException("Weifu cannot fabricate a simple-card effect.");
            simple ??= continuation with { RecoveryPolicySources = continuation.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null };
        }
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, AdjustedSimpleContinuation = simple,
            TargetsAdjusted = use.TargetsAdjusted || r.Selected.Count > 0, WeifuUse = use.WeifuUse! with { Offered = true, OriginalTargets = use.TargetSeats, AddedTargets = r.Selected },
            Enhancements = r.Selected.Count == 0 ? use.Enhancements : use.Enhancements | CurrentCardEnhancement.ExtraTarget }, action);
        for (var i = 0; i < r.Selected.Count; i++) AdvanceEventRulesAndQueueFact(new WeifuTargetAddedEvent(f.Id, use.Id, action.ActionId, r.Source, r.Selected[i], i));
        FinishQianZhao(GetActiveProgramFrame(f.Id));
    }
    private void PublishQianZhaoChoice(int chooser, ProgramSkillFrame f, IReadOnlyList<PromptChoice> choices, string text, bool isPrivate)
    {
        if (choices.Count == 0) throw new InvalidOperationException("Qian Zhao has no legal native continuation choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = isPrivate, TargetSeat = chooser, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 90)]
    private bool DistanceCardUseExemption90HasWeifuUseDistance(ProgramCardDistanceQuery q) =>
        HasWeifuUseDistance(q.Actor, q.Kind, q.CardUseFrameId);


    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 40)]
    private bool DistancePhysicalSlash40HasWeifuUseDistance(ProgramSlashDistanceQuery q) =>
        q.ExistingUseFrameId is not null && HasWeifuUseDistance(q.Actor, q.Kind, q.ExistingUseFrameId);

}
