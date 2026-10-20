using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 28000, SkillProgramEffectOp.MouCaoPiSongwei)]
    private bool CanGainMouSongwei(ProgramTriggerLegalityContext c) => c.Owner.Role == Role.Lord && HasMouXingshang(c.Owner);
}
