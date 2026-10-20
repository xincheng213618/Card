using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record KuangshouQuotaFrozenEvent(long FrameId, int HistoryCutoff, int DamagedSeatCount, int RequiredDiscard) : IGameEvent;
internal sealed class ResolveKuangshouDamageDrawDiscardDescriptor : HanFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard; }
