using System.Globalization;
using System.Numerics;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly EquipmentSlot[] HuZhaoSlots = Enum.GetValues<EquipmentSlot>();
    private const string HuZhaoDrawReason = "skill-program.anniversary:midu.draw";
    private const string HuZhaoJudgmentReason = "skill-program.anniversary:midu.abolish-judgment";
    [ProgramCapability] private sealed class AnniversaryHuZhaoHost(GameEngine e) : IAnniversaryHuZhaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => e.BeginHuZhaoMidu(frame); }
    private CardConversionSource HuZhaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int HuZhaoAvailable(int seat) => HuZhaoSlots.Select((s, i) => _players[seat].EquipmentSlotCapacity(s) > 0 ? 1 << i : 0)
        .Aggregate(_players[seat].JudgmentAreaAbolished ? 0 : 32, (a, b) => a | b);
    private static int HuZhaoCount(int mask) => BitOperations.PopCount((uint)mask);
    private static string HuZhaoAreaName(int index) => index == 5 ? "判定区" : HuZhaoSlots[index] switch
    { EquipmentSlot.Weapon => "武器栏", EquipmentSlot.Armor => "防具栏", EquipmentSlot.OffensiveHorse => "进攻坐骑栏", EquipmentSlot.DefensiveHorse => "防御坐骑栏", _ => "宝物栏" };
    private SkillProgramStepOutcome BeginHuZhaoMidu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HuZhaoMidu is not null || f.SkillId != HuZhaoComposition.Midu || f.ActivationId != "abolish-or-restore" || f.TriggerId is not null || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("Midu requires its exact original unstarted zero-input Play activation.");
        var r = new HuZhaoMiduReceipt(HuZhaoSource(f), f.GameplayHash, _turnNumber, _turnProgression.OwnerSeat, HuZhaoMiduStage.Areas, HuZhaoAvailable(f.OwnerSeat));
        ReplaceRuntimeTop(f = f with { HuZhaoMidu = r });
        AdvanceEventRulesAndQueueFact(new HuZhaoMiduStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.TurnOwner, r.AvailableMask));
        PublishHuZhaoMidu(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HuZhaoChoices(ProgramSkillFrame f)
    {
        var r = f.HuZhaoMidu!; var list = new List<PromptChoice>();
        PromptChoice Choice(string mode, int value, string label, int? target = null) => new(new($"hu-zhao.{f.Id}.{r.Stage}.{mode}.{value}.{target}"), label, [],
            target is { } t ? [t] : [], new Dictionary<string, string> { ["program-action"] = "anniversary-hu-zhao", ["mode"] = mode, ["value"] = value.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == HuZhaoMiduStage.Areas)
        {
            var available = HuZhaoAvailable(f.OwnerSeat);
            for (var mask = 0; mask < 64; mask++)
                if ((mask & available) == mask) list.Add(Choice("abolish", mask, mask == 0 ? "不废除区域，令一名角色摸零张牌" : "废除" +
                    string.Join("、", Enumerable.Range(0, 6).Where(i => (mask & (1 << i)) != 0).Select(HuZhaoAreaName)) + $"，令一名角色摸{HuZhaoCount(mask)}张牌"));
            for (var i = 0; i < 6; i++) if ((available & (1 << i)) == 0) list.Add(Choice("restore", 1 << i, "恢复" + HuZhaoAreaName(i) + "，获得活墨"));
        }
        else if (r.Stage == HuZhaoMiduStage.Recipient)
            foreach (var p in _players.Where(p => p.IsAlive)) list.Add(Choice("recipient", r.SelectedMask, $"令{p.Name}摸{HuZhaoCount(r.SelectedMask)}张牌", p.Seat));
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishHuZhaoMidu(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, HuZhaoChoices(f), "弥笃：选择废除区域或恢复一个区域。", f.OwnerSeat);
    }
    [ProgramAnswer("anniversary-hu-zhao")]
    private void AnswerHuZhaoMidu(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuZhaoMidu: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !ValidHuZhaoMidu(f) ||
            !AssistedChoicesEqual([c], HuZhaoChoices(f).Where(x => x.Id == c.Id).ToArray())) throw new InvalidOperationException("Midu lost its exact owner/area/recipient menu.");
        ClearPendingDecision(); var mode = c.Parameters["mode"]; var mask = int.Parse(c.Parameters["value"], CultureInfo.InvariantCulture);
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishProgramSkill(f, false); return; }
        if (r.Stage == HuZhaoMiduStage.Areas && mode == "abolish")
        { ReplaceRuntimeTop(f = f with { HuZhaoMidu = r with { SelectedMask = mask, Stage = HuZhaoMiduStage.Recipient } }); PublishHuZhaoMidu(f); return; }
        var restore = mode == "restore"; var available = HuZhaoAvailable(f.OwnerSeat);
        if (restore ? r.Stage != HuZhaoMiduStage.Areas || HuZhaoCount(mask) != 1 || (mask & available) != 0 : r.Stage != HuZhaoMiduStage.Recipient || mask != r.SelectedMask || (mask & available) != mask)
            throw new InvalidOperationException("Midu cannot pay an unavailable or changed area selection.");
        var recipient = restore ? f.OwnerSeat : c.Targets.Single(); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, HuZhaoMidu = r with { Stage = HuZhaoMiduStage.CostChildren,
            SelectedMask = mask, Restore = restore, Recipient = recipient, Paid = true, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        // Payment is committed before native equipment-loss observers can run.
        AdvanceEventRulesAndQueueFact(new HuZhaoMiduPaidEvent(f.Id, r.Source, r.GameplayHash, mask, restore, recipient, before, before));
        for (var i = 0; i < 5; i++) if ((mask & (1 << i)) != 0)
        {
            if (restore) { if (!RestoreEquipmentSlot(_players[f.OwnerSeat], HuZhaoSlots[i])) throw new InvalidOperationException("Midu restoration lost its abolished slot."); }
            else SetEquipmentSlotCapacity(_players[f.OwnerSeat], HuZhaoSlots[i], 0);
        }
        if ((mask & 32) != 0)
        {
            var p = _players[f.OwnerSeat]; p.JudgmentAreaAbolished = !restore;
            AdvanceEventRulesAndQueueFact(new PlayerAreasAbolishedEvent(p.Seat, p.EquipmentAreaAbolished, p.JudgmentAreaAbolished));
            if (!restore) MoveCards(GetJudgment(p).ToArray(), CardLocation.Judgment(p.Seat), CardLocation.DiscardPile, new(HuZhaoJudgmentReason));
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { HuZhaoMidu = f.HuZhaoMidu! with { After = _movementSequence } }); ContinueHuZhaoMidu(f.Id);
    }
    private bool DrainHuZhaoChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueHuZhaoMidu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuZhaoMidu: { } r } f || f.Id != id) return;
        AssertHuZhaoMidu(f);
        if (r.Stage is HuZhaoMiduStage.CostChildren or HuZhaoMiduStage.DrawChildren && DrainHuZhaoChildren(id)) return;
        f = GetActiveProgramFrame(id); r = f.HuZhaoMidu!;
        if (r.Stage is HuZhaoMiduStage.Areas or HuZhaoMiduStage.Recipient) { PublishHuZhaoMidu(f); return; }
        if (r.Stage == HuZhaoMiduStage.CostChildren && !r.Restore && HuZhaoCount(r.SelectedMask) > 0 && _players[r.Recipient].IsAlive && _winner == Winner.None)
        {
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, HuZhaoMidu = r with { Stage = HuZhaoMiduStage.Drawing, Before = _movementSequence, After = _movementSequence } });
            BeginOperationProgramDraw(f, r.Recipient, HuZhaoCount(r.SelectedMask), new(HuZhaoDrawReason)); return;
        }
        if (r.Stage is not (HuZhaoMiduStage.CostChildren or HuZhaoMiduStage.DrawChildren)) return;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, HuZhaoMidu = r with { Stage = HuZhaoMiduStage.Completed },
            ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("midu-branch", r.Restore ? "restore" : "abolish", f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new HuZhaoMiduCompletedEvent(f.Id, r.Restore, r.Recipient, r.Restore ? 0 : HuZhaoCount(r.SelectedMask)));
        AdvanceRuntimeProgram(id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu)]
    private void ReturnHuZhaoDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.HuZhaoMidu!;
        if (!ValidHuZhaoMidu(f) || r.Stage != HuZhaoMiduStage.Drawing || f.NativeDraw is not null || outcome.RecipientSeat != r.Recipient || outcome.RequestedCount != HuZhaoCount(r.SelectedMask) || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Midu lost its once-paid original native draw return.");
        ReplaceRuntimeTop(f with { HuZhaoMidu = r with { Stage = HuZhaoMiduStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueHuZhaoMidu(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 43000)] private bool ResumeHuZhaoMidu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuZhaoMidu: { Stage: not HuZhaoMiduStage.Completed } } f || f.Id != id) return false;
        ContinueHuZhaoMidu(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 43000)] private bool ReturnHuZhaoMovement(ProgramSkillFrame f) => f.HuZhaoMidu is not null && ResumeHuZhaoMidu(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43000)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43000)]
    private bool ContinuePaidHuZhao(ProgramSkillFrame f) => f.HuZhaoMidu is { Paid: true } && ValidHuZhaoMidu(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu)] private PromptChoice HuZhaoAi(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters["mode"] == "restore") ?? p.Choices.FirstOrDefault(c => c.Targets.Contains(f.OwnerSeat)) ?? p.Choices[^1];
    [ProgramDistanceRule(ProgramDistanceStage.DistanceBaseTerms, 43000)] private void HuZhaoDistance(ProgramDistanceQuery q, List<RuleQueryBaseTerm> terms)
    {
        if (q.Source.Seat == q.Target.Seat) return;
        int Bonus(CharacterState p) => !p.IsAlive || !HasRuntimeSkill(p, HuZhaoComposition.Xianwang) ? 0 : HuZhaoSlots.Count(s => p.EquipmentSlotCapacity(s) == 0) switch { 0 => 0, < 3 => 1, _ => 2 };
        var outgoing = Bonus(q.Source); var incoming = Bonus(q.Target);
        if (outgoing != 0) terms.Add(new($"anniversary:xianwang:{q.Source.Seat}:outgoing", -outgoing));
        if (incoming != 0) terms.Add(new($"anniversary:xianwang:{q.Target.Seat}:incoming", incoming));
    }
}
