namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ConvertingGifts
{
    internal static TurnEndingBoundaryItem CreateGiftRetentionEndingItem(GiftHandRetentionObligation due)=>
        new(TurnEndingBoundaryItemKind.GiftRetention,0,$"program:{due.Source.SkillId}:{due.Source.BindingId}:{due.Source.SkillInstanceId}:due:{due.Id}",RetentionId:due.Id,RetentionOwnerSeat:due.Source.OwnerSeat);
}
