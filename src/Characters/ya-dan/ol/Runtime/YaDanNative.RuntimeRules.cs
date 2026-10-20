namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ya_dan_ol_YaDanNative
{
    internal static QingyaAction FreezeQingyaAction(LegalAction a)=>new(a.Kind,a.CardId!.Value,a.PlayedCardKind,a.TargetCardId,
        a.TargetSeats.Count>0?a.TargetSeats:a.TargetSeat is{}seat?[seat]:[]);
}
