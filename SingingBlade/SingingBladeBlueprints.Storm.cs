using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.AreaEffects;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.ActivatableAbilities.Restrictions;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;
using UnityEngine;

namespace SingingBlade
{
    public static partial class SingingBladeBlueprints
    {
        // ----------------------------------------------------------------
        // «Гроза Элизиума»
        // ----------------------------------------------------------------
        //
        // Переписанный аналог ванильного CallLightningCritical — зачарования скимитара
        // "Гнев медвежьего бога" (ea4da1b2cf1db1147b9e9974135d43ad). Структура повторена
        // один в один, медвежья часть заменена на путь Азаты:
        //
        //   ваниль                                    | у нас
        //   ------------------------------------------|---------------------------------
        //   крит скимитаром/естественной атакой        | то же самое
        //     -> вызов грозы, 5d6, Рефлекс DC 24       |   -> то же заклинание, тот же DC
        //   естественные атаки носителя: 1d6 звук+эл.  | то же самое
        //   ...1d10, если носитель в форме МЕДВЕДЯ     | ...1d10, если носитель совершил
        //                                              |    Первое вознесение азаты
        //   AddFeatureToPet(AnimalCompanion) — зверю   | AddFeatureToPet(AzataHavocDragon)
        //                                              |    — то есть Айву
        //   зверь-МЕДВЕДЬ бьёт на 1d10                 | Айву бьёт на 1d10 ВСЕГДА: фича
        //                                              |    и так достаётся только ей
        //
        // Почему заклинание берём ванильное, а не свой ContextActionDealDamage: урон вышел
        // бы тот же, но без готового Fx удара молнии и без строки в боевом логе.

        private static BlueprintWeaponEnchantment BuildStormEnchantment()
        {
            var enchantment = new BlueprintWeaponEnchantment();
            enchantment.AssetGuid = BlueprintGuid.Parse(Guids.StormEnchantmentGuid);
            enchantment.name = "SingingBladeStormEnchantment";

            Reflect.Set(enchantment, "m_EnchantmentCost", 1);
            Reflect.Set(enchantment, "m_IdentifyDC", 5);
            // Непустые имя и описание -> зачарование становится отдельной записью в списке
            // "Свойства" тултипа предмета, со своим всплывающим описанием.
            Reflect.Set(enchantment, "m_EnchantName", SingingBladeLocalization.CreateString(L.StormName));
            Reflect.Set(enchantment, "m_Description", SingingBladeLocalization.CreateString(L.StormDescription));
            Reflect.Set(enchantment, "m_Prefix", new LocalizedString());
            Reflect.Set(enchantment, "m_Suffix", new LocalizedString());

            // ActionsOnInitiator НЕ ставим (в отличие от зачарования песни): молния должна
            // бить в того, кого только что раскритовали, то есть действие выполняется в
            // контексте цели. Ванильный оригинал здесь тоже с false.
            enchantment.ComponentsArray = new BlueprintComponent[]
            {
                new AddInitiatorAttackWithWeaponTrigger
                {
                    CriticalHit = true,
                    OnlyHit = true,
                    Action = new ActionList { Actions = new GameAction[] { LightningUnlessSkyCut() } }
                }
            };

            return enchantment;
        }

        // Скрытая фича носителя клинка: гром и молния на его СОБСТВЕННЫХ естественных
        // атаках и ударах без оружия (сам клинок сюда не попадает — AllNaturalAndUnarmed
        // отсекает всё остальное оружие).
        //
        // Выдаётся НЕ через AddUnitFeatureEquipment на зачаровании, хотя штатный способ
        // именно такой: в этом моде та цепочка уже рвалась молча (см. историю с
        // переключателем в CLAUDE.md). Выдаём из кода по событию смены экипировки, тем же
        // способом, что и способность «Разрезать небеса» — он проверен и наблюдаем.
        private static BlueprintFeature BuildStormFeature()
        {
            var feature = BuildHiddenFeature(Guids.StormFeatureGuid, "SingingBladeStormFeature");

            var toPet = new AddFeatureToPet();
            Reflect.SetEnum(toPet, "m_PetType", (int)PetType.AzataHavocDragon);
            Reflect.Set(toPet, "m_Feature", Reflect.Ref<BlueprintFeatureReference>(Guids.StormPetFeatureGuid));

            feature.ComponentsArray = new BlueprintComponent[]
            {
                NaturalAttackStorm(alwaysGreater: false),
                NaturalAttackLightningOnCrit(gated: true),
                toPet
            };

            return feature;
        }

        // То же самое для Айву. Отдельный блюпринт нужен потому, что AddFeatureToPet умеет
        // выдать питомцу именно ФИЧУ, а не набор компонентов.
        //
        // Урон у неё сразу 1d10, без всякой проверки: в оригинале повышенный урон получал
        // зверь-медведь, а эта фича по построению достаётся только дракону Азаты
        // (m_PetType = AzataHavocDragon), так что проверять нечего.
        private static BlueprintFeature BuildStormPetFeature()
        {
            var feature = BuildHiddenFeature(Guids.StormPetFeatureGuid, "SingingBladeStormPetFeature");

            feature.ComponentsArray = new BlueprintComponent[]
            {
                NaturalAttackStorm(alwaysGreater: true),
                // У Айву гейта нет: бафф «Разрезать небеса» висит на магусе, а не на ней,
                // и её собственные криты к этой способности отношения не имеют.
                NaturalAttackLightningOnCrit(gated: false)
            };

            return feature;
        }

