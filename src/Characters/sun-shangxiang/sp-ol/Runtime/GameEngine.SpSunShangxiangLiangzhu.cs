using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasSpSunShangxiangLiangzhu => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveSpSunShangxiangLiangzhu);
    private static CardConversionSource SpSunShangxiangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static bool IsSpSunShangxiangLiangzhuTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveSpSunShangxiangLiangzhu }];
    [ProgramCapability] private sealed class SpSunShangxiangOlCapability(GameEngine engine) : ISpSunShangxiangOlHost
    {
        public SkillProgramStepOutcome Liangzhu(ProgramSkillFrame f) => engine.BeginSpSunShangxiangLiangzhu(f);
        public SkillProgramStepOutcome CompleteFanxiang(ProgramSkillFrame f) => engine.CompleteSpSunShangxiangFanxiang(f);
    }
    private void CaptureSpSunShangxiangRecovery(HpChangeContext change)
    {
        if (!HasSpSunShangxiangLiangzhu || change.Kind != HpChangeKind.Recovery || change.Amount <= 0 ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != change.TargetSeat || !IsRecordedActualDiscardRecoveryPhase(phase)) return;
        foreach (var owner in _players.Where(p => p.IsAlive))
        foreach (var c in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.AfterHpRecovered).Where(c => IsSpSunShangxiangLiangzhuTrigger(GetProgramTrigger(c))))
            AdvanceEventRulesAndQueueFact(new SpSunShangxiangRecoveryQualifiedEvent(new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash,
                change.Id, change.ParentFrameId, change.TargetSeat, change.HpBefore, change.HpAfter, phase));
    }
    private IReadOnlyList<ProgramTriggerCandidate> CollectSpSunShangxiangRecoveryCandidates(HpChangeContext change) => ProgramEventHistory<SpSunShangxiangRecoveryQualifiedEvent>()
        .Where(q => q.ChangeId == change.Id && _players[q.Source.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[q.Source.OwnerSeat], q.Source.SkillId, q.Source.SkillInstanceId) &&
            _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash == q.GameplayHash)
        .Select(q => new ProgramTriggerCandidate(q.Source.OwnerSeat, q.Source.SkillId, q.Source.BindingId, q.Source.SkillInstanceId, q.GameplayHash,
            _contentRegistry.GetSkill(q.Source.SkillId).Program!.Triggers.Single(t => t.Id == q.Source.BindingId).Priority)).ToArray();
    private SpSunShangxiangRecoveryQualifiedEvent? SpSunShangxiangRecoveryQualification(ProgramTriggerCandidate c, HpChangeContext change) =>
        ProgramEventHistory<SpSunShangxiangRecoveryQualifiedEvent>().SingleOrDefault(q => q.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) &&
            q.GameplayHash == c.GameplayHash && q.ChangeId == change.Id && q.RecoveryParentId == change.ParentFrameId && q.RecoveredSeat == change.TargetSeat &&
            q.HpBefore == change.HpBefore && q.HpAfter == change.HpAfter && change.Kind == HpChangeKind.Recovery && change.Amount == change.HpAfter - change.HpBefore &&
            q.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && q.Phase.ActorSeat == change.TargetSeat && IsRecordedActualDiscardRecoveryPhase(q.Phase));
    private ProgramSkillWindowContext CreateSpSunShangxiangRecoveryContext(HpChangeContext change, ProgramTriggerCandidate c)
    {
        if (SpSunShangxiangRecoveryQualification(c, change) is null) throw new InvalidOperationException("Liangzhu requires its frozen actual own-Play recovery.");
        return new(SkillProgramTriggerWindow.AfterHpRecovered, change.Id, c.OwnerSeat, SourceSeat: change.SourceSeat, TargetSeat: change.TargetSeat,
            Amount: change.Amount, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), HpChange: change);
    }
    private bool CanRunSpSunShangxiangLiangzhu(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => context.Window == SkillProgramTriggerWindow.AfterHpRecovered &&
        context.OwnerSeat == c.OwnerSeat && context.HpChange is { Kind: HpChangeKind.Recovery, Amount: > 0 } change && context.ParentFrameId == change.Id &&
        context.TargetSeat == change.TargetSeat && context.SourceSeat == change.SourceSeat && context.Amount == change.Amount && c.OccurrenceIndex == 0 &&
        SpSunShangxiangRecoveryQualification(c, change) is not null;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32300)]
    private bool CanOfferSpSunShangxiangLiangzhu(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        !IsSpSunShangxiangLiangzhuTrigger(t) || CanRunSpSunShangxiangLiangzhu(c, context);
    private SkillProgramStepOutcome BeginSpSunShangxiangLiangzhu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SpSunShangxiangLiangzhu is not null || f.InstructionIndex != 1 || f.ActivationId != f.TriggerId || f.WindowContext is not { } w ||
            !CanRunSpSunShangxiangLiangzhu(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), w))
            throw new InvalidOperationException("Liangzhu cannot repeat or borrow another native recovery occurrence.");
        var r = new SpSunShangxiangLiangzhuReceipt(SpSunShangxiangSource(f), f.GameplayHash, w.ParentFrameId, w.TargetSeat!.Value);
        ReplaceRuntimeTop(f = f with { SpSunShangxiangLiangzhu = r });
        AdvanceEventRulesAndQueueFact(new SpSunShangxiangLiangzhuStartedEvent(f.Id, r.Source, r.GameplayHash, r.ChangeId, r.RecoveredSeat));
        PublishSpSunShangxiangLiangzhu(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> SpSunShangxiangLiangzhuChoices(ProgramSkillFrame f)
    {
        var r = f.SpSunShangxiangLiangzhu!; var choices = new List<PromptChoice>();
        foreach (var (branch, text) in new[] { ("self", "自己摸一张牌"), ("help", $"令{_players[r.RecoveredSeat].Name}摸两张牌"), ("decline", "不发动良助") })
        {
            if (branch == "help" && !_players[r.RecoveredSeat].IsAlive) continue;
            choices.Add(new(new($"sp-sun-shangxiang.{f.Id}.{branch}"), text, [], [], new Dictionary<string, string> {
                ["program-action"] = "sp-sun-shangxiang-liangzhu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch }));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishSpSunShangxiangLiangzhu(ProgramSkillFrame f)
    {
        var choices = SpSunShangxiangLiangzhuChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "良助：自己摸一张牌，或令刚于自己出牌阶段回复体力的角色摸两张牌。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("sp-sun-shangxiang-liangzhu")]
    private void AnswerSpSunShangxiangLiangzhu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SpSunShangxiangLiangzhu: { Stage: SpSunShangxiangLiangzhuStage.Choosing } r } f ||
            !ValidSpSunShangxiangLiangzhu(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !SpSunShangxiangLiangzhuChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Liangzhu lost its exact optional source-owner choice.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "decline") { FinishSpSunShangxiangLiangzhu(f); return; }
        var recipient = branch == "help" ? r.RecoveredSeat : f.OwnerSeat; var count = branch == "help" ? 2 : 1;
        ReplaceRuntimeTop(f = f with { SpSunShangxiangLiangzhu = r with { Stage = SpSunShangxiangLiangzhuStage.Drawing, Branch = branch, RecipientSeat = recipient, Requested = count } });
        AdvanceEventRulesAndQueueFact(new SpSunShangxiangLiangzhuChoiceEvent(f.Id, branch, recipient, count));
        BeginOperationProgramDraw(f, recipient, count, new("skill-program.ol:liangzhu.draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveSpSunShangxiangLiangzhu)]
    private void ReturnSpSunShangxiangLiangzhuDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!ValidSpSunShangxiangLiangzhu(f) || f.SpSunShangxiangLiangzhu is not { Stage: SpSunShangxiangLiangzhuStage.Drawing, Draw: null } r ||
            outcome.RecipientSeat != r.RecipientSeat || outcome.RequestedCount != r.Requested) throw new InvalidOperationException("Liangzhu must return its accepted actual native draw.");
        ReplaceRuntimeTop(f = f with { SpSunShangxiangLiangzhu = r with { Stage = SpSunShangxiangLiangzhuStage.Children, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new SpSunShangxiangLiangzhuDrawEvent(f.Id, outcome.RecipientSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId));
        if (r.Branch == "help") AdvanceEventRulesAndQueueFact(new SpSunShangxiangHelpedEvent(f.Id, f.OwnerSeat, r.RecoveredSeat, r.ChangeId));
        ContinueSpSunShangxiangLiangzhu(f.Id);
    }
    private void ContinueSpSunShangxiangLiangzhu(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { SpSunShangxiangLiangzhu: { } r } f || f.Id != id) return;
        if (!ValidSpSunShangxiangLiangzhu(f)) throw new InvalidOperationException("Liangzhu lost its original paid native draw cursor.");
        if (r.Stage == SpSunShangxiangLiangzhuStage.Choosing) { PublishSpSunShangxiangLiangzhu(f); return; }
        if (r.Stage == SpSunShangxiangLiangzhuStage.Drawing) return;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        FinishSpSunShangxiangLiangzhu(GetActiveProgramFrame(id));
    }
    private void FinishSpSunShangxiangLiangzhu(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new SpSunShangxiangLiangzhuCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { SpSunShangxiangLiangzhu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveSpSunShangxiangLiangzhu)]
    private PromptChoice AiSpSunShangxiangLiangzhu(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "help" &&
        AreProgramDistributionAllies(_players[f.OwnerSeat], _players[f.SpSunShangxiangLiangzhu!.RecoveredSeat])) ?? p.Choices.Single(c => c.Parameters["branch"] == "self");
}
