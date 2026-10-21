using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenZfReason = "skill-program.anniversary:shencai.";
    private static readonly PlayerMarkerKind[] ShenZfFour = [PlayerMarkerKind.ShencaiChi, PlayerMarkerKind.ShencaiZhang, PlayerMarkerKind.ShencaiTu, PlayerMarkerKind.ShencaiLiu];
    private static readonly PrintedCardTerms[] ShenZfTerms = [PrintedCardTerms.Hp, PrintedCardTerms.Weapon, PrintedCardTerms.Play, PrintedCardTerms.Distance];
    private static CardConversionSource ShenZfSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private int ShenZfMark(int seat, PlayerMarkerKind kind) => _players[seat].Markers.GetValueOrDefault(kind);
    private string ShenZfIssuedInstance(int seat) => "shencai-issued:" + seat.ToString(CultureInfo.InvariantCulture);
    private bool ShenZfIssued(int seat) => IsValidPlayerSeat(seat) && ProgramEventHistory<ShenZhangFeiSentenceEvent>().Any(e => e.TargetSeat == seat &&
        e.GameplayHash == _contentRegistry.GetSkill(ShenZhangFeiComposition.Shencai).Program!.GameplayHash);
    private int ShenZfLimit(int seat) => Math.Min(5, 1 + ProgramEventHistory<ShenZhangFeiXunshiEvent>().Count(e => e.Source.OwnerSeat == seat));
    [ProgramCapability] private sealed class ShenZhangFeiCapability(GameEngine e) : IShenZhangFeiHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShenZf(f, op); }
    [ProgramCapability] private sealed class ShenZhangFeiSelection(GameEngine e) : IShenZhangFeiSelection
    { public int Limit(int owner) => e.ShenZfLimit(owner); }

    private SkillProgramStepOutcome BeginShenZf(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenZhangFei is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Shencai requires the original unpaid instruction.");
        if (op == SkillProgramEffectOp.ResolveShenZhangFeiXunshi)
        {
            var action = ShenZfUseAction(f.WindowContext!);
            if (action is null || !ShenZfColorless(action) || action.ActorSeat != f.OwnerSeat) return SkillProgramStepOutcome.Continue;
            if (!ProgramEventHistory<ShenZhangFeiXunshiEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.ActionId == action.ActionId))
                AdvanceEventRulesAndQueueFact(new ShenZhangFeiXunshiEvent(ShenZfSource(f), f.GameplayHash, action.ActionId, Math.Min(5, ShenZfLimit(f.OwnerSeat) + 1)));
            return SkillProgramStepOutcome.Continue;
        }
        var claiming = f.TriggerId == "judgment-first-gain";
        var target = claiming ? f.WindowContext!.Judgment!.SubjectSeat : op == SkillProgramEffectOp.ResolveShenZhangFeiShencai ? f.SelectedTargetSeats.Single() : f.OwnerSeat;
        var amount = f.TriggerId == "chi-damage" ? f.WindowContext!.Amount : 1;
        var r = new ShenZhangFeiReceipt(ShenZfSource(f), f.GameplayHash, op, f.WindowContext?.ParentFrameId ?? 0, target, amount,
            op == SkillProgramEffectOp.ResolveShenZhangFeiShencai && !claiming ? ShenZhangFeiStage.Judging : claiming ? ShenZhangFeiStage.GainChildren : ShenZhangFeiStage.PenaltyChildren)
        { Before = _movementSequence, After = _movementSequence, BatchId = f.WindowContext?.MovementBatch?.Id ?? 0 };
        ReplaceRuntimeTop(f = f with { ShenZhangFei = r });
        AdvanceEventRulesAndQueueFact(new ShenZhangFeiStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,target,amount));
        if (claiming) ClaimShenZfJudgment(f);
        else if (op == SkillProgramEffectOp.ResolveShenZhangFeiShencai)
        {
            if (target == f.OwnerSeat || !_players[target].IsAlive) throw new InvalidOperationException("Shencai requires its selected other living subject.");
            ReplaceRuntimeTop(f = f with { ShenZhangFei = r with { JudgmentId = _resolutionSequence + 1 } });
            _ = BeginJudgment(null,target,"anniversary-shencai",f.Id,null,JudgmentContinuationKind.ProgramSkill,
                sourceSeat:f.OwnerSeat,programResultBind:"shencai-judgment",programResultVisibility:SkillProgramCardSetVisibility.Public);
        }
        else PayShenZfPunishment(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenZf(long id) => TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueShenZf(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenZhangFei:{ } r } f && f.Id == id)
        {
            RequireShenZf(f);
            if (r.Stage == ShenZhangFeiStage.Judging) return;
            if (DrainShenZf(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenZhangFei!;
            if (r.Stage == ShenZhangFeiStage.JudgmentCleanupChildren)
            {
                ReplaceRuntimeTop(f with { ShenZhangFei=r with { Stage=ShenZhangFeiStage.JudgmentReturned },PendingMovementContinuation=null });
                continue;
            }
            if (r.Stage == ShenZhangFeiStage.JudgmentReturned && r.ResultCardId is { } resultId && _cardZones.GetLocation(resultId) == CardLocation.Processing)
            {
                if (!HasUnclaimedShenZfJudgmentResult(f,resultId,_movementSequence))
                    throw new InvalidOperationException("Shencai cannot dispose a result without its exact native judgment return and unclaimed processing transfer.");
                MoveShenZfCard(f,GetAdvancedCard(resultId),CardLocation.Processing,CardLocation.DiscardPile,ShenZhangFeiStage.JudgmentCleanupChildren,"judgment-unclaimed");
                continue;
            }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShenZf(f); return; }
            if (r.Stage == ShenZhangFeiStage.JudgmentReturned)
            {
                ReplaceRuntimeTop(f = f with { ShenZhangFei = r with { Stage = ShenZhangFeiStage.MarkChildren }, PendingMovementContinuation = null });
                SentenceShenZf(f); continue;
            }
            if (r.Stage is ShenZhangFeiStage.MarkChildren or ShenZhangFeiStage.TakeChoice)
            {
                var terms = r.ResultKind is { } kind ? CardPrintedInfo.Terms(kind) & (PrintedCardTerms.Hp|PrintedCardTerms.Weapon|PrintedCardTerms.Play|PrintedCardTerms.Distance) : PrintedCardTerms.None;
                if (r.ResultCardId is not null && terms == PrintedCardTerms.None && _players[r.TargetSeat].IsAlive)
                {
                    var choices = ShenZfTakeChoices(f);
                    if (choices.Count > 0)
                    {
                        ReplaceRuntimeTop(f = f with { ShenZhangFei = r with { Stage = ShenZhangFeiStage.TakeChoice } });
                        PublishShenZfTake(f, choices); return;
                    }
                }
            }
            FinishShenZf(f); return;
        }
    }
    private void FinishShenZf(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { ShenZhangFei = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    private void SentenceShenZf(ProgramSkillFrame f)
    {
        var r = f.ShenZhangFei!;
        if (r.ResultCardId is null || r.ResultKind is not { } kind || !_players[r.TargetSeat].IsAlive) return;
        var terms = CardPrintedInfo.Terms(kind) & (PrintedCardTerms.Hp|PrintedCardTerms.Weapon|PrintedCardTerms.Play|PrintedCardTerms.Distance);
        if (terms == PrintedCardTerms.None) MutateParticipantMarker(f,r.TargetSeat,PlayerMarkerKind.ShencaiSi,1);
        else
        {
            foreach (var m in ShenZfFour) if (ShenZfMark(r.TargetSeat,m) is > 0 and var count) MutateParticipantMarker(f,r.TargetSeat,m,-count);
            for (var index = 0; index < ShenZfFour.Length; index++) if (terms.HasFlag(ShenZfTerms[index])) MutateParticipantMarker(f,r.TargetSeat,ShenZfFour[index],1);
        }
        AdvanceEventRulesAndQueueFact(new ShenZhangFeiSentenceEvent(f.Id,r.Source,r.GameplayHash,r.TargetSeat,terms,ShenZfMark(r.TargetSeat,PlayerMarkerKind.ShencaiSi)));
    }
    private IReadOnlyList<PromptChoice> ShenZfTakeChoices(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.ShenZhangFei!.TargetSeat,
        [CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment],OwnedCardMoveIntent.Obtain)
        .Select(c => c with { Parameters = new Dictionary<string,string>(c.Parameters) { ["program-action"] = "shen-zf-take" } }).ToArray();
    private void PublishShenZfTake(ProgramSkillFrame f,IReadOnlyList<PromptChoice> choices)
    {
        var s = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger,f.OwnerSeat,"神裁：获得受判角色区域内的一张牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[f.ShenZhangFei!.TargetSeat],f.OwnerSeat)
        { PromptId = CreatePromptId(),IsPrivate = true,TargetSeat = f.ShenZhangFei.TargetSeat,Choices = Array.AsReadOnly(choices.ToArray()),SkillPrompt = new(f.SkillId,s.Name,s.Name,s.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("shen-zf-take")] private void AnswerShenZfTake(PromptChoice chosen)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Shencai lost its card-choice parent.");
        RequireShenZf(f); var r = f.ShenZhangFei!;
        if (r.Stage != ShenZhangFeiStage.TakeChoice || !ShenZfTakeChoices(f).Any(c => c.Id == chosen.Id && c.Cards.SequenceEqual(chosen.Cards) && c.Targets.SequenceEqual(chosen.Targets) && c.Parameters.OrderBy(x=>x.Key).SequenceEqual(chosen.Parameters.OrderBy(x=>x.Key))))
            throw new InvalidOperationException("Shencai requires the exact currently published public card or hidden hand slot.");
        var from = new CardLocation(Enum.Parse<CardZoneKind>(chosen.Parameters["source-zone"]),r.TargetSeat);
        var card = _cardZones.CardsAt(from)[int.Parse(chosen.Parameters["slot-index"],CultureInfo.InvariantCulture)];
        ClearPendingDecision();
        MoveShenZfCard(f,card,from,card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat),ShenZhangFeiStage.TakeChildren,"take");
        ContinueShenZf(f.Id);
    }
    private void MoveShenZfCard(ProgramSkillFrame f,Card card,CardLocation from,CardLocation to,ShenZhangFeiStage stage,string purpose)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { ShenZhangFei = f.ShenZhangFei! with { Stage=stage,CardId=card.Id,From=from,To=to,Before=before,After=before },PendingMovementContinuation=new(f.OwnerSeat,0,null) });
        MoveCard(card,from,to,new(ShenZfReason+purpose),m =>
        {
            var now = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(now with { ShenZhangFei=now.ShenZhangFei! with { Paid=true,After=m.Sequence } });
            AdvanceEventRulesAndQueueFact(new ShenZhangFeiMovementPaidEvent(f.Id,stage,card.Id,from,to,before,m.Sequence));
        });
    }
    private void PayShenZfPunishment(ProgramSkillFrame f)
    {
        var r = f.ShenZhangFei!;
        if (!CanRunIssuedShenZf(ShenZfFrameCandidate(f),f.WindowContext!)) throw new InvalidOperationException("The issued punishment lost its original marked participant and native occurrence.");
        ReplaceRuntimeTop(f = f with { ShenZhangFei = r with { Paid=true } });
        AdvanceEventRulesAndQueueFact(new ShenZhangFeiPunishmentPaidEvent(f.Id,f.TriggerId!,f.OwnerSeat,r.Amount,r.BatchId));
        if (f.TriggerId == "chi-damage") new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,f.OwnerSeat,r.Amount);
        else if (f.TriggerId == "tu-hand-loss")
        {
            var hand = GetHand(_players[f.OwnerSeat]);
            if (hand.Count > 0) MoveShenZfCard(f,hand[_random.Next(hand.Count)],CardLocation.Hand(f.OwnerSeat),CardLocation.DiscardPile,ShenZhangFeiStage.PenaltyChildren,"tu-discard");
        }
        else if (f.TriggerId == "liu-turn-ending") TurnOverProgramTarget(f.Id,f.OwnerSeat,f.OwnerSeat);
        else if (f.TriggerId == "si-turn-ended")
        {
            AdvanceEventRulesAndQueueFact(new ProgramSkillCauseDeathDeclaredEvent(f.Id,f.SkillId,f.TriggerId,f.OwnerSeat,f.OwnerSeat));
            BeginPlayerDeath(f.Id,_players[f.OwnerSeat],null,null,null,causingProgramSkillFrameId:f.Id); return;
        }
        ContinueShenZf(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,38600)] private bool ResumeShenZf(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenZhangFei:not null } f || f.Id != id) return false; ContinueShenZf(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement,38600)] private bool ReturnShenZfMovement(ProgramSkillFrame f) => f.ShenZhangFei is not null && ResumeShenZf(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,38600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,38600)] private bool KeepShenZfPaid(ProgramSkillFrame f) =>
        f.ShenZhangFei is not null || f.SkillId == ShenZhangFeiComposition.Shencai && f.TriggerId is { } binding &&
        ShenZfPenaltyMarker(binding) is not null && f.ActivationId == binding && f.InstructionIndex == 0 &&
        f.WindowContext is { } context && CanRunIssuedShenZf(ShenZfFrameCandidate(f),context);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenZhangFeiShencai)] private PromptChoice AiShenZf(PendingDecision d,ProgramSkillFrame f) => d.Choices.First();
}
