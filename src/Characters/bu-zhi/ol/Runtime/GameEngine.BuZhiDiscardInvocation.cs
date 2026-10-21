namespace CardGame.Core;
public sealed partial class GameEngine
{
    private sealed record NativeBuZhiDiscardInvocationDraft(long Id, long FrameId, CardConversionSource Source, string GameplayHash,
        int InstructionIndex, int Turn, int TurnOwner, CardMoveReason Reason, long Before,
        IReadOnlyList<CardZoneKind> Zones, IReadOnlyList<NativeBuZhiDiscardMaterial> Eligible);
    private NativeBuZhiDiscardInvocationDraft? CaptureNativeBuZhiDiscardInvocation(ProgramSkillFrame frame, IReadOnlyList<CardZoneKind> zones, CardMoveReason reason)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit) || !_setupComplete || _winner != Winner.None || _turnNumber < 1) return null;
        var active = GetActiveProgramFrame(frame.Id);
        if (_resolutionStack.LastOrDefault()?.Id != active.Id || active.OwnerSeat != frame.OwnerSeat || active.GameplayHash != frame.GameplayHash || active.InstructionIndex < 1) throw new InvalidOperationException("Native discard aggregation requires its exact current owning program.");
        var effect = ProgramInstructionResolver.Default.Resolve(active,_contentRegistry.GetSkill(active.SkillId).Program!).GetPausedInstruction(active.InstructionIndex).Effect;
        if (effect?.Op != SkillProgramEffectOp.DiscardOwnedZoneCards || !effect.Zones.SequenceEqual(zones) || zones.Count == 0 || zones.Distinct().Count()!=zones.Count || zones.Any(z=>z is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment))) throw new InvalidOperationException("Native discard aggregation cannot capture another opcode or invented zones.");
        var eligible = zones.SelectMany(z => _cardZones.CardsAt(new(z,active.OwnerSeat)).Where(c => !IsSelfHandCategoryDiscardForbidden(active.OwnerSeat,c,new(z,active.OwnerSeat),OwnedCardMoveIntent.Discard)).Select(c=>new NativeBuZhiDiscardMaterial(c.Id,c.Kind,new(z,active.OwnerSeat),NormalizeEphemeralCardDestination(c, new(z,active.OwnerSeat), (c.IsGeneralWeapon && z==CardZoneKind.Equipment || IsGuanduThunderChariotDeparture(c,new(z,active.OwnerSeat),CardLocation.DiscardPile))?CardLocation.OutsideGame:CardLocation.DiscardPile),0))).ToArray();
        return new(++_resolutionSequence,active.Id,new(active.SkillId,GetProgramBindingId(active),active.OwnerSeat,active.SkillInstanceId),active.GameplayHash,active.InstructionIndex,_turnNumber,_turnProgression.OwnerSeat,reason,_movementSequence,Array.AsReadOnly(zones.ToArray()),Array.AsReadOnly(eligible));
    }
    private void CompleteNativeBuZhiDiscardInvocation(NativeBuZhiDiscardInvocationDraft? draft, IReadOnlyList<int> actualIds)
    {
        if(draft is null || actualIds.Count==0)return;
        var f=GetActiveProgramFrame(draft.FrameId);
        if(_resolutionStack.LastOrDefault()?.Id!=f.Id || new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId)!=draft.Source || f.GameplayHash!=draft.GameplayHash || f.InstructionIndex!=draft.InstructionIndex || _turnNumber!=draft.Turn || _turnProgression.OwnerSeat!=draft.TurnOwner || actualIds.Distinct().Count()!=actualIds.Count)throw new InvalidOperationException("One synchronous native discard invocation lost its owning source or repeated an entity.");
        var after=_movementSequence;
        var materials=_cardMovements.Where(m=>m.Sequence>draft.Before && m.Sequence<=after && m.From.OwnerSeat==draft.Source.OwnerSeat && draft.Zones.Contains(m.From.Zone) && m.Reason==draft.Reason)
            .OrderBy(m=>m.Sequence).Select(m=>new NativeBuZhiDiscardMaterial(m.CardId,m.CardKind,m.From,m.To,m.Sequence)).ToArray();
        if(!materials.Select(m=>m.CardId).SequenceEqual(actualIds) || materials.Any(m=>!draft.Eligible.Any(e=>e.CardId==m.CardId && e.PrintedKind==m.PrintedKind && e.From==m.From && e.To==m.To)))throw new InvalidOperationException("Native discard aggregation lost its exact physical invocation result.");
        var last=materials[^1]; var index=_pendingCardsMovedBatches.FindLastIndex(b=>b.ParentFrameId==draft.FrameId && b.OriginSkillId==draft.Source.SkillId && b.OriginSkillInstanceId==draft.Source.SkillInstanceId && b.OriginOwnerSeat==draft.Source.OwnerSeat && b.Movements.Any(m=>m.Sequence==last.MovementSequence && m.CardId==last.CardId));
        if(index<0 || _pendingCardsMovedBatches[index].NativeBuZhiDiscardInvocation is not null)throw new InvalidOperationException("Native discard invocation lost its last unclaimed genuine batch.");
        var proof=new NativeBuZhiDiscardInvocationProof{InvocationId=draft.Id,ParentFrameId=draft.FrameId,Source=draft.Source,GameplayHash=draft.GameplayHash,InstructionIndex=draft.InstructionIndex,ActualTurnNumber=draft.Turn,ActualTurnOwnerSeat=draft.TurnOwner,Reason=draft.Reason,SequenceBefore=draft.Before,SequenceAfter=after,LastBatchId=_pendingCardsMovedBatches[index].Id,Zones=draft.Zones,Materials=materials};
        _pendingCardsMovedBatches[index]=_pendingCardsMovedBatches[index] with{NativeBuZhiDiscardInvocation=proof};
        AdvanceEventRulesAndQueueFact(NativeBuZhiDiscardFact(proof));
    }
    private static NativeBuZhiDiscardInvocationRecordedEvent NativeBuZhiDiscardFact(NativeBuZhiDiscardInvocationProof p)=>new(p.InvocationId,p.ParentFrameId,p.Source,p.GameplayHash,p.InstructionIndex,p.ActualTurnNumber,p.ActualTurnOwnerSeat,p.Reason,p.SequenceBefore,p.SequenceAfter,p.LastBatchId,p.ActualCount,p.Zones.Aggregate(0,(mask,z)=>mask | (z==CardZoneKind.Hand?1:z==CardZoneKind.Equipment?2:4)));
    private bool ValidNativeBuZhiDiscardInvocation(NativeBuZhiDiscardInvocationProof p)
    {
        if(p.InvocationId<=0 || p.ParentFrameId<=0 || p.InstructionIndex<1 || !IsValidPlayerSeat(p.Source.OwnerSeat) || !IsValidPlayerSeat(p.ActualTurnOwnerSeat) || p.ActualTurnNumber<1 || p.ActualCount<1 || p.LastBatchId<=0 || p.SequenceBefore<0 || p.SequenceAfter<=p.SequenceBefore || p.SequenceAfter>_movementSequence || p.Materials.Select(m=>m.CardId).Distinct().Count()!=p.ActualCount || p.Materials.Select(m=>m.MovementSequence).Distinct().Count()!=p.ActualCount || p.Zones.Count==0 || p.Zones.Distinct().Count()!=p.Zones.Count || p.Zones.Any(z=>z is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)) || _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash!=p.GameplayHash || string.IsNullOrWhiteSpace(p.Source.BindingId) || string.IsNullOrWhiteSpace(p.Source.SkillInstanceId))return false;
        var issued=CompleteProgramEventHistory().OfType<NativeBuZhiDiscardInvocationRecordedEvent>().Where(e=>e.InvocationId==p.InvocationId).ToArray(); if(issued is not [var fact] || fact!=NativeBuZhiDiscardFact(p))return false;
        var actual=_cardMovements.Where(m=>m.Sequence>p.SequenceBefore && m.Sequence<=p.SequenceAfter && m.From.OwnerSeat==p.Source.OwnerSeat && p.Zones.Contains(m.From.Zone) && m.Reason==p.Reason).OrderBy(m=>m.Sequence).Select(m=>new NativeBuZhiDiscardMaterial(m.CardId,m.CardKind,m.From,m.To,m.Sequence)).ToArray();
        return actual.SequenceEqual(p.Materials) && p.Materials.All(m=>m.From.OwnerSeat==p.Source.OwnerSeat && p.Zones.Contains(m.From.Zone) && (m.To==CardLocation.DiscardPile || IsEphemeralCardDestroyedMaterial(m.CardId,m.From,m.To) || m.From.Zone==CardZoneKind.Equipment && m.To==CardLocation.OutsideGame && (GetAdvancedCard(m.CardId).IsGeneralWeapon ||
            m.PrintedKind==CardKind.ThunderChariot && _cardMovements.SingleOrDefault(move=>move.Sequence==m.MovementSequence) is { } destroyed &&
            IsExactGuanduChariotDestruction(new(m.CardId,m.PrintedKind,m.From),destroyed))));
    }
}
