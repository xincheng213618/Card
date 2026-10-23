using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string AnniversaryHzLieqiong = "anniversary-god:lieqiong", AnniversaryHzZhanjue = "anniversary-god:zhanjue";
    private bool HasAnniversaryHuangZhongCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong);
    private static CardConversionSource AnniversaryHzSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private IEnumerable<AnniversaryHzBodyIssuedEvent> ActiveAnniversaryHzBody(int target, AnniversaryHzBody body) => !HasAnniversaryHuangZhongCapability ? [] :
        ProgramEventHistory<AnniversaryHzBodyIssuedEvent>().Where(e => e.Target == target && e.Body == body &&
            !ProgramEventHistory<AnniversaryHzStatusConsumedEvent>().Any(c => c.Id == e.Id) && !ProgramEventHistory<AnniversaryHzStatusExpiredEvent>().Any(c => c.Id == e.Id));
    private bool HasAnniversaryHzPendingInvalidation(int actor) => IsValidPlayerSeat(actor) && _players[actor].IsAlive && ActiveAnniversaryHzBody(actor, AnniversaryHzBody.Zhongshu).Any();
    private AnniversaryHzZhanjueIssuedEvent? ActiveAnniversaryHzZhanjue(int actor, int mode) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == actor &&
        !ProgramEventHistory<ActualPhaseEndedEvent>().Any(e => e.Phase == phase) ? ProgramEventHistory<AnniversaryHzZhanjueIssuedEvent>().LastOrDefault(e => e.Source.OwnerSeat == actor && e.Phase == phase && e.Mode == mode &&
            !ProgramEventHistory<AnniversaryHzZhanjueSpentEvent>().Any(c => c.Id == e.Id)) : null;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39600)]
    private bool CanTriggerAnniversaryHz(ProgramTriggerLegalityContext g)
    {
        if (g.SkillId == AnniversaryHzLieqiong) return g.WindowContext is { SourceSeat: var source, TargetSeat: { } target } && source == g.OwnerSeat && target != g.OwnerSeat && _players[target].IsAlive;
        if (g.SkillId != AnniversaryHzZhanjue) return true;
        if (g.Trigger.Window == SkillProgramTriggerWindow.PlayPhaseStarting) return g.Owner.HasHpValue;
        return AnniversaryHzRecoveryReservation(g.OwnerSeat, g.WindowContext?.DamageFrameId) is not null;
    }
    private AnniversaryHzZhanjueSpentEvent? AnniversaryHzRecoveryReservation(int actor, long? damageId) => damageId is null ? null :
        ProgramEventHistory<AnniversaryHzZhanjueSpentEvent>().LastOrDefault(c => c.Origin == damageId && c.Amount > 0 &&
            ProgramEventHistory<AnniversaryHzZhanjueIssuedEvent>().Any(e => e.Id == c.Id && e.Mode == 2 && e.Source.OwnerSeat == actor) &&
            !ProgramEventHistory<AnniversaryHzZhanjueRecoveredEvent>().Any(e => e.IssueId == c.Id));
    [ProgramCapability] private sealed class AnniversaryHzCapability(GameEngine engine) : IAnniversaryHzHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginAnniversaryHz(f, op); }
    private SkillProgramStepOutcome BeginAnniversaryHz(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryHuangZhong is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Huang Zhong requires his original unexecuted instruction.");
        var r = new AnniversaryHzReceipt(AnniversaryHzSource(f), f.GameplayHash, op, f.InstructionIndex, AnniversaryHzStage.Choose);
        if (op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong)
        {
            if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: var source, TargetSeat: { } target } || source != f.OwnerSeat || target == f.OwnerSeat || !_players[target].IsAlive)
                throw new InvalidOperationException("Lieqiong requires actual damage dealt to a living other participant.");
            r = r with { Target = target };
        }
        else if (op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue)
        {
            if (f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat)
                throw new InvalidOperationException("Zhanjue requires its actual Play phase start.");
            r = r with { Target = f.OwnerSeat, Phase = phase };
        }
        else
        {
            var reservation = AnniversaryHzRecoveryReservation(f.OwnerSeat, f.WindowContext?.DamageFrameId) ?? throw new InvalidOperationException("Zhanjue recovery lost its exact reserved first actual damage.");
            r = r with { Target = f.OwnerSeat, Stage = AnniversaryHzStage.Children, Amount = reservation.Amount, IssueId = reservation.Id, DamageId = reservation.Origin };
            ReplaceRuntimeTop(f = f with { AnniversaryHuangZhong = r });
            AdvanceEventRulesAndQueueFact(new AnniversaryHzZhanjueRecoveredEvent(f.Id, r.IssueId, r.DamageId, r.Amount));
            RecoverAnniversaryHz(f, r.Amount); ContinueAnniversaryHz(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        ReplaceRuntimeTop(f with { AnniversaryHuangZhong = r }); ContinueAnniversaryHz(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool AnniversaryHzAlreadyHit(int owner, int target) => ProgramEventHistory<AnniversaryHzBodyIssuedEvent>().Any(e => e.Source.OwnerSeat == owner && e.Target == target && e.TurnNumber == _turnNumber && e.TurnOwner == _currentSeat);
    private PromptChoice AnniversaryHzChoice(ProgramSkillFrame f, string option, string label) => new(new($"anniversary-hz.{f.Id}.{option}"), label, [], [],
        new Dictionary<string, string> { ["program-action"] = "anniversary-huang-zhong", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
    private IReadOnlyList<PromptChoice> AnniversaryHzChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryHuangZhong!;
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue) return [AnniversaryHzChoice(f, "slash", "摸体力值张牌，下张杀无距离限制且不可响应"), AnniversaryHzChoice(f, "recover", "摸损失体力值张牌，下次伤害后等量回复")];
        return Enum.GetValues<AnniversaryHzBody>().Where(b => b != AnniversaryHzBody.Tianchong || AnniversaryHzAlreadyHit(f.OwnerSeat, r.Target) && _players[r.Target].HasHpValue && _players[r.Target].Hp > 0)
            .Select(b => AnniversaryHzChoice(f, b.ToString(), b switch { AnniversaryHzBody.Lifeng => "力烽：随机弃置一半手牌", AnniversaryHzBody.Diji => "地机：下次受到伤害+1", AnniversaryHzBody.Zhongshu => "中枢：下张使用牌无效", AnniversaryHzBody.Qihai => "气海：禁用、禁打出红桃牌", _ => "天冲：失去所有体力值" })).ToArray();
    }
    private void PublishAnniversaryHz(ProgramSkillFrame f)
    {
        var choices = AnniversaryHzChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "请选择。", [], [], f.AnniversaryHuangZhong!.Target)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-huang-zhong")]
    private void AnswerAnniversaryHz(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.AnniversaryHuangZhong ?? throw new InvalidOperationException("Huang Zhong lost his original choice.");
        if (r.Stage != AnniversaryHzStage.Choose || _pendingDecision?.PlayerSeat != f.OwnerSeat || !AnniversaryHzChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Huang Zhong requires the exact published source choice.");
        ClearPendingDecision(); var option = choice.Parameters["option"];
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue)
        {
            var mode = option == "slash" ? 1 : 2; var amount = Math.Max(0, mode == 1 ? _players[f.OwnerSeat].Hp : _players[f.OwnerSeat].MaxHp - _players[f.OwnerSeat].Hp);
            r = r with { Mode = mode, Amount = amount, Stage = amount == 0 ? AnniversaryHzStage.Children : AnniversaryHzStage.Drawing };
            ReplaceRuntimeTop(f = f with { AnniversaryHuangZhong = r });
            AdvanceEventRulesAndQueueFact(new AnniversaryHzZhanjueIssuedEvent(f.Id, r.Source, r.Phase!, mode, amount));
            if (amount > 0) { BeginOperationProgramDraw(f, f.OwnerSeat, amount, new("skill.anniversary-huang-zhong.zhanjue.draw")); return; }
        }
        else
        {
            var body = Enum.Parse<AnniversaryHzBody>(option); var before = _movementSequence;
            r = r with { Body = body, Stage = body == AnniversaryHzBody.Tianchong ? AnniversaryHzStage.TianchongPayment : AnniversaryHzStage.Children, Before = before, After = before,
                Amount = body == AnniversaryHzBody.Tianchong ? _players[r.Target].Hp : 0 };
            ReplaceRuntimeTop(f = f with { AnniversaryHuangZhong = r });
            AdvanceEventRulesAndQueueFact(new AnniversaryHzBodyIssuedEvent(f.Id, r.Source, r.Target, body, _turnNumber, _currentSeat));
            AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【裂穹】，击伤 {_players[r.Target].Name} 的{option}。", f.OwnerSeat, r.Target);
            if (body == AnniversaryHzBody.Lifeng)
            {
                var hand = GetHand(_players[r.Target]);
                var available = hand.Where(c => !IsForeignEquipmentDiscardPrevented(r.Target, c, CardLocation.Hand(r.Target), OwnedCardMoveIntent.Discard)).ToList();
                var selected = new List<Card>(); var count = Math.Min((hand.Count + 1) / 2, available.Count);
                while (selected.Count < count) { var c = available[_random.Next(available.Count)]; selected.Add(c); available.Remove(c); }
                ReplaceRuntimeTop(f = f with { AnniversaryHuangZhong = r with { Cards = selected.Select(c => c.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                if (selected.Count > 0) MoveProgramLogicalDiscard(f, r.Target, selected.Select(c => c.Id).ToArray(), new("skill.anniversary-huang-zhong.lifeng"));
                ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { AnniversaryHuangZhong = f.AnniversaryHuangZhong! with { After = _movementSequence } });
            }
        }
        ContinueAnniversaryHz(f.Id);
    }
    private bool DrainAnniversaryHz(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueAnniversaryHz(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryHuangZhong: { } r } f && f.Id == id)
        {
            if (r.Stage == AnniversaryHzStage.Choose) { PublishAnniversaryHz(f); return; }
            if (r.Stage == AnniversaryHzStage.Drawing) return;
            if (r.Stage == AnniversaryHzStage.TianchongPayment)
            {
                var outcome = ApplyProgramHpLoss(id, f.SkillId, r.Target, r.Amount, (_, _, _) =>
                { var live = GetActiveProgramFrame(id); ReplaceRuntimeTop(live with { AnniversaryHuangZhong = live.AnniversaryHuangZhong! with { HpPaid = true, Stage = AnniversaryHzStage.TianchongChildren } }); });
                var dying = _resolutionStack.OfType<DyingFrame>().LastOrDefault(d => d.ParentFrameId == id && d.VictimSeat == r.Target);
                if (dying is not null)
                { var original = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == id); ReplaceRuntimeFrame(id, original with { AnniversaryHuangZhong = original.AnniversaryHuangZhong! with { DyingId = dying.Id } }); }
                if (outcome == SkillProgramStepOutcome.AwaitChild) return;
                continue;
            }
            if (DrainAnniversaryHz(id)) return;
            f = GetActiveProgramFrame(id); r = f.AnniversaryHuangZhong!;
            if (r.Stage == AnniversaryHzStage.TianchongChildren && r.HpPaid && r.DyingId > 0 && !_players[r.Target].IsAlive &&
                ProgramEventHistory<DyingResolvedEvent>().Any(e => e.ResolutionId == r.DyingId && e.VictimSeat == r.Target && !e.Survived))
            {
                ReplaceRuntimeTop(f = f with { AnniversaryHuangZhong = r with { Stage = AnniversaryHzStage.Children } });
                ChangeProgramMaximumHp(f, 1); continue;
            }
            ReplaceRuntimeTop(f with { AnniversaryHuangZhong = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(id); return;
        }
    }
    private void RecoverAnniversaryHz(ProgramSkillFrame f, int amount)
    {
        if (_players[f.OwnerSeat].IsAlive) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, amount, null, null);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue)]
    private void ReturnAnniversaryHzDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryHuangZhong!;
        if (r.Stage != AnniversaryHzStage.Drawing || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Amount || f.NativeDraw is not null) throw new InvalidOperationException("Zhanjue lost its exact native draw return.");
        ReplaceRuntimeTop(f with { AnniversaryHuangZhong = r with { Stage = AnniversaryHzStage.Children, Before = outcome.SequenceBefore, After = outcome.SequenceAfter } }); ContinueAnniversaryHz(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 39600)] private bool ResumeAnniversaryHz(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHuangZhong: not null } f || f.Id != id) return false; ContinueAnniversaryHz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39600)] private bool ReturnAnniversaryHzMovement(ProgramSkillFrame f) => f.AnniversaryHuangZhong is not null && ResumeAnniversaryHz(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 39600)] private bool AnniversaryHzMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.AnniversaryHuangZhong is { Body: AnniversaryHzBody.Lifeng, Stage: AnniversaryHzStage.Children } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39600)]
    private bool ContinueIssuedAnniversaryHz(ProgramSkillFrame f) => f.AnniversaryHuangZhong is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong)] private PromptChoice ChooseAnniversaryHzBody(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "Tianchong") ?? p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue)] private PromptChoice ChooseAnniversaryHzDraw(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39600)] private void AssertAnniversaryHz(ProgramSkillFrame f)
    {
        if (f.AnniversaryHuangZhong is not { } r) return;
        if (r.Source != AnniversaryHzSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) ||
            !IsValidPlayerSeat(r.Target) || r.Amount < 0 || r.After < r.Before || r.Cards.Distinct().Count() != r.Cards.Count || r.Mode is < 0 or > 2 || r.DyingId < 0 ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongLieqiong && (f.SkillId != AnniversaryHzLieqiong || r.Target == f.OwnerSeat) ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongZhanjue && (f.SkillId != AnniversaryHzZhanjue || r.Phase is null || r.Phase.ActorSeat != f.OwnerSeat) ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryHuangZhongDamageBenefit && (f.SkillId != AnniversaryHzZhanjue || r.IssueId <= 0 || r.DamageId != f.WindowContext?.DamageFrameId))
            throw new InvalidOperationException("Huang Zhong lost his original source, physical discard, paid HP, phase or typed native return.");
    }
}
