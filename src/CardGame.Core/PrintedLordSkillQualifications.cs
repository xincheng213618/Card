namespace CardGame.Core;

// A qualification belongs to one existing printed grant, never to a role or
// an acquired skill id. The issuing skill need not remain live afterwards.
public sealed record PrintedLordSkillQualification(int BeneficiarySeat, string GeneralId,
    string TemplateSourceId, string GrantId, string SkillId, string SkillInstanceId,
    long ProgramFrameId, CardConversionSource Issuer, string GameplayHash);

internal static class PrintedLordSkillQualifications
{
    internal static bool Matches(CharacterState owner, SkillGrant grant)
    {
        if (grant.PrintedLordQualification is not { } q || q.BeneficiarySeat != owner.Seat ||
            q.GrantId != grant.GrantId || q.SkillId != grant.SkillId || q.SkillInstanceId != grant.SkillInstanceId ||
            q.TemplateSourceId != grant.SourceId || grant.LordProjection is not null || grant.GeneralLibraryProjection is not null ||
            q.ProgramFrameId <= 0 || q.Issuer.OwnerSeat == owner.Seat ||
            string.IsNullOrWhiteSpace(q.Issuer.SkillId) || string.IsNullOrWhiteSpace(q.Issuer.BindingId) ||
            string.IsNullOrWhiteSpace(q.Issuer.SkillInstanceId) || string.IsNullOrWhiteSpace(q.GameplayHash)) return false;
        var general = grant.SourceId switch
        {
            CharacterState.PrimarySkillSource => owner.General,
            CharacterState.SecondarySkillSource => owner.SecondaryGeneral,
            _ => null
        };
        return general?.Id == q.GeneralId && general.Skills.Any(s => s.ContentId == grant.SkillId && s.Tags.HasFlag(SkillTag.Lord));
    }
}

// Hidden printed-general slots stay on the trusted owning receipt/grant. This
// public fact announces qualification without disclosing slot ids or skills.
public sealed record PrintedLordQualificationIssuedEvent(long ProgramFrameId, int BeneficiarySeat,
    CardConversionSource Issuer, string GameplayHash) : IGameEvent;

public sealed partial class GameEngine
{
    private bool IsPrintedLordGrantQualified(CharacterState owner, SkillGrant grant) =>
        PrintedLordSkillQualifications.Matches(owner, grant);

    // Menu and execution share this selection only for a newly qualified
    // printed Lord grant. Explicit acquired instances never borrow its right.
    // This is called after binding construction, never from its pure matcher.
    private string? PreferredQualifiedPrintedLordInstance(CharacterState owner, string skillId)
    {
        if (owner.Role == Role.Lord || !owner.SkillGrants.Grants.Any(g => g.SkillId == skillId && IsPrintedLordGrantQualified(owner, g))) return null;
        if (!_contentRegistry.GetSkill(skillId).Tags.HasFlag(SkillTag.Lord)) return null;
        return GetSkillBindingShard(owner).ActiveGrants.Where(g => g.SkillId == skillId && IsPrintedLordGrantQualified(owner, g))
            .OrderBy(g => g.SkillInstanceId, StringComparer.Ordinal).ThenBy(g => g.GrantId, StringComparer.Ordinal)
            .Select(g => g.SkillInstanceId).FirstOrDefault();
    }

    private IReadOnlyList<PrintedLordSkillQualification> CapturePrintedLordQualifications(ProgramSkillFrame f, int seat)
    {
        var target = _players[seat]; var issuer = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        return Array.AsReadOnly(target.SkillGrants.Grants.Where(g =>
                g.SourceId is CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource &&
                g.LordProjection is null && g.GeneralLibraryProjection is null && _contentRegistry.GetSkill(g.SkillId).Tags.HasFlag(SkillTag.Lord))
            .Select(g => new PrintedLordSkillQualification(seat,
                (g.SourceId == CharacterState.PrimarySkillSource ? target.General : target.SecondaryGeneral)!.Id,
                g.SourceId, g.GrantId, g.SkillId, g.SkillInstanceId, f.Id, issuer, f.GameplayHash)).ToArray());
    }

    private void IssueCapturedPrintedLordQualifications(ProgramSkillFrame f, PrintedLordBenefitReceipt r)
    {
        var target = _players[r.BeneficiarySeat];
        foreach (var q in r.PrintedQualifications)
        {
            var grant = target.SkillGrants.Grants.SingleOrDefault(g => g.GrantId == q.GrantId && g.SkillId == q.SkillId &&
                g.SourceId == q.TemplateSourceId && g.SkillInstanceId == q.SkillInstanceId);
            if (grant is null || !PrintedLordSkillQualifications.Matches(target, grant with { PrintedLordQualification = q })) continue;
            // Preserve an already qualified exact grant's original provenance.
            if (!PrintedLordSkillQualifications.Matches(target, grant)) target.SkillGrants.SetPrintedLordQualification(grant.GrantId, q);
        }
        AdvanceEventRulesAndQueueFact(new PrintedLordQualificationIssuedEvent(f.Id, r.BeneficiarySeat, r.Source, r.GameplayHash));
    }
}
