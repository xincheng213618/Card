using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource DyingSuitsSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private static string DyingSuitsDrawReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.draw";
    private static string DyingSuitsDiscardReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.discard";
    private bool HasIssuedDyingSuitsRound(int owner, string skill) => CompleteProgramEventHistory().OfType<DyingSuitsDrawIssuedEvent>()
        .Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.ActualRound == _roundNumber);
    private int[] DyingSuitsRecipients(int owner, int victim) => _players.Where(p => p.IsAlive && p.Seat != owner && p.Seat != victim).Select(p => p.Seat).ToArray();
    private bool CanRunDyingSuitsAndEndingHistory(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach))
            return context.Window == SkillProgramTriggerWindow.DyingEntering && context.TargetSeat is { } victim && IsValidPlayerSeat(victim) &&
                _players[victim].IsAlive && _players[victim].Hp <= 0 && _roundNumber > 0 && !HasIssuedDyingSuitsRound(candidate.OwnerSeat, candidate.SkillId) &&
                DyingSuitsRecipients(candidate.OwnerSeat, victim).Length > 0;
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.UseOwnPlayHistoryAtEnding))
            return context.Window == SkillProgramTriggerWindow.TurnEnding && candidate.OwnerSeat == _currentSeat && OwnPlayHistory(candidate.OwnerSeat).Count > 0;
        return true;
    }
    private SkillProgramStepOutcome BeginDyingSuits(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.DyingSuits is not null || !ExactDyingOwnedCardEntry(f, out var dying, out var entry))
            throw new InvalidOperationException("Dying suits must start on the exact original entry/candidate/victim.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !_players[dying.VictimSeat].IsAlive || _players[dying.VictimSeat].Hp > 0 || HasIssuedDyingSuitsRound(f.OwnerSeat, f.SkillId))
        { FinishProgramSkill(f, false); return SkillProgramStepOutcome.AwaitChild; }
        var r = new DyingSuitsReceipt(1, DyingSuitsSource(f), f.GameplayHash, _roundNumber, _turnNumber, _currentSeat,
            entry.Id, dying.Id, dying.VictimSeat, CaptureDyingSuitsOriginalCursor(dying, entry), DyingSuitsStage.Recipient, [], [], []);
        ReplaceRuntimeTop(f = f with { DyingSuits = r });
        AdvanceEventRulesAndQueueFact(new DyingSuitsOriginalCursorIssuedEvent(f.Id, DyingSuitsCursorHash(r.OriginalCursor)));
        PublishDyingSuitsChoice(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DyingSuitsChoices(ProgramSkillFrame f)
    {
        var r = f.DyingSuits!; var result = new List<PromptChoice>();
        PromptChoice Choice(string key, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => new(new($"dying-suits.{f.Id}.{key}"), label, cards, targets,
            new Dictionary<string,string> { ["program-action"]="dying-suits", ["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture), ["option"]=key });
        if (r.Stage == DyingSuitsStage.Recipient)
            foreach (var seat in DyingSuitsRecipients(f.OwnerSeat, r.VictimSeat)) result.Add(Choice($"recipient-{seat}", $"令 {_players[seat].Name} 摸四张牌，然后弃置四张牌", [], [seat]));
        else if (r.Stage == DyingSuitsStage.SelectingDiscard && r.RecipientSeat is { } payer)
            foreach (var c in DyingSuitsDiscardCandidates(f, payer).Where(c => !r.SelectedCardIds.Contains(c.Id)))
                result.Add(Choice($"discard-{c.Id}", $"弃置【{c.DisplayName}】（{r.SelectedCardIds.Count + 1}/{r.RequiredDiscardCount}）", [c.Id], []));
        return Array.AsReadOnly(result.ToArray());
    }
    private Card[] DyingSuitsDiscardCandidates(ProgramSkillFrame f, int payer) => GetHand(_players[payer]).Concat(GetEquipment(_players[payer])).Where(c =>
        !c.IsGeneralWeapon && !IsSelfHandCategoryDiscardForbidden(payer, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        !IsForeignEquipmentDiscardPrevented(payer, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        !(payer == f.OwnerSeat && IsActiveProgramSourceEquipmentCard(payer, f.SkillId, f.SkillInstanceId, c))).OrderBy(c => c.Id).ToArray();
    private PromptChoice SelectAiDyingSuits(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.DyingSuits!; var recipientChoice = r.Stage == DyingSuitsStage.Recipient;
        var suits = recipientChoice ? new Dictionary<int, Suit>() : DyingSuitsDiscardCandidates(f, decision.PlayerSeat)
            .Where(c => decision.Choices.Any(choice => choice.Cards.Contains(c.Id)))
            .ToDictionary(c => c.Id, c => GetProgramEffectiveSuit(_players[decision.PlayerSeat], c));
        Suit[] selectedSuits = recipientChoice ? [] : r.SelectedCardIds.Select(id => GetProgramEffectiveSuit(_players[decision.PlayerSeat], GetAttackCard(id))).ToArray();
        var (choice, thought) = _aiBrains[decision.PlayerSeat].ChooseDyingSuits(CreateSnapshot(decision.PlayerSeat),
            decision.Choices, r.VictimSeat, recipientChoice, suits, selectedSuits, r.RequiredDiscardCount, ++_thoughtSequence);
        AddThought(thought); return choice;
    }
    private void PublishDyingSuitsChoice(ProgramSkillFrame f)
    {
        var r = f.DyingSuits!; var seat = r.Stage == DyingSuitsStage.Recipient ? f.OwnerSeat : r.RecipientSeat!.Value;
        var choices = DyingSuitsChoices(f); if (choices.Count == 0) { FinishProgramSkill(f, true); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, r.Stage == DyingSuitsStage.Recipient ? "请选择另一名其他角色。" : "请选择本次实际弃置的牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId=CreatePromptId(), IsPrivate=true, TargetSeat=seat, Choices=choices, SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveDyingSuitsChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertDyingSuits(f); var r = f.DyingSuits!;
        var expected = DyingSuitsChoices(f).SingleOrDefault(c => c.Id == choice.Id);
        if (expected is null || !expected.Cards.SequenceEqual(choice.Cards) || !expected.Targets.SequenceEqual(choice.Targets))
            throw new InvalidOperationException("Dying suits lost its private exact published selection.");
        ClearPendingDecision();
        if (r.Stage == DyingSuitsStage.Recipient)
        {
            var seat = choice.Targets.Single();
            if (_winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) || !_players[f.OwnerSeat].IsAlive ||
                !_players[r.VictimSeat].IsAlive || _players[r.VictimSeat].Hp > 0 || HasIssuedDyingSuitsRound(f.OwnerSeat,f.SkillId)) { FinishProgramSkill(f,false); return; }
            var before = RecipientContestSequence;
            ReplaceRuntimeTop(f = f with { DyingSuits = r with { Stage=DyingSuitsStage.Drawing, RecipientSeat=seat, DrawBefore=before, DrawAfter=before }, PendingMovementContinuation=new(seat,0,null) });
            var cards = DrawCards(_players[seat],4,log:true,reason:new(DyingSuitsDrawReason(f)));
            f = GetActiveProgramFrame(f.Id); r = f.DyingSuits! with { DrawAfter=RecipientContestSequence, ActualDrawCount=cards.Count };
            ReplaceRuntimeTop(f with { DyingSuits=r });
            AdvanceEventRulesAndQueueFact(new DyingSuitsDrawIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualRound,r.ActualTurn,r.TurnOwnerSeat,r.EntryFrameId,r.DyingFrameId,r.VictimSeat,seat,r.DrawBefore,r.DrawAfter,r.ActualDrawCount));
            AdvanceRuntimeProgram(f.Id); return;
        }
        var payer = r.RecipientSeat!.Value; var id = choice.Cards.Single(); var location = _cardZones.GetLocation(id);
        var ids = r.SelectedCardIds.Append(id).ToArray(); var locations = r.SelectedLocations.Append(location).ToArray();
        ReplaceRuntimeTop(f = f with { DyingSuits=r with { SelectedCardIds=ids, SelectedLocations=locations } });
        if (ids.Length < r.RequiredDiscardCount) { PublishDyingSuitsChoice(f); return; }
        r = f.DyingSuits!;
        if (_winner != Winner.None || !_players[payer].IsAlive || ids.Where((card,i) => _cardZones.GetLocation(card) != locations[i]).Any() ||
            ids.Any(card => !DyingSuitsDiscardCandidates(f,payer).Any(c => c.Id == card))) { FinishProgramSkill(f,true); return; }
        var suits = ids.Select(card => GetProgramEffectiveSuit(_players[payer], GetAttackCard(card))).ToArray(); var costBefore = RecipientContestSequence;
        ReplaceRuntimeTop(f = f with { DyingSuits=r with { Stage=DyingSuitsStage.Discarding, DiscardSuits=suits, DiscardBefore=costBefore, DiscardAfter=costBefore }, PendingMovementContinuation=new(payer,0,null) });
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(DyingSuitsDiscardReason(f)), (_, records) =>
        {
            var root = GetActiveProgramFrame(f.Id); var paid = root.DyingSuits! with { DiscardAfter=records.Max(m => m.Sequence) };
            ReplaceRuntimeTop(root with { DyingSuits=paid });
            AdvanceEventRulesAndQueueFact(new DyingSuitsDiscardPaidEvent(f.Id,payer,paid.DiscardBefore,paid.DiscardAfter,ids,suits));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumeDyingSuits(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.DyingSuits is not { } r) return false;
        AssertDyingSuits(f);
        if (r.Stage is DyingSuitsStage.Recipient or DyingSuitsStage.SelectingDiscard) { if (_pendingDecision is null) PublishDyingSuitsChoice(f); return true; }
        if (r.Stage is DyingSuitsStage.Drawing or DyingSuitsStage.Discarding)
        {
            if (TryDrainDyingSuits(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation=null });
            var recipient = r.RecipientSeat!.Value;
            if (_winner != Winner.None || !_players[recipient].IsAlive || !_players[r.VictimSeat].IsAlive)
            { FinishProgramSkill(f,true); return true; }
            if (r.Stage == DyingSuitsStage.Drawing)
            {
                var count = Math.Min(4,DyingSuitsDiscardCandidates(f,recipient).Length);
                if (count == 0) { FinishProgramSkill(f,true); return true; }
                ReplaceRuntimeTop(f = f with { DyingSuits=r with { Stage=DyingSuitsStage.SelectingDiscard, RequiredDiscardCount=count } }); PublishDyingSuitsChoice(f); return true;
            }
            if (r.SelectedCardIds.Count != 4 || r.DiscardSuits.Distinct().Count() != 4 || r.DiscardSuits.Contains(Suit.None) || _players[r.VictimSeat].Hp > 0 ||
                !CanUsePeachToRescue(recipient,r.VictimSeat) || IsCardUseForbidden(recipient,CardKind.Peach,CardActionType.Use) ||
                IsDirectedCardTargetProhibited(recipient,r.VictimSeat,CardKind.Peach) || IsCardTargetProhibited(_players[r.VictimSeat],CardKind.Peach,Suit.None) ||
                HasBeneficiarySuitShield(recipient,r.VictimSeat,Suit.None)) { FinishProgramSkill(f,true); return true; }
            IssueDyingSuitsPeach(f); return true;
        }
        if (r.Stage == DyingSuitsStage.Complete) { FinishProgramSkill(f,true); return true; }
        throw new InvalidOperationException("A dying-suits Peach cannot resume its root before its typed card-use return.");
    }
    private bool TryDrainDyingSuits(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private bool ReturnDyingSuitsMovement(ProgramSkillFrame f)
    { if (f.DyingSuits is null) return false; if (!IsDyingSuitsMovement(f,null,f.PendingMovementContinuation!)) throw new InvalidOperationException("Dying suits lost its paid movement return."); AdvanceRuntimeProgram(f.Id); return true; }
    private bool IsDyingSuitsMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) => pending is not null &&
        f.DyingSuits is { Stage:DyingSuitsStage.Drawing or DyingSuitsStage.Discarding, RecipientSeat:{ } recipient } &&
        (effect is null || effect.Op == SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach) && pending.SubjectSeat == recipient && pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidDyingSuitsReceipt(f);
    private void IssueDyingSuitsPeach(ProgramSkillFrame f)
    {
        var r = f.DyingSuits!; var actor = r.RecipientSeat!.Value; var useId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,null,CardActionType.Use,actor,actor,null,null,null,CardKind.Peach,[r.VictimSeat],[],[],effectiveSuit:Suit.None,effectiveRank:0));
        var ret = new DyingSuitsPeachReturn(f.Id,f.InstructionIndex,r.Source,f.GameplayHash,r.DyingFrameId,r.VictimSeat,actor,useId,action.ActionId);
        ReplaceRuntimeTop(f with { DyingSuits=r with { Stage=DyingSuitsStage.PeachIssued,PeachReturn=ret } });
        PushRuntimeFrame(new CardUseFrame(useId,actor,0,CardKind.Peach,[r.VictimSeat],PhysicalCardIds:[]) { Action=action,DyingSuitsPeachReturn=ret });
        AdvanceEventRulesAndQueueFact(new DyingSuitsPeachIssuedEvent(ret));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(useId,0,CardKind.Peach,actor)); AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(useId,[r.VictimSeat]));
        RecordYingboCardUse(useId,actor,CardKind.Peach); RecordProgramUsedBasicCard(actor,CardKind.Peach); RecordActualPlayPhaseUse(action);
        BeginSimpleCardUse(useId,new(0,SimpleCardUseEffect.Recovery));
    }
    private bool ContinueDyingSuitsPeach(long id, ProgramSimpleCardContinuation continuation)
    {
        var use = LifecycleCardUse(id)!; if (use.DyingSuitsPeachReturn is not { } ret) return false;
        if (continuation.CardId != 0 || continuation.Effect != SimpleCardUseEffect.Recovery || !ValidDyingSuitsPeachUse(use,ret))
            throw new InvalidOperationException("Dying suits lost its genuine zero-material Peach producer.");
        SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);
        if (_winner == Winner.None && _players[ret.ActorSeat].IsAlive)
            CompleteRecoveryCardUse(_players[ret.ActorSeat],_players[ret.VictimSeat],new(0,CardKind.Peach,Suit.None,0),id,CardKind.Peach,1,[]);
        else FinishCardUse(id,new(0,CardKind.Peach,Suit.None,0),CardKind.Peach);
        return true;
    }
    private void ReturnDyingSuitsPeach(CardUseFrame use)
    {
        if (use.DyingSuitsPeachReturn is not { } ret) return; var f = GetActiveProgramFrame(ret.ProgramFrameId);
        if (f.DyingSuits is not { Stage:DyingSuitsStage.PeachIssued } r || r.PeachReturn != ret || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Original-Dying Peach returned without its complete exact use.");
        ReplaceRuntimeTop(f with { DyingSuits=r with { Stage=DyingSuitsStage.Complete } }); AdvanceEventRulesAndQueueFact(new DyingSuitsPeachReturnedEvent(ret)); AdvanceRuntimeProgram(f.Id);
    }
    private sealed partial class ProgramSkillHost : IDyingSuitsAndEndingHistoryHost
    {
        public SkillProgramStepOutcome DrawDiscardSuits(ProgramSkillFrame f) => engine.BeginDyingSuits(f);
        public SkillProgramStepOutcome EndingHistoricalUses(ProgramSkillFrame f) => engine.BeginEndingHistoricalUses(f);
    }
}
