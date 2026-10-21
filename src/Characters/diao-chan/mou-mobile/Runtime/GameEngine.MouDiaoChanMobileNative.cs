namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueMouDiaoChanDuel(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.MouDiaoChanMobile!;
        while (_winner == Winner.None && r.Index < r.Targets.Count)
        {
            var actor = r.Targets[r.Index];
            var candidates = r.Targets.Where(s => s != actor && _players[s].IsAlive).OrderBy(s => RelativeTableOrder(s, actor)).ToArray();
            if (_players[actor].IsAlive && candidates is [var target, ..] && CanIssueSelectedActorDuel(target, actor))
            {
                var ring = new MouDiaoChanMobileRingOrigin(f.Id, r.Index, actor, target, r.Source, r.GameplayHash);
                AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileDuelPlannedEvent(ring));
                var origin = new ProgramSelectedActorDuelOrigin(f.Id, f.InstructionIndex, f.OwnerSeat, actor, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, _turnNumber)
                    { InitialTargetSeat = target, MouDiaoChanRing = ring };
                var card = new Card(0, CardKind.Duel, Suit.None, 0);
                var id = BeginCardUse(card, actor, [target], CardKind.Duel, physicalCardIds: [], selectedActorDuelOrigin: origin);
                var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id);
                ReplaceRuntimeFrame(f.Id, parent with { MouDiaoChanMobile = r with { UseId = id } });
                var use = LifecycleCardUse(id)!;
                AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileDuelIssuedEvent(ring, id, use.Action!.ActionId));
                BeginJizhiOrNullificationWindow(id, card, actor, [target], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
                return;
            }
            r = r with { Index = r.Index + 1 };
            ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r });
        }
        FinishMouDiaoChan(f);
    }
    private void AssertMouDiaoChanRingUse(CardUseFrame use, ProgramSelectedActorDuelOrigin origin)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (origin.MouDiaoChanRing is not { } ring || at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { MouDiaoChanMobile: { } r } f ||
            !ValidMouDiaoChan(f) || !r.Paid || r.Stage != MouDiaoChanMobileStage.Duels || r.Index != ring.Index ||
            r.Index >= r.Targets.Count || r.Targets[r.Index] != ring.ActorSeat || (r.UseId != 0 && r.UseId != use.Id) ||
            ring.FrameId != f.Id || ring.Source != r.Source || ring.GameplayHash != r.GameplayHash || ring.TargetSeat == ring.ActorSeat || !r.Targets.Contains(ring.TargetSeat) ||
            origin.ParentProgramFrameId != f.Id || origin.InstructionIndex != f.InstructionIndex || origin.OwnerSeat != f.OwnerSeat || origin.InitialActorSeat != ring.ActorSeat || origin.InitialTargetSeat != ring.TargetSeat ||
            origin.SkillId != f.SkillId || origin.ActivationId != f.ActivationId || origin.SkillInstanceId != f.SkillInstanceId || origin.GameplayHash != f.GameplayHash || origin.TurnNumber != r.Phase.TurnNumber ||
            origin.OrderedPairPayment is not null || origin.ParticipantMode is not null || use.CardId != 0 || use.CardKind != CardKind.Duel || use.PhysicalCardIds?.Count != 0 ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveIsRed: false } ||
            ProgramEventHistory<MouDiaoChanMobileDuelPlannedEvent>().Count(e => e.Origin == ring) != 1)
            throw new InvalidOperationException("Lijian Duel lost its exact paid ring origin and original owning frame.");
    }
    [NativeUseReturn(31700)]
    private bool ReturnMouDiaoChanRingUse(CardUseFrame use)
    {
        if (use.SelectedActorDuelOrigin?.MouDiaoChanRing is not { } ring || use.SelectedActorDuelOrigin.AttackStarted) return false;
        CompleteMouDiaoChanRingDuel(ring.FrameId, ring.Index, use.Id); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 31700)]
    private bool ReturnMouDiaoChanRingAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDiaoChanMobile: { Stage: MouDiaoChanMobileStage.Duels } r } f || r.UseId != completion.ResolutionId) return false;
        if (completion.ProgramFrameId != f.Id || !completion.RequestedSlashChild) throw new InvalidOperationException("Lijian lost its original completely returned native Duel.");
        CompleteMouDiaoChanRingDuel(f.Id, r.Index, r.UseId); return true;
    }
    private void CompleteMouDiaoChanRingDuel(long frameId, int index, long useId)
    {
        var f = GetActiveProgramFrame(frameId); var r = f.MouDiaoChanMobile!;
        if (!ValidMouDiaoChan(f) || !r.Paid || r.Stage != MouDiaoChanMobileStage.Duels || r.UseId != useId || r.Index != index ||
            ProgramEventHistory<MouDiaoChanMobileDuelIssuedEvent>().Count(e => e.UseId == useId && e.Origin.FrameId == frameId && e.Origin.Index == index && e.Origin.Source == r.Source) != 1 ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId && e.CardId == 0 && e.CardKind == CardKind.Duel) != 1 ||
            ProgramEventHistory<MouDiaoChanMobileDuelReturnedEvent>().Any(e => e.FrameId == frameId && e.Index == index))
            throw new InvalidOperationException("Lijian cannot repeat a completed Duel or borrow another use's return.");
        AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileDuelReturnedEvent(frameId, index, useId));
        ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Index = r.Index + 1, UseId = 0 } });
        IssueMouDiaoChanDuel(f);
    }
}
