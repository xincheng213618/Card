using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LianjuGainReason = "skill-program.li-wan.lianju.gain", SilvDrawReason = "skill-program.li-wan.silv.draw", SilvRefundReason = "skill-program.li-wan.silv.refund";
    [ProgramCapability]
    private sealed class LiWanCapability(GameEngine engine) : ILiWanProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.BeginLiWan(frame, op); }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26720)]
    private bool CanOfferLiWan(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects is not [{ } effect] || !LiWanComposition.Handles(effect.Op)) return true;
        if (effect.Op is SkillProgramEffectOp.ResolveLianjuOppositeColorReturn or SkillProgramEffectOp.ReturnDiscardedSilvEntity)
            return CanRunIssuedLiWanCandidate(c, context);
        return effect.Op == SkillProgramEffectOp.OfferLianjuSameColorGift
            ? context.Window == SkillProgramTriggerWindow.TurnEnding && context.SourceSeat == c.OwnerSeat && context.TargetSeat == c.OwnerSeat && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat)
            : context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat == c.OwnerSeat && context.Amount > 0 && context.DamageFrameId is not null;
    }
    private SkillProgramStepOutcome BeginLiWan(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.LiWan is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !CanOfferLiWan(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0, context.OccurrenceIndex), GetProgramTrigger(f), context))
            throw new InvalidOperationException("Li Wan requires her exact unissued native event candidate.");
        var r = new LiWanReceipt(LiWanSource(f), f.GameplayHash, op, f.InstructionIndex, context.ParentFrameId, _turnNumber, LianjuEndingSerial, LiWanStage.Color);
        if (op == SkillProgramEffectOp.ResolveLianjuOppositeColorReturn)
        {
            var promise = LianjuDuePromises()[context.OccurrenceIndex];
            r = r with { IsRed = !promise.IsRed, RecipientSeat = f.OwnerSeat, PromiseFrameId = promise.FrameId, Stage = LiWanStage.Cards, ChosenCards = [] };
            AdvanceEventRulesAndQueueFact(new LianjuPromiseConsumedEvent(promise.FrameId, context.ParentFrameId, LianjuEndingSerial));
        }
        if (op == SkillProgramEffectOp.ReturnDiscardedSilvEntity)
        {
            var move = context.MovementBatch!.Movements[context.MovementIndex!.Value];
            r = r with { RecipientSeat = GetProgramDiscardSource(move)!.Value.OwnerSeat!.Value, RefundSequence = move.Sequence, ChosenCards = [move.CardId] };
        }
        if (op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage)
        {
            var damage = _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == context.DamageFrameId)
                ?? throw new InvalidOperationException("Silv requires its real applied damage frame.");
            r = r with { RecipientSeat = f.OwnerSeat, Damage = new(damage.Id, damage.ParentFrameId, context.Amount, f.OwnerSeat) };
        }
        ReplaceRuntimeTop(f = f with { LiWan = r });
        AdvanceEventRulesAndQueueFact(new LiWanStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, r.ActualTurn, r.EndingSerial, r.PromiseFrameId, r.RefundSequence));
        if (op is SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage or SkillProgramEffectOp.ReturnDiscardedSilvEntity)
        { PayLiWan(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishLiWan(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> LiWanChoices(ProgramSkillFrame f)
    {
        var r = f.LiWan!; var list = new List<PromptChoice>();
        void Add(string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => list.Add(new(new($"li-wan.{f.Id}.{branch}"), label,
            cards ?? [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "li-wan", ["branch"] = branch }));
        if (r.Stage == LiWanStage.Color)
        { Add("red", "选择红色牌（可以不选牌）"); Add("black", "选择黑色牌（可以不选牌）"); }
        else if (r.Stage == LiWanStage.Recipient)
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)) Add($"recipient.{p.Seat}", $"令{p.Name}获得牌", targets: [p.Seat]);
        else if (r.Stage == LiWanStage.Cards)
        {
            if (r.ChosenCards is not { } selected || r.IsRed is not { } red) throw new InvalidOperationException("Lianju lost its frozen color draft.");
            if (selected.Count < 2)
                foreach (var id in LianjuEligibleCards(r.Op == SkillProgramEffectOp.OfferLianjuSameColorGift ? f.OwnerSeat : _currentSeat, red).Except(selected))
                    Add($"card.{id}", $"选择【{GetAdvancedCard(id).DisplayName}】", [id]);
            Add("finish", $"确定获得{selected.Count}张牌");
        }
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishLiWan(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = LiWanChoices(f);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "联句：选择颜色、获得者及至多两张同色的本回合使用牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("li-wan")]
    private void AnswerLiWan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LiWan: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !LiWanChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Li Wan lost her exact public native draft.");
        AssertLiWan(f); ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (r.Stage == LiWanStage.Color) r = r with { IsRed = branch == "red", Stage = LiWanStage.Recipient };
        else if (r.Stage == LiWanStage.Recipient) r = r with { RecipientSeat = choice.Targets.Single(), Stage = LiWanStage.Cards, ChosenCards = [] };
        else if (r.Stage == LiWanStage.Cards && branch != "finish") r = r with { ChosenCards = r.ChosenCards!.Concat(choice.Cards).ToArray() };
        else { PayLiWan(f); return; }
        ReplaceRuntimeTop(f = f with { LiWan = r }); PublishLiWan(f);
    }
    private void PayLiWan(ProgramSkillFrame f)
    {
        var r = f.LiWan!; var recipient = r.RecipientSeat ?? throw new InvalidOperationException("Li Wan has no frozen recipient.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, LiWan = r with { Stage = LiWanStage.Children, Before = before, After = before },
            PendingMovementContinuation = new(recipient, 0, null) });
        var count = 0;
        if (_players[recipient].IsAlive)
        {
            if (r.Op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage) count = DrawCards(_players[recipient], 1, true, new(SilvDrawReason)).Count;
            else
            {
                var ids = r.ChosenCards!.Where(id => _cardZones.GetLocation(id) == CardLocation.DiscardPile).ToArray();
                if (ids.Length != r.ChosenCards!.Count) throw new InvalidOperationException("Lianju cannot substitute or recycle a selected entity before payment.");
                if (ids.Length > 0) MoveCards(ids.Select(GetAdvancedCard).ToArray(), CardLocation.DiscardPile, CardLocation.Hand(recipient),
                    new(r.Op == SkillProgramEffectOp.ReturnDiscardedSilvEntity ? SilvRefundReason : LianjuGainReason));
                count = ids.Length;
            }
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { LiWan = f.LiWan! with { After = _movementSequence, Count = count } });
        AdvanceEventRulesAndQueueFact(new LiWanPaidEvent(f.Id, recipient, before, _movementSequence, count));
        if (r.Op == SkillProgramEffectOp.OfferLianjuSameColorGift)
            AdvanceEventRulesAndQueueFact(new LianjuPromiseGrantedEvent(f.Id, r.Source, r.GameplayHash, r.EndingSerial, r.ActualTurn, recipient, r.IsRed!.Value));
        if (r.Op != SkillProgramEffectOp.ReturnDiscardedSilvEntity)
        {
            var issuers = r.Op == SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage ? new[] { (Source: r.Source, Hash: r.GameplayHash) } :
                EnabledUniqueProgramTriggers(_players[f.OwnerSeat], SkillProgramTriggerWindow.AfterDamageApplied)
                    .Where(b => b.Trigger.Effects is [{ Op: SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage }])
                    .Select(b => (Source: new CardConversionSource(b.SkillId, b.Trigger.Id, f.OwnerSeat, b.SkillInstanceId), Hash: b.Program.GameplayHash)).ToArray();
            foreach (var move in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence && m.To == CardLocation.Hand(recipient) &&
                         m.Reason.Value is LianjuGainReason or SilvDrawReason).ToArray())
                foreach (var issuer in issuers.Take(1))
                    if (SilvMark(move.CardId) is null) AdvanceEventRulesAndQueueFact(new SilvEntityMarkedEvent(move.CardId, issuer.Source, issuer.Hash, f.Id, move.Sequence));
        }
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainLiWanChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26720)]
    private bool ResumeLiWan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LiWan: { } r } f || f.Id != id) return false;
        AssertLiWan(f);
        if (r.Stage != LiWanStage.Children) { if (_pendingDecision is null) PublishLiWan(f); return true; }
        if (DrainLiWanChildren(id)) return true;
        AdvanceEventRulesAndQueueFact(new LiWanCompletedEvent(id));
        ReplaceRuntimeTop(f = f with { LiWan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26720)]
    private bool ReturnLiWanMovement(ProgramSkillFrame f) => f.LiWan is not null && ResumeLiWan(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.OfferLianjuSameColorGift, SkillProgramEffectOp.ResolveLianjuOppositeColorReturn)]
    private PromptChoice AiLiWan(PendingDecision prompt, ProgramSkillFrame f) => f.LiWan!.Stage switch
    {
        LiWanStage.Color => prompt.Choices.First(c => c.Parameters["branch"] == (LianjuEligibleCards(f.OwnerSeat, true).Count >= LianjuEligibleCards(f.OwnerSeat, false).Count ? "red" : "black")),
        LiWanStage.Recipient => prompt.Choices.OrderBy(c => GetHand(_players[c.Targets[0]]).Count).First(),
        _ => prompt.Choices.FirstOrDefault(c => c.Cards.Count > 0) ?? prompt.Choices.Single()
    };
}
