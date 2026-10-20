namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouXiahouShiQiaoshi(ProgramSkillFrame f)
    {
        if (f.MouXiahouShiQiaoshi is not { } r || f.MouXiahouShiYanyu is not null || r.Source != MouXiahouShiSource(f) || r.GameplayHash != f.GameplayHash ||
            !ExactMouXiahouShiDamageContext(f) || !Enum.IsDefined(r.Stage) || r.WindowId != f.WindowContext!.ParentFrameId || r.DamageFrameId != f.WindowContext.DamageFrameId ||
            r.DamageSourceSeat != f.WindowContext.SourceSeat || r.DamageAmount != f.WindowContext.Amount || r.DamageSourceSeat == f.OwnerSeat ||
            r.Before < 0 || r.Before > r.After || r.After > _movementSequence || _contentRegistry.GetSkill(f.SkillId).Program is not { } p ||
            p.GameplayHash != f.GameplayHash || !MouXiahouShiComposition.IsQiaoshi(p)) return false;
        var start = new MouXiahouShiQiaoshiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.WindowId, r.DamageFrameId, r.AttackResolutionId, r.DamageSourceSeat, r.DamageAmount);
        if (ProgramEventHistory<MouXiahouShiQiaoshiStartedEvent>().Count(e => e == start) != 1 || !MouXiahouShiTurnBefore(start, r.Turn)) return false;
        var branches = ProgramEventHistory<MouXiahouShiQiaoshiBranchAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Branch is null) return r.Stage == MouXiahouShiQiaoshiStage.SourceChoice && branches.Length == 0 && r.Draw is null && !r.RecoveryReturned && r.RequestedRecovery == 0 && r.BaseRecovery == 0 && r.RecoveryHpBefore == 0;
        if (r.Branch is not ("recover-and-draw" or "decline") || branches is not [var branch] ||
            branch != new MouXiahouShiQiaoshiBranchAcceptedEvent(f.Id, r.Source, r.Turn, r.DamageSourceSeat, r.Branch) || r.Stage == MouXiahouShiQiaoshiStage.SourceChoice) return false;
        if (r.Branch == "decline") return r.Stage == MouXiahouShiQiaoshiStage.RecoveryChildren && r.RequestedRecovery == 0 && r.BaseRecovery == 0 && r.Draw is null && !r.RecoveryReturned;
        if (r.RequestedRecovery != r.DamageAmount || r.BaseRecovery < 0 || r.BaseRecovery > r.RequestedRecovery ||
            ProgramEventHistory<MouXiahouShiQiaoshiRecoveryIssuedEvent>().Count(e => e == new MouXiahouShiQiaoshiRecoveryIssuedEvent(f.Id, r.DamageSourceSeat, f.OwnerSeat, r.DamageAmount, r.RequestedRecovery, r.RecoveryHpBefore, r.BaseRecovery)) != 1) return false;
        var returned = ProgramEventHistory<MouXiahouShiQiaoshiRecoveryReturnedEvent>().Count(e => e == new MouXiahouShiQiaoshiRecoveryReturnedEvent(f.Id, r.DamageSourceSeat, f.OwnerSeat, r.RequestedRecovery));
        if (returned != (r.RecoveryReturned ? 1 : 0) || r.Stage is MouXiahouShiQiaoshiStage.Drawing or MouXiahouShiQiaoshiStage.DrawChildren && !r.RecoveryReturned) return false;
        return r.Stage == MouXiahouShiQiaoshiStage.DrawChildren ? r.Draw is { } draw && MouXiahouShiDrawSigned(f, draw, r.DamageSourceSeat, 2) : r.Draw is null;
    }
}
