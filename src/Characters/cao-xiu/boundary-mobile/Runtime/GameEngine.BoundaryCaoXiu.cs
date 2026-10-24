namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class BoundaryCaoXiuHost(GameEngine engine) : IBoundaryCaoXiuHost
    {
        public SkillProgramStepOutcome BeginQingxi(ProgramSkillFrame frame) => engine.BeginBoundaryCaoXiuQingxi(frame);
    }

    private SkillProgramStepOutcome BeginBoundaryCaoXiuQingxi(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.BoundaryCaoXiuQingxi is not null ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } target } context ||
            context.SourceSeat != frame.OwnerSeat || target == frame.OwnerSeat ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not BeforeDamageProgramWindowFrame window ||
            window.Prevented || window.RedirectedTargetSeat is not null ||
            CurrentDamageAttempt is not { IsSourceLess: false, DamageWasApplied: false } attack ||
            attack.SourceSeat != frame.OwnerSeat || attack.TargetSeat != target)
            throw new InvalidOperationException("Boundary Qingxi requires the owner's original pending damage to another role.");

        var distance = GetCombatDistance(frame.OwnerSeat, target);
        ReplaceRuntimeTop(frame with { BoundaryCaoXiuQingxi = new(window.Id, attack.ResolutionId, target, distance, Math.Max(1, 4 - distance)) });
        PublishBoundaryCaoXiuQingxi(GetActiveProgramFrame(frame.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishBoundaryCaoXiuQingxi(ProgramSkillFrame frame)
    {
        var draft = frame.BoundaryCaoXiuQingxi!;
        var hand = GetHand(_players[draft.TargetSeat]);
        var choices = new List<PromptChoice>();
        choices.AddRange(hand.Where(card => draft.SelectedIds.Contains(card.Id)).Select(card =>
            new PromptChoice(new ChoiceId("boundary-qingxi.unpick-" + card.Id), "取消选择【" + card.DisplayName + "】", [card.Id], [],
                new Dictionary<string, string> { ["program-action"] = "boundary-qingxi", ["branch"] = "unpick" })));
        if (hand.Count >= draft.RequiredCount)
        {
            if (draft.SelectedIds.Count < draft.RequiredCount)
                choices.AddRange(hand.Where(card => !draft.SelectedIds.Contains(card.Id)).Select(card =>
                    new PromptChoice(new ChoiceId("boundary-qingxi.card-" + card.Id), "选择弃置【" + card.DisplayName + "】", [card.Id], [],
                        new Dictionary<string, string> { ["program-action"] = "boundary-qingxi", ["branch"] = "card" })));
            else
                choices.Add(new(new ChoiceId("boundary-qingxi.discard"), $"弃置所选{draft.RequiredCount}张手牌", [], [],
                    new Dictionary<string, string> { ["program-action"] = "boundary-qingxi", ["branch"] = "discard" }));
        }
        choices.Add(new(new ChoiceId("boundary-qingxi.bonus"), "令此伤害+1", [], [],
            new Dictionary<string, string> { ["program-action"] = "boundary-qingxi", ["branch"] = "bonus" }));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var decision = new PendingDecision(DecisionKind.ProgramTrigger, draft.TargetSeat,
            $"【{skill.Name}】弃置{draft.RequiredCount}张手牌，或令此伤害+1。", choices.SelectMany(c => c.Cards).ToArray(), [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, Choices = choices.ToArray(),
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        var remaining = draft.RequiredCount - draft.SelectedIds.Count;
        _pendingDecision = NativeOwnedSelection(decision, frame, "qingxi-hand", "branch", ["card"], ["discard"],
            draft.SelectedIds, remaining, remaining);
        _status = _players[draft.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("boundary-qingxi")]
    private void ResolveBoundaryCaoXiuQingxi(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Qingxi lost its owning program frame.");
        var draft = frame.BoundaryCaoXiuQingxi ?? throw new InvalidOperationException("Qingxi lost its original target choice.");
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (_pendingDecision?.PlayerSeat != draft.TargetSeat || choice.Parameters.GetValueOrDefault("program-action") != "boundary-qingxi" ||
            choice.Targets.Count != 0 || branch is not ("card" or "unpick" or "discard" or "bonus"))
            throw new InvalidOperationException("Invalid Qingxi target choice.");
        AssertBoundaryCaoXiuQingxi(frame);
        if (branch == "unpick")
        {
            if (choice.Cards is not [var card] || !draft.SelectedIds.Contains(card) || _cardZones.GetLocation(card) != CardLocation.Hand(draft.TargetSeat))
                throw new InvalidOperationException("Qingxi can undo only its staged unspent hand entity.");
            ClearPendingDecision();
            ReplaceRuntimeTop(frame with { BoundaryCaoXiuQingxi = draft with { SelectedIds = draft.SelectedIds.Where(id => id != card).ToArray() } });
            PublishBoundaryCaoXiuQingxi(GetActiveProgramFrame(frame.Id));
            return;
        }
        if (branch == "card")
        {
            if (choice.Cards is not [var card] || draft.SelectedIds.Count >= draft.RequiredCount || draft.SelectedIds.Contains(card) ||
                _cardZones.GetLocation(card) != CardLocation.Hand(draft.TargetSeat))
                throw new InvalidOperationException("Qingxi can stage only a distinct original hand entity.");
            ClearPendingDecision();
            ReplaceRuntimeTop(frame with { BoundaryCaoXiuQingxi = draft with { SelectedIds = [.. draft.SelectedIds, card] } });
            PublishBoundaryCaoXiuQingxi(GetActiveProgramFrame(frame.Id));
            return;
        }
        if (choice.Cards.Count != 0 || branch == "discard" && draft.SelectedIds.Count != draft.RequiredCount)
            throw new InvalidOperationException("Qingxi requires its entire frozen hand-card payment.");
        var window = (BeforeDamageProgramWindowFrame)_resolutionStack.Single(f => f.Id == draft.DamageWindowId);
        var attack = CurrentDamageAttempt!;
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { BoundaryCaoXiuQingxi = null });
        var after = window.Amount;
        if (branch == "bonus")
        {
            attack.IncreaseFinalizedDamageAmount(1);
            after = checked(window.Amount + 1);
            ReplaceRuntimeFrame(window.Id, window with { Amount = after });
        }
        else
            MoveProgramLogicalDiscard(frame, draft.TargetSeat, draft.SelectedIds, new CardMoveReason("skill-program.boundary-qingxi.discard"));
        AdvanceEventRulesAndQueueFact(new BoundaryCaoXiuQingxiResolvedEvent(frame.Id,
            new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), draft.AttackId, draft.TargetSeat,
            draft.Distance, draft.RequiredCount, branch == "discard", window.Amount, after));
        if (!TryBeginCardsMovedProgramWindow(frame.Id)) AdvanceRuntimeProgram(frame.Id);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 51100)]
    private void AssertBoundaryCaoXiuQingxi(ProgramSkillFrame frame)
    {
        if (frame.BoundaryCaoXiuQingxi is not { } draft) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ResolveBoundaryCaoXiuQingxi ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context ||
            context.ParentFrameId != draft.DamageWindowId || context.SourceSeat != frame.OwnerSeat || context.TargetSeat != draft.TargetSeat ||
            draft.TargetSeat == frame.OwnerSeat || draft.Distance < 1 || draft.RequiredCount != Math.Max(1, 4 - draft.Distance) ||
            draft.SelectedIds.Count > draft.RequiredCount || draft.SelectedIds.Distinct().Count() != draft.SelectedIds.Count ||
            draft.SelectedIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(draft.TargetSeat)) ||
            CurrentDamageAttempt is not { IsSourceLess: false, DamageWasApplied: false } attack || attack.ResolutionId != draft.AttackId ||
            attack.SourceSeat != frame.OwnerSeat || attack.TargetSeat != draft.TargetSeat ||
            _resolutionStack.SingleOrDefault(f => f.Id == draft.DamageWindowId) is not BeforeDamageProgramWindowFrame { Prevented: false, RedirectedTargetSeat: null } window ||
            window.SourceSeat != frame.OwnerSeat || window.TargetSeat != draft.TargetSeat)
            throw new InvalidOperationException("Qingxi must retain its exact damage parent, frozen distance and unspent target hand entities.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != draft.TargetSeat ||
             prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") != "boundary-qingxi")))
            throw new InvalidOperationException("Qingxi must retain its actual target's private prompt.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveBoundaryCaoXiuQingxi)]
    private static PromptChoice SelectAiBoundaryCaoXiuQingxi(PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) =>
        decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "discard") ??
        decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "card") ?? decision.Choices.Single();
}
