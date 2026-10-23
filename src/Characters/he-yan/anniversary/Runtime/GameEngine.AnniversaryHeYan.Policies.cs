namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsAnniversaryHeYanHandUseForbidden(int seat, IReadOnlyList<int> ids) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryHeYanYachai) &&
        ProgramEventHistory<AnniversaryHeYanHandUseBannedEvent>().Any(e => e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat && e.AffectedSeat == seat) &&
        ids.Any(id => id > 0 && _cardZones.GetLocation(id) == CardLocation.Hand(seat));
    private bool HeYanResponseIsUse(int actor, CardKind kind, bool dying = false) => dying || kind == CardKind.Nullification || IsProgramResponseCardUse(_players[actor], kind);
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 44200)]
    private bool HeYanMaterialBan(ProgramCardMaterialQuery q) => (!q.IsResponse || HeYanResponseIsUse(q.ActorSeat, q.Kind)) && IsAnniversaryHeYanHandUseForbidden(q.ActorSeat, q.CardIds.ToArray());
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 44200)]
    private bool HeYanNativeResponseBan(ProgramNativeResponseQuery q) => HeYanResponseIsUse(q.Provider.Seat, q.Kind, q.DyingUse) && IsAnniversaryHeYanHandUseForbidden(q.Provider.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 44200)]
    private bool HeYanViewAsBan(ProgramViewAsLegalityQuery q) => (!q.ForResponse || q.RequestedUseTarget is not null || HeYanResponseIsUse(q.Owner.Seat, q.Kind, q.DyingUse)) && IsAnniversaryHeYanHandUseForbidden(q.Owner.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 44200)]
    private bool HeYanLegalUseBan(ProgramLegalActionQuery q) => q.Action.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.EndPlay or LegalActionKind.UseEquipmentEffect) &&
        q.Action.CardId is { } id && IsAnniversaryHeYanHandUseForbidden(q.Actor.Seat, [id]);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 44200)] private void HeYanSubmittedUseBan(ProgramSubmittedUseQuery q)
    { if (IsAnniversaryHeYanHandUseForbidden(q.ActorSeat, q.CardIds)) throw new InvalidOperationException("Yachai prohibits using Hand materials for the remainder of this actual turn."); }
}
