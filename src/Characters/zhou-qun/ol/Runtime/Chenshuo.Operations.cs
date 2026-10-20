using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record ChenshuoHandShownEvent(long FrameId, int Index, ZhouQunShownCard Card) : IGameEvent;
public sealed record ChenshuoTopRevealedEvent(long FrameId, int Index, int CardId, CardKind Kind, Suit Suit, int Rank, int NameLength, bool Matched) : IGameEvent;
internal sealed class ChenshuoDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveChenshuoRepeatedReveal){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
