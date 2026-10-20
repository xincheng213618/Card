namespace CardGame.Core;
public sealed record DemonSupportGrantedEvent(long FrameId, int OwnerSeat, int RecipientSeat, string SkillId) : IGameEvent;
internal sealed class GrantExclusiveRendeDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantExclusiveRende; }
internal sealed class GrantExclusiveQingnangDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantExclusiveQingnang; }
internal sealed class GrantExclusiveLongyinDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantExclusiveLongyin; }
