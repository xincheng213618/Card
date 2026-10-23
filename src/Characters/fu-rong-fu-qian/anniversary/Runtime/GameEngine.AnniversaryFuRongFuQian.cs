using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnniversaryFuPairAction = "anniversary-fu-rong-fu-qian";
    private static CardConversionSource AnniversaryFuPairSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static ProgramTriggerCandidate AnniversaryFuPairCandidate(ProgramSkillFrame f) => new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId,
        f.GameplayHash, 0, f.WindowContext?.OccurrenceIndex ?? 0);
    private bool HasAnniversaryFuPair => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei);
    private IEnumerable<AnniversaryXueweiGrant> ActiveAnniversaryXueweiGrants() => ProgramEventHistory<AnniversaryXueweiArmedEvent>().Select(e => e.Grant)
        .Where(g => !ProgramEventHistory<AnniversaryXueweiExpiredEvent>().Any(e => e.GrantFrameId == g.FrameId));

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44700)]
    private void ExpireAnniversaryXuewei(TurnStartedEvent fact)
    {
        if (!HasAnniversaryFuPair) return;
        foreach (var grant in ActiveAnniversaryXueweiGrants().Where(g => g.Source.OwnerSeat == fact.ActorSeat && g.ActualTurn < fact.TurnNumber).ToArray())
            AdvanceEventRulesAndQueueFact(new AnniversaryXueweiExpiredEvent(grant.FrameId, fact.TurnNumber));
    }

    // The ordinary collector already supplies live skill instances. This adds only
    // elemental-chain entries and the still-live issuer's durable, previously paid grant.
    private IEnumerable<BeforeDamageProgramCandidate> CollectAnniversaryFuRongFuQianXueweiCandidates(int sourceSeat, int targetSeat, bool rangeChainOnly)
    {
        if (!HasAnniversaryFuPair || !IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive ||
            ProgramEventHistory<AnniversaryXueweiArmedEvent>().Count == 0) yield break;
        var trigger = _contentRegistry.GetSkill(AnniversaryFuRongFuQianComposition.Xuewei).Program!.Triggers.Single(t => t.Id == AnniversaryFuRongFuQianComposition.Protect);
        foreach (var grant in ActiveAnniversaryXueweiGrants().Where(g => g.TargetSeat == targetSeat && IsValidPlayerSeat(g.Source.OwnerSeat) &&
                     _players[g.Source.OwnerSeat].IsAlive && g.Hash == _contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash)
                     .GroupBy(g => (g.Source.OwnerSeat, g.Source.SkillInstanceId)).Select(g => g.OrderByDescending(x => x.FrameId).First()))
        {
            var owner = _players[grant.Source.OwnerSeat];
            if (!rangeChainOnly && HasRuntimeSkillInstance(owner, grant.Source.SkillId, grant.Source.SkillInstanceId)) continue;
            var facts = CaptureProgramTriggerFacts(owner) with { EventTargetHp = UnambiguousHp(_players[targetSeat]) ?? 0,
                EventTargetHpUndefined = UnambiguousHp(_players[targetSeat]) is null };
            yield return new(new(owner.Seat, grant.Source.SkillId, trigger.Id, grant.Source.SkillInstanceId, grant.Hash, trigger.Priority), facts);
        }
    }
    private AnniversaryXueweiGrant? AnniversaryXueweiWitness(ProgramTriggerCandidate c, ProgramSkillWindowContext context, bool allowPrevented = false)
    {
        if (c.SkillId != AnniversaryFuRongFuQianComposition.Xuewei || c.BindingId != AnniversaryFuRongFuQianComposition.Protect ||
            context.Window != SkillProgramTriggerWindow.BeforeDamageApplied || context.OwnerSeat != c.OwnerSeat || context.TargetSeat is not { } target || context.Amount is not > 0 ||
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } parent ||
            parent.TargetSeat != target || parent.Amount != context.Amount || parent.RedirectedTargetSeat is not null || parent.Prevented && !allowPrevented ||
            context.SourceSeat != (BeforeDamageHasNoSource(parent) ? null : parent.SourceSeat) ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex].Candidate != c)
            return null;
        return ActiveAnniversaryXueweiGrants().Where(g => g.Source.OwnerSeat == c.OwnerSeat && g.Source.SkillId == c.SkillId &&
            g.Source.SkillInstanceId == c.SkillInstanceId && g.Hash == c.GameplayHash && g.TargetSeat == target && g.ActualTurn <= _turnNumber)
            .OrderByDescending(g => g.FrameId).FirstOrDefault();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 44700)]
    private bool AdmitAnniversaryXuewei(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        IsValidPlayerSeat(c.OwnerSeat) && _players[c.OwnerSeat].IsAlive && AnniversaryXueweiWitness(c, context) is not null;
    private bool CanArmAnniversaryXuewei(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        context.Window == SkillProgramTriggerWindow.TurnEnding && context.OwnerSeat == c.OwnerSeat && context.SourceSeat == c.OwnerSeat && c.OwnerSeat == _currentSeat &&
        _players[c.OwnerSeat].IsAlive && _players[c.OwnerSeat].HasHpValue &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is { } parent &&
        parent.OwnerSeat == c.OwnerSeat && parent.ItemIndex >= 0 && parent.ItemIndex < parent.Items.Count && parent.Items[parent.ItemIndex].Candidate == c;
    private bool CanOfferAnniversaryYuguan(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.AfterTurnEnded || !_players[c.OwnerSeat].IsAlive || !_players[c.OwnerSeat].HasHpValue ||
            _players[c.OwnerSeat].MaxHp <= 0 || _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } parent ||
            !IsActualAfterTurnEndedParent(parent) || AfterTurnEndedCandidate(parent) != c) return false;
        var ownerHp = UnambiguousHp(_players[c.OwnerSeat]);
        if (ownerHp is null) return false;
        var lost = Math.Max(0, _players[c.OwnerSeat].MaxHp - ownerHp.Value);
        return _players.Where(p => p.IsAlive && p.HasHpValue).All(p => UnambiguousHp(p) is { } hp && Math.Max(0, p.MaxHp - hp) <= lost);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44700,
        SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei, SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei, SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan)]
    private bool CanOfferAnniversaryFuPair(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context) => trigger.Effects.Single().Op switch
    {
        SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei => CanArmAnniversaryXuewei(c, context),
        SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei => _players[c.OwnerSeat].IsAlive && AnniversaryXueweiWitness(c, context) is not null,
        _ => CanOfferAnniversaryYuguan(c, context)
    };
    [ProgramCapability] private sealed class AnniversaryFuRongFuQianCapability(GameEngine engine) : IAnniversaryFuRongFuQianHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginAnniversaryFuPair(f, op); }

    private SkillProgramStepOutcome BeginAnniversaryFuPair(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryFuRongFuQian is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            !CanOfferAnniversaryFuPair(AnniversaryFuPairCandidate(f), GetProgramTrigger(f), context))
            throw new InvalidOperationException("Fu Rong Fu Qian lost his unpaid native window or original protection grant.");
        var grant = op == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei ? AnniversaryXueweiWitness(AnniversaryFuPairCandidate(f), context)! : null;
        var stage = op switch { SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei => AnniversaryFuRongFuQianStage.ArmChoice,
            SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei => AnniversaryFuRongFuQianStage.HpChildren, _ => AnniversaryFuRongFuQianStage.MaximumChildren };
        var r = new AnniversaryFuRongFuQianReceipt(AnniversaryFuPairSource(f), f.GameplayHash, op, context.ParentFrameId, _turnNumber, stage,
            grant, grant?.TargetSeat ?? -1, grant is null ? 0 : context.Amount, HpBefore: _players[f.OwnerSeat].Hp);
        ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryFuRongFuQianStartedEvent(f.Id, r.Source, r.Hash, op, r.ParentId, r.ActualTurn, grant?.FrameId ?? 0, r.ProtectedSeat, r.PreventedAmount));
        if (op == SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei) { PublishAnniversaryFuPair(f); return SkillProgramStepOutcome.AwaitChoice; }
        if (op == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei) PreventProgramCurrentDamage(f);
        else
        {
            var owner = _players[f.OwnerSeat]; var beforeMax = owner.MaxHp; var beforeHp = owner.Hp;
            if (beforeMax == 1)
            {
                owner.MaxHp = 0; owner.Hp = Math.Min(owner.Hp, 0);
                AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat, -1, 0, f.SkillId) { HpBeforeClamp = beforeHp });
            }
            else ChangeProgramMaximumHp(f, -1);
            var x = Math.Max(0, owner.MaxHp - owner.Hp); var targets = Math.Min(x, _players.Count(p => p.IsAlive));
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { AnniversaryFuRongFuQian = r with { MaximumBefore = beforeMax, MaximumAfter = owner.MaxHp,
                HpBeforeMaximum = beforeHp, HpAfterMaximum = owner.Hp, LostHp = x, TargetCount = targets } });
            r = f.AnniversaryFuRongFuQian!;
            AdvanceEventRulesAndQueueFact(new AnniversaryYuguanMaximumPaidEvent(f.Id, r.MaximumBefore, r.MaximumAfter, r.HpBeforeMaximum, r.HpAfterMaximum, r.LostHp, r.TargetCount));
            if (owner.MaxHp == 0) { BeginPlayerDeath(f.Id, owner, null, null, null, causingProgramSkillFrameId: f.Id); return SkillProgramStepOutcome.AwaitChild; }
        }
        ContinueAnniversaryFuPair(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private Dictionary<string, string> AnniversaryFuPairArgs(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = AnniversaryFuPairAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
    private IReadOnlyList<PromptChoice> AnniversaryFuPairChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryFuRongFuQian!;
        var choices = new List<PromptChoice>();
        if (r.Stage == AnniversaryFuRongFuQianStage.ArmChoice)
            foreach (var target in _players.Where(p => p.IsAlive && p.HasHpValue && UnambiguousHp(p) is { } hp && hp <= _players[f.OwnerSeat].Hp))
                choices.Add(new(new($"fu-pair.{f.Id}.protect.{target.Seat}"), $"保护{target.Name}，直到你的下回合开始。", [], [target.Seat], AnniversaryFuPairArgs(f, "protect")));
        else if (r.Stage == AnniversaryFuRongFuQianStage.TargetsChoice)
        {
            if (r.Targets.Count < r.TargetCount)
                foreach (var target in _players.Where(p => p.IsAlive && !r.Targets.Contains(p.Seat)))
                    choices.Add(new(new($"fu-pair.{f.Id}.refill.{r.Targets.Count}.{target.Seat}"), $"令{target.Name}将手牌摸至体力上限（已选{r.Targets.Count}/{r.TargetCount}）。", [], [target.Seat], AnniversaryFuPairArgs(f, "refill")));
            if (r.Targets.Count == r.TargetCount || choices.Count == 0)
                choices.Add(new(new($"fu-pair.{f.Id}.done"), "确定御关补牌角色。", [], [], AnniversaryFuPairArgs(f, "done")));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishAnniversaryFuPair(ProgramSkillFrame f)
    {
        var choices = AnniversaryFuPairChoices(f); if (choices.Count == 0) { FinishAnniversaryFuPair(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：选择角色。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = false, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(AnniversaryFuPairAction)] private void AnswerAnniversaryFuPair(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryFuRongFuQian: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AnniversaryFuPairChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Fu Rong Fu Qian requires his original current public target selection.");
        RequireAnniversaryFuPair(f); ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == AnniversaryFuRongFuQianStage.ArmChoice)
        {
            var target = choice.Targets.Single();
            AdvanceEventRulesAndQueueFact(new AnniversaryXueweiArmedEvent(new(f.Id, r.Source, r.Hash, r.ActualTurn, target, _players[f.OwnerSeat].Hp, _players[target].Hp)));
            FinishAnniversaryFuPair(f); return;
        }
        if (choice.Parameters.GetValueOrDefault("branch") == "done")
        { ReplaceRuntimeTop(f with { AnniversaryFuRongFuQian = r with { Stage = AnniversaryFuRongFuQianStage.DrawChildren } }); ContinueAnniversaryFuPair(f.Id); return; }
        var seat = choice.Targets.Single();
        ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r with { Targets = r.Targets.Append(seat).ToArray() } });
        AdvanceEventRulesAndQueueFact(new AnniversaryFuRongFuQianTargetChosenEvent(f.Id, r.Targets.Count, seat)); PublishAnniversaryFuPair(f);
    }

    private bool DrainAnniversaryFuPair(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueAnniversaryFuPair(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryFuRongFuQian: { } r } f && f.Id == id)
        {
            RequireAnniversaryFuPair(f);
            if (r.Stage == AnniversaryFuRongFuQianStage.Drawing) return;
            if (r.Stage is AnniversaryFuRongFuQianStage.ArmChoice or AnniversaryFuRongFuQianStage.TargetsChoice)
            { if (_pendingDecision is null) PublishAnniversaryFuPair(f); return; }
            if (r.Stage == AnniversaryFuRongFuQianStage.HpChildren && !r.HpPaid)
            {
                var result = ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1, (before, after, actual) =>
                {
                    var active = GetActiveProgramFrame(id); var receipt = active.AnniversaryFuRongFuQian!;
                    ReplaceRuntimeTop(active with { AnniversaryFuRongFuQian = receipt with { HpPaid = true, HpBefore = before, HpAfter = after, ActualLoss = actual } });
                    AdvanceEventRulesAndQueueFact(new AnniversaryXueweiHpPaidEvent(id, before, after, actual));
                });
                if (result == SkillProgramStepOutcome.AwaitChild) return;
                f = GetActiveProgramFrame(id); r = f.AnniversaryFuRongFuQian!;
            }
            if (DrainAnniversaryFuPair(id)) return;
            f = GetActiveProgramFrame(id); r = f.AnniversaryFuRongFuQian!;
            if (r.Stage == AnniversaryFuRongFuQianStage.HpChildren)
            {
                ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r with { Stage = AnniversaryFuRongFuQianStage.DrawChildren,
                    Targets = new[] { f.OwnerSeat, r.ProtectedSeat }.Distinct().ToArray() } }); r = f.AnniversaryFuRongFuQian!;
            }
            else if (r.Stage == AnniversaryFuRongFuQianStage.MaximumChildren)
            {
                if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || r.TargetCount == 0) { FinishAnniversaryFuPair(f); return; }
                ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r with { Stage = AnniversaryFuRongFuQianStage.TargetsChoice } });
                PublishAnniversaryFuPair(f); return;
            }
            else if (r.Stage == AnniversaryFuRongFuQianStage.DrawChildren && r.DrawRecipient >= 0)
            {
                ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r with { Cursor = r.Cursor + 1, DrawRecipient = -1, RequestedDraw = 0 } }); r = f.AnniversaryFuRongFuQian!;
            }
            if (_winner != Winner.None || r.Cursor >= r.Targets.Count) { FinishAnniversaryFuPair(f); return; }
            var recipient = _players[r.Targets[r.Cursor]];
            var requested = !recipient.IsAlive ? 0 : r.Operation == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei ? 1 : Math.Max(0, recipient.MaxHp - GetHand(recipient).Count);
            if (requested == 0)
            { ReplaceRuntimeTop(f with { AnniversaryFuRongFuQian = r with { Cursor = r.Cursor + 1 } }); continue; }
            ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r with { Stage = AnniversaryFuRongFuQianStage.Drawing, DrawRecipient = recipient.Seat, RequestedDraw = requested } });
            BeginOperationProgramDraw(f, recipient.Seat, requested, AnniversaryFuPairDrawReason(r.Operation)); return;
        }
    }
    private static CardMoveReason AnniversaryFuPairDrawReason(SkillProgramEffectOp operation) => new(operation == SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei
        ? "skill-program.anniversary:xuewei.draw" : "skill-program.anniversary:yuguan.draw");
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianXuewei, SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan)]
    private void ReturnAnniversaryFuPairDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id); RequireAnniversaryFuPair(f); var r = f.AnniversaryFuRongFuQian!;
        if (r.Stage != AnniversaryFuRongFuQianStage.Drawing || f.NativeDraw is not null || draw.RecipientSeat != r.DrawRecipient || draw.RequestedCount != r.RequestedDraw ||
            r.Draws.Any(d => d.Index == r.Cursor) || draw.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Fu Rong Fu Qian lost his exact once-issued native draw return.");
        ReplaceRuntimeTop(f = f with { AnniversaryFuRongFuQian = r with { Stage = AnniversaryFuRongFuQianStage.DrawChildren,
            Draws = r.Draws.Append(new(r.Cursor, r.DrawRecipient, r.RequestedDraw, draw)).ToArray() } });
        AdvanceEventRulesAndQueueFact(new AnniversaryFuRongFuQianDrawReturnedEvent(f.Id, r.Cursor, draw.RecipientSeat, draw.RequestedCount,
            draw.ActualDrawIds.Count, draw.AttemptFrameId, draw.SequenceBefore, draw.SequenceAfter));
        ContinueAnniversaryFuPair(f.Id);
    }
    private void FinishAnniversaryFuPair(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new AnniversaryFuRongFuQianCompletedEvent(f.Id)); ReplaceRuntimeTop(f with { AnniversaryFuRongFuQian = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 44700)] private bool ResumeAnniversaryFuPair(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryFuRongFuQian: not null } f || f.Id != id) return false; ContinueAnniversaryFuPair(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44700)] private bool ReturnAnniversaryFuPairMovement(ProgramSkillFrame f) => f.AnniversaryFuRongFuQian is not null && ResumeAnniversaryFuPair(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ArmAnniversaryFuRongFuQianXuewei)] private PromptChoice AiAnniversaryXuewei(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.OrderBy(c => c.Targets.Single() == f.OwnerSeat ? 0 : 1).ThenBy(c => _players[c.Targets.Single()].Hp).First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryFuRongFuQianYuguan)] private PromptChoice AiAnniversaryYuguan(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.OrderBy(c => c.Targets.Count == 0 || c.Targets.Single() == f.OwnerSeat ? 0 : 1)
            .ThenByDescending(c => c.Targets.Count == 0 ? 0 : _players[c.Targets.Single()].MaxHp - GetHand(_players[c.Targets.Single()]).Count).First();
}
