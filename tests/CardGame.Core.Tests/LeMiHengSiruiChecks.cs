using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class LeMiHengChecks
{
    private const string SmSkill = "le:sirui", SmOwner = "fixture:lms-owner", SmMode = "identity:classic-le-mi-heng-sirui";
    private const string SmDriver = "fixture:lms-driver", SmQuiet = "fixture:lms-quiet", SmWatch = "fixture:lms-watch", SmConditional = "fixture:lms-conditional";
    private const string SmSetup = "fixture:lms-setup", SmInsert = "fixture:lms-insert", SmRegrant = "fixture:lms-regrant", SmForced = "fixture:lms-forced", SmPeerDodge = "fixture:lms-peer-dodge";

    public static void SiruiExactNamesHeOutputsDistanceAndOrdinaryQuotasCold()
    {
        var s = SmScenario.Start("pool"); s.Stock(); s.Use("wound"); s.Play();
        foreach (var target in new[] { 1, 2 }) { s.Use("give", [s.V().Hand.First(c => c.Kind == CardKind.Slash).Id], [target]); s.Play(); }
        var actions = s.Conversions(); var expected = new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Peach, CardKind.Alcohol,
            CardKind.DrawTwo, CardKind.Duel, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.PeachGarden, CardKind.FiveGrains,
            CardKind.Dismantlement, CardKind.Snatch, CardKind.FireAttack, CardKind.IronChain, CardKind.BorrowedSword };
        s.Need(actions.Select(a => a.PlayedCardKind!.Value).ToHashSet().SetEquals(expected) && actions.All(a =>
            CardCatalog.Get(s.Card(a.CardId!.Value).Kind).DisplayName.EnumerateRunes().Count() == CardCatalog.Get(a.PlayedCardKind!.Value).DisplayName.EnumerateRunes().Count()) &&
            actions.Any(a => a.PlayedCardKind == s.Card(a.CardId!.Value).Kind) &&
            !actions.Any(a => a.PlayedCardKind is CardKind.Indulgence or CardKind.Lightning or CardKind.SupplyShortage),
            "Genuine native menus expose all sixteen active basic/ordinary-trick outputs with exact canonical name lengths, including same-kind conversion, without delayed or unsupported names.");
        var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == slash && a.ConversionSource is null && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]))); s.Play();
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.ConversionSource is null && a.Kind == LegalActionKind.Slash) &&
            s.Conversions().Any(a => a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([2])) &&
            s.G.CreateSnapshot(0).Players[0].Equipment.Count == 0,
            "After a real ordinary Slash exhausts its quota, the unspent method alone can issue a distant Slash without an equipped Crossbow granting a false exemption.");
        var chosen = s.Conversions().First(a => a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([2])); var physical = s.Card(chosen.CardId!.Value);
        s.RejectSource(chosen); s.Cold(); s.Native(chosen); s.Play(); var paid = s.E<SiruiPhaseUseIssuedEvent>().Single();
        s.Need(paid.OutputKind == CardKind.Slash && paid.CardId == physical.Id && paid.PrintedKind == CardKind.Slash && paid.From == CardLocation.Hand(0) &&
            s.E<SiruiMaterialPaidEvent>().Single(e => e.OwnerFrameId == paid.OwnerFrameId).CardId == physical.Id &&
            s.E<CardUseDeclaredEvent>().Any(e => e.ResolutionId == paid.OwnerFrameId && e.CardKind == CardKind.Slash && e.SourceSeat == 0) && s.Conversions().Length == 0,
            "A real same-name distant native Slash retains its original suit/rank/material invoice and spends the shared phase allowance once."); s.Cold();

        s = SmScenario.Start("snatch"); s.Stock(); s.Use("give", [s.V().Hand.First(c => c.Kind == CardKind.Slash).Id], [2]); s.Play();
        chosen = s.Conversions().First(a => a.PlayedCardKind == CardKind.Snatch && a.TargetSeats.SequenceEqual([2])); physical = s.Card(chosen.CardId!.Value);
        s.Native(chosen); s.Play(); paid = s.E<SiruiPhaseUseIssuedEvent>().Single();
        var taken = s.G.CardMovements.Single(m => m.From == CardLocation.Hand(2) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Snatch);
        s.Need(paid.OutputKind == CardKind.Snatch && paid.PrintedKind == physical.Kind &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == paid.OwnerFrameId && e.CardKind == CardKind.Snatch) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == taken.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.SnatchFinished && m.Sequence > taken.Sequence) == 1 &&
            s.E<TargetCardTakenEvent>().Count(e => e.ResolutionId == paid.OwnerFrameId && e.SourceSeat == 0 && e.TargetSeat == 2 && e.FromZone == CardZoneKind.Hand && e.PublicCardId is null) == 1,
            "The actual single-card trick pipeline accepts distant Snatch and completes its real target-card selection and obtain."); s.Cold();

        s = SmScenario.Start("alcohol"); s.StockFour();
        s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource is null)); s.Play();
        s.Native(s.G.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == SmDriver && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]))); s.Play();
        s.Need(!s.V().HasAlcoholEffect && !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource is null) &&
            s.Conversions().Any(a => a.PlayedCardKind == CardKind.Alcohol), "A genuine ordinary Alcohol has been consumed by a real Slash; its ordinary quota remains spent while Sirui's distinct count exemption remains legal.");
        s.Native(s.Conversions().First(a => a.PlayedCardKind == CardKind.Alcohol)); s.Play();
        s.Need(s.V().HasAlcoholEffect && s.E<SiruiPhaseUseIssuedEvent>().Single().OutputKind == CardKind.Alcohol && s.E<SiruiMaterialPaidEvent>().Length == 1,
            "Sirui Alcohol follows the real Alcohol effect and original material completion without recharging or borrowing the ordinary quota."); s.Cold();

        s = SmScenario.Start("banned"); s.Stock(); s.Use("ban-slash"); s.Play();
        s.Need(s.Conversions().All(a => a.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) && s.Conversions().Any(a => a.PlayedCardKind == CardKind.Duel),
            "Distance and count exemptions do not bypass a genuine turn card-use prohibition, while unrelated legal output names remain available."); s.Cold();
        s = SmScenario.Start("grain"); s.StockFour(); var ox = s.V().Hand[0].Id; s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == ox && a.Kind == LegalActionKind.Equip && a.ConversionSource is null)); s.Play();
        var grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(s.V().WoodenOxGrain!.Any(c => c.Id == grain) && s.Conversions().Any(a => a.CardId == ox) && s.Conversions().All(a => a.CardId != grain) &&
            Enumerable.Range(1, 3).All(v => (s.G.CreateSnapshot(v).Players[0].WoodenOxGrain?.Count ?? 0) == 0),
            "An actual owner equipment entity is an HE input, while the same-name real private Ox grain never becomes a method material."); s.Cold();

        s = SmScenario.Start("qinglong"); s.Stock(); s.Need(s.V().Equipment is [{ Kind: CardKind.QinglongCrescentBlade }], "The genuine GameStarting equipment producer installs the deck's only real weapon.");
        s.Use("give", [s.V().Hand.First(c => c.Kind == CardKind.Duel).Id], [1]); s.Play();
        s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1])));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade });
        var outer = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardAttack is not null);
        var dualMaterial = s.V().Hand.Where(c => c.Kind == CardKind.Duel).Select(c => c.Id).ToHashSet();
        var typed = s.P!.Choices.Where(c => c.Cards.Count == 1 && dualMaterial.Contains(c.Cards[0])).ToArray();
        s.Need(s.P.Choices.Select(c => c.Id).Distinct().Count() == s.P.Choices.Count && s.E<SiruiPhaseUseIssuedEvent>().Length == 0 &&
            outer.CardAttack!.SuccessfulDodgeResponses == 1 && s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) &&
            typed.Length == dualMaterial.Count * 2 && typed.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == SmSkill && c.Parameters.GetValueOrDefault("response-card-kind") is "FireSlash" or "ThunderSlash") &&
            dualMaterial.All(id => typed.Where(c => c.Cards.SequenceEqual([id])).Select(c => c.Parameters["response-card-kind"]).ToHashSet().SetEquals(["FireSlash", "ThunderSlash"])),
            "A real ordinary Slash and real Dodge expose unique native Qinglong choices: every two-character Duel offers exactly FireSlash/ThunderSlash, without a fake plain Slash alias.");
        s.Need(typed.All(c => c.Description.Contains(CardCatalog.Get(Enum.Parse<CardKind>(c.Parameters["response-card-kind"])).DisplayName, StringComparison.Ordinal)) &&
            dualMaterial.All(id => typed.Where(c => c.Cards.SequenceEqual([id])).Select(c => c.Description).Distinct(StringComparer.Ordinal).Count() == 2),
            "The real Qinglong FireSlash and ThunderSlash choices display their respective native card names and distinguish both outputs for the same material.");
        var followup = typed.First(c => c.Parameters["response-card-kind"] == nameof(CardKind.FireSlash)); s.Private(0); s.Cold(); s.Answer(c => c.Id == followup.Id); s.Reach(() => s.Hold("cost"));
        var forced = s.E<SiruiPhaseUseIssuedEvent>().Single();
        s.Need(forced.OwnerFrameId != outer.Id && forced.OutputKind == CardKind.FireSlash && forced.PrintedKind == CardKind.Duel && forced.CardId == followup.Cards.Single() &&
            forced.From == CardLocation.Hand(0) && s.E<SiruiMaterialPaidEvent>().Count(e => e.OwnerFrameId == forced.OwnerFrameId && e.CardId == forced.CardId) == 1,
            "The selected native elemental follow-up pays its actual Duel entity once in the original active owner Play."); s.Private(0); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<SiruiPhaseUseIssuedEvent>().Length == 1 && s.E<SiruiMaterialPaidEvent>().Length == 1 &&
            s.E<QinglongCrescentBladeResolvedEvent>().Count(e => e.ResolutionId == outer.Id && e.Used && e.EffectiveSlashKind == CardKind.FireSlash && e.SlashCardIds.SequenceEqual([forced.CardId])) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == forced.OwnerFrameId && e.CardKind == CardKind.FireSlash) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == outer.Id) == 1 && s.Conversions().Length == 0,
            "Cold typed return finishes both real Slash uses once and spends only one shared Sirui phase entitlement."); s.Cold();
    }

    public static void SiruiTrueUseResponseRequestAndPlayedExclusionsCold()
    {
        foreach (var scene in new[] { "dodge", "null", "rescue-peach", "rescue-alcohol", "forced" })
        {
            var s = SmScenario.Start(scene); if (scene == "null") s.Stock(); else s.StockFour();
            var output = scene switch { "dodge" => CardKind.Dodge, "null" => CardKind.Nullification, "rescue-peach" => CardKind.Peach, "rescue-alcohol" => CardKind.Alcohol, _ => CardKind.Slash };
            if (scene == "dodge") { s.Use("incoming", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])); s.Reach(() => s.P?.Kind == DecisionKind.RespondDodge); }
            else if (scene == "null") { s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]))); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); }
            else if (scene.StartsWith("rescue-", StringComparison.Ordinal)) { s.Use(scene == "rescue-peach" ? "peer-dying" : "self-dying", targets: scene == "rescue-peach" ? [1] : []); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); }
            else { s.Use("give", [s.V().Hand[0].Id], [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "assisted-physical-slash" && c.Parameters.GetValueOrDefault("request-option") == "target") == true); s.Answer(c => c.Parameters.GetValueOrDefault("request-option") == "target" && c.Targets.SequenceEqual([1])); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == SmSkill) == true); }
            var outputName = output.ToString(); var requiredBinding = "equal-name-" + char.ToLowerInvariant(outputName[0]) + outputName[1..];
            var prompt = s.P!; var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == SmSkill && c.Parameters.GetValueOrDefault("conversion-binding-id") == requiredBinding &&
                (c.Parameters.GetValueOrDefault("output-kind") is not { } named || named == output.ToString()) &&
                (c.Parameters.GetValueOrDefault("effective-kind") is not { } effective || effective == output.ToString()));
            var rescueTarget = scene.StartsWith("rescue-", StringComparison.Ordinal) ? scene == "rescue-peach" ? 1 : 0 : -1;
            var dyingId = rescueTarget < 0 ? 0 : s.G.ResolutionStack.OfType<DyingFrame>().Single(d => d.VictimSeat == rescueTarget).Id;
            var original = choice.Cards.Single(); s.Private(0); s.Cold(); s.Answer(c => c.Id == choice.Id); s.Reach(() => s.Hold("cost"));
            var paid = s.E<SiruiPhaseUseIssuedEvent>().Single();
            s.Need(paid.OutputKind == output && paid.CardId == original && paid.Source.OwnerSeat == 0 && paid.Phase is { ActorSeat: 0, ActualTurnOwnerSeat: 0, Kind: ActualDiscardRecoveryPhaseKind.Play } &&
                s.E<SiruiMaterialPaidEvent>().Count(e => e.OwnerFrameId == paid.OwnerFrameId && e.CardId == original) == 1,
                "The real response, rescue or forced request pays its exact original HE entity and the same actual owner-Play allowance, retaining its original native lifecycle.");
            void RescueOnce()
            {
                var declared = s.G.Events.Single(e => e.Payload is CardUseDeclaredEvent d && d.ResolutionId == paid.OwnerFrameId && d.CardId == original && d.CardKind == output && d.SourceSeat == 0);
                var recovery = s.G.Events.Single(e => e.Payload is RecoveryAppliedEvent r && r.SourceSeat == 0 && r.TargetSeat == rescueTarget && r.Amount == 1 && r.RemainingHp == 1);
                var finished = s.G.Events.Single(e => e.Payload is CardUseFinishedEvent f && f.ResolutionId == paid.OwnerFrameId && f.CardKind == output);
                s.Need(s.V(rescueTarget) is { IsAlive: true, Hp: 1 } &&
                    declared.Sequence < recovery.Sequence && recovery.Sequence < finished.Sequence && s.E<TargetsConfirmedEvent>().Count(e => e.ResolutionId == paid.OwnerFrameId && e.TargetSeats.SequenceEqual([rescueTarget])) == 1 &&
                    s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == rescueTarget && e.Amount == 1) == 1 &&
                    s.E<DyingResponseEvent>().Count(e => e.ResolutionId == dyingId && e.ResponderSeat == 0 && (output == CardKind.Peach ? e.UsedPeach && e.PeachCardId == original && !e.UsedAlcohol : e.UsedAlcohol && e.AlcoholCardId == original && !e.UsedPeach)) == 1 &&
                    s.E<DyingResolvedEvent>().Count(e => e.ResolutionId == dyingId && e.VictimSeat == rescueTarget && e.Survived) == 1 &&
                    s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == paid.OwnerFrameId && e.CardKind == output) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
                    "The ordinary native rescue has already recovered HP0 to1, completed its exact Use and dying parent once, and retired its original material before the queued movement observer returns.");
            }
            if (rescueTarget >= 0) RescueOnce();
            s.Private(0); s.Cold(); s.Continue(); s.RejectOld(prompt); s.Play();
            s.Need(s.E<SiruiNativeActionBoundEvent>().Count(e => e.OwnerFrameId == paid.OwnerFrameId && e.OutputKind == output && e.ActorSeat == 0 && e.ProviderSeat == 0) == 1 &&
                s.Conversions().Length == 0 && s.E<SiruiPhaseUseIssuedEvent>().Length == 1,
                "Every native true-use family binds once to its original actor/provider and exhausts all other outputs in that actual Play.");
            if (rescueTarget >= 0) { RescueOnce(); s.Need(s.E<SiruiMaterialPaidEvent>().Count(e => e.OwnerFrameId == paid.OwnerFrameId) == 1, "Cold movement-child return cannot pay or recover the already completed native rescue again."); }
            if (scene == "dodge") { s.Use("incoming", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])); s.Reach(() => s.P?.Kind == DecisionKind.RespondDodge); s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != SmSkill), "A second true Dodge in the same paused actual Play does not receive another output allowance."); }
            s.Cold();
        }
        foreach (var scene in new[] { "played-duel", "played-arrows", "played-barbarian", "provider" })
        {
            var s = SmScenario.Start(scene);
            if (scene == "played-duel") { s.StockFour(); s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); }
            else if (scene is "played-arrows" or "played-barbarian")
            {
                s.Stock(); var groupKind = scene == "played-arrows" ? CardKind.ArrowBarrage : CardKind.BarbarianAssault;
                var actualGroup = s.V().Hand.Single(c => c.Kind == groupKind).Id; s.Use("give", [actualGroup], [1]); s.Play();
                s.Native(s.G.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "ol:huiyun" && a.PlayedCardKind == CardKind.FireAttack && a.TargetSeats.SequenceEqual([1])));
                s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.FireAttackDiscard }); s.Private(0); s.Cold(); s.Answer(c => c.Cards.Count == 1);
                s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:huiyun" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "1"));
                s.Answer(c => c.Parameters.GetValueOrDefault("option") == "1");
                s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash });
                s.Need(s.G.State.CurrentSeat == 0 && s.G.State.Phase == TurnPhase.Play && s.E<CardUseDeclaredEvent>().Any(e => e.CardId == actualGroup && e.SourceSeat == 1 && e.CardKind == groupKind) &&
                    s.E<HuiyunNativeIssuedEvent>().Any(e => e.Invoice.ActorSeat == 1 && e.Invoice.CardId == actualGroup && e.Invoice.Kind == groupKind),
                    "A real Huiyun target consumes its sole natural Hand AOE through the original native child while Sirui's owner remains in the same actual own Play.");
            }
            else if (scene == "provider") { var lord = s.G.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat; s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([lord]))); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } p && p.TargetSeat == lord); }
            var output = scene is "played-duel" or "played-barbarian" ? CardKind.Slash : CardKind.Dodge;
            s.Need(s.P is { PlayerSeat: 0 } && s.P.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != SmSkill) &&
                s.P.Choices.Any(c => c.Cards.Count == 1), "A real own-Play pure played/provider request has a live same-length HE fallback but no use-only method supply.");
            s.Private(0); s.Cold(); s.Answer(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") != SmSkill); s.Play();
            s.Need(s.E<SiruiPhaseUseIssuedEvent>().Length == 0 && s.Conversions().Length > 0 &&
                (scene == "provider" ? s.E<FactionDefenseRequestedEvent>().Length == 1 : scene == "played-duel" ? s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedSlash) : s.E<GroupResponseEvent>().Any(e => e.ResponderSeat == 0 && e.RequiredCardKind == output && e.UsedResponse)),
                "Ordinary real played/provision payment completes its native parent without consuming the still-available method allowance."); s.Cold();
        }
        var weapon = SmScenario.Start("general-weapon"); weapon.StockFour(); weapon.Use("weapon");
        weapon.Reach(() => weapon.P?.Choices.Any(c => c.Parameters.ContainsKey("advanced-value")) == true);
        weapon.Answer(c => c.Parameters.GetValueOrDefault("advanced-value") == "fixture:lms-reserve");
        weapon.Answer(c => c.Parameters.GetValueOrDefault("advanced-value") == "finish"); weapon.Play();
        var generated = weapon.V().Equipment.Single(c => c.Kind == CardKind.GeneralWeapon).Id;
        weapon.Need(CardCatalog.Get(CardKind.GeneralWeapon).DisplayName.EnumerateRunes().Count() == CardCatalog.Get(CardKind.Nullification).DisplayName.EnumerateRunes().Count(),
            "The native sampled-general weapon has the actual canonical four-character input name for Nullification."); weapon.Cold();
        weapon.Native(weapon.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1])));
        weapon.Reach(() => weapon.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
        var originalDuel = weapon.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel).Id;
        var conversion = weapon.P!.Choices.First(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == SmSkill && c.Cards.SequenceEqual([generated]));
        weapon.Private(0); weapon.Cold(); weapon.Answer(c => c.Id == conversion.Id); weapon.Reach(() => weapon.Hold("cost"));
        var issuedWeapon = weapon.E<SiruiPhaseUseIssuedEvent>().Single(); var material = weapon.E<SiruiMaterialPaidEvent>().Single();
        weapon.Need(issuedWeapon is { PrintedKind: CardKind.GeneralWeapon, OutputKind: CardKind.Nullification } && issuedWeapon.CardId == generated && issuedWeapon.From == CardLocation.Equipment(0) &&
            material.OwnerFrameId == issuedWeapon.OwnerFrameId && material.CardId == generated && material.From == CardLocation.Equipment(0) && material.To == CardLocation.OutsideGame &&
            weapon.G.CardMovements.Count(m => m.CardId == generated && m.From == CardLocation.Equipment(0) && m.To == CardLocation.OutsideGame) == 1 &&
            !weapon.G.CardMovements.Any(m => m.CardId == generated && m.To is { Zone: CardZoneKind.Processing or CardZoneKind.DiscardPile }),
            "A genuinely generated GeneralWeapon pays the exact native Equipment-to-Outside invoice, without a fictitious Processing or discard material."); weapon.Private(0); weapon.Cold(); weapon.Continue(); weapon.Play();
        weapon.Need(weapon.E<SiruiNativeActionBoundEvent>().Count(e => e.OwnerFrameId == issuedWeapon.OwnerFrameId && e.ActorSeat == 0 && e.OutputKind == CardKind.Nullification) == 1 &&
            weapon.E<NullificationRespondedEvent>().Count(e => e.ResponderSeat == 0 && e.NullificationCardId == generated) == 1 && weapon.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == originalDuel) == 1 &&
            weapon.G.CardMovements.Count(m => m.CardId == generated && m.From == CardLocation.Equipment(0) && m.To == CardLocation.OutsideGame) == 1 && weapon.Conversions().Length == 0,
            "After cold return the real Nullification completes once, drains the original Duel parent and never retires or charges the already removed generated entity again."); weapon.Cold();
    }

    public static void SiruiPaidChildrenRegainPhaseQuotaAndRejectedSourcesCold()
    {
        var s = SmScenario.Start("regain"); s.Stock(); var firstChoice = s.Conversions().First(a => a.PlayedCardKind == CardKind.DrawTwo);
        s.RejectSource(firstChoice); s.Cold(); s.Native(firstChoice); s.Play(); var first = s.E<SiruiPhaseUseIssuedEvent>().Single();
        s.Use("lose-sirui"); s.Play(); s.Need(!s.V().Skills!.Any(k => k.ContentId == SmSkill), "The original method source is actually removed.");
        s.Use("grant-sirui"); s.Play(); s.Need(s.V().Skills!.Any(k => k.ContentId == SmSkill) && s.Conversions().Length == 0,
            "A real new skill instance cannot refresh the owner+skill+actual-Play entitlement that the old source already spent."); s.Cold();
        s.End(); s.Play(); var secondChoice = s.Conversions().First(a => a.PlayedCardKind == CardKind.DrawTwo); s.Native(secondChoice); s.Play();
        var uses = s.E<SiruiPhaseUseIssuedEvent>();
        s.Need(uses.Length == 2 && uses[0].Phase.Token != uses[1].Phase.Token && uses[0].Phase.TurnNumber == uses[1].Phase.TurnNumber &&
            uses[0].Source.SkillInstanceId != uses[1].Source.SkillInstanceId && uses[0] == first && s.E<SiruiMaterialPaidEvent>().Length == 2,
            "A genuine inserted Play gets one new phase allowance in the same actual turn, retaining both original source identities and once-only native invoices."); s.Cold();

        s = SmScenario.Start("dianjun"); s.StockFour(); s.Native(s.Conversions().First(a => a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]))); s.Play();
        var ordinaryPhase = s.E<SiruiPhaseUseIssuedEvent>().Single(); s.End(); s.Play();
        s.Need(s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == ordinaryPhase.Phase.TurnNumber) && s.G.State.TurnNumber == ordinaryPhase.Phase.TurnNumber &&
            s.E<ZhangYiResolutionStartedEvent>().Single(e => e.Source.SkillId == "ol:dianjun").ActualTurnNumber == ordinaryPhase.Phase.TurnNumber && s.Conversions().Any(),
            "Real Dianjun opens an end-owned Play after the same actual TurnEnded fact; the new actual Play token admits Sirui."); s.Cold();
        s.Native(s.Conversions().First(a => a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]))); s.Play(); var tail = s.E<SiruiPhaseUseIssuedEvent>().Last();
        s.Need(s.E<SiruiPhaseUseIssuedEvent>().Length == 2 && tail.Phase.Token != ordinaryPhase.Phase.Token && tail.Phase.TurnNumber == ordinaryPhase.Phase.TurnNumber &&
            s.E<SiruiMaterialPaidEvent>().Length == 2 && s.Conversions().Length == 0,
            "A second real method Use spends only the fresh end-owned phase allowance; the already emitted actual-turn end does not prohibit this native extra Play."); s.Cold();

        s = SmScenario.Start("distance-loss"); s.StockFour(); s.Native(s.Conversions().First(a => a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([2]))); s.Reach(() => s.Hold("cost"));
        var distant = s.E<SiruiPhaseUseIssuedEvent>().Single(); s.Private(0); s.Cold(); s.Continue();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:yidian");
        if (s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "designated-extra-target") == true);
        s.Need(!s.V().Skills!.Any(k => k.ContentId == SmSkill) && s.P!.Choices.Any(c => c.Targets.SequenceEqual([3])) && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == distant.OwnerFrameId),
            "The already paid distant Slash retains its original native permission after real source loss and offers a genuine additional target on that same Use."); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "designated-extra-target" && c.Targets.SequenceEqual([3])); s.Play();
        var added = s.E<DesignatedExtraTargetResolvedEvent>().Single(e => e.CardUseFrameId == distant.OwnerFrameId);
        s.Need(added.OriginalTargetSeats.SequenceEqual([2]) && added.AddedTargetSeats.SequenceEqual([3]) && added.ResultTargetSeats.SequenceEqual([2, 3]) &&
            s.E<SiruiPhaseUseIssuedEvent>().Length == 1 && s.E<SiruiMaterialPaidEvent>().Length == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == distant.OwnerFrameId) == 1 &&
            s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0).Select(e => e.TargetSeat).Order().SequenceEqual([2, 3]),
            "Both original distant and genuinely appended targets resolve through one accepted Slash and exactly one paid method material, without requiring the lost skill again."); s.Cold();

        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            s = SmScenario.Start(scene); s.StockFour(); var lion = s.V().Hand[0].Id;
            s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == lion && a.Kind == LegalActionKind.Equip && a.ConversionSource is null)); s.Play(); s.Use("wound"); s.Play();
            var choice = s.Conversions().First(a => a.CardId == lion && a.PlayedCardKind == CardKind.DrawTwo); s.Native(choice); s.Reach(() => s.Hold("recovered"));
            var issued = s.E<SiruiPhaseUseIssuedEvent>().Single();
            s.Need(issued.From == CardLocation.Equipment(0) && issued.CardId == lion && s.E<SiruiMaterialPaidEvent>().Single(e => e.OwnerFrameId == issued.OwnerFrameId).From == CardLocation.Equipment(0) &&
                s.V().Hp == s.V().MaxHp && s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == issued.OwnerFrameId),
                "The exact equal-name equipment cost pays through the real Silver Lion recovery child before its converted trick effect.");
            s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.Hold("cost")); s.Private(0); s.Cold(); s.Continue();
            s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == issued.OwnerFrameId)); s.PlayUnlessDead();
            s.Need((scene == "paid-loss" ? s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == SmSkill) : !s.V().IsAlive) &&
                s.E<SiruiPhaseUseIssuedEvent>().Length == 1 && s.E<SiruiMaterialPaidEvent>().Length == 1 &&
                s.G.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.OwnerFrameId) == 1,
                "Already-paid source loss or owner death drains the original native payment/effect children without a second cost, fresh method Use or lost parent."); s.Cold();
        }
    }

    private sealed class SmScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardSnapshot Card(int id) => Enumerable.Range(0, 4).SelectMany(i => V(i).Hand.Concat(V(i).Equipment)).First(c => c.Id == id);
        public LegalAction[] Conversions() => G.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == SmSkill).ToArray();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == SmWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Stock() { Use("stock-all"); Play(); Use("stock-all"); Play(); }
        public void StockFour() { Use("stock-four"); Play(); }
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, SmDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public PlayCardCommand Command(LegalAction a, CardConversionSource? source = null) => new(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = source ?? a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources };
        public void Native(LegalAction a) => Accept(Command(a));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayUnlessDead() => Reach(() => !V().IsAlive || P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The human selects a genuine canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Private(int seat) => Need(P is { } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the actual native response or child chooser receives its exact choices and private material identities.");
        public void RejectSource(LegalAction action) { var before = State(); Need(!G.Submit(Command(action, action.ConversionSource! with { SkillInstanceId = "forged-sirui-source" })).Accepted && before == State() &&
            !G.Submit(Command(action, action.ConversionSource! with { BindingId = "unpublished-equal-name-output" })).Accepted && before == State(), "A forged instance or binding cannot substitute for the published exact conversion source or consume its phase allowance."); }
        public void RejectOld(PendingDecision old) { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && before == State(), "A previous native request token cannot replay the paid output."); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 190; i++) { if (done()) return; Need(G.State.Winner == Winner.None, "Native fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End(); else if (p.SkillPrompt?.SkillId == SmWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.SkillPrompt?.SkillId == SmInsert && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")) Answer(c => c.Targets.SequenceEqual([0]));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "target-card-slot")) Answer(c => c.Parameters.GetValueOrDefault("action") == "target-card-slot");
                else throw new InvalidOperationException("Unexpected Sirui boundary " + line + " " + Diagnostic()); }
            throw new InvalidOperationException("Missing bounded Sirui boundary " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command) { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(r)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves native true-use identity, exact HE payment, source-wide actual-phase quota and the original child return."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, p.PromptId, p.SourceSeat, p.TargetSeat, p.RequiredCardKind, ChoiceCount = p.Choices.Count,
                Choices = p.Choices.Take(6).Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }) },
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step, f.Sirui }), Issued = E<SiruiPhaseUseIssuedEvent>(), Paid = E<SiruiMaterialPaidEvent>(), Bound = E<SiruiNativeActionBoundEvent>(),
            Finished = E<CardUseFinishedEvent>().Where(e => E<SiruiPhaseUseIssuedEvent>().Any(paid => paid.OwnerFrameId == e.ResolutionId)),
            TargetProcessing = G.CardMovements.Where(m => m.From == CardLocation.Hand(2) && m.To == CardLocation.Processing).TakeLast(8),
            ObtainedFromProcessing = G.CardMovements.Where(m => m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)).TakeLast(8) });
        public static SmScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new SmFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene is "paid-death" or "provider" ? Role.Renegade : Role.Lord, ModeId = SmMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new SmScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, SmOwner, g.Revision, s.P!.PromptId));
            s.Play(); return s;
        }
    }
    private sealed class SmFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-mi-heng-sirui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            LmProduction(b, "le-mi-heng", SmSkill);
            if (scene == "provider") CharacterFixtureContent.Register(b, "classic:cao-cao");
            if (scene == "dianjun") LmProduction(b, "ol-zhang-yi", "ol:dianjun");
            if (scene == "distance-loss") LmProduction(b, "ordinary-sun-shao", "ol:yidian");
            if (scene is "played-arrows" or "played-barbarian") LmProduction(b, "ol-hu-ban", "ol:huiyun");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}").AsObject();
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var incoming = A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", targets: 1); incoming["targetKind"] = "otherLiving";
            var acts = new JsonArray(A("stock-all", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), A("stock-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1), A("wound", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),
                A("self-dying", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":13}"), A("peer-dying", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":12}", targets: 1),
                A("ban-slash", "{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],\"actionTypes\":[\"use\"]}"),
                A("lose-sirui", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"le:sirui\"],\"sourceBind\":\"" + SmRegrant + "\"}"), A("grant-sirui", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"le:sirui\"]}"), duel, incoming);
            if (scene == "general-weapon") acts.Add(A("weapon", "{\"op\":\"equipSampledGenerals\",\"target\":\"owner\",\"amount\":1}"));
            var driver = new JsonObject { ["id"] = SmDriver, ["revision"] = 1, ["activations"] = acts, ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]") };
            if (scene == "provider") driver["modifiers"]!.AsArray().Add(N("{\"id\":\"real-provider-test-attack\",\"query\":\"slashDistanceLimit\",\"operation\":\"unlimited\",\"value\":0,\"priority\":0}"));
            if (scene is "alcohol" or "dodge" or "played-duel" or "played-arrows" or "played-barbarian" or "provider") driver["viewAs"] = N("[{\"id\":\"ordinary-fallback\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"" + (scene is "dodge" or "played-arrows" or "provider" ? "dodge" : "slash") + "\",\"forPlay\":" + (scene == "alcohol" ? "true" : "false") + ",\"forResponse\":" + (scene == "alcohol" ? "false" : "true") + ",\"allowSameKind\":true}]");
            var defs = new JsonArray(driver, N($$"""{"id":"{{SmQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""),
                N($$"""{"id":"{{SmRegrant}}","revision":1,"modifiers":[{"id":"real-granted-neutral","query":"handLimit","operation":"add","value":1,"priority":0}]}"""));
            var own = new List<string> { SmDriver, scene is "paid-loss" or "distance-loss" ? SmConditional : SmSkill };
            if (scene is "paid-loss" or "distance-loss") defs.Add(N($$"""{"id":"{{SmConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-method","skillId":"{{SmSkill}}","minimumHp":12}]}"""));
            if (scene == "dianjun") own.Add("ol:dianjun");
            if (scene == "distance-loss") own.Add("ol:yidian");
            if (scene == "qinglong") own.Add(SmSetup);
            if (scene is "played-arrows" or "played-barbarian") own.Add("ol:huiyun");
            if (scene == "forced") defs.Add(N($$"""{"id":"{{SmForced}}","revision":1,"triggers":[{"id":"real-foreign-request-in-owner-play","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","chooserRef":{"kind":"selectedTarget"},"resultBind":"forced-use"}]}]}"""));
            if (scene == "regain") { defs.Add(N($$"""{"id":"{{SmInsert}}","revision":1,"triggers":[{"id":"new-real-play","window":"playEnding","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}""")); own.Add(SmInsert); }
            var mixed = scene is "pool" or "snatch" or "banned" or "regain" or "null";
            if (mixed || scene == "qinglong") defs.Add(N($$"""{"id":"{{SmSetup}}","revision":1,"triggers":[{"id":"actual-native-weapon","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"actual-equipment"}]}]}"""));
            if (scene == "qinglong") defs.Add(N($$"""{"id":"{{SmPeerDodge}}","revision":1,"viewAs":[{"id":"actual-two-character-dodge","inputKinds":["duel"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]}"""));
            var watch = new JsonArray();
            if (scene is "dodge" or "null" or "rescue-peach" or "rescue-alcohol" or "forced" or "paid-loss" or "paid-death" or "distance-loss" or "general-weapon" or "qinglong")
                foreach (var zone in new[] { "hand", "equipment" })
                    watch.Add(N("{\"id\":\"real-method-cost-" + zone + "\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + zone + "\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + CardMoveReasons.Use.Value + "\",\"" + CardMoveReasons.Respond.Value + "\",\"" + CardMoveReasons.Nullification.Value + "\"],\"optional\":false,\"effects\":[" + Choice("cost") + (scene is "paid-loss" or "paid-death" or "distance-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-death" ? 20 : 2) + "}" : "") + "]}"));
            if (scene is "paid-loss" or "paid-death") watch.Add(N("{\"id\":\"real-lion-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("recovered") + "]}"));
            if (watch.Count > 0) { defs.Add(new JsonObject { ["id"] = SmWatch, ["revision"] = 1, ["triggers"] = watch }); own.Add(SmWatch); }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实思锐原生使用与付款夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "真实思锐夹具") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string pick = "fixture:lms-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) }); b.AddGeneral(new(SmOwner, "真实乐祢衡思锐者", "supporter", pick, scene == "provider" ? "wei" : "qun", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lms-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var pp = "fixture:lms-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); var skills = new List<string> { SmQuiet }; if (mixed) skills.Add(SmSetup); if (i == 1 && scene == "forced") skills.Add(SmForced); if (i == 1 && scene == "qinglong") skills.Add(SmPeerDodge); if (scene == "provider") skills.Add("classic:hujia"); b.AddGeneral(new(peers[i - 1], "真实对局角色", "supporter", pp, scene == "provider" ? "wei" : "shu", 12, skills)); }
            if (scene == "general-weapon") { const string reservePick = "fixture:lms-reserve-pick"; b.AddSkill(new(reservePick, "真实未选备用", "只提供原生魂牌来源") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -200000000d) }); b.AddGeneral(new("fixture:lms-reserve", "真实未选武将", "supporter", reservePick, "shu", 4, [])); }
            var singleKind = scene == "grain" ? CardKind.WoodenOx : scene == "alcohol" ? CardKind.Alcohol : scene is "paid-loss" or "paid-death" ? CardKind.SilverLion : scene == "general-weapon" ? CardKind.Duel : CardKind.Slash;
            const string single = "fixture:lms-physical-card"; var definition = CardCatalog.Get(singleKind);
            b.AddCard(new(single, definition.DisplayName, definition.CategoryName, "真实同名原生牌堆实体", singleKind));
            if (scene is "played-arrows" or "played-barbarian") { var group = CardCatalog.Get(scene == "played-arrows" ? CardKind.ArrowBarrage : CardKind.BarbarianAssault); b.AddCard(new("fixture:lms-natural-group", group.DisplayName, group.CategoryName, "真实晖云展示并使用的原AOE", group.Kind)); }
            if (scene == "qinglong") b.AddCard(new("fixture:lms-real-qinglong", "青龙偃月刀", "装备牌", "真实青龙后续杀", CardKind.QinglongCrescentBlade));
            var cards = scene is "played-arrows" or "played-barbarian" ? Enumerable.Range(0, 25).Select(i => new ContentDeckPhysicalCard(i == 0 ? "fixture:lms-natural-group" : "standard:slash", Suit.Heart, 7)).ToArray() :
                scene == "qinglong" ? Enumerable.Range(0, 25).Select(i => new ContentDeckPhysicalCard(i == 0 ? "fixture:lms-real-qinglong" : i <= 12 ? "standard:slash" : "standard:duel", Suit.Heart, 7)).ToArray() :
                mixed ? Enumerable.Range(0, scene == "pool" ? 44 : 36).Select(i => new ContentDeckPhysicalCard(i < 12 ? "standard:slash" : i < 24 ? "standard:duel" : "standard:crossbow", Suit.Heart, 7)).ToArray() : Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard(single, Suit.Heart, 7)).ToArray();
            b.AddDeck(new("fixture:lms-deck", "真实一二四字牌名", scene == "provider" ? 4 : 0, 0, []) { PhysicalCards = cards });
            var roles = scene is "paid-death" or "provider" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(SmMode, "真实思锐原生边界", 4, 4, roles, "fixture:lms-deck", GeneralCandidateCount: scene == "general-weapon" ? 5 : 4, GeneralPoolIds: scene == "general-weapon" ? [SmOwner, .. peers, "fixture:lms-reserve"] : [SmOwner, .. peers]));
        }
    }
}
