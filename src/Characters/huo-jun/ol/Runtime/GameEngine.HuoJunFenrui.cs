using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HuoJunDiscardReason = "skill-program.huo-jun.fenrui.discard";
    private static string HuoJunSlotName(EquipmentSlot s) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_huo_jun_ol_HuoJunFenrui.HuoJunSlotName(s);
    private CardConversionSource HuoJunSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class HuoJunProgramCapability(GameEngine e) : IHuoJunProgramHost
    { public SkillProgramStepOutcome RestoreSlotAndUseMatchingEquipment(ProgramSkillFrame f) => e.BeginHuoJunFenrui(f); }
    private bool HuoJunHasAbolishedSlot(int seat) => Enum.GetValues<EquipmentSlot>().Any(s => _players[seat].EquipmentSlotCapacity(s) == 0);
    private bool HuoJunDiscardable(int seat, CardZoneKind zone, Card c) => !c.IsGeneralWeapon &&
        !IsSelfHandCategoryDiscardForbidden(seat, c, new(zone, seat), OwnedCardMoveIntent.Discard);
    private bool HuoJunHasDiscard(int seat) => new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Any(z =>
        _cardZones.CardsAt(new(z, seat)).Any(c => HuoJunDiscardable(seat, z, c)));
    private bool HuoJunDamageUsed(int owner, string skill) => ProgramEventHistory<HuoJunFenruiDamageCommittedEvent>()
        .Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26210)]
    private bool CanOfferHuoJunFenrui(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        !t.Effects.Any(e => e.Op == SkillProgramEffectOp.RestoreSlotAndUseMatchingEquipment) ||
        context.Window == SkillProgramTriggerWindow.TurnEnding && HuoJunHasAbolishedSlot(c.OwnerSeat) && HuoJunHasDiscard(c.OwnerSeat);
    private SkillProgramStepOutcome BeginHuoJunFenrui(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HuoJunFenrui is not null || f.TriggerId is null || f.InstructionIndex != 1 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.OwnerSeat != f.OwnerSeat || window.ItemIndex < 0 || window.ItemIndex >= window.Items.Count ||
            window.Items[window.ItemIndex] is not { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate } || !MountObserverCandidateMatches(f, candidate) ||
            !HuoJunHasAbolishedSlot(f.OwnerSeat) || !HuoJunHasDiscard(f.OwnerSeat))
            throw new InvalidOperationException("Fenrui requires its actual ending candidate, one discardable entity and an abolished equipment category.");
        ReplaceRuntimeTop(f = f with { HuoJunFenrui = new(HuoJunSource(f), f.GameplayHash, f.InstructionIndex, window.Id,
            _turnNumber, _turnProgression.OwnerSeat, HuoJunFenruiStage.Cost) });
        PublishHuoJunFenrui(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HuoJunFenruiChoices(ProgramSkillFrame f)
    {
        var r = f.HuoJunFenrui!;
        if (r.Stage == HuoJunFenruiStage.Cost)
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat,
                [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
                canSelect: (zone, card) => HuoJunDiscardable(f.OwnerSeat, zone, card)).Select(c => c with {
                    Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "huo-jun-fenrui", ["branch"] = "cost" } }).ToArray());
        PromptChoice Choice(string branch, int value, string label, IReadOnlyList<int>? targets = null) => new(new($"huojun.{f.Id}.{r.Stage}.{branch}.{value}"), label, [], targets ?? [],
            new Dictionary<string, string> { ["program-action"] = "huo-jun-fenrui", ["branch"] = branch, ["value"] = value.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == HuoJunFenruiStage.Restore)
            return Array.AsReadOnly(Enum.GetValues<EquipmentSlot>().Where(s => _players[f.OwnerSeat].EquipmentSlotCapacity(s) == 0)
                .Select(s => Choice("restore", (int)s, "复原" + HuoJunSlotName(s))).ToArray());
        if (r.Stage != HuoJunFenruiStage.DamageChoice) return [];
        var result = new List<PromptChoice>(); var own = GetEquipment(_players[f.OwnerSeat]).Count;
        if (!HuoJunDamageUsed(f.OwnerSeat, f.SkillId))
            result.AddRange(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && GetEquipment(p).Count < own)
                .Select(p => Choice("damage", p.Seat, $"对 {p.Name} 造成{own - GetEquipment(p).Count}点伤害", [p.Seat])));
        result.Add(Choice("finish", 0, "不造成伤害")); return Array.AsReadOnly(result.ToArray());
    }
    private void PublishHuoJunFenrui(ProgramSkillFrame f)
    {
        var choices = HuoJunFenruiChoices(f);
        if (choices.Count == 0) { FinishHuoJunFenrui(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.HuoJunFenrui!.Stage switch {
            HuoJunFenruiStage.Cost => "奋锐：弃置一张手牌或装备牌", HuoJunFenruiStage.Restore => "奋锐：复原一个装备栏", _ => "奋锐：是否对装备数更少的角色造成伤害？" },
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("huo-jun-fenrui")]
    private void AnswerHuoJunFenrui(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuoJunFenrui: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], HuoJunFenruiChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Fenrui lost its exact private payer, restored slot or current equipment-count target.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || r.CostCardId is null && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { FinishHuoJunFenrui(f); return; }
        var branch = choice.Parameters["branch"];
        if (branch == "cost")
        {
            var id = choice.Cards.Single(); var from = _cardZones.GetLocation(id); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, HuoJunFenrui = r with {
                Stage = HuoJunFenruiStage.CostChildren, CostCardId = id, CostFrom = from, SequenceBefore = before, SequenceAfter = before } });
            MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new(HuoJunDiscardReason));
            f = GetActiveProgramFrame(f.Id); r = f.HuoJunFenrui! with { SequenceAfter = _movementSequence };
            ReplaceRuntimeTop(f with { HuoJunFenrui = r });
            AdvanceEventRulesAndQueueFact(new HuoJunFenruiCostPaidEvent(f.Id, r.Source, id, from, before, r.SequenceAfter));
        }
        else if (branch == "restore")
        {
            var slot = (EquipmentSlot)int.Parse(choice.Parameters["value"], CultureInfo.InvariantCulture);
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, HuoJunFenrui = r with { Stage = HuoJunFenruiStage.RestoreChildren, RestoredSlot = slot } });
            if (!RestoreEquipmentSlot(_players[f.OwnerSeat], slot)) throw new InvalidOperationException("Fenrui must restore its one actual abolished native slot.");
            AdvanceEventRulesAndQueueFact(new HuoJunFenruiSlotRestoredEvent(f.Id, f.OwnerSeat, slot));
        }
        else if (branch == "damage")
        {
            var target = choice.Targets.Single(); var own = GetEquipment(_players[f.OwnerSeat]).Count; var other = GetEquipment(_players[target]).Count;
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, HuoJunFenrui = r with {
                Stage = HuoJunFenruiStage.DamageIssued, DamageTargetSeat = target, DamageAmount = own - other } });
            // "若如此做" consumes the branch at the actual native attempt, including prevention or redirection.
            AdvanceEventRulesAndQueueFact(new HuoJunFenruiDamageCommittedEvent(f.Id, r.Source, target, own, other, own - other));
            BeginProgramSkillDamage(f, target, own - other); return;
        }
        else if (branch == "finish") { FinishHuoJunFenrui(f); return; }
        else throw new InvalidOperationException("Unknown Fenrui choice.");
        ContinueHuoJunFenrui(f.Id);
    }
    private bool DrainHuoJunFenruiChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueHuoJunFenrui(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { HuoJunFenrui: { } r } f && f.Id == id)
        {
            AssertHuoJunFenrui(f);
            if (DrainHuoJunFenruiChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.HuoJunFenrui!;
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishHuoJunFenrui(f); return; }
            switch (r.Stage)
            {
                case HuoJunFenruiStage.Cost:
                case HuoJunFenruiStage.Restore:
                case HuoJunFenruiStage.DamageChoice: PublishHuoJunFenrui(f); return;
                case HuoJunFenruiStage.CostChildren:
                    if (!HuoJunHasAbolishedSlot(f.OwnerSeat)) { FinishHuoJunFenrui(f); return; }
                    ReplaceRuntimeTop(f with { HuoJunFenrui = r with { Stage = HuoJunFenruiStage.Restore } }); continue;
                case HuoJunFenruiStage.RestoreChildren: IssueHuoJunRandomEquipment(f); return;
                case HuoJunFenruiStage.NativeEquipment: throw new InvalidOperationException("Fenrui's native equipment use must return through its exact native receipt.");
                case HuoJunFenruiStage.EquipmentChildren:
                    if (HuoJunDamageUsed(f.OwnerSeat, f.SkillId)) { FinishHuoJunFenrui(f); return; }
                    ReplaceRuntimeTop(f with { HuoJunFenrui = r with { Stage = HuoJunFenruiStage.DamageChoice } }); continue;
                case HuoJunFenruiStage.DamageIssued:
                    if (f.AttackAttempt is not null) { CompleteDamageAttack(new ProgramAttackHandle(this, f.Id)); return; }
                    FinishHuoJunFenrui(f); return;
                default: throw new InvalidOperationException("Unknown Fenrui stage.");
            }
        }
    }
    private void FinishHuoJunFenrui(ProgramSkillFrame f)
    { var paid = f.HuoJunFenrui?.CostCardId is not null; ReplaceRuntimeTop(f = f with { HuoJunFenrui = null }); FinishProgramSkill(f, paid); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26210)]
    private bool ResumeHuoJunFenrui(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuoJunFenrui: not null } f || f.Id != id) return false;
        ContinueHuoJunFenrui(id); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26210)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26210)]
    private bool CanContinuePaidHuoJunFenrui(ProgramSkillFrame f) => f.HuoJunFenrui is { CostCardId: not null } r &&
        ProgramEventHistory<HuoJunFenruiCostPaidEvent>().Any(e => e.FrameId == f.Id && e.Source == r.Source);
    [ProgramAiChoice(SkillProgramEffectOp.RestoreSlotAndUseMatchingEquipment)]
    private PromptChoice AiHuoJunFenrui(PendingDecision p, ProgramSkillFrame f) => f.HuoJunFenrui!.Stage == HuoJunFenruiStage.Cost
        ? p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First()
        : p.Choices.OrderByDescending(c => c.Parameters["branch"] == "damage" ? GetEquipment(_players[f.OwnerSeat]).Count - GetEquipment(_players[c.Targets.Single()]).Count : 0).First();
}
