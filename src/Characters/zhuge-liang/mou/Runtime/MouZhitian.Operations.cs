namespace CardGame.Core;
internal sealed class MouZhitianViewDescriptor : MouZhugeOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MouZhitianView; }
public sealed record MouZhitianFirePayment(int CardId, Suit RevealedSuit, long MovementSequence);
