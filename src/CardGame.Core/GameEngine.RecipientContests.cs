using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string RecipientContestGiftReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian}";
    private bool CanStartRecipientContest(CharacterState owner) => owner.IsAlive && GetHand(owner).Count > 0;
    private SkillProgramStepOutcome BeginRecipientContest(ProgramSkillFrame supplied, int recipient, string bind)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.RecipientContest is not null || f.InstructionIndex != 1 || f.TriggerId is not null || f.WindowContext is not null ||
            f.SelectedTargetSeats is not [var selected] || selected != recipient || recipient == f.OwnerSeat ||
            !IsValidPlayerSeat(recipient) || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("A recipient contest requires its original one-other-target actual Play activation.");
        if (!RecipientContestUnissuedSource(f) || !_players[recipient].IsAlive || !CanStartRecipientContest(_players[f.OwnerSeat]))
        { CancelProgramBindingAndCleanup(f, "原赠牌来源或受赠者失效，未支付。"); return SkillProgramStepOutcome.AwaitChild; }
        var ids = GetHand(_players[f.OwnerSeat]).Select(c => c.Id).ToArray(); var before = RecipientContestSequence;
        var r = new RecipientContestReceipt(1, RecipientContestSource(f), f.GameplayHash, _turnNumber, _currentSeat,
            recipient, bind, ids, before, before);
        ReplaceRuntimeTop(f with { RecipientContest = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(recipient), new(RecipientContestGiftReason(f)), (_, records) =>
        {
            var current = GetActiveProgramFrame(f.Id); var paid = r with { SequenceAfter = records.Max(m => m.Sequence) };
            ReplaceRuntimeTop(current with { RecipientContest = paid });
            AdvanceEventRulesAndQueueFact(new RecipientContestGiftPaidEvent(f.Id, paid.Source, paid.GameplayHash,
                paid.TurnNumber, paid.TurnOwnerSeat, recipient, bind, ids.Length, paid.SequenceBefore, paid.SequenceAfter));
        });
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private int[] RecipientContestThirds(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat &&
        p.Seat != f.RecipientContest!.RecipientSeat && CanBePindianTarget(f.RecipientContest.RecipientSeat, p.Seat)).Select(p => p.Seat).ToArray();
    private void PublishRecipientContestThird(ProgramSkillFrame f)
    {
        var r = f.RecipientContest!; var targets = RecipientContestThirds(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (targets.Length == 0) { CancelProgramBindingAndCleanup(f, "赠牌保留，已无合法第三位拼点对象。"); return; }
        var choices = Array.AsReadOnly(targets.Select(seat => new PromptChoice(new($"recipient-contest.{f.Id}.third-{seat}"),
            $"令 {_players[r.RecipientSeat].Name} 与 {_players[seat].Name} 拼点", [], [seat],
            new Dictionary<string, string> { ["program-action"] = "recipient-contest", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "选择受赠者的另一位拼点对象。", [], targets, f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveRecipientContestChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertRecipientContestPrograms(f); var r = f.RecipientContest!;
        if (r.Stage != RecipientContestStage.ChoosingThird || _pendingDecision?.PlayerSeat != f.OwnerSeat || choice.Cards.Count != 0 ||
            choice.Targets is not [var third] || !RecipientContestThirds(f).Contains(third) ||
            choice.Id.Value != $"recipient-contest.{f.Id}.third-{third}") throw new InvalidOperationException("The recipient contest lost its exact published third participant.");
        ClearPendingDecision();
        if (!RecipientContestUnissuedSource(f) || !_players[r.RecipientSeat].IsAlive)
        { CancelProgramBindingAndCleanup(f, "保留赠牌，取消尚未发行的拼点。"); return; }
        var id = _resolutionSequence + 1;
        ReplaceRuntimeTop(f with { RecipientContest = r with { Stage = RecipientContestStage.Pindian, ThirdSeat = third, PindianFrameId = id } });
        AdvanceEventRulesAndQueueFact(new RecipientContestStartedEvent(f.Id, r.RecipientSeat, third, id));
        var skill = _contentRegistry.GetSkill(f.SkillId);
        BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, skill.Name, skill.Description), r.RecipientSeat, third,
            programResultBind: r.ResultBind, programResultVisibility: SkillProgramCardSetVisibility.Public);
    }
    private bool ResumeRecipientContest(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.RecipientContest is not { } r) return false;
        AssertRecipientContestPrograms(f);
        if (r.Stage == RecipientContestStage.GiftChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (!RecipientContestUnissuedSource(f) || !_players[r.RecipientSeat].IsAlive)
            { CancelProgramBindingAndCleanup(f, "已赠实体和孩子保留，未发行拼点取消。"); return true; }
            ReplaceRuntimeTop(f = f with { RecipientContest = r with { Stage = RecipientContestStage.ChoosingThird } });
            PublishRecipientContestThird(f); return true;
        }
        if (r.Stage == RecipientContestStage.ChoosingThird)
        { if (_pendingDecision is null) PublishRecipientContestThird(f); return true; }
        if (r.Stage == RecipientContestStage.Pindian)
        {
            if (!RecipientContestResultMatches(f, out _)) throw new InvalidOperationException("A real recipient Pindian returned without its original result.");
            ReplaceRuntimeTop(f with { RecipientContest = r with { Stage = RecipientContestStage.ResultReady } });
            var host = new ProgramSkillHost(this); new SkillProgramExecutor().Run(id, host, host); return true;
        }
        if (r.Stage == RecipientContestStage.SlashIssued) throw new InvalidOperationException("A contest winner Slash returned without its typed completion.");
        if (r.Stage == RecipientContestStage.Complete) { FinishProgramSkill(f, true); return true; }
        return false;
    }
    private bool RecipientContestResultMatches(ProgramSkillFrame f, out PindianResult result)
    {
        result = null!; var r = f.RecipientContest!;
        if (r.PindianFrameId is not { } id || r.ThirdSeat is not { } third ||
            f.PindianResultBindings.SingleOrDefault(b => b.Name == r.ResultBind) is not { } binding) return false;
        var facts = CompleteProgramEventHistory().OfType<PindianResultDeterminedEvent>().Where(e => e.FrameId == id && e.SkillId == f.SkillId).ToArray();
        if (facts is not [var fact] || fact.Result.SourceSeat != r.RecipientSeat || fact.Result.OpponentSeat != third ||
            binding.SourceSeat != r.RecipientSeat || binding.OpponentSeat != third || binding.SourceRank != fact.Result.SourceRank ||
            binding.OpponentRank != fact.Result.OpponentRank || binding.SourceWon != fact.Result.SourceWon || binding.Visibility != SkillProgramCardSetVisibility.Public) return false;
        result = fact.Result; return true;
    }
    private SkillProgramStepOutcome UseRecipientContestWinner(ProgramSkillFrame supplied, string bind)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.RecipientContest;
        if (r is not { Stage: RecipientContestStage.ResultReady } || f.InstructionIndex != 2 || bind != r.ResultBind ||
            !RecipientContestResultMatches(f, out var result)) throw new InvalidOperationException("Winner use requires the exact preceding revealed recipient contest.");
        if (result.SourceRank == result.OpponentRank) { ReplaceRuntimeTop(f with { RecipientContest = r with { Stage = RecipientContestStage.Complete } }); return SkillProgramStepOutcome.Continue; }
        var actor = result.SourceWon ? result.SourceSeat : result.OpponentSeat; var target = result.SourceWon ? result.OpponentSeat : result.SourceSeat;
        if (!RecipientContestUnissuedSource(f) || !CanUseForeignTurnContestSlash(actor, target))
        { CancelProgramBindingAndCleanup(f, "真实拼点结果保留，尚未发行的赢家杀取消。"); return SkillProgramStepOutcome.AwaitChild; }
        if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("A recipient winner Slash cannot overwrite another attack.");
        var useId = ++_resolutionSequence; var winner = _players[actor];
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId, CardActionType.Use,
            actor, actor, null, null, null, CardKind.Slash, [target], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var returned = new PindianWinnerSlashReturn(f.Id, 2, r.Source, f.GameplayHash, bind, r.PindianFrameId!.Value,
            r.RecipientSeat, r.ThirdSeat!.Value, result.SourceRank, result.OpponentRank, actor, target, useId, action.ActionId);
        ReplaceRuntimeTop(f with { RecipientContest = r with { Stage = RecipientContestStage.SlashIssued, SlashReturn = returned } });
        PushRuntimeFrame(new CardUseFrame(useId, actor, 0, CardKind.Slash, [target], PhysicalCardIds: []) { Action = action, PindianWinnerSlashReturn = returned });
        AdvanceEventRulesAndQueueFact(new PindianWinnerSlashIssuedEvent(returned));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(useId, 0, CardKind.Slash, actor));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(useId, [target]));
        RecordYingboCardUse(useId, actor, CardKind.Slash); RecordProgramUsedBasicCard(actor, CardKind.Slash);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor, CardKind.Slash); RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, useId, actor, target, card: null, damageAmount: winner.HasAlcoholEffect ? 2 : 1,
            playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(winner, _players[target], CardKind.Slash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(useId, winner); winner.HasAlcoholEffect = false; ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor, target)); TryMarkProgramUseCommitted(useId);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, [target], ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void CompletePindianWinnerSlash(AttackCompletionReceipt completion)
    {
        var returned = completion.PindianWinnerSlashReturn ?? throw new InvalidOperationException("Missing recipient winner typed return.");
        var f = GetActiveProgramFrame(returned.ProgramFrameId); var r = f.RecipientContest;
        if (completion.ProgramFrameId != f.Id || completion.ResolutionId != returned.CardUseFrameId ||
            r is not { Stage: RecipientContestStage.SlashIssued } || r.SlashReturn != returned ||
            returned.Source != RecipientContestSource(f) || returned.GameplayHash != f.GameplayHash || f.InstructionIndex != 2 ||
            !RecipientContestResultMatches(f, out _) || _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == returned.CardUseFrameId) ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == returned.CardUseFrameId) != 1)
            throw new InvalidOperationException("The winner Slash returned outside its original paid gift/result and owning use.");
        ReplaceRuntimeTop(f = f with { RecipientContest = r with { Stage = RecipientContestStage.Complete } });
        AdvanceEventRulesAndQueueFact(new PindianWinnerSlashReturnedEvent(returned)); FinishProgramSkill(f, true);
    }
    private sealed partial class ProgramSkillHost : IRecipientContestProgramHost
    {
        public SkillProgramStepOutcome DiscardDrawPeer(ProgramSkillFrame f) => engine.BeginUniqueHpPeer(f);
        public SkillProgramStepOutcome GiveAllHandContest(ProgramSkillFrame f, int seat, string bind) => engine.BeginRecipientContest(f, seat, bind);
        public SkillProgramStepOutcome UseContestWinnerSlash(ProgramSkillFrame f, string bind) => engine.UseRecipientContestWinner(f, bind);
    }
}
