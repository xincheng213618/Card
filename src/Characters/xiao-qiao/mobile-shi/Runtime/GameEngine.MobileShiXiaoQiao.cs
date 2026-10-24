using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShiXqHeyun = "mobile:shi-heyun", ShiXqYinhui = "mobile:shi-yinhui", ShiXqDrawReason = "skill.mobile-shi-xiao-qiao.heyun.draw";
    private static CardConversionSource ShiXqSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static string ShiXqGrantSource(int owner, long frame) => $"acquired:mobile:shi-yinhui:{owner}:{frame}";
    private string[] ShiXqVisibleOwnedSkills(int viewer, int seat)
    {
        var player = _players[seat]; var visible = new List<GeneralSkillDefinition>();
        if (player.IdentityConcealed && viewer != seat) return [];
        if (player.GeneralSelected && (player.GeneralRevealed || viewer == seat)) visible.AddRange(OwnedRuntimeSkills(player, player.General, includeAcquired: true));
        if (player.SecondaryGeneral is { } second && player.SecondaryGeneralSelected && (player.SecondaryGeneralRevealed || viewer == seat)) visible.AddRange(OwnedRuntimeSkills(player, second, includeAcquired: false));
        var owned = GetSkillBindingShard(player).ActiveGrants.Select(g => g.SkillId).ToHashSet(StringComparer.Ordinal);
        return visible.Where(s => _modeDefinition.WenheActivity is null || IsWenhePlayerSkillVisible(player, s, viewer, false)).Select(s => s.ContentId).OfType<string>()
            .Where(owned.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private string[] ShiXqLosableSkills(int owner)
    {
        var grants = GetSkillBindingShard(_players[owner]).ActiveGrants;
        return ShiXqVisibleOwnedSkills(owner, owner).Where(id => grants.Any(g => g.SkillId == id && !g.IsModeRule) && !grants.Any(g => g.SkillId == id && g.IsModeRule)).ToArray();
    }
    private int[] ShiXqHeyunTargets(int owner)
    {
        if (ShiXqLosableSkills(owner).Length == 0) return [];
        var own = ShiXqVisibleOwnedSkills(owner, owner).ToHashSet(StringComparer.Ordinal);
        return _players.Where(p => p.IsAlive && ShiXqVisibleOwnedSkills(owner, p.Seat).Any(own.Contains)).Select(p => p.Seat).ToArray();
    }
    private (int Seat, string Skill)[] ShiXqCopyOptions(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner)
        .SelectMany(p => ShiXqVisibleOwnedSkills(owner, p.Seat).Select(id => (p.Seat, Skill: id))).ToArray();
    private ShiXqCopiedEvent[] ShiXqGrantLedger(int owner) => ProgramEventHistory<ShiXqCopiedEvent>().Where(e => e.Source.OwnerSeat == owner && e.Source.SkillId == ShiXqYinhui &&
        e.GrantSourceId == ShiXqGrantSource(owner, e.FrameId) && e.GrantId == e.GrantSourceId + ":" + e.SkillId).ToArray();
    private ShiXqCopiedEvent[] ShiXqLiveCopies(int owner) => ShiXqGrantLedger(owner).Where(e => _players[owner].SkillGrants.Grants.Any(g => g.GrantId == e.GrantId && g.SourceId == e.GrantSourceId && g.SkillId == e.SkillId && g.SkillInstanceId == e.SkillInstanceId)).ToArray();
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 50400, SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun)]
    private bool CanActivateShiXqHeyun(CharacterState owner) => ShiXqHeyunTargets(owner.Seat).Length != 0;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 50400)]
    private bool CanOfferShiXqYinhui(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) => c.SkillId != ShiXqYinhui ||
        context.Window == SkillProgramTriggerWindow.RoundStarting && _players[c.OwnerSeat].IsAlive && ShiXqCopyOptions(c.OwnerSeat).Length != 0;
    [ProgramCapability] private sealed class ShiXqHost(GameEngine engine) : IShiXqHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp operation) => engine.BeginShiXq(f, operation); }
    private SkillProgramStepOutcome BeginShiXq(ProgramSkillFrame input, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(input.Id); var copying = operation == SkillProgramEffectOp.ResolveMobileShiXiaoQiaoYinhui;
        if (f.ShiXiaoQiao is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.SkillId != (copying ? ShiXqYinhui : ShiXqHeyun) || copying && (f.TriggerId != "copy-current-skill" || f.WindowContext?.Window != SkillProgramTriggerWindow.RoundStarting) ||
            !copying && (f.TriggerId is not null || f.ActivationId != "lose-for-draw" || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play))
            throw new InvalidOperationException("Shi Xiao Qiao requires its original round-start or twice-per-Play skill-change instruction.");
        var r = new ShiXqReceipt(ShiXqSource(f), f.GameplayHash, _turnNumber, _roundNumber, f.WindowContext?.ParentFrameId ?? f.ParentFrameId ?? 0,
            operation, copying ? ShiXqStage.RevokeChildren : ShiXqStage.Target) { Before = _movementSequence, After = _movementSequence, Paid = copying };
        if (copying) r = r with { RevokeIds = ShiXqGrantLedger(f.OwnerSeat).Where(e => !ProgramEventHistory<ShiXqGrantClearedEvent>().Any(c => c.Owner == f.OwnerSeat && c.GrantId == e.GrantId)).Select(e => e.GrantId).Distinct(StringComparer.Ordinal).ToArray() };
        ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r });
        AdvanceEventRulesAndQueueFact(new ShiXqStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.Round, r.ParentId, operation));
        if (!copying) { PublishShiXq(f); return SkillProgramStepOutcome.AwaitChoice; }
        var removed = RevokeOwnedProgramSkillSources(f, r.RevokeIds);
        foreach (var id in r.RevokeIds)
        {
            var old = ShiXqGrantLedger(f.OwnerSeat).Single(e => e.GrantId == id);
            AdvanceEventRulesAndQueueFact(new ShiXqGrantClearedEvent(f.Id, f.OwnerSeat, id, old.SkillId, removed.Any(g => g.GrantId == id)));
        }
        PublishShiXqSkillMutation(f.Id); ContinueShiXq(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> ShiXqChoices(ProgramSkillFrame f)
    {
        var r = f.ShiXiaoQiao!; var choices = new List<PromptChoice>();
        void Add(string branch, string key, string label, int? target = null, string? skill = null)
        {
            var parameters = new Dictionary<string, string> { ["program-action"] = "mobile-shi-xiao-qiao", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
            if (skill is not null) parameters["skill-id"] = skill;
            choices.Add(new(new($"shi-xiao-qiao.{f.Id}.{branch}.{key}"), label, [], target is { } seat ? [seat] : [], parameters));
        }
        if (r.Stage == ShiXqStage.Target)
            foreach (var seat in ShiXqHeyunTargets(f.OwnerSeat)) Add("target", seat.ToString(CultureInfo.InvariantCulture), "令" + _players[seat].Name + "摸两张牌", seat);
        if (r.Stage == ShiXqStage.LoseSkill && _players[r.Target].IsAlive && ShiXqHeyunTargets(f.OwnerSeat).Contains(r.Target))
            foreach (var id in ShiXqLosableSkills(f.OwnerSeat)) Add("lose", id, "失去【" + _contentRegistry.GetSkill(id).Name + "】，令" + _players[r.Target].Name + "摸两张牌", skill: id);
        if (r.Stage == ShiXqStage.CopyTarget)
            foreach (var seat in ShiXqCopyOptions(f.OwnerSeat).Select(o => o.Seat).Distinct()) Add("donor", seat.ToString(CultureInfo.InvariantCulture), "获得" + _players[seat].Name + "当前拥有的一个技能", seat);
        if (r.Stage == ShiXqStage.CopySkill)
            foreach (var option in ShiXqCopyOptions(f.OwnerSeat).Where(o => o.Seat == r.Target)) Add("copy", option.Skill, "获得【" + _contentRegistry.GetSkill(option.Skill).Name + "】", r.Target, option.Skill);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishShiXq(ProgramSkillFrame f)
    {
        var choices = ShiXqChoices(f); if (choices.Count == 0) { FinishShiXq(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.ShiXiaoQiao!.Stage switch
        {
            ShiXqStage.Target => "合韵：选择一名与你有相同技能的角色，可以选择自己。",
            ShiXqStage.LoseSkill => "合韵：选择自己失去的一个技能；随后原目标摸两张牌。",
            ShiXqStage.CopyTarget => "音洄：已经清除此前因此获得的来源，选择一名其他角色。",
            _ => "音洄：选择其当前拥有的一个技能。"
        }, [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), TargetSeat = f.OwnerSeat, Choices = choices, IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mobile-shi-xiao-qiao")]
    private void AnswerShiXq(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXiaoQiao: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiXqChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Shi Xiao Qiao lost its exact current common-skill target, lost skill or public current donor skill.");
        RequireShiXq(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage is ShiXqStage.Target or ShiXqStage.CopyTarget)
        {
            ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r with { Target = selected.Targets.Single(), Stage = r.Stage == ShiXqStage.Target ? ShiXqStage.LoseSkill : ShiXqStage.CopySkill } });
            PublishShiXq(f); AdvanceRulesAndPublishState(); return;
        }
        var id = selected.Parameters["skill-id"];
        if (r.Stage == ShiXqStage.LoseSkill)
        {
            foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
            ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r with { SelectedSkill = id, Paid = true, Stage = ShiXqStage.LossChildren, Before = _movementSequence, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new ShiXqHeyunPaidEvent(f.Id, r.Source, r.Target, id));
            ReplaceProgramSkills(f, [id], [], ProgramSkillSourceRemoval.NonModePreserveProjections, acquireFirst: false);
            PublishShiXqSkillMutation(f.Id); ContinueShiXq(f.Id); AdvanceRulesAndPublishState(); return;
        }
        var sourceId = ShiXqGrantSource(f.OwnerSeat, f.Id);
        ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r with { SelectedSkill = id, Paid = true, Stage = ShiXqStage.GrantChildren, Before = _movementSequence, After = _movementSequence } });
        var grant = GrantOwnedProgramSkillSource(f, sourceId, id);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShiXiaoQiao = f.ShiXiaoQiao! with { GrantId = grant.GrantId, GrantSource = grant.SourceId, GrantedInstance = grant.SkillInstanceId } });
        AdvanceEventRulesAndQueueFact(new ShiXqCopiedEvent(f.Id, r.Source, r.Hash, r.Round, r.Target, id, grant.GrantId, grant.SourceId, grant.SkillInstanceId));
        PublishShiXqSkillMutation(f.Id); ContinueShiXq(f.Id); AdvanceRulesAndPublishState();
    }
    private void PublishShiXqSkillMutation(long id)
    {
        var f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceRulesAndPublishState();
        f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { ShiXiaoQiao = f.ShiXiaoQiao! with { After = _movementSequence } });
    }
    private bool DrainShiXq(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueShiXq(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXiaoQiao: { } r } f || f.Id != id || _pendingDecision is not null || r.Stage == ShiXqStage.Drawing) return;
        RequireShiXq(f);
        if (r.Stage is ShiXqStage.LossChildren or ShiXqStage.RevokeChildren or ShiXqStage.GrantChildren or ShiXqStage.DrawChildren)
        {
            if (DrainShiXq(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShiXiaoQiao!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == ShiXqStage.RevokeChildren)
            {
                if (!_players[f.OwnerSeat].IsAlive) { FinishShiXq(f); return; }
                ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r with { Stage = ShiXqStage.CopyTarget } }); PublishShiXq(f); return;
            }
            if (r.Stage == ShiXqStage.LossChildren)
            {
                if (!_players[r.Target].IsAlive) { FinishShiXq(f); return; }
                ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r with { Stage = ShiXqStage.Drawing } });
                BeginOperationProgramDraw(f, r.Target, 2, new(ShiXqDrawReason)); return;
            }
            FinishShiXq(f); return;
        }
        PublishShiXq(f);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun)]
    private void ReturnShiXqDraw(ProgramSkillFrame input, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(input.Id); var r = f.ShiXiaoQiao!;
        if (r.Stage != ShiXqStage.Drawing || r.Draw is not null || !r.Paid || outcome.RecipientSeat != r.Target || outcome.RequestedCount != 2)
            throw new InvalidOperationException("Heyun lost its once-paid skill loss or original two-card native draw.");
        ReplaceRuntimeTop(f = f with { ShiXiaoQiao = r with { Stage = ShiXqStage.DrawChildren, Draw = outcome, Before = checked((int)outcome.SequenceBefore), After = checked((int)outcome.SequenceAfter) }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiXqDrawPaidEvent(f.Id, r.Target, outcome.AttemptFrameId, outcome.ActualDrawIds.Count, outcome.Replacement is not null, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueShiXq(f.Id);
    }
    private void FinishShiXq(ProgramSkillFrame f)
    {
        if (f.ShiXiaoQiao!.Stage == ShiXqStage.Complete) return;
        ReplaceRuntimeTop(f = f with { ShiXiaoQiao = f.ShiXiaoQiao with { Stage = ShiXqStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new ShiXqCompletedEvent(f.Id, f.ShiXiaoQiao.Operation)); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 50400)] private bool ResumeShiXq(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXiaoQiao: not null } f || f.Id != id) return false; ContinueShiXq(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 50400)] private bool ReturnShiXqMovement(ProgramSkillFrame f)
    { if (f.ShiXiaoQiao is null) return false; ContinueShiXq(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiXiaoQiaoHeyun, SkillProgramEffectOp.ResolveMobileShiXiaoQiaoYinhui)]
    private PromptChoice ChooseShiXq(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.ShiXiaoQiao!.Stage == ShiXqStage.Target) return p.Choices.FirstOrDefault(c => c.Targets.SequenceEqual([f.OwnerSeat])) ?? p.Choices[0];
        if (f.ShiXiaoQiao.Stage == ShiXqStage.LoseSkill) return p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("skill-id") != ShiXqHeyun && c.Parameters.GetValueOrDefault("skill-id") != ShiXqYinhui) ?? p.Choices[0];
        return p.Choices[0];
    }
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiXiaoQiaoPublicStates(CharacterState owner, string id)
    {
        if (id is not (ShiXqHeyun or ShiXqYinhui)) return [];
        var copies = ShiXqLiveCopies(owner.Seat);
        return GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == id).SelectMany(i => id == ShiXqHeyun
            ? new[] { new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "heyun-uses", ProgramPublicRuleStateKind.ActivationLimit, 2, _programPhaseUses.GetValueOrDefault((owner.Seat, ShiXqHeyun, "lose-for-draw")), SkillUsageScope.Phase) }
            : new[] { new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "yinhui-grants", ProgramPublicRuleStateKind.GameActivationProgress, copies.Length) }
                .Concat(copies.Select(c => new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "yinhui-skill:" + c.SkillId, ProgramPublicRuleStateKind.GameActivationProgress, 1)))).ToArray();
    }
}