        // Пустая скрытая фича-носитель компонентов. Отдельным хелпером — чтобы не забыть
        // ни одно поле-контейнер: у BlueprintFeature инициализатора нет только у
        // IsPrerequisiteFor, но правило мода — задавать ВСЕ явно (см. CLAUDE.md).
        private static BlueprintFeature BuildHiddenFeature(string guid, string name)
        {
            var feature = new BlueprintFeature
            {
                Ranks = 1,
                ReapplyOnLevelUp = false,
                IsClassFeature = false,
                HideInUI = true,
                HideInCharacterSheetAndLevelUp = true,
                Groups = new FeatureGroup[0],
                IsPrerequisiteFor = new List<BlueprintFeatureReference>()
            };
            feature.AssetGuid = BlueprintGuid.Parse(guid);
            feature.name = name;

            Reflect.Set(feature, "m_DisplayName", SingingBladeLocalization.CreateString(L.StormName));
            Reflect.Set(feature, "m_Description", SingingBladeLocalization.CreateString(L.StormDescription));
            Reflect.Set(feature, "m_DescriptionShort", new LocalizedString());

            return feature;
        }

        // Звуковой и электрический урон на каждой естественной атаке.
        // alwaysGreater = true -> всегда 1d10 (вариант Айву);
        // иначе 1d10, если носитель совершил Первое вознесение азаты, и 1d6 в остальных
        // случаях — это замена ванильной проверке "он сейчас медведь".
        private static AddInitiatorAttackWithWeaponTrigger NaturalAttackStorm(bool alwaysGreater)
        {
            var greater = new GameAction[]
            {
                EnergyDamage(DamageEnergyType.Sonic, DiceType.D10),
                EnergyDamage(DamageEnergyType.Electricity, DiceType.D10)
            };

            GameAction[] actions;
            if (alwaysGreater)
            {
                actions = greater;
            }
            else
            {
                actions = new GameAction[]
                {
                    new Conditional
                    {
                        Comment = "Азата явил свою природу — гром и молния сильнее",
                        ConditionsChecker = new ConditionsChecker
                        {
                            Operation = Operation.And,
                            Conditions = new Condition[] { CasterHasFact(Guids.AzataFirstAscensionFeature) }
                        },
                        IfTrue = new ActionList { Actions = greater },
                        IfFalse = new ActionList
                        {
                            Actions = new GameAction[]
                            {
                                EnergyDamage(DamageEnergyType.Sonic, DiceType.D6),
                                EnergyDamage(DamageEnergyType.Electricity, DiceType.D6)
                            }
                        }
                    }
                };
            }

            return new AddInitiatorAttackWithWeaponTrigger
            {
                OnlyHit = true,
                AllNaturalAndUnarmed = true,
                Action = new ActionList { Actions = actions }
            };
        }

        // Крит естественной атакой бьёт молнией так же, как крит клинком.
        private static AddInitiatorAttackWithWeaponTrigger NaturalAttackLightningOnCrit(bool gated)
        {
            var action = gated ? (GameAction)LightningUnlessSkyCut() : CallLightning();

            return new AddInitiatorAttackWithWeaponTrigger
            {
                OnlyHit = true,
                CriticalHit = true,
                AllNaturalAndUnarmed = true,
                Action = new ActionList { Actions = new GameAction[] { action } }
            };
        }

        // Молния, но только пока НЕ активна способность «Разрезать небеса».
        //
        // Решение пользователя 2026-09-22: два эффекта на одном крите мешают друг другу —
        // когда магус ведёт заклинание через клинок, удар должен доставлять именно его,
        // а не устраивать заодно грозу. Поэтому гроза — режим "обычного" боя клинком.
        //
        // Проверяем бафф режима на КАСТЕРЕ, и это работает независимо от ActionsOnInitiator:
        // тот переключает только ЦЕЛЬ действий (AbstractWeaponTrigger.RunActions выбирает
        // rule.Initiator или rule.Target), а MaybeCaster в контексте зачарования — всегда
        // носитель оружия, его и видит ContextConditionCasterHasFact.
        private static Conditional LightningUnlessSkyCut()
        {
            return new Conditional
            {
                Comment = "Молния не бьёт, пока небеса уже разрезаны",
                ConditionsChecker = new ConditionsChecker
                {
                    Operation = Operation.And,
                    Conditions = new Condition[] { CasterHasFact(Guids.CutTheSkiesBuffGuid, not: true) }
                },
                IfTrue = new ActionList { Actions = new GameAction[] { CallLightning() } },
                IfFalse = new ActionList()
            };
        }

        // Удар молнии как от "Вызова грозы": 5d6, спасбросок Реакции со сложностью 24
        // уменьшает вдвое. Всё это лежит в самом заклинании, мы только переопределяем DC —
        // ровно как ванильный оригинал.
        private static ContextActionCastSpell CallLightning()
        {
            var cast = new ContextActionCastSpell
            {
                OverrideDC = true,
                DC = 24,
                OverrideSpellLevel = false,
                SpellLevel = 0,
                CastByTarget = false
            };
            Reflect.Set(cast, "m_Spell", Reflect.Ref<BlueprintAbilityReference>(Guids.CallLightningStormAbility));
            return cast;
        }

        private static ContextActionDealDamage EnergyDamage(DamageEnergyType energy, DiceType dice)
        {
            return new ContextActionDealDamage
            {
                DamageType = new Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription
                {
                    Type = Kingmaker.RuleSystem.Rules.Damage.DamageType.Energy,
                    Energy = energy
                },
                Value = new ContextDiceValue
                {
                    DiceType = dice,
                    DiceCountValue = 1,
                    BonusValue = 0
                },
                // Поле читается только для вариантов с истощением, но C#-null в контейнере
                // блюпринта — та самая привычка, на которой мод уже четырежды падал.
                Duration = new ContextDurationValue
                {
                    Rate = DurationRate.Rounds,
                    DiceType = DiceType.Zero,
                    DiceCountValue = 0,
                    BonusValue = 0
                }
            };
        }

    }
}
