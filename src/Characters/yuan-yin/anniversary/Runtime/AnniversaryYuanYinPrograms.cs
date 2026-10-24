using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record AnniversaryYuanYinReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, string Mode, string Stage)
{
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int Requested { get; init; }
    public int Next { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long DeathId { get; init; }
    public int Victim { get; init; } = -1;
    public int Recipient { get; init; } = -1;
    public int? CardId { get; init; }
    public CardLocation? From { get; init; }
    public HappyWeiYanGainRequestSettlement? Gain { get; init; }
    public bool Delivered { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryYuanYinReceipt? AnniversaryYuanYin { get; init; } }
public sealed record AnniversaryYuanYinStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, string Mode) : IGameEvent;
public sealed record AnniversaryYuanYinMoshouIssuedEvent(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, int Requested, int Next) : IGameEvent;
public sealed record AnniversaryYuanYinMoshouAdvancedEvent(long FrameId, CardConversionSource Source, string Hash, int Requested, int Next, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryYuanYinCompletedEvent(long FrameId) : IGameEvent;
public sealed record AnniversaryYuanYinGiftPaidEvent(long FrameId, long DeathId, int Victim, int Recipient, bool Delivered, long Before, long After) : IGameEvent;
public sealed record AnniversaryYuanYinBenefitIssuedEvent(long FrameId, string Benefit, int Owner, int Requested) : IGameEvent;
public sealed record AnniversaryYuanYinDeathWindowIssuedEvent(long WindowId, long DeathId, int Victim, int? Killer, int CandidateCount) : IGameEvent;
public sealed record AnniversaryYuanYinDeathWindowCompletedEvent(long WindowId, long DeathId) : IGameEvent;
internal interface IAnniversaryYuanYinHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class AnniversaryYuanYinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryYuanYinHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.CharacterDied)];
}
internal sealed class ResolveAnniversaryYuanYinMoshouDescriptor : AnniversaryYuanYinDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou; }
internal sealed class ResolveAnniversaryYuanYinYunjiuDescriptor : AnniversaryYuanYinDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu; }
internal sealed class AnniversaryYuanYinHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryYuanYinHost>().Begin(f, op);
}
internal static class AnniversaryYuanYinComposition
{
    internal const string Moshou = "anniversary:moshou", Yunjiu = "anniversary:yunjiu";
    [ProgramCatalogValidation(49300)] internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou or SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu);
        if (p.Id is not (Moshou or Yunjiu) && !owns) return;
        var first = p.Id == Moshou;
        if (p.Id is not (Moshou or Yunjiu) || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Triggers is not [var t] || !t.Optional || t.UsageScope is not null || t.UsageLimit is not null ||
            t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            e.Op != (first ? SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou : SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu) ||
            (first ? t.Id != "black-target-cycle" || t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Target || t.Condition.Kind != SkillProgramTriggerConditionKind.CardActionCardIsBlack :
                t.Id != "dead-owned-card-gift" || t.Window != SkillProgramTriggerWindow.CharacterDied || t.Subject != SkillProgramTriggerSubject.Owner))
            throw new InvalidOperationException(path + ": Yuan Yin requires his real black-target draw cycle and optional exact death-owned-card gift.");
    }
}
