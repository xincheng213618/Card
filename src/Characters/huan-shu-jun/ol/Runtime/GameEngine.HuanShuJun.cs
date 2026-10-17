using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HuanShuJunChoice = "huan-shu-jun";
    private const string HuanpeiDrawReason = "skill-program.huan-shu-jun.huanpei.draw";
    private const string HuanpeiDiscardReason = "skill-program.huan-shu-jun.huanpei.discard";
    private const string MiaoxinDrawReason = "skill-program.huan-shu-jun.miaoxin.draw";
    private bool HasHuanShuJunCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWenyiHealthOneAid) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh);
    private static CardConversionSource HuanShuJunSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HuanpeiRoundSpent(int owner) => ProgramEventHistory<HuanpeiAdjustmentAcceptedEvent>().Any(e => e.OwnerSeat == owner && e.Round == _roundNumber);
    private int WenyiRemaining(int owner) => Math.Max(0, 1 + ProgramEventHistory<MiaoxinWenyiRefreshedEvent>().Count(e => e.OwnerSeat == owner) - ProgramEventHistory<WenyiPaidEvent>().Count(e => e.OwnerSeat == owner));
    private IEnumerable<CardConversionSource> HuanpeiSources(CharacterState owner)
    {
        if (!owner.IsAlive || !owner.HasHpValue || _winner != Winner.None || _roundNumber < 1 || HuanpeiRoundSpent(owner.Seat)) yield break;
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
            foreach (var activation in instance.Program.Activations.Where(a => a.Effects is [{ Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse }]))
                if (HasRuntimeSkillInstance(owner, instance.SkillId, instance.SkillInstanceId))
                    yield return new(instance.SkillId, activation.Id, owner.Seat, instance.SkillInstanceId);
    }
    [ProgramCapability] private sealed class HuanShuJunCapability(GameEngine e) : IHuanShuJunHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginHuanShuJun(f, op); }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28100, SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse)]
    private bool CanActivateHuanpei(ProgramActivationLegalityContext c) => c.Owner.IsAlive && c.Owner.HasHpValue && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat && HuanpeiSources(c.Owner).Any(s => s.SkillId == c.SkillId) && CanAdjustHuanpeiHand(c.Owner);
    private bool CanAdjustHuanpeiHand(CharacterState actor) => actor.Hp <= GetHand(actor).Count ? GetSelfDiscardableOwnedHand(actor).Count >= GetHand(actor).Count - Math.Max(0, actor.Hp) : true;
    private SkillProgramStepOutcome BeginHuanShuJun(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HuanShuJun is not null) { ContinueHuanShuJun(f.Id); return SkillProgramStepOutcome.AwaitChoice; }
        var owner = _players[f.OwnerSeat]; var c = f.WindowContext;
        var target = op == SkillProgramEffectOp.ResolveWenyiHealthOneAid ? c?.HpChange?.TargetSeat ?? throw new InvalidOperationException("Wenyi lost its real HP-one window.") : f.OwnerSeat;
        var stage = op == SkillProgramEffectOp.ResolveWenyiHealthOneAid ? HuanShuJunStage.WenyiChoice : op == SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh ? HuanShuJunStage.Drawing : HuanShuJunStage.AdjustHand;
        var r = new HuanShuJunReceipt(HuanShuJunSource(f), f.GameplayHash, op, c?.ParentFrameId ?? 0, _turnNumber, _turnProgression.OwnerSeat, stage)
        { TargetSeat = target, FrozenHandCount = GetHand(owner).Count, FrozenHp = owner.Hp, Round = _roundNumber };
        if (op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse)
        {
            if (!HuanpeiSources(owner).Contains(r.Source) || !CanAdjustHuanpeiHand(owner)) throw new InvalidOperationException("Huanpei lost its live source, unspent round or adjustable Hand.");
            r = r with { Request = new(HuanpeiIntent.Play, null, null, null, f.OwnerSeat, 0, null, null, new PromptId(0), Revision) };
        }
        if (op == SkillProgramEffectOp.ResolveWenyiHealthOneAid && (!WenyiHpRoot(f) || WenyiRemaining(f.OwnerSeat) == 0)) throw new InvalidOperationException("Wenyi requires its exact issued real HP change and an available charge.");
        if (op == SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh)
        {
            var refresh = WenyiRemaining(f.OwnerSeat) == 0;
            r = r with { Paid = true, RequestedCount = refresh ? 1 : 2, RefreshWenyi = refresh };
        }
        ReplaceRuntimeTop(f = f with { HuanShuJun = r });
        AdvanceEventRulesAndQueueFact(new HuanShuJunStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.TurnOwner, target, r.FrozenHandCount, r.FrozenHp));
        if (op == SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh)
            AdvanceEventRulesAndQueueFact(new MiaoxinBenefitIssuedEvent(f.Id, f.OwnerSeat, r.WindowId, c!.OccurrenceIndex, r.RequestedCount, r.RefreshWenyi));
        ContinueHuanShuJun(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HuanShuJunChoices(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Args(string branch) => new() { [HuanShuJunChoice] = branch, ["program-action"] = HuanShuJunChoice, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null, string suffix = "") => new(new($"huan-shu-jun.{f.Id}.{r.Stage}.{branch}.{suffix}"), label, cards ?? [], [], Args(branch));
        if (r.Stage == HuanShuJunStage.AdjustHand)
        {
            var required = Math.Max(0, GetHand(_players[f.OwnerSeat]).Count - Math.Max(0, _players[f.OwnerSeat].Hp));
            foreach (var card in GetSelfDiscardableOwnedHand(_players[f.OwnerSeat])) choices.Add(Choice("cost-toggle", (r.Selected.Contains(card.Id) ? "取消选择" : "选择") + card.DisplayName, [card.Id], card.Id.ToString(CultureInfo.InvariantCulture)));
            if (r.Selected.Count == required) choices.Add(Choice("cost-confirm", required == 0 ? "调整手牌并使用基本牌" : $"弃置选中的{required}张手牌"));
            choices.Add(Choice("cancel", "取消"));
        }
        else if (r.Stage == HuanShuJunStage.BasicChoice)
        {
            foreach (var a in HuanpeiPaidPlayActions(f))
            {
                var p = Args("basic-use"); p["kind"] = a.PlayedCardKind!.Value.ToString();
                choices.Add(new(new($"huan-shu-jun.{f.Id}.basic.{a.PlayedCardKind}.{string.Join('-', a.TargetSeats)}"), a.Description, [], a.TargetSeats, p));
            }
        }
        else if (r.Stage == HuanShuJunStage.WenyiChoice)
        {
            if (WenyiTargetCanReceivePeach(f)) choices.Add(Choice("wenyi-peach", "对其使用一张桃"));
            if (r.TargetSeat != f.OwnerSeat && _players[r.TargetSeat].IsAlive)
                foreach (var card in GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Where(card => EquipmentCatalog.IsEquipment(card.Kind) && !card.IsGeneralWeapon))
                    choices.Add(Choice("wenyi-equipment", "交给其并令其使用" + card.DisplayName, [card.Id], card.Id.ToString(CultureInfo.InvariantCulture)));
            choices.Add(Choice("cancel", "取消温宜"));
        }
        else if (r.Stage == HuanShuJunStage.WenyiPeachRequest)
        {
            choices.AddRange(WenyiPeachSupplyChoices(f));
            choices.Add(Choice("cancel", "取消温宜"));
        }
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishHuanShuJun(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!;
        if (!r.Paid && (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))) { FinishHuanShuJun(f); return; }
        var choices = HuanShuJunChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) { FinishHuanShuJun(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage == HuanShuJunStage.AdjustHand ? "环佩：将手牌数调整至当前体力值。" : r.Stage == HuanShuJunStage.BasicChoice ? "环佩：选择一张基本牌及其合法目标。" : "温宜：使用桃，或交给目标装备牌令其使用。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), r.TargetSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("huan-shu-jun")]
    private void AnswerHuanShuJun(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuanShuJun: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !HuanShuJunChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Huan Shu Jun requires her exact current private decision.");
        AssertHuanShuJun(f);
        if (r.Stage == HuanShuJunStage.WenyiPeachRequest && !choice.Parameters.ContainsKey(HuanShuJunChoice)) { if (!TryResolveWenyiPeachSupply(choice)) throw new InvalidOperationException("Wenyi lost its exact supplied Peach."); return; }
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters[HuanShuJunChoice];
        if (branch == "cancel") { FinishHuanShuJun(f); return; }
        if (branch == "cost-toggle")
        {
            var id = choice.Cards.Single(); var next = r.Selected.Contains(id) ? r.Selected.Where(x => x != id).ToArray() : r.Selected.Append(id).ToArray();
            ReplaceRuntimeTop(f = f with { HuanShuJun = r with { Selected = next } }); PublishHuanShuJun(f); return;
        }
        if (branch == "cost-confirm") { PayHuanpeiAdjustment(f); return; }
        if (branch == "basic-use") { ChooseHuanpeiOutput(f, Enum.Parse<CardKind>(choice.Parameters["kind"]), choice.Targets); return; }
        if (branch == "wenyi-peach") { ReplaceRuntimeTop(f = f with { HuanShuJun = r with { Stage = HuanShuJunStage.WenyiPeachRequest } }); PublishHuanShuJun(f); return; }
        if (branch == "wenyi-equipment") { GiveWenyiEquipment(f, choice.Cards.Single()); return; }
        throw new InvalidOperationException("Unknown Huan Shu Jun branch.");
    }
    private void PayHuanpeiAdjustment(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!; var owner = _players[f.OwnerSeat]; var hand = GetHand(owner).Count; var hp = Math.Max(0, owner.Hp); var discard = Math.Max(0, hand - hp);
        if (r.Paid || !HuanpeiSources(owner).Contains(r.Source) || !CanAdjustHuanpeiHand(owner) || r.Selected.Count != discard || r.Selected.Distinct().Count() != discard || r.Selected.Any(id => GetSelfDiscardableOwnedHand(owner).All(c => c.Id != id))) throw new InvalidOperationException("Huanpei changed its exact current Hand adjustment cost or paid round.");
        var before = _movementSequence;
        if (r.Request is { Intent: HuanpeiIntent.WenyiPeach, ParentFrameId: { } wenyiId } && _resolutionStack.SingleOrDefault(x => x.Id == wenyiId) is ProgramSkillFrame wenyi && !wenyi.HuanShuJun!.Paid)
            PrepareWenyiSuppliedPeach(wenyi, f.OwnerSeat, [], r.Source);
        ReplaceRuntimeTop(f = f with { HuanShuJun = r = r with { Paid = true, FrozenHandCount = hand, FrozenHp = hp, RequestedCount = Math.Max(0, hp - hand), Before = before, After = before, Stage = HuanShuJunStage.AdjustmentChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HuanpeiAdjustmentAcceptedEvent(f.Id, f.OwnerSeat, r.Round, hand, hp, Math.Abs(hand - hp), before));
        if (discard > 0)
            MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Selected, new(HuanpeiDiscardReason), (batch, moves) =>
            {
                for (var i = 0; i < moves.Count; i++) { var m = moves[i]; AdvanceEventRulesAndQueueFact(new HuanpeiCostEntityEvent(f.Id, i, m.CardId, m.CardKind, m.From, m.Sequence)); }
            });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { HuanShuJun = f.HuanShuJun! with { After = _movementSequence } });
        if (r.RequestedCount > 0) { ReplaceRuntimeTop(f = f with { HuanShuJun = f.HuanShuJun! with { Stage = HuanShuJunStage.Drawing } }); BeginOperationProgramDraw(f, f.OwnerSeat, r.RequestedCount, new(HuanpeiDrawReason)); return; }
        AdvanceEventRulesAndQueueFact(new HuanpeiAdjustmentCompletedEvent(f.Id, before, _movementSequence, 0, 0)); ContinueHuanShuJun(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh)]
    private void ReturnHuanShuJunDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.HuanShuJun ?? throw new InvalidOperationException("Huan Shu Jun's draw lost its own receipt.");
        if (r.Stage != HuanShuJunStage.Drawing || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.RequestedCount || !r.Paid) throw new InvalidOperationException("Huan Shu Jun draw returned to another paid request.");
        ReplaceRuntimeTop(f = f with { HuanShuJun = r with { DrawOutcome = outcome, Stage = HuanShuJunStage.DrawChildren, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HuanShuJunDrawReturnedEvent(f.Id, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        if (r.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse) AdvanceEventRulesAndQueueFact(new HuanpeiAdjustmentCompletedEvent(f.Id, r.Before, outcome.SequenceAfter, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count));
        ContinueHuanShuJun(f.Id);
    }
    private bool DrainHuanShuJun(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id);
    private void ContinueHuanShuJun(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { HuanShuJun: { } r } f && f.Id == id && _pendingDecision is null)
        {
            AssertHuanShuJun(f);
            if (f.NativeDraw is not null) return;
            if (r.Stage is HuanShuJunStage.AdjustmentChildren or HuanShuJunStage.DrawChildren or HuanShuJunStage.EquipmentGiftChildren or HuanShuJunStage.NativeChildren or HuanShuJunStage.FaceChildren or HuanShuJunStage.ChainChildren)
            { if (DrainHuanShuJun(id)) return; f = GetActiveProgramFrame(id); r = f.HuanShuJun!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); }
            if (_winner != Winner.None) { FinishHuanShuJun(f); return; }
            if (r.Stage == HuanShuJunStage.Drawing)
            { if (!_players[f.OwnerSeat].IsAlive) { FinishHuanShuJun(f); return; } BeginOperationProgramDraw(f, f.OwnerSeat, r.RequestedCount, new(r.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse ? HuanpeiDrawReason : MiaoxinDrawReason)); return; }
            if (r.Op == SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh && r.Stage == HuanShuJunStage.DrawChildren)
            {
                if (r.RefreshWenyi && _players[f.OwnerSeat].IsAlive) AdvanceEventRulesAndQueueFact(new MiaoxinWenyiRefreshedEvent(f.Id, f.OwnerSeat)); FinishHuanShuJun(f); return;
            }
            if (r.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse && r.Stage is HuanShuJunStage.AdjustmentChildren or HuanShuJunStage.DrawChildren)
            {
                if (!_players[f.OwnerSeat].IsAlive) { FinishHuanShuJun(f); return; }
                ReplaceRuntimeTop(f = f with { HuanShuJun = r with { Stage = HuanShuJunStage.BasicChoice } });
                if (r.Request?.Intent != HuanpeiIntent.Play) { ContinueHuanpeiOriginalRequest(f); return; }
                PublishHuanShuJun(f); return;
            }
            if (r.Stage == HuanShuJunStage.EquipmentGiftChildren) { IssueWenyiEquipmentUse(f); return; }
            if (r.Stage == HuanShuJunStage.NativeChildren) { RestoreWenyiRecipient(f, true); return; }
            if (r.Stage == HuanShuJunStage.FaceChildren) { RestoreWenyiRecipient(f, false); return; }
            if (r.Stage == HuanShuJunStage.ChainChildren) { FinishHuanShuJun(f); return; }
            if (r.Stage is HuanShuJunStage.NativeIssuing or HuanShuJunStage.NativeUse) return;
            PublishHuanShuJun(f); return;
        }
    }
    private void FinishHuanShuJun(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!; ClearPendingDecision(); AdvanceEventRulesAndQueueFact(new HuanShuJunCompletedEvent(f.Id, r.Op, r.Paid));
        ReplaceRuntimeTop(f = f with { HuanShuJun = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
        if (r.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse && r.Request is { Intent: HuanpeiIntent.WenyiPeach, ParentFrameId: { } parentId } && _resolutionStack.LastOrDefault() is ProgramSkillFrame { HuanShuJun: { Paid: true } } parent && parent.Id == parentId) FinishUnissuedWenyiPeach(parent);
        else if (r.Op == SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse && r.Request is { Intent: not HuanpeiIntent.Play } && r.OriginalDecision is not null) RepublishHuanpeiOriginal(r);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28100)]
    private bool ResumeHuanShuJun(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuanShuJun: not null } f || f.Id != id || f.InstructionIndex == 0) return false; ContinueHuanShuJun(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28100)]
    private bool ReturnHuanShuJunMovement(ProgramSkillFrame f) { if (f.HuanShuJun is null) return false; ContinueHuanShuJun(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, SkillProgramEffectOp.ResolveWenyiHealthOneAid, SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh)]
    private PromptChoice ChooseHuanShuJunAi(PendingDecision prompt, ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!;
        if (r.Stage == HuanShuJunStage.AdjustHand)
            return prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault(HuanShuJunChoice) == "cost-confirm") ?? prompt.Choices.First(c => c.Parameters.GetValueOrDefault(HuanShuJunChoice) == "cost-toggle" && !r.Selected.Contains(c.Cards.Single()));
        return prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault(HuanShuJunChoice) != "cancel") ?? prompt.Choices[0];
    }
}
