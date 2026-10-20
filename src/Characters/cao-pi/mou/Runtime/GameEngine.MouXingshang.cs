using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouXingshang(CharacterState owner) => HasRuntimeSkill(owner, "mou:xingshang");
    private static bool HasPrintedMouXingshang(CharacterState owner) => owner.General.Skills.Concat(owner.SecondaryGeneral?.Skills ?? []).Any(s => s.ContentId == "mou:xingshang");
    private string[] MouCaoPiPrintedSkills(CharacterState dead) => dead.General.Skills.Concat(dead.SecondaryGeneral?.Skills ?? [])
        .Where(s => s.ContentId is { } id && !_contentRegistry.GetSkill(id).Tags.HasFlag(SkillTag.Lord))
        .Select(s => s.ContentId!).Distinct(StringComparer.Ordinal).ToArray();
    [ProgramNativeDrawReturn(SkillProgramEffectOp.MouCaoPiXingshang)]
    private void ReturnMouCaoPiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MouCaoPi is not { Stage: MouCaoPiStage.DrawRequest, Option: "draw" } r || f.NativeDraw is not null ||
            outcome.RecipientSeat != r.TargetSeat || outcome.RequestedCount != 3 || outcome.ActualDrawIds.Count > 3 ||
            outcome.SequenceBefore < r.MovementBefore || outcome.SequenceAfter < outcome.SequenceBefore ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0)
            throw new InvalidOperationException("Xingshang lost its exact paid draw request or confused replacement gains with drawn cards.");
        ReplaceRuntimeTop(f = f with { MouCaoPi = r with { Stage = MouCaoPiStage.DrawChildren, MovementBefore = outcome.SequenceBefore,
            MovementAfter = outcome.SequenceAfter, ActualDraw = outcome.ActualDrawIds.Count } });
        ContinueMouCaoPi(f.Id);
    }
}
