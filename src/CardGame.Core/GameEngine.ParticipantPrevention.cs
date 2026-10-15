namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome PreventParticipantDamageDrawActualSource(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.WindowContext is not {Window:SkillProgramTriggerWindow.BeforeDamageApplied} c || _resolutionStack.Count<2 ||
            _resolutionStack[^2] is not BeforeDamageProgramWindowFrame parent || parent.Id!=c.ParentFrameId || parent.Prevented || parent.Amount<=0 ||
            !IsValidPlayerSeat(parent.SourceSeat) || parent.SourceSeat==parent.TargetSeat ||
            f.OwnerSeat!=parent.SourceSeat && f.OwnerSeat!=parent.TargetSeat || !_players[parent.SourceSeat].IsAlive ||
            c.SourceSeat!=parent.SourceSeat || c.TargetSeat!=parent.TargetSeat || c.Amount!=parent.Amount)
            throw new InvalidOperationException("Participant prevention lost its exact sourced damage attempt.");
        var before=_movementSequence;
        PreventProgramCurrentDamage(f);
        DrawProgramCards(f.Id,parent.SourceSeat,2,null,null,SkillProgramCardSetVisibility.Private,
            new($"skill-program.{f.SkillId}.prevented-damage-draw"));
        var after=_movementSequence;
        var actual=_cardMovements.Count(m=>m.Sequence>before && m.Sequence<=after && m.From==CardLocation.DrawPile &&
            m.To==CardLocation.Hand(parent.SourceSeat) && m.Reason.Value==$"skill-program.{f.SkillId}.prevented-damage-draw");
        f=GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with {PreventionDrawReceipt=new(f.InstructionIndex-1,parent.Id,parent.SourceSeat,parent.TargetSeat,parent.Amount,2,before,after,actual)});
        return AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat);
    }
    private bool IsParticipantPreventionDraw(ProgramSkillFrame f) => f.InstructionIndex>0 &&
        ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect.Op==
        SkillProgramEffectOp.PreventParticipantDamageDrawActualSource;
    [ProgramCapability]
    private sealed class ParticipantPreventionCapability(GameEngine engine) : IParticipantPreventionHost
    {public SkillProgramStepOutcome PreventParticipantDamageDrawActualSource(ProgramSkillFrame f)=>engine.PreventParticipantDamageDrawActualSource(f);}
}
