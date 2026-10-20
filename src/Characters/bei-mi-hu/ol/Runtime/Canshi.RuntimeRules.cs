using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_bei_mi_hu_ol_Canshi
{
    internal static int[] BeiMiHuPrimaryTargets(CardUseFrame use)=>(use.CardKind==CardKind.DrawTwo && use.TargetSeats.Count==0?[use.SourceSeat]:
        use.CardKind==CardKind.BorrowedSword?use.TargetSeats.Where((_,i)=>i%2==0):use.TargetSeats).Distinct().ToArray();

    internal static bool BeiMiHuMutationEqual(BeiMiHuTargetMutation x,BeiMiHuTargetMutation y)=>x.ProgramFrameId==y.ProgramFrameId && x.CardUseId==y.CardUseId && x.ActionId==y.ActionId && x.Source==y.Source &&
        x.GameplayHash==y.GameplayHash && x.Turn==y.Turn && x.Kind==y.Kind && CardGame.Core.RuntimeSkills.RuntimeRules_bei_mi_hu_ol_Canshi.BeiMiHuActionIdentityEqual(x.OriginalAction,y.OriginalAction) && x.BeforeTargets.SequenceEqual(y.BeforeTargets) && x.AfterTargets.SequenceEqual(y.AfterTargets) && x.PaidMarkerSeats.SequenceEqual(y.PaidMarkerSeats);

    internal static bool BeiMiHuActionIdentityEqual(CardActionContext x,CardActionContext y)=>x is not null && y is not null && x.ActionId==y.ActionId && x.ParentActionId==y.ParentActionId && x.Type==y.Type &&
        x.ActorSeat==y.ActorSeat && x.ProviderSeat==y.ProviderSeat && x.RequesterSeat==y.RequesterSeat && x.ResponderSeat==y.ResponderSeat && x.OpponentSeat==y.OpponentSeat && x.EffectiveKind==y.EffectiveKind &&
        x.PhysicalCards.SequenceEqual(y.PhysicalCards) && x.ConversionChain.SequenceEqual(y.ConversionChain) && x.EffectiveSuit==y.EffectiveSuit && x.EffectiveRank==y.EffectiveRank && x.EffectiveIsRed==y.EffectiveIsRed && x.FactionOrigin==y.FactionOrigin;
}
