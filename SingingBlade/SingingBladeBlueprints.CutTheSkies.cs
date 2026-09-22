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
        // «Разрезать небеса»: переключатель проведения лучевых заклинаний через клинок
        // ----------------------------------------------------------------

        // Бафф "режим включён". Обязателен сам по себе: BlueprintActivatableAbility
        // требует непустой m_Buff. Заодно по нему рантайм-код понимает, включён ли режим
        // (см. CutTheSkies.IsModeActive).
        private static BlueprintBuff BuildCutTheSkiesBuff()
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.CutTheSkiesBuffGuid);
            buff.name = "SingingBladeCutTheSkiesBuff";

            // ВСЯ механика дистанционного удара — вот этот один компонент, и до
            // 2026-09-22 его здесь не было вовсе: бафф вешался, очко резерва тратилось,
            // а клинок оставался обычным (симптом у игрока: "способность тратится, но
            // ничего не происходит"). В старой схеме (RangedStrike = false) досягаемость
            // меняться НЕ должна — там подход к цели делает код, — поэтому компонент
            // ставится только в режиме дистанционного удара.
            //
            // Почему бонус к стату, а не патч дальности: движок считает дальность оружия
            // как AttackRange + ReachRange, где ReachRange = max(Reach - 5, 0)
            // (CharacterStats.ReachRange, UnitDescriptor.GetWeaponRange). У среднего
            // существа Reach = 5 (модификатор размера, WeaponSizeExtension), значит
            // +20 к стату даёт ReachRange 20 футов и дальность удара 5 + 20 = 25.
            // Дальше всё работает само: ItemEntityWeapon.AttackRange спрашивает
            // GetWeaponRange у владельца, а UnitAttack.GetApproachRadius — у оружия,
            // поэтому цель в 25 футах считается "достаточно близкой" и подход не нужен.
            //
            // AddStatBonus, а не AddContextStatBonus: ровно так это делает ваниль для
            // этого же стата (BloodragerSerpentineElasticityBuff, +5 Reach,
            // Descriptor = UntypedStackable), и он не зависит от наличия контекста.
            buff.ComponentsArray = new BlueprintComponent[]
            {
                new AddStatBonus
                {
                    Descriptor = ModifierDescriptor.UntypedStackable,
                    Stat = StatType.Reach,
                    Value = CutTheSkies.ReachBonusFeet,
                    ScaleByBasicAttackBonus = false
                }
            };

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(L.CutTheSkiesName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(L.CutTheSkiesDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.SpellStrikeAbility, ModIcons.CutTheSkies));

            // Оба PrefabLink обязаны быть не null — иначе Buff.OnRemove() падает и
            // иконка баффа навсегда залипает в панели (подробный разбор в BuildSongBuff).
            buff.FxOnStart = new PrefabLink();
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

        // Скрытый бафф-«растяжка»: дотягивает досягаемость клинка до конкретной цели,
        // когда заклинание уходит дальше обычного удара (см. CutTheSkies.TryReachTarget).
        //
        // Величина добавки переменная, а Value у AddStatBonus — поле блюпринта, одно на всех.
        // Поэтому величина задаётся РАНГОМ баффа: AddStatBonus.OnTurnOn считает
        // Value * Fact.GetRank(), у буффа GetRank() возвращает Rank, а Rank ставится
        // экземпляру через Buff.SetRank(). При Value = 1 ранг N даёт ровно +N футов.
        //
        // ВАЖНО: Ranks обязан быть больше любой мыслимой добавки. SetRank обрезает по
        // Blueprint.Ranks, а по умолчанию там 0 — с нулём бонус молча вышел бы нулевым.
        //
        // Длительность ставится при наложении (один раунд) и нужна как последняя страховка:
        // даже если все явные точки снятия почему-то не сработают, добавка исчезнет сама.
        private static BlueprintBuff BuildReachStretchBuff()
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = MaxReachStretchFeet
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.ReachStretchBuffGuid);
            buff.name = "SingingBladeReachStretchBuff";

            buff.ComponentsArray = new BlueprintComponent[]
            {
                new AddStatBonus
                {
                    Descriptor = ModifierDescriptor.UntypedStackable,
                    Stat = StatType.Reach,
                    Value = 1,
                    ScaleByBasicAttackBonus = false
                }
            };

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(L.CutTheSkiesName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(L.CutTheSkiesDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.SpellStrikeAbility, ModIcons.CutTheSkies));

            // Служебный бафф — в панели ему делать нечего.
            Reflect.SetEnum(buff, "m_Flags", 2); // BlueprintBuff.Flags.HiddenInUi

            buff.FxOnStart = new PrefabLink();
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

        // Потолок растяжки досягаемости, в футах. Дальность "далёкого" заклинания на
        // высоких уровнях доходит до сотен футов, но бить клинком через пол-локации
        // смысла нет; к тому же Ranks у баффа — это верхняя граница ранга.
        public const int MaxReachStretchFeet = 120;

        // Способность «Разрезать небеса»: быстрое действие, стоит одно очко Мистического
        // резерва, на один раунд вешает на магуса бафф режима.
        //
        // Форма скопирована с ванильной магусовской арканы "Мистическая точность"
        // (ArcaneAccuracyAbility в Classes/Magus/Arcanas): тот же Type = Extraordinary,
        // Range = Personal, ActionType = Swift и тот же компонент AbilityResourceLogic на
        // ресурс ArcanePoolResourse. Это ровно тот способ, которым ваниль берёт плату из
        // Мистического резерва, — своего изобретать не нужно.
        //
        //
        // ВАРИАНТОВ ДВА — по одному на каждый вид мистического резерва.
        // `AbilityResourceLogic` умеет ровно один `m_RequiredResource`, а у Магуса и у
        // Чародейского наследника резервы это РАЗНЫЕ блюпринты: архетип (и отдельный класс)
        // Наследника заменяет магусовскую ArcanePoolFeature на EldritchPoolFeature, которая
        // выдаёт свой ресурс. Первая версия знала только про магусовский — у Наследника
        // счётчик на кнопке показывал 0, а игра на нажатие отвечала "нет ресурсов".
        // Ваниль в этом месте делает ровно то же самое, что и мы: дублирует способность
        // под каждый резерв (ArcaneWeaponSwitchAbility / EldritchWeaponSwitchAbility).
        // Кому какой вариант выдать, решает CutTheSkies.RefreshToggle.
        private static BlueprintAbility BuildCutTheSkiesAbility(string abilityGuid, string resourceGuid, string name)
        {
            var ability = new BlueprintAbility
            {
                Type = AbilityType.Extraordinary,
                Range = AbilityRange.Personal,
                CanTargetPoint = false,
                CanTargetFriends = false,
                CanTargetEnemies = false,
                CanTargetSelf = true,
                SpellResistance = false,
                NotOffensive = true,
                Hidden = false,
                ActionBarAutoFillIgnored = false,
                EffectOnAlly = AbilityEffectOnUnit.None,
                EffectOnEnemy = AbilityEffectOnUnit.None,
                ActionType = Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Swift,
                // Анимация — тоже как у Мистической точности: короткий жест "на себя",
                // а не дефолтный Omni (замах в сторону цели). Цели у способности нет,
                // и Omni на быстром действии выглядит как лишний взмах в пустоту.
                // (Соседнее поле AnimationStyle в ванильном .jbp тоже заполнено, но оно
                // помечено [Obsolete] и его не читает НИКТО в декомпиле — не дублируем.)
                Animation = Kingmaker.Visual.Animation.Kingmaker.Actions.UnitAnimationActionCastSpell.CastAnimationStyle.Self,
                // Пустой (не null!) массив — из той же семьи рисков, что PrefabLink и
                // ActionList: у блюпринтов из JSON контейнеры созданы всегда, а мы строим
                // блюпринт через new. Здесь потребитель (ResourcesPreload) как раз
                // страхуется EmptyIfNull(), но правило мода — не оставлять C#-null.
                ResourceAssetIds = new string[0]
            };
            ability.AssetGuid = BlueprintGuid.Parse(abilityGuid);
            ability.name = name;

            Reflect.Set(ability, "m_DisplayName", SingingBladeLocalization.CreateString(L.CutTheSkiesName));
            Reflect.Set(ability, "m_Description", SingingBladeLocalization.CreateString(L.CutTheSkiesDescription));
            Reflect.Set(ability, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(ability, "m_Icon", FactIcon(Guids.SpellStrikeAbility, ModIcons.CutTheSkies));

            // Эта способность, в отличие от "Голоса клинка", ВИДНА игроку, поэтому пустые
            // (не null!) LocalizedString здесь обязательны: иначе в тултипе в строках
            // "Длительность"/"Спасбросок" покажется буквальное "<null>".
            ability.LocalizedDuration = new LocalizedString();
            ability.LocalizedSavingThrow = new LocalizedString();

            var runAction = new AbilityEffectRunAction
            {
                Actions = new ActionList
                {
                    Actions = new GameAction[]
                    {
                        ApplyBuff(Guids.CutTheSkiesBuffGuid, toCaster: true,
                                  seconds: CutTheSkies.ModeDurationSeconds)
                    }
                }
            };

            var resource = new AbilityResourceLogic
            {
                CostIsCustom = false,
                Amount = 1,
                ResourceCostIncreasingFacts = new List<BlueprintUnitFactReference>(),
                ResourceCostDecreasingFacts = new List<BlueprintUnitFactReference>()
            };
            Reflect.Set(resource, "m_RequiredResource",
                        Reflect.Ref<BlueprintAbilityResourceReference>(resourceGuid));
            Reflect.Set(resource, "m_IsSpendResource", true);

            ability.ComponentsArray = new BlueprintComponent[] { runAction, resource };

            return ability;
        }

    }
}
