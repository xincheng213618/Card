using System.Text.Json.Serialization;

namespace CardGame.Core;
// Guess and actual masks stay in the private owning frame; this public fact reveals no unchosen hand names or entities.
public sealed record DeruGuessCommittedEvent(long FrameId) : IGameEvent;
internal sealed class ResolveDeruBasicNamesDescriptor : YuanHuanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDeruBasicNames; }
