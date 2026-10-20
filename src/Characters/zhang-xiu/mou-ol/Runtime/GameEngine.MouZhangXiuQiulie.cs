using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouZhangXiuOlCostReason = "skill-program.mou-ol:qiulie.cost", MouZhangXiuOlDefenseReason = "skill-program.mou-ol:qiulie.defense",
        MouZhangXiuOlDrawReason = "skill-program.mou-ol:zhuijiao.draw", MouZhangXiuOlPenaltyReason = "skill-program.mou-ol:zhuijiao.discard";
    private static CardConversionSource MouZhangXiuOlSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasMouZhangXiuOl => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao);
    private MouZhangXiuOlHuntChosenEvent? MouZhangXiuOlHunt(int owner) => ProgramEventHistory<MouZhangXiuOlHuntChosenEvent>().SingleOrDefault(e => e.Source.OwnerSeat == owner && e.ActualTurn == _turnNumber);
    private bool CanChooseMouZhangXiuOlHunt(int owner) => _players[owner].IsAlive && owner == _currentSeat && _winner == Winner.None &&
        !ProgramEventHistory<MouZhangXiuOlHuntChosenEvent>().Any(e => e.Source.OwnerSeat == owner) && _players.Any(p => p.IsAlive && p.Seat != owner);
    private ActualDiscardRecoveryPhaseKey? MouZhangXiuOlCurrentPhase(int owner, SkillProgramTriggerWindow window) =>
        CurrentActualDiscardRecoveryPhase() is { } p && p.ActorSeat == owner && p.ActualTurnOwnerSeat == owner && p.TurnNumber == _turnNumber &&
        p.Kind == MouZhangXiuOlComposition.Phase(window) ? p : null;
    private bool MouZhangXiuOlPhaseAlreadyOffered(int owner, ActualDiscardRecoveryPhaseKey p) => ProgramEventHistory<MouZhangXiuOlPhaseOfferedEvent>().Any(e => e.Source.OwnerSeat == owner && e.Phase.Token == p.Token);
    private bool CanUseMouZhangXiuOlHunt(int owner, int target) => _winner == Winner.None && IsValidPlayerSeat(target) && owner != target && _players[owner].IsAlive && _players[target].IsAlive &&
        CanUseVirtualSlashTarget(_players[owner], _players[target], CardKind.Slash, Suit.None, effectiveColor: null, effectiveRank: 0, physicalCardIds: []) &&
        !IsDirectedCardTargetProhibited(owner, target, CardKind.Slash) && !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null);
    private Card[] MouZhangXiuOlDiscardable(int owner, bool defense = false) => GetHand(_players[owner]).Concat(GetEquipment(owner)).Where(c =>
        !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) && !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        (!defense || GetProgramCardCategory(AdvancedEffectiveHandKind(_players[owner], c)) == SkillProgramCardCategory.Basic || EquipmentCatalog.IsEquipment(c.Kind) && EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Weapon)).ToArray();
    [ProgramCapability] private sealed class MouZhangXiuOlCapability(GameEngine engine) : IMouZhangXiuOlHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveMouZhangXiuQiulie or SkillProgramEffectOp.UseMouZhangXiuQiulieSlash => engine.BeginMouZhangXiuOlQiulie(f, op),
            SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash => engine.BeginMouZhangXiuOlDefense(f),
            _ => engine.BeginMouZhangXiuOlZhuijiao(f, op)
        };
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 33000, SkillProgramEffectOp.ResolveMouZhangXiuQiulie, SkillProgramEffectOp.UseMouZhangXiuQiulieSlash)]
    private bool QualifyMouZhangXiuOlPhase(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (MouZhangXiuOlCurrentPhase(c.OwnerSeat, w.Window) is not { } p || MouZhangXiuOlPhaseAlreadyOffered(c.OwnerSeat, p)) return false;
        if (t.Effects[0].Op == SkillProgramEffectOp.ResolveMouZhangXiuQiulie) return CanChooseMouZhangXiuOlHunt(c.OwnerSeat);
        return MouZhangXiuOlHunt(c.OwnerSeat) is { } hunt && CanUseMouZhangXiuOlHunt(c.OwnerSeat, hunt.TargetSeat) && MouZhangXiuOlDiscardable(c.OwnerSeat).Length > 0;
    }
    private SkillProgramStepOutcome BeginMouZhangXiuOlQiulie(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouZhangXiuOl is not null || f.InstructionIndex != 1 || f.ActivationId != f.TriggerId || f.WindowContext is not { } w ||
            MouZhangXiuOlCurrentPhase(f.OwnerSeat, w.Window) is not { } phase || MouZhangXiuOlPhaseAlreadyOffered(f.OwnerSeat, phase)) throw new InvalidOperationException("Qiulie requires its current actual phase and untouched opportunity.");
        var target = operation == SkillProgramEffectOp.ResolveMouZhangXiuQiulie ? -1 : MouZhangXiuOlHunt(f.OwnerSeat)?.TargetSeat ?? -1;
        if (operation == SkillProgramEffectOp.ResolveMouZhangXiuQiulie ? !CanChooseMouZhangXiuOlHunt(f.OwnerSeat) : !CanUseMouZhangXiuOlHunt(f.OwnerSeat, target)) throw new InvalidOperationException("Qiulie cannot fabricate its limited target or a legal native Slash.");
        var r = new MouZhangXiuOlReceipt(MouZhangXiuOlSource(f), f.GameplayHash, operation, target < 0 ? MouZhangXiuOlStage.Target : MouZhangXiuOlStage.Cost) { Phase = phase, TargetSeat = target };
        ReplaceRuntimeTop(f = f with { MouZhangXiuOl = r }); AdvanceEventRulesAndQueueFact(new MouZhangXiuOlStartedEvent(f.Id, r.Source, r.GameplayHash, operation, 0));
        if (target >= 0) AdvanceEventRulesAndQueueFact(new MouZhangXiuOlPhaseOfferedEvent(f.Id, r.Source, phase, target));
        PublishMouZhangXiuOl(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int MouZhangXiuOlChooser(ProgramSkillFrame f) => f.MouZhangXiuOl!.Stage == MouZhangXiuOlStage.Defense ? f.MouZhangXiuOl.TargetSeat : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> MouZhangXiuOlChoices(ProgramSkillFrame f)
    {
        var r = f.MouZhangXiuOl!; var result = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, int? card = null, int? target = null) => new(new($"mou-zhang-xiu.{f.Id}.{r.Stage}.{branch}.{card}.{target}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "mou-zhang-xiu-ol", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage == MouZhangXiuOlStage.Target)
        {
            result.AddRange(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => Choice("target", $"本回合追猎{p.Name}", target: p.Seat)));
            result.Add(Choice("decline", "不发动仇猎"));
        }
        else if (r.Stage is MouZhangXiuOlStage.Cost or MouZhangXiuOlStage.Defense or MouZhangXiuOlStage.Penalty)
        {
            var defense = r.Stage == MouZhangXiuOlStage.Defense; var payer = defense ? r.TargetSeat : f.OwnerSeat;
            if (_players[payer].IsAlive && (r.Stage != MouZhangXiuOlStage.Cost || CanUseMouZhangXiuOlHunt(f.OwnerSeat, r.TargetSeat)))
                result.AddRange(MouZhangXiuOlDiscardable(payer, defense).Select(c => Choice("card", $"弃置【{c.DisplayName}】" + (defense ? "，令此【杀】无效" : ""), c.Id)));
            if (r.Stage != MouZhangXiuOlStage.Penalty) result.Add(Choice("decline", defense ? "不弃牌，继续结算此【杀】" : "不弃牌使用【杀】"));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishMouZhangXiuOl(ProgramSkillFrame f)
    {
        var r = f.MouZhangXiuOl!; var choices = MouZhangXiuOlChoices(f); if (choices.Count == 0) { FinishMouZhangXiuOl(f); return; }
        var chooser = MouZhangXiuOlChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage switch { MouZhangXiuOlStage.Target => "仇猎：选择本回合追猎目标。", MouZhangXiuOlStage.Defense => "可弃置一张基本牌或武器牌，令仇猎的【杀】无效。", MouZhangXiuOlStage.Penalty => "追剿：此【杀】未造成伤害，弃置一张牌。", _ => "仇猎：可弃置一张牌，视为对原目标使用【杀】。" },
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-zhang-xiu-ol")]
    private void AnswerMouZhangXiuOl(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhangXiuOl: { } r } f || !ValidMouZhangXiuOl(f) || _pendingDecision?.PlayerSeat != MouZhangXiuOlChooser(f) ||
            !MouZhangXiuOlChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Mou Zhang Xiu accepts only the original current chooser and live published cost.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"]; if (branch == "decline") { FinishMouZhangXiuOl(f); return; }
        if (r.Stage == MouZhangXiuOlStage.Target)
        {
            if (!CanChooseMouZhangXiuOlHunt(f.OwnerSeat)) throw new InvalidOperationException("Qiulie's limited use was already consumed.");
            var target = choice.Targets.Single(); AdvanceEventRulesAndQueueFact(new MouZhangXiuOlHuntChosenEvent(f.Id, r.Source, r.GameplayHash, r.Phase!.TurnNumber, target));
            AdvanceEventRulesAndQueueFact(new MouZhangXiuOlPhaseOfferedEvent(f.Id, r.Source, r.Phase, target));
            ReplaceRuntimeTop(f = f with { MouZhangXiuOl = r with { TargetSeat = target, Stage = MouZhangXiuOlStage.Cost } });
            if (!CanUseMouZhangXiuOlHunt(f.OwnerSeat, target) || MouZhangXiuOlDiscardable(f.OwnerSeat).Length == 0) FinishMouZhangXiuOl(f); else PublishMouZhangXiuOl(f); return;
        }
        if (branch != "card" || r.Stage is not (MouZhangXiuOlStage.Cost or MouZhangXiuOlStage.Defense or MouZhangXiuOlStage.Penalty)) throw new InvalidOperationException("Mou Zhang Xiu cannot pay at another cursor.");
        var defense = r.Stage == MouZhangXiuOlStage.Defense; var payer = defense ? r.TargetSeat : f.OwnerSeat; var cardId = choice.Cards.Single();
        if (!MouZhangXiuOlDiscardable(payer, defense).Any(c => c.Id == cardId) || r.Stage == MouZhangXiuOlStage.Cost && !CanUseMouZhangXiuOlHunt(f.OwnerSeat, r.TargetSeat)) throw new InvalidOperationException("Qiulie cannot pay an unavailable entity or an illegal Slash.");
        var stage = defense ? MouZhangXiuOlStage.DefenseChildren : r.Stage == MouZhangXiuOlStage.Penalty ? MouZhangXiuOlStage.PenaltyChildren : MouZhangXiuOlStage.CostChildren;
        var before = _movementSequence; ReplaceRuntimeTop(f = f with { MouZhangXiuOl = r with { Stage = stage, PaidCardId = cardId, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, payer, [cardId], new(defense ? MouZhangXiuOlDefenseReason : stage == MouZhangXiuOlStage.PenaltyChildren ? MouZhangXiuOlPenaltyReason : MouZhangXiuOlCostReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouZhangXiuOl = f.MouZhangXiuOl! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouZhangXiuOlDiscardPaidEvent(f.Id, payer, cardId, before, _movementSequence, defense)); ContinueMouZhangXiuOl(f.Id);
    }
    private bool DrainMouZhangXiuOl(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueMouZhangXiuOl(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhangXiuOl: { } r } f || f.Id != id) return;
        if (!ValidMouZhangXiuOl(f)) throw new InvalidOperationException("Mou Zhang Xiu lost its source, phase or paid native return.");
        if (r.Stage is MouZhangXiuOlStage.Target or MouZhangXiuOlStage.Cost or MouZhangXiuOlStage.Defense or MouZhangXiuOlStage.Penalty) { PublishMouZhangXiuOl(f); return; }
        if (r.Stage is MouZhangXiuOlStage.Drawing or MouZhangXiuOlStage.Issuing or MouZhangXiuOlStage.NativeUse) return;
        if (DrainMouZhangXiuOl(id)) return; f = GetActiveProgramFrame(id); r = f.MouZhangXiuOl!;
        if (r.Stage == MouZhangXiuOlStage.CostChildren) { IssueMouZhangXiuOlSlash(f); return; }
        if (r.Stage == MouZhangXiuOlStage.DefenseChildren && LifecycleCardUse(r.UseId) is { } use)
        {
            foreach (var target in use.TargetSeats) MarkCardEffectIneffective(use.Id, target);
            AdvanceEventRulesAndQueueFact(new MouZhangXiuOlQiulieNullifiedEvent(f.Id, use.Id, r.TargetSeat, r.PaidCardId!.Value));
        }
        if (r.Stage == MouZhangXiuOlStage.DrawChildren) SetMouZhangXiuOlEntry(r.UseId, r.Source, e => e with { DrawReturned = true });
        if (r.Operation == SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao) SetMouZhangXiuOlEntry(r.UseId, r.Source, e => e with { PenaltyReturned = true });
        FinishMouZhangXiuOl(f);
    }
    private void FinishMouZhangXiuOl(ProgramSkillFrame f)
    {
        var r = f.MouZhangXiuOl!; if (r.Operation == SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao && LifecycleCardUse(r.UseId) is not null) SetMouZhangXiuOlEntry(r.UseId, r.Source, e => e with { PenaltyReturned = true });
        AdvanceEventRulesAndQueueFact(new MouZhangXiuOlCompletedEvent(f.Id, r.Operation)); ReplaceRuntimeTop(f = f with { MouZhangXiuOl = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 33000)]
    private bool ResumeMouZhangXiuOl(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhangXiuOl: not null } f || f.Id != id) return false;
        ContinueMouZhangXiuOl(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33000)] private bool ReturnMouZhangXiuOlMovement(ProgramSkillFrame f) => f.MouZhangXiuOl is not null && ResumeMouZhangXiuOl(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouZhangXiuQiulie, SkillProgramEffectOp.UseMouZhangXiuQiulieSlash, SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash, SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao)]
    private PromptChoice AiMouZhangXiuOl(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.MouZhangXiuOl!.Stage == MouZhangXiuOlStage.Target) return p.Choices.FirstOrDefault(c => c.Targets is [var target] && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[target])) ?? p.Choices.Single(c => c.Parameters["branch"] == "decline");
        if (f.MouZhangXiuOl.Stage == MouZhangXiuOlStage.Cost && AreProgramDistributionAllies(_players[f.OwnerSeat], _players[f.MouZhangXiuOl.TargetSeat])) return p.Choices.Single(c => c.Parameters["branch"] == "decline");
        return p.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat])).FirstOrDefault() ?? p.Choices.Single();
    }
}
