namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int HuCheErZhong(CharacterState p) => HasAnniversaryHuCheErRules && HasProgramSkill(p, AnniversaryHuCheErComposition.Fuzhong) ? p.Markers.GetValueOrDefault(PlayerMarkerKind.AnniversaryHuCheErZhong) : 0;
    private int AnniversaryHuCheErDrawBonus(CharacterState p) => HuCheErZhong(p) >= 1 ? 1 : 0;
    private IEnumerable<RuleQueryContribution> AnniversaryHuCheErHandLimitContributions(CharacterState p) => HuCheErZhong(p) >= 3 ? [new FiniteRuleQueryContribution($"state:{p.Seat}:anniversary:fuzhong:hand-limit", SkillRuleOperation.Add, 3)] : [];
    [ProgramDistanceRule(ProgramDistanceStage.DistanceBaseTerms, 49500)] private void HuCheErOutgoingDistance(ProgramDistanceQuery q, List<RuleQueryBaseTerm> terms)
    { if (q.Source.Seat != q.Target.Seat && HuCheErZhong(q.Source) >= 2) terms.Add(new($"state:{q.Source.Seat}:anniversary:fuzhong:outgoing-distance", -2)); }
    private SkillProgramStepOutcome RecordHuCheErGain(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryHuCheErComposition.Fuzhong || f.TriggerId != "foreign-turn-gain" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { MovementBatch: { } batch } c || !HuCheErGain(f.OwnerSeat, c) ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent || parent.Batch.Id != batch.Id || parent.Batch.TurnNumber != batch.TurnNumber || !parent.Batch.Movements.SequenceEqual(batch.Movements) || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("Fuzhong requires an unpaid actual out-of-turn native Hand gain batch.");
        var before = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.AnniversaryHuCheErZhong);
        MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.AnniversaryHuCheErZhong, 1);
        AdvanceEventRulesAndQueueFact(new AnniversaryHuCheErGainRecordedEvent(f.Id, HuCheErSource(f), f.GameplayHash, batch.Id, batch.TurnNumber, batch.MovementTiming!.ActualTurnOwnerSeat, before, checked(before + 1)));
        return SkillProgramStepOutcome.Continue;
    }
    private SkillProgramStepOutcome SpendHuCheErFour(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryHuCheErComposition.Fuzhong || f.TriggerId != "ending-four" || f.ActivationId != f.TriggerId || f.InstructionIndex != 3 || f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } c ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent || parent.OwnerSeat != f.OwnerSeat || parent.TurnNumber != _turnNumber || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate) ||
            ProgramEventHistory<AnniversaryHuCheErFourRemovedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Fuzhong must remove exactly four marks after its original native ending damage returns.");
        var before = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.AnniversaryHuCheErZhong);
        if (before < 4) throw new InvalidOperationException("Fuzhong's original four marks are no longer available.");
        MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.AnniversaryHuCheErZhong, -4);
        AdvanceEventRulesAndQueueFact(new AnniversaryHuCheErFourRemovedEvent(f.Id, HuCheErSource(f), f.GameplayHash, _turnNumber, before, before - 4));
        return SkillProgramStepOutcome.Continue;
    }
    private IEnumerable<AnniversaryHuCheErSlashForbiddenEvent> ActiveHuCheErSlashBans(int actor) => !HasAnniversaryHuCheErRules || ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat) ? [] :
        ProgramEventHistory<AnniversaryHuCheErSlashForbiddenEvent>().Where(e => e.Actor == actor && e.Turn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat && e.Source.SkillId == AnniversaryHuCheErComposition.Daoji && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash &&
            ProgramEventHistory<AnniversaryHuCheErStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && s.Hash == e.Hash && s.UseId == e.UseId && s.ActionId == e.ActionId && s.Actor == actor && s.Turn == e.Turn && s.TurnOwner == e.TurnOwner) == 1);
    private bool HuCheErSlashBan(int actor, CardKind kind) => IsSlashCard(kind) && ActiveHuCheErSlashBans(actor).Any();
    [ProgramCardLegality(ProgramCardLegalityStage.BeforeIntrinsicUseBan, 49500)] private bool HuCheErUseBan(ProgramCardUseQuery q)
    {
        if (!HuCheErSlashBan(q.ActorSeat, q.Kind)) return false;
        // A ban applies to new declarations, not an already paid Use that rides
        // below the Weapon use which issued it. Action ids are native issuance order.
        return !(q.ActionType == CardActionType.Use && q.ExistingUseFrameId is { } id && LifecycleCardUse(id) is { Action: { Type: CardActionType.Use } a } use &&
            a.ActorSeat == q.ActorSeat && a.EffectiveKind == q.Kind && ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.SourceSeat == a.ActorSeat && e.CardKind == a.EffectiveKind) == 1 &&
            ActiveHuCheErSlashBans(q.ActorSeat).All(b => a.ActionId < b.ActionId));
    }
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 49500)] private bool HuCheErResponseBan(ProgramNativeResponseQuery q) => HuCheErSlashBan(q.Provider.Seat, q.Kind);
}
