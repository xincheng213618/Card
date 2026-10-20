using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueCaoXiDraw(ProgramSkillFrame f)
    {
        var r = f.CaoXiJianxuan!; var before = _movementSequence; var cursor = checked(r.Cursor + 1);
        ReplaceRuntimeTop(f = f with { CaoXiJianxuan = r with { Cursor = cursor, Before = before, After = before, ActualDrawCount = 0 } });
        var drawn = DrawCards(_players[r.RecipientSeat!.Value], 1, true, new(CaoXiDrawReason));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { CaoXiJianxuan = f.CaoXiJianxuan! with { After = _movementSequence, ActualDrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new CaoXiJianxuanDrawPaidEvent(f.Id, cursor, r.RecipientSeat.Value, drawn.Count, before, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
}
