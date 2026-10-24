using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record AnniversaryZhangHengReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, string Stage)
{
    public int Donor { get; init; } = -1;
    public long Before { get; init; }
    public long After { get; init; }
    private IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryZhangHengReceipt? AnniversaryZhangHeng { get; init; } }
public sealed record AnniversaryZhangHengStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn) : IGameEvent;
public sealed record AnniversaryZhangHengDonorEvent(long FrameId, int Donor) : IGameEvent;
public sealed record AnniversaryZhangHengMovedEvent(long FrameId, int Donor, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryZhangHengCompletedEvent(long FrameId) : IGameEvent;
internal interface IAnniversaryZhangHengHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f); }
internal sealed class ResolveAnniversaryZhangHengDangzaiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryZhangHengHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class AnniversaryZhangHengHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryZhangHengHost>().Begin(f);
}
internal static class AnniversaryZhangHengComposition
{
    internal const string Liangjue = "anniversary:liangjue", Dangzai = "anniversary:dangzai";
    [ProgramCatalogValidation(49600)] internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai);
        if (p.Id is not (Liangjue or Dangzai) && !owns) return;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Zhang Heng requires real native movement and Play-start bindings.");
        if (p.Id == Dangzai && p.Triggers is [{ Id: "take-delayed-subset", Window: SkillProgramTriggerWindow.PlayPhaseStarting, Subject: SkillProgramTriggerSubject.Owner,
                Optional: true, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }]) return;
        if (p.Id != Liangjue || p.Triggers.Count != 4) throw new InvalidOperationException(path + ": Liangjue requires all four real per-entity field boundaries.");
        foreach (var t in p.Triggers)
        {
            var (window, zone) = t.Id switch
            {
                "black-equipment-leaves" => (SkillProgramTriggerWindow.CardsMoved, CardZoneKind.Equipment),
                "black-judgment-leaves" => (SkillProgramTriggerWindow.CardsMoved, CardZoneKind.Judgment),
                "black-equipment-enters" => (SkillProgramTriggerWindow.CardsGained, CardZoneKind.Equipment),
                "black-judgment-enters" => (SkillProgramTriggerWindow.CardsGained, CardZoneKind.Judgment),
                _ => throw new InvalidOperationException(path + ": Unknown Zhang Heng field binding.")
            };
            if (t.Window != window || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.MovementOccurrence != SkillProgramMovementOccurrence.PerCard ||
                !(window == SkillProgramTriggerWindow.CardsMoved ? t.SourceZones : t.DestinationZones).SequenceEqual([zone]) ||
                t.Effects is not [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 2, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.AwaitOwnedCardMovement, Target: SkillProgramEffectTarget.Owner },
                    { Op: SkillProgramEffectOp.LoseHp, Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition: { Kind: SkillProgramConditionKind.HpAtLeast, Value: 2 } }])
                throw new InvalidOperationException(path + ": Liangjue must draw two and drain its native observers before checking current HP>1 for each black entity.");
        }
    }
}
