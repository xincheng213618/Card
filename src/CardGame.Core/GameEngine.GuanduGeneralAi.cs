using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramAiChoice(SkillProgramEffectOp.ResolveGuanduShicai, SkillProgramEffectOp.ResolveGuanduZezhu,
        SkillProgramEffectOp.ResolveGuanduJieliang, SkillProgramEffectOp.ResolveGuanduJieliangClaim,
        SkillProgramEffectOp.ResolveGuanduCangchu, SkillProgramEffectOp.ResolveGuanduSushou,
        SkillProgramEffectOp.ResolveGuanduPolu, SkillProgramEffectOp.ResolveGuanduThunderChariot,
        SkillProgramEffectOp.ResolveGuanduChoulue, SkillProgramEffectOp.ResolveGuanduHengjiang,
        SkillProgramEffectOp.ResolveGuanduHengjiangReward, SkillProgramEffectOp.ResolveGuanduXiying,
        SkillProgramEffectOp.ResolveGuanduBeizhan, SkillProgramEffectOp.ResolveGuanduYuanlve,
        SkillProgramEffectOp.ResolveGuanduMoushi, SkillProgramEffectOp.ResolveGuanduZhongjie,
        SkillProgramEffectOp.ResolveGuanduSuishiDying, SkillProgramEffectOp.ResolveGuanduSuishiDeath,
        SkillProgramEffectOp.ResolveGuanduFuyuan)]
    private PromptChoice SelectGuanduGeneralAi(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.SkillId == "guandu:yuanlve" && f.HuangQuan is { Stage: "offer" } pending)
        {
            var actions = JianjiActions(f);
            double NativeValue(PromptChoice c)
            {
                if (c.Parameters.GetValueOrDefault("option") == "decline") return 0;
                var a = actions[int.Parse(c.Parameters["option"], CultureInfo.InvariantCulture)];
                var harmful = a.Kind is LegalActionKind.Slash or LegalActionKind.Duel or LegalActionKind.FireAttack or LegalActionKind.Snatch or LegalActionKind.Dismantlement or LegalActionKind.UnexpectedAssault;
                if (harmful) return a.TargetSeats.Any(s => GuanduAllies(pending.RecipientSeat, s)) ? -100 : 20;
                return a.Kind == LegalActionKind.Intoxication && a.TargetSeats.Any(s => GuanduAllies(pending.RecipientSeat, s)) ? -50 : 10;
            }
            return decision.Choices.OrderByDescending(NativeValue).First();
        }
        var r = f.GuanduGeneral;
        if (r is null) return decision.Choices[0];
        double Value(PromptChoice c)
        {
            var option = c.Parameters.GetValueOrDefault("option");
            var keep = c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[decision.PlayerSeat]) : 0;
            var target = c.Targets.FirstOrDefault(-1);
            var allied = target >= 0 && GuanduAllies(f.OwnerSeat, target);
            if (option == "skip") return -10;
            return r.Stage switch
            {
                "shicai-cost" => _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault() is { } top ? GetKeepValue(top, _players[f.OwnerSeat]) - keep : -20,
                "jieliang-cost" => GuanduAllies(f.OwnerSeat, r.TargetSeat) ? -30 : 30 - keep,
                "jieliang-claim" => keep,
                "zezhu-take" => option == "take-hand" ? 7 : keep + 3,
                "zezhu-give" => -keep,
                "sushou-give" => (allied && target != f.OwnerSeat ? 50 : -50) - VaryGuanduRecipientHand(target) - keep,
                "xiying-cost" => 50 - keep,
                "xiying-answer" => option == "ban" ? GuanduAllies(f.OwnerSeat, decision.PlayerSeat) ? 10 : -20 : -keep,
                "beizhan-target" => allied ? 50 + _players[target].MaxHp - VaryGuanduRecipientHand(target) : -50,
                "zhongjie-target" => allied ? 60 - _players[target].Hp : -50,
                "yuanlve-give" or "moushi-give" => (allied && target != f.OwnerSeat ? 60 : target == f.OwnerSeat ? 10 : -50) - VaryGuanduRecipientHand(target) - keep,
                "choulue-target" => allied ? 30 + VaryGuanduRecipientHand(target) : 0,
                "choulue-gift" => GuanduAllies(f.OwnerSeat, decision.PlayerSeat) ? 30 - keep : -30,
                _ => -keep
            };
        }
        return decision.Choices.OrderByDescending(Value).First();
    }
    private int VaryGuanduRecipientHand(int seat) => IsValidPlayerSeat(seat) ? GetHand(_players[seat]).Count : 0;
}
