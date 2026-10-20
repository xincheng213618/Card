using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouSunJianOlYipoDrawReason = "skill-program.mou-ol:yipo.draw", MouSunJianOlYipoDiscardReason = "skill-program.mou-ol:yipo.discard";
    private static CardConversionSource MouSunJianOlSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class MouSunJianOlCapability(GameEngine engine) : IMouSunJianOlHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolveMouSunJianYipo ? engine.BeginMouSunJianOlYipo(f) : engine.BeginMouSunJianOlHulie(f, op);
    }
    private bool HasMouSunJianOlHpSubject(int seat) => IsValidPlayerSeat(seat) && _players[seat].IsAlive && CollectProgramTriggerCandidates(_players[seat], SkillProgramTriggerWindow.AfterHealthChanged)
        .Any(c => c.SkillId == MouSunJianOlComposition.Yipo && GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ResolveMouSunJianYipo }]);
    private void CaptureMouSunJianOlHp(HpChangeContext change)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouSunJianYipo) || change.Amount <= 0 || change.HpAfter <= 0 || change.HpAfter == change.HpBefore ||
            !_players[change.TargetSeat].IsAlive || ProgramEventHistory<MouSunJianOlHpFirstReachedEvent>().Any(e => e.OwnerSeat == change.TargetSeat && e.Hp == change.HpAfter)) return;
        var qualified = CollectProgramTriggerCandidates(_players[change.TargetSeat], SkillProgramTriggerWindow.AfterHealthChanged)
            .Where(c => c.SkillId == MouSunJianOlComposition.Yipo && GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ResolveMouSunJianYipo }]).ToArray();
        if (qualified.Length == 0) return;
        AdvanceEventRulesAndQueueFact(new MouSunJianOlHpFirstReachedEvent(change.TargetSeat, change.HpAfter, change.Id));
        foreach (var c in qualified) AdvanceEventRulesAndQueueFact(new MouSunJianOlYipoQualifiedEvent(new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash, change.Id, change.ParentFrameId, change.HpBefore, change.HpAfter));
    }
    private bool QualifiedMouSunJianOlYipo(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => c.SkillId == MouSunJianOlComposition.Yipo && c.BindingId == "first-positive-hp" &&
        w.Window == SkillProgramTriggerWindow.AfterHealthChanged && w.OwnerSeat == c.OwnerSeat && c.OccurrenceIndex == 0 && w.HpChange is { Amount: > 0, HpAfter: > 0 } h &&
        h.TargetSeat == c.OwnerSeat && h.HpBefore != h.HpAfter && w.ParentFrameId == h.Id &&
        ProgramEventHistory<MouSunJianOlHpFirstReachedEvent>().SingleOrDefault(e => e.OwnerSeat == c.OwnerSeat && e.Hp == h.HpAfter)?.ChangeId == h.Id &&
        ProgramEventHistory<MouSunJianOlYipoQualifiedEvent>().Count(e => e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) &&
            e.GameplayHash == c.GameplayHash && e.ChangeId == h.Id && e.ParentId == h.ParentFrameId && e.HpBefore == h.HpBefore && e.HpAfter == h.HpAfter) == 1;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32500)]
    private bool CanOfferMouSunJianOlYipo(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) =>
        !t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveMouSunJianYipo) || QualifiedMouSunJianOlYipo(c, w);
    private SkillProgramStepOutcome BeginMouSunJianOlYipo(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouSunJianOlYipo is not null || f.InstructionIndex != 1 || f.ActivationId != f.TriggerId || f.WindowContext is not { } w ||
            !QualifiedMouSunJianOlYipo(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), w))
            throw new InvalidOperationException("Yipo requires its genuine first reached positive HP occurrence.");
        var r = new MouSunJianOlYipoReceipt(MouSunJianOlSource(f), f.GameplayHash, w.ParentFrameId);
        ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r });
        AdvanceEventRulesAndQueueFact(new MouSunJianOlYipoStartedEvent(f.Id, r.Source, r.GameplayHash, r.ChangeId));
        PublishMouSunJianOlYipo(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private Card[] MouSunJianOlYipoDiscardable(int seat) => GetHand(_players[seat]).Concat(GetEquipment(seat)).Where(card =>
        !IsForeignEquipmentDiscardPrevented(seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard) &&
        !IsSelfHandCategoryDiscardForbidden(seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private IReadOnlyList<PromptChoice> MouSunJianOlYipoChoices(ProgramSkillFrame f)
    {
        var r = f.MouSunJianOlYipo!; var choices = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, int? target = null, int? card = null) => new(new($"mou-yipo.{f.Id}.{r.Stage}.{branch}.{target}.{card}"), label,
            card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = "mou-sun-jian-yipo", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage == MouSunJianOlYipoStage.Target)
        {
            choices.AddRange(_players.Where(p => p.IsAlive).Select(p => Choice("target", p.Name, p.Seat)));
            choices.Add(Choice("decline", "不发动毅魄"));
        }
        else if (r.Stage == MouSunJianOlYipoStage.Option)
        {
            var x = Math.Max(1, _players[f.OwnerSeat].MaxHp - _players[f.OwnerSeat].Hp);
            choices.Add(Choice("draw-many", $"令其摸{x}张牌，然后其弃置一张牌"));
            choices.Add(Choice("discard-many", $"令其摸一张牌，然后其弃置{x}张牌"));
            choices.Add(Choice("decline", "不发动毅魄"));
        }
        else if (r.Stage == MouSunJianOlYipoStage.Discard && r.TargetSeat is { } payer && _players[payer].IsAlive)
        {
            var cards = MouSunJianOlYipoDiscardable(payer); var count = Math.Min(r.DiscardCount, cards.Length);
            if (r.Selected.Count < count) choices.AddRange(cards.Where(card => !r.Selected.Contains(card.Id)).Select(card => Choice("card", $"弃置【{card.DisplayName}】", card: card.Id)));
            choices.AddRange(r.Selected.Select(id => Choice("unpick", $"撤回【{GetAdvancedCard(id).DisplayName}】", card: id)));
            if (r.Selected.Count == count) choices.Add(Choice("confirm", $"弃置{count}张牌"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouSunJianOlYipo(ProgramSkillFrame f)
    {
        var r = f.MouSunJianOlYipo!; var chooser = r.Stage == MouSunJianOlYipoStage.Discard ? r.TargetSeat!.Value : f.OwnerSeat;
        var choices = MouSunJianOlYipoChoices(f); if (choices.Count == 0) { FinishMouSunJianOl(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouSunJianOlYipoStage.Discard ? "毅魄：选择自己要弃置的牌。" : "毅魄：选择一名角色及摸弃方式。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-sun-jian-yipo")]
    private void AnswerMouSunJianOlYipo(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouSunJianOlYipo: { } r } f || !ValidMouSunJianOlYipo(f) ||
            _pendingDecision?.PlayerSeat != (r.Stage == MouSunJianOlYipoStage.Discard ? r.TargetSeat : f.OwnerSeat) || !MouSunJianOlYipoChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Yipo changed its actual chooser, original HP event or published payment.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (branch == "decline") { FinishMouSunJianOl(f); return; }
        if (r.Stage == MouSunJianOlYipoStage.Target)
        { ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r with { TargetSeat = choice.Targets.Single(), Stage = MouSunJianOlYipoStage.Option } }); PublishMouSunJianOlYipo(f); return; }
        if (r.Stage == MouSunJianOlYipoStage.Option)
        {
            if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !_players[r.TargetSeat!.Value].IsAlive) { FinishMouSunJianOl(f); return; }
            var x = Math.Max(1, _players[f.OwnerSeat].MaxHp - _players[f.OwnerSeat].Hp); var draw = branch == "draw-many" ? x : 1; var discard = branch == "draw-many" ? 1 : x;
            ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r with { Branch = branch, X = x, DrawCount = draw, DiscardCount = discard, Stage = MouSunJianOlYipoStage.Drawing } });
            AdvanceEventRulesAndQueueFact(new MouSunJianOlYipoChosenEvent(f.Id, r.TargetSeat.Value, branch, x, draw, discard));
            BeginOperationProgramDraw(f, r.TargetSeat.Value, draw, new(MouSunJianOlYipoDrawReason)); return;
        }
        if (r.Stage != MouSunJianOlYipoStage.Discard) throw new InvalidOperationException("Yipo cannot pay before its native draw returns.");
        if (branch is "card" or "unpick")
        {
            var selected = branch == "card" ? r.Selected.Append(choice.Cards.Single()).ToArray() : r.Selected.Where(id => id != choice.Cards.Single()).ToArray();
            ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r with { Selected = selected } }); PublishMouSunJianOlYipo(f); return;
        }
        var eligible = MouSunJianOlYipoDiscardable(r.TargetSeat!.Value);
        if (branch != "confirm" || !_players[r.TargetSeat.Value].IsAlive || r.Selected.Count != Math.Min(r.DiscardCount, eligible.Length) ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Any(id => eligible.All(card => card.Id != id)))
            throw new InvalidOperationException("Yipo requires the recipient's current discardable HE entities and exact frozen requested count before paying.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r with { Stage = MouSunJianOlYipoStage.DiscardChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (r.Selected.Count > 0) MoveProgramLogicalDiscard(f, r.TargetSeat!.Value, r.Selected, new(MouSunJianOlYipoDiscardReason));
        f = GetActiveProgramFrame(f.Id); r = f.MouSunJianOlYipo!;
        ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouSunJianOlYipoDiscardEvent(f.Id, r.TargetSeat!.Value, before, _movementSequence, r.Selected.Count)); ContinueMouSunJianOlYipo(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouSunJianYipo)]
    private void ReturnMouSunJianOlYipoDraw(ProgramSkillFrame f, NativeDrawOutcome draw)
    {
        if (!ValidMouSunJianOlYipo(f) || f.MouSunJianOlYipo is not { Stage: MouSunJianOlYipoStage.Drawing } r || draw.RecipientSeat != r.TargetSeat || draw.RequestedCount != r.DrawCount)
            throw new InvalidOperationException("Yipo lost its once-issued native draw return.");
        ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = r with { Draw = draw, Stage = MouSunJianOlYipoStage.DrawChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouSunJianOlYipoDrawEvent(f.Id, draw.RecipientSeat, draw.RequestedCount, draw.ActualDrawIds.Count, draw.ReplacementGainedIds.Count)); ContinueMouSunJianOlYipo(f.Id);
    }
    private void ContinueMouSunJianOlYipo(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouSunJianOlYipo: { } r } f || f.Id != id) return;
        if (!ValidMouSunJianOlYipo(f)) throw new InvalidOperationException("Yipo lost its accepted native effect cursor.");
        if (r.Stage is MouSunJianOlYipoStage.Target or MouSunJianOlYipoStage.Option or MouSunJianOlYipoStage.Discard) { PublishMouSunJianOlYipo(f); return; }
        if (r.Stage == MouSunJianOlYipoStage.Drawing) return;
        if (DrainMouSunJianOlChildren(id)) return;
        f = GetActiveProgramFrame(id); r = f.MouSunJianOlYipo!;
        if (r.Stage == MouSunJianOlYipoStage.DrawChildren && _players[r.TargetSeat!.Value].IsAlive && MouSunJianOlYipoDiscardable(r.TargetSeat.Value).Length > 0)
        { ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MouSunJianOlYipo = r with { Stage = MouSunJianOlYipoStage.Discard } }); PublishMouSunJianOlYipo(f); return; }
        FinishMouSunJianOl(f);
    }
    private bool DrainMouSunJianOlChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void FinishMouSunJianOl(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new MouSunJianOlCompletedEvent(f.Id, f.SkillId));
        ReplaceRuntimeTop(f = f with { MouSunJianOlYipo = null, MouSunJianOlHulie = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 32500)]
    private bool ResumeMouSunJianOl(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouSunJianOlYipo is not null) { ContinueMouSunJianOlYipo(id); return true; }
        if (f.MouSunJianOlHulie is { Stage: MouSunJianOlHulieStage.Children }) { if (!DrainMouSunJianOlChildren(id)) FinishMouSunJianOl(GetActiveProgramFrame(id)); return true; }
        return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 32500)]
    private bool ReturnMouSunJianOlMovement(ProgramSkillFrame f) => (f.MouSunJianOlYipo is not null || f.MouSunJianOlHulie is not null) && ResumeMouSunJianOl(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouSunJianYipo)]
    private PromptChoice AiMouSunJianOlYipo(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.MouSunJianOlYipo!.Stage == MouSunJianOlYipoStage.Target) return p.Choices.First(c => c.Targets.Contains(f.OwnerSeat));
        if (f.MouSunJianOlYipo.Stage == MouSunJianOlYipoStage.Option) return p.Choices.Single(c => c.Parameters["branch"] == (AreProgramDistributionAllies(_players[f.OwnerSeat], _players[f.MouSunJianOlYipo.TargetSeat!.Value]) ? "draw-many" : "discard-many"));
        return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "confirm") ?? p.Choices.Where(c => c.Parameters["branch"] == "card").OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat])).First();
    }
}
