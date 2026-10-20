namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<Card> MingchaRandomCards(int seat) => Array.AsReadOnly(GetHand(_players[seat]).Concat(GetEquipment(seat)).OrderBy(c => c.Id).ToArray());
    private bool IsMingchaNormalDrawReplacement(ProgramSkillFrame f)=>f.WindowContext is{Window:SkillProgramTriggerWindow.DrawPhaseStarting}context&&
        ProgramEventHistory<RuanHuiStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==RuanHuiSource(f)&&e.GameplayHash==f.GameplayHash&&e.ParentId==context.ParentFrameId&&e.Op==SkillProgramEffectOp.ResolveMingchaRevealDrawChoice)==1&&
        ProgramEventHistory<RuanHuiDecisionEvent>().Count(e=>e.FrameId==f.Id&&e.ReplaceDraw)==1&&ProgramEventHistory<RuanHuiCompletedEvent>().Count(e=>e.FrameId==f.Id)==1;
}
