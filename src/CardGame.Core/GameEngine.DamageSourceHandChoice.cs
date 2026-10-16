using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DamageSourceHandDrawReason = "skill-program.damage-source-hand.draw";
    private const string DamageSourceHandDiscardReason = "skill-program.damage-source-hand.discard";
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 270)]
    private bool CanOfferDamageSourceHandChoice(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDamageSourceHandChoice)) return true;
        return context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, DamageFrameId: not null, Amount: > 0 } &&
            IsValidPlayerSeat(source);
    }
    private SkillProgramStepOutcome BeginDamageSourceHandChoice(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.DamageSourceHandChoice is not null || f.WindowContext is not
            { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, DamageFrameId: { } damageId, Amount: > 0 } context ||
            ActiveDamageTrigger is not { } window || window.Id != context.ParentFrameId || window.ParentFrameId != damageId || context.TargetSeat != f.OwnerSeat)
            throw new InvalidOperationException("The hand choice requires its actual sourced damage occurrence.");
        var r = new DamageSourceHandChoiceReceipt { Source = AmbushSource(f), GameplayHash = f.GameplayHash, InstructionIndex = f.InstructionIndex,
            WindowFrameId = window.Id, DamageFrameId = damageId, DamageSourceSeat = source, DamageAmount = context.Amount,
            SourceHandCount = GetHand(_players[source]).Count, OwnerHandCount = GetHand(_players[f.OwnerSeat]).Count };
        ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = r });
        AdvanceEventRulesAndQueueFact(new DamageSourceHandChoiceOfferedEvent(f.Id, r));
        PublishDamageSourceHandChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DamageSourceHandChoices(ProgramSkillFrame f)
    {
        var r = f.DamageSourceHandChoice!;
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null) => new(
            new($"damage-source-hand.{f.Id}.{r.Stage}.{branch}.{string.Join('.', cards ?? [])}"), label, cards ?? [], [],
            new Dictionary<string, string> { ["program-action"] = "damage-source-hand", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        var choices = new List<PromptChoice>();
        if (r.Stage == DamageSourceHandChoiceStage.Branch)
        {
            var draw = Math.Max(0, Math.Min(5, r.SourceHandCount) - GetHand(_players[f.OwnerSeat]).Count);
            choices.Add(Choice("draw", $"摸 {draw} 张牌（至多摸至五张）"));
            if (_players[r.DamageSourceSeat].IsAlive && GetHand(_players[f.OwnerSeat]).Count >= r.SourceHandCount + 1)
                choices.Add(Choice("retaliate", $"弃置至少 {r.SourceHandCount + 1} 张手牌，对伤害来源造成1点伤害"));
        }
        else if (r.Stage == DamageSourceHandChoiceStage.DiscardSelection)
        {
            choices.AddRange(GetHand(_players[f.OwnerSeat]).Where(c => !r.SelectedCards.Contains(c.Id)).Select(c => Choice("card", $"选择弃置【{c.DisplayName}】", [c.Id])));
            if (r.SelectedCards.Count >= r.SourceHandCount + 1 && _players[r.DamageSourceSeat].IsAlive)
                choices.Add(Choice("pay", $"确认弃置 {r.SelectedCards.Count} 张手牌"));
        }
        choices.Add(Choice("decline", "取消")); return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishDamageSourceHandChoice(ProgramSkillFrame f)
    {
        var choices = DamageSourceHandChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = f.DamageSourceHandChoice! with { Choices = choices } });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], rSource(f))
        { PromptId = CreatePromptId(), IsPrivate = f.DamageSourceHandChoice!.Stage == DamageSourceHandChoiceStage.DiscardSelection,
            Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        static int rSource(ProgramSkillFrame frame) => frame.DamageSourceHandChoice!.DamageSourceSeat;
    }
    [ProgramAnswer("damage-source-hand")]
    private void ResolveDamageSourceHandChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Damage hand choice lost owner.");
        RequireDamageSourceHandChoice(f); var r = f.DamageSourceHandChoice!;
        if (!AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], DamageSourceHandChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Damage hand choice no longer matches its published branch or hand cost.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "decline" || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { CompleteDamageSourceHandChoice(f, false); return; }
        if (r.Stage == DamageSourceHandChoiceStage.Branch && branch == "retaliate")
        {
            ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = r with { Stage = DamageSourceHandChoiceStage.DiscardSelection, Choices = [] } });
            PublishDamageSourceHandChoice(f); return;
        }
        if (r.Stage == DamageSourceHandChoiceStage.DiscardSelection && branch == "card")
        {
            ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = r with { SelectedCards = r.SelectedCards.Append(choice.Cards.Single()).ToArray(), Choices = [] } });
            PublishDamageSourceHandChoice(f); return;
        }
        var draw = r.Stage == DamageSourceHandChoiceStage.Branch && branch == "draw";
        if (!draw && (branch != "pay" || r.SelectedCards.Count < r.SourceHandCount + 1 ||
            r.SelectedCards.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat))))
            throw new InvalidOperationException("Retaliation must pay at least the frozen source hand count plus one from hand.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = r with { Draw = draw, Stage = DamageSourceHandChoiceStage.MovementChildren,
            MovementBefore = before, Choices = [] }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var actual = draw ? DrawCards(_players[f.OwnerSeat], Math.Max(0, Math.Min(5, r.SourceHandCount) - GetHand(_players[f.OwnerSeat]).Count), true, new(DamageSourceHandDrawReason)).Count : 0;
        if (!draw) MoveProgramLogicalDiscard(f, f.OwnerSeat, r.SelectedCards, new(DamageSourceHandDiscardReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = f.DamageSourceHandChoice! with { MovementAfter = _movementSequence, DrawActual = actual } });
        AdvanceEventRulesAndQueueFact(new DamageSourceHandChoicePaidEvent(f.Id, f.DamageSourceHandChoice!));
        if (!DrainNamedHandChoiceChildren(f)) ReturnDamageSourceHandMovement(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20702)]
    private bool ReturnDamageSourceHandMovement(ProgramSkillFrame f)
    {
        if (f.DamageSourceHandChoice is null) return false;
        RequireDamageSourceHandChoice(f);
        if (f.DamageSourceHandChoice.Stage != DamageSourceHandChoiceStage.MovementChildren || f.PendingMovementContinuation is null)
            throw new InvalidOperationException("Damage hand adjustment lost typed paid return.");
        if (DrainNamedHandChoiceChildren(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        var r = f.DamageSourceHandChoice!;
        if (r.Draw || _winner != Winner.None || !_players[r.DamageSourceSeat].IsAlive)
        { CompleteDamageSourceHandChoice(f, true); return true; }
        ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = r with { Stage = DamageSourceHandChoiceStage.DamageIssued } });
        BeginProgramSkillDamage(f, r.DamageSourceSeat, 1); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20702)]
    private bool ResumeDamageSourceHandChoice(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DamageSourceHandChoice: { } r } f || f.Id != id) return false;
        RequireDamageSourceHandChoice(f);
        if (f.PendingMovementContinuation is not null) { ReturnDamageSourceHandMovement(f); return true; }
        if (r.Stage == DamageSourceHandChoiceStage.DamageIssued)
        {
            if (f.AttackAttempt is not null) CompleteDamageAttack(new ProgramAttackHandle(this, f.Id));
            else CompleteDamageSourceHandChoice(f, true);
        }
        return true;
    }
    private void CompleteDamageSourceHandChoice(ProgramSkillFrame f, bool completed)
    {
        var r = f.DamageSourceHandChoice!;
        AdvanceEventRulesAndQueueFact(new DamageSourceHandChoiceCompletedEvent(f.Id, r.DamageFrameId, r.DamageSourceSeat, r.Draw));
        ReplaceRuntimeTop(f = f with { DamageSourceHandChoice = null, PendingMovementContinuation = null }); FinishProgramSkill(f, completed);
    }
    private void RequireDamageSourceHandChoice(ProgramSkillFrame f)
    {
        if (f.DamageSourceHandChoice is not { } r || r.Source != AmbushSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context || context.ParentFrameId != r.WindowFrameId ||
            context.DamageFrameId != r.DamageFrameId || context.TargetSeat != f.OwnerSeat || context.SourceSeat != r.DamageSourceSeat || context.Amount != r.DamageAmount ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveDamageSourceHandChoice || r.SourceHandCount < 0 || r.OwnerHandCount < 0 ||
            r.SelectedCards.Distinct().Count() != r.SelectedCards.Count ||
            CompleteProgramEventHistory().OfType<DamageSourceHandChoiceOfferedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Receipt.Source == r.Source &&
                e.Receipt.WindowFrameId == r.WindowFrameId && e.Receipt.DamageFrameId == r.DamageFrameId && e.Receipt.SourceHandCount == r.SourceHandCount) != 1)
            throw new InvalidOperationException("Damage hand choice lost its exact sourced damage, instruction or frozen count.");
        if (r.Stage is not (DamageSourceHandChoiceStage.MovementChildren or DamageSourceHandChoiceStage.DamageIssued)) return;
        if (CompleteProgramEventHistory().OfType<DamageSourceHandChoicePaidEvent>().Count(e => e.ProgramFrameId == f.Id && e.Receipt.Source == r.Source &&
            e.Receipt.MovementBefore == r.MovementBefore && e.Receipt.MovementAfter == r.MovementAfter && e.Receipt.Draw == r.Draw &&
            e.Receipt.SelectedCards.SequenceEqual(r.SelectedCards)) != 1) throw new InvalidOperationException("Damage hand choice lost paid issuance.");
        var moves = _cardMovements.Where(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
            m.Reason.Value == (r.Draw ? DamageSourceHandDrawReason : DamageSourceHandDiscardReason)).ToArray();
        if (r.Draw ? moves.Length != r.DrawActual || moves.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat))
            : r.SelectedCards.Count < r.SourceHandCount + 1 || moves.Length != r.SelectedCards.Count ||
                !moves.Select(m => m.CardId).Order().SequenceEqual(r.SelectedCards.Order()) || moves.Any(m => m.From != CardLocation.Hand(f.OwnerSeat) || m.To != CardLocation.DiscardPile))
            throw new InvalidOperationException("Damage hand adjustment lost its exact real draw or paid hand discard.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 430)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 430)]
    private bool CanContinueDamageSourceHandChoice(ProgramSkillFrame f)
    {
        if (f.DamageSourceHandChoice is not { Stage: DamageSourceHandChoiceStage.MovementChildren or DamageSourceHandChoiceStage.DamageIssued }) return false;
        RequireDamageSourceHandChoice(f); return true;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveDamageSourceHandChoice)]
    private PromptChoice SelectAiDamageSourceHandChoice(PendingDecision d, ProgramSkillFrame f)
    {
        if (f.DamageSourceHandChoice!.Stage == DamageSourceHandChoiceStage.Branch)
            return d.Choices.FirstOrDefault(c => c.Parameters["branch"] == "draw") ?? d.Choices.FirstOrDefault(c => c.Parameters["branch"] == "retaliate") ?? d.Choices[0];
        return d.Choices.FirstOrDefault(c => c.Parameters["branch"] == "pay") ?? d.Choices.Where(c => c.Cards.Count == 1)
            .OrderBy(c => GetKeepValue(GetAttackCard(c.Cards[0]), _players[f.OwnerSeat])).FirstOrDefault() ?? d.Choices[0];
    }
    [ProgramCapability]
    private sealed class DamageSourceHandChoiceCapability(GameEngine engine) : IDamageSourceHandChoiceHost
    { public SkillProgramStepOutcome ResolveDamageSourceHandChoice(ProgramSkillFrame f) => engine.BeginDamageSourceHandChoice(f); }
}
