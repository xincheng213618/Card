namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LianZhuReceipt? LianZhu { get; init; }
}
public sealed partial class GameEngine
{
    private sealed record XiaHuiMovementSource(CardConversionSource Source, string GameplayHash, Suit OriginalSuit);

    private IReadOnlyDictionary<int,IReadOnlyList<XiaHuiMovementSource>>? CaptureXiaHuiOriginalSources(IEnumerable<CardLocation> sources)
    {
        if (!_contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.XiaHuiBlackHandAndGainedEntityRestrictions)) return null;
        Dictionary<int,IReadOnlyList<XiaHuiMovementSource>>? frozen=null;
        foreach(var source in sources)
        {
            if(source.OwnerSeat is not {} owner || !_players[owner].IsAlive) continue;
            var policies = CardPolicies(_players[owner],SkillProgramCardPolicyKind.XiaHuiBlackHandAndGainedEntityRestrictions).ToArray();
            if(policies.Length == 0) continue;
            foreach(var card in _cardZones.CardsAt(source))
            {
                var suit = EffectiveSuit(_players[owner],card);
                if(suit is not (Suit.Spade or Suit.Club)) continue;
                (frozen??=[]).Add(card.Id,Array.AsReadOnly(policies.Select(item => new XiaHuiMovementSource(
                    new(item.Source.SkillId,item.Policy.Id,owner,item.Source.SkillInstanceId),
                    _contentRegistry.GetSkill(item.Source.SkillId).Program!.GameplayHash,suit)).ToArray()));
            }
        }
        return frozen is null ? null : new System.Collections.ObjectModel.ReadOnlyDictionary<int,IReadOnlyList<XiaHuiMovementSource>>(frozen);
    }
    private IEnumerable<ProgramTriggerCandidate> CollectBuZhiMovementCandidates(CardMovementBatchContext batch)
    {
        if(batch.Movements.Count<2 && (batch.NativeDrawInvocation?.ActualCount ?? 0)<2 &&
            (batch.NativeBuZhiDiscardInvocation?.ActualCount ?? 0)<2 ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit)) yield break;
        foreach(var owner in _players)
        {
            if(!owner.IsAlive || !BuZhiMovementQualifies(batch,owner.Seat)) continue;
            foreach(var candidate in CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.CardsMoved))
                if(GetProgramTrigger(candidate).Effects.Any(e=>e.Op==SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit)) yield return candidate;
        }
    }
}
