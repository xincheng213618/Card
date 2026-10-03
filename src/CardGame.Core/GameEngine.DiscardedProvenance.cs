namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksDiscardedEntityProvenance => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance);
    private void CaptureProvenanceJudgmentOrigin(JudgmentFrame judgment, Card card, Suit effectiveSuit)
    {
        if (!TracksDiscardedEntityProvenance) return;
        var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id);
        if (entry is null || entry.To != _cardZones.GetLocation(card.Id) || entry.To != CardLocation.Processing && entry.To != CardLocation.Judgment(judgment.TargetSeat)) return;
        AdvanceEventRulesAndQueueFact(new ProgramJudgmentEntityOriginEvent(judgment.Id, judgment.TargetSeat, card.Id, effectiveSuit, entry.Sequence));
    }
    private void CaptureDiscardedEntityOrigin(Card card, CardMovementRecord movement)
    {
        if (!TracksDiscardedEntityProvenance || movement.To != CardLocation.DiscardPile) return;
        var previous = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.Sequence < movement.Sequence);
        var judged = CompleteProgramEventHistory().OfType<ProgramJudgmentEntityOriginEvent>().LastOrDefault(e => e.CardId == card.Id);
        if (judged is not null && previous is not null && previous.To == movement.From &&
            (previous.Sequence == judged.EntryMovementSequence &&
                (movement.From == CardLocation.Processing || movement.From == CardLocation.Judgment(judged.SubjectSeat)) ||
             IsProvenanceJudgmentResultHandoff(card.Id, judged, previous, movement.From)))
        {
            AdvanceEventRulesAndQueueFact(new ProgramDiscardedEntityOriginEvent(movement.Sequence, judged.SubjectSeat, judged.EffectiveSuit, judged.JudgmentFrameId)); return;
        }
        if (GetProgramDiscardSource(movement)?.OwnerSeat is not { } source) return;
        AdvanceEventRulesAndQueueFact(new ProgramDiscardedEntityOriginEvent(movement.Sequence, source, EffectiveSuit(_players[source], card), null));
    }
    private bool IsProvenanceJudgmentResultHandoff(int cardId, ProgramJudgmentEntityOriginEvent judged,
        CardMovementRecord previous, CardLocation currentFrom)
    {
        // A program's final judgment result has exactly one typed handoff to
        // Processing before its configured disposition. No other Processing
        // movement inherits a judgment origin.
        if (currentFrom != CardLocation.Processing || previous.To != CardLocation.Processing ||
            previous.From != CardLocation.Judgment(judged.SubjectSeat) ||
            previous.Reason.Value != "skill-program.judgment.result") return false;
        var entry = _cardMovements.LastOrDefault(m => m.CardId == cardId && m.Sequence < previous.Sequence);
        return entry is not null && entry.Sequence == judged.EntryMovementSequence && entry.To == previous.From &&
            CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Any(e => e.ResolutionId == judged.JudgmentFrameId &&
                e.TargetSeat == judged.SubjectSeat && e.CardId == cardId && e.Suit == judged.EffectiveSuit);
    }
    private ProgramDiscardedEntityOriginEvent? DiscardedProvenanceOrigin(CardMovementRecord movement) =>
        CompleteProgramEventHistory().OfType<ProgramDiscardedEntityOriginEvent>().SingleOrDefault(e => e.MovementSequence == movement.Sequence);
    private int[] MatchingProvenanceDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate, SkillProgramTrigger trigger)
    {
        var current = _cardZones.CardsAt(CardLocation.DiscardPile).ToDictionary(c => c.Id);
        return batch.Movements.Select((m,i) => (m,i,origin:DiscardedProvenanceOrigin(m)))
            .Where(x => x.origin is not null && x.m.To == CardLocation.DiscardPile && x.origin.SourceSeat != candidate.OwnerSeat &&
                current.ContainsKey(x.m.CardId) && (trigger.Suits.Count == 0 || trigger.Suits.Contains(x.origin.EffectiveSuit)))
            .Select(x => x.i).ToArray();
    }
    private sealed partial class ProgramSkillHost : IDiscardedProvenanceProgramHost
    {
        public SkillProgramStepOutcome ClaimDiscardedEntityWithProvenance(ProgramSkillFrame frame, string provenanceId) => engine.BeginDiscardedProvenanceClaim(frame,provenanceId);
        public SkillProgramStepOutcome OfferFaceUpForOutsideClaims(ProgramSkillFrame frame, string provenanceId) => engine.OfferProvenanceFaceUp(frame,provenanceId);
        public SkillProgramStepOutcome UseVirtualAlcohol(ProgramSkillFrame frame) => engine.BeginProvenanceVirtualAlcohol(frame);
    }
    private SkillProgramStepOutcome BeginDiscardedProvenanceClaim(ProgramSkillFrame input, string tag)
    {
        var f=GetActiveProgramFrame(input.Id);
        if (f.ProvenanceClaim is not null || f.WindowContext is not { Window:SkillProgramTriggerWindow.DiscardPileReceived,MovementBatch:{ } batch,MovementIndex:{ } index } || index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("A provenance claim lost its exact per-entity discard window.");
        var movement=batch.Movements[index]; var origin=DiscardedProvenanceOrigin(movement);
        if (origin is null || origin.SourceSeat==f.OwnerSeat || movement.To!=CardLocation.DiscardPile || !_cardMovements.Contains(movement))
            throw new InvalidOperationException("A provenance claim lost its captured discard/judgment origin.");
        if (_winner!=Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) || _cardZones.GetLocation(movement.CardId)!=CardLocation.DiscardPile)
        { CancelProgramBindingAndCleanup(f,"落英实体或来源已失效，未领取牌。"); return SkillProgramStepOutcome.AwaitChild; }
        var card=_cardZones.CardsAt(CardLocation.DiscardPile).Single(c=>c.Id==movement.CardId);
        ReplaceRuntimeTop(f with { ProvenanceClaim=new(f.InstructionIndex,tag,card.Id,movement.Sequence,0,ProgramProvenanceClaimStage.Claiming) });
        MoveCard(card,CardLocation.DiscardPile,CardLocation.Hand(f.OwnerSeat),new($"skill-program.{f.SkillId}.provenance-claim"), beforeFact: claimed =>
        {
            var active=GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { ProvenanceClaim=active.ProvenanceClaim! with { ClaimMovementSequence=claimed.Sequence } });
            AdvanceEventRulesAndQueueFact(new ProgramDiscardedEntityClaimedEvent(f.Id,new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),tag,card.Id,movement.Sequence,claimed.Sequence,_turnNumber,_currentSeat));
        });
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeProvenanceClaim(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=id || f.ProvenanceClaim is not { } receipt) return false;
        AssertProvenanceClaim(f);
        if (receipt.Stage is ProgramProvenanceClaimStage.Claiming or ProgramProvenanceClaimStage.Flipping)
        {
            if (TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id)) return true;
            if (receipt.Stage==ProgramProvenanceClaimStage.Flipping)
            { ReplaceRuntimeTop(f with { ProvenanceClaim=null }); return false; }
            ReplaceRuntimeTop(f with { ProvenanceClaim=receipt with { Stage=ProgramProvenanceClaimStage.Claimed } }); return false;
        }
        return receipt.Stage==ProgramProvenanceClaimStage.Choosing;
    }
    private bool ProvenanceThresholdQualified(ProgramSkillFrame f, ProgramProvenanceClaimReceipt receipt) =>
        _winner==Winner.None && _players[f.OwnerSeat].IsAlive && _players[f.OwnerSeat].IsFaceDown && _currentSeat!=f.OwnerSeat &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) &&
        CardPolicies(_players[f.OwnerSeat],SkillProgramCardPolicyKind.ClaimedEntitiesFaceDownUse).Any(p=>p.Policy.Id==receipt.ProvenanceId) &&
        GetOutsideProvenanceCount(f.OwnerSeat,f.SkillId,f.SkillInstanceId,receipt.ProvenanceId)>=_players[f.OwnerSeat].MaxHp;
    private int GetOutsideProvenanceCount(int seat,string skill,string instance,string tag)
    {
        var facts=CompleteProgramEventHistory().ToArray();
        var cutoff=Array.FindLastIndex(facts,e=>e is TurnStartedEvent t && t.ActorSeat==seat || e is ProgramProvenanceFaceUpResetEvent r && r.OwnerSeat==seat);
        return facts.Skip(cutoff+1).OfType<ProgramDiscardedEntityClaimedEvent>().Count(e=>e.Origin.OwnerSeat==seat && e.Origin.SkillId==skill && e.Origin.SkillInstanceId==instance && e.ProvenanceId==tag && e.ActualTurnOwnerSeat!=seat);
    }
    private SkillProgramStepOutcome OfferProvenanceFaceUp(ProgramSkillFrame input,string tag)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.ProvenanceClaim is not { Stage:ProgramProvenanceClaimStage.Claimed } receipt || receipt.ProvenanceId!=tag || f.InstructionIndex!=receipt.InstructionIndex+1)
            throw new InvalidOperationException("The provenance threshold lost its completed one-entity claim.");
        if (!ProvenanceThresholdQualified(f,receipt)) { ReplaceRuntimeTop(f with { ProvenanceClaim=null }); return SkillProgramStepOutcome.Continue; }
        ReplaceRuntimeTop(f with { ProvenanceClaim=receipt with { Stage=ProgramProvenanceClaimStage.Choosing } });
        PromptChoice Choice(string option,string label)=>new(new($"provenance-face.{f.Id}.{option}"),label,[],[],new Dictionary<string,string>{["program-action"]="provenance-face-up",["frame-id"]=f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),["option"]=option});
        var policy=CardPolicies(_players[f.OwnerSeat],SkillProgramCardPolicyKind.ClaimedEntitiesFaceDownUse).First(p=>p.Policy.Id==tag);
        var skill=_contentRegistry.GetSkill(policy.Source.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"回合外累计获得落英牌已达到体力上限，可以翻至正面。",[],[],f.OwnerSeat)
        { PromptId=CreatePromptId(),IsPrivate=true,Choices=Array.AsReadOnly(new[]{Choice("flip","翻至正面"),Choice("pass","保持背面")}), SkillPrompt=new(policy.Source.SkillId,skill.Name,skill.Name,skill.Description) };
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveProvenanceFaceUp(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.ProvenanceClaim is not { Stage:ProgramProvenanceClaimStage.Choosing } receipt || _pendingDecision?.PlayerSeat!=f.OwnerSeat || choice.Cards.Count!=0 || choice.Targets.Count!=0 || choice.Parameters.GetValueOrDefault("frame-id")!=f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) || choice.Parameters.GetValueOrDefault("option") is not ("flip" or "pass"))
            throw new InvalidOperationException("A provenance face-up choice lost its exact unpaid owner.");
        var flip=choice.Parameters["option"]=="flip" && ProvenanceThresholdQualified(f,receipt);
        ClearPendingDecision();
        if (!flip) { ReplaceRuntimeTop(f with { ProvenanceClaim=null }); AdvanceRuntimeProgram(f.Id); return; }
        ReplaceRuntimeTop(f with { ProvenanceClaim=receipt with { Stage=ProgramProvenanceClaimStage.Flipping } });
        SetProgramTargetFaceState(f.Id,f.OwnerSeat,f.OwnerSeat,false);
        AdvanceRuntimeProgram(f.Id);
    }
    private void RecordProvenanceFaceUpReset(int owner,bool wasFaceDown)
    {
        if (TracksDiscardedEntityProvenance && wasFaceDown && !_players[owner].IsFaceDown)
            AdvanceEventRulesAndQueueFact(new ProgramProvenanceFaceUpResetEvent(owner,_turnNumber));
    }
    private void AssertProvenanceClaim(ProgramSkillFrame f)
    {
        if (f.ProvenanceClaim is not { } r) return;
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions;
        if(r.InstructionIndex<1 || r.InstructionIndex>=plan.Count || plan[r.InstructionIndex-1].Op!=SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance || plan[r.InstructionIndex-1].SourceBind!=r.ProvenanceId || plan[r.InstructionIndex].Op!=SkillProgramEffectOp.OfferFaceUpForOutsideClaims ||
            f.InstructionIndex!=(r.Stage is ProgramProvenanceClaimStage.Claiming or ProgramProvenanceClaimStage.Claimed?r.InstructionIndex:r.InstructionIndex+1) ||
            !CompleteProgramEventHistory().OfType<ProgramDiscardedEntityClaimedEvent>().Any(e=>e.FrameId==f.Id && e.CardId==r.CardId && e.DiscardMovementSequence==r.DiscardMovementSequence && e.ClaimMovementSequence==r.ClaimMovementSequence && e.Origin==new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId) && e.ProvenanceId==r.ProvenanceId) ||
            !_cardMovements.Any(m=>m.Sequence==r.ClaimMovementSequence && m.CardId==r.CardId && m.From==CardLocation.DiscardPile && m.To==CardLocation.Hand(f.OwnerSeat)))
            throw new InvalidOperationException("A provenance claim lost its immutable exact entity receipt.");
        if (r.Stage==ProgramProvenanceClaimStage.Choosing && _resolutionStack.LastOrDefault()?.Id==f.Id &&
            (_pendingDecision is not { Kind:DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat!=f.OwnerSeat || prompt.Choices.Count!=2 || prompt.Choices.Any(c=>c.Cards.Count!=0 || c.Targets.Count!=0 || c.Parameters.GetValueOrDefault("program-action")!="provenance-face-up")))
            throw new InvalidOperationException("A provenance threshold lost its exact optional chooser.");
    }
}
