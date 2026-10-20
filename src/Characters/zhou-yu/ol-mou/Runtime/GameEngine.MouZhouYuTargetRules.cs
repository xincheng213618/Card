namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.NativeUseMaterialQualification, 10)]
    private bool MouGuquNativeUseMaterialQualified(ProgramNativeUseMaterialQuery query) =>
        IsMouGuquUseAllowed(query.ActorSeat, query.Kind, query.PhysicalCardIds, query.Source, query.ExistingUseFrameId);
}
