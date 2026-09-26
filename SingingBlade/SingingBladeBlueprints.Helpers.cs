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
        // Общие помощники
        // ----------------------------------------------------------------

        // ContextRankConfig, считающий уровень персонажа по классу Магус ИЛИ Eldritch Scion
        // (в этой игре Eldritch Scion — отдельный BlueprintCharacterClass, а не архетип поверх
        // Магуса, поэтому Archetype-фильтр не нужен: достаточно перечислить оба класса в m_Class).
        private static ContextRankConfig MagusLevelRank(AbilityRankType tag, ContextRankProgression progression, int startLevel, int stepLevel)
        {
            var rank = new ContextRankConfig();
            Reflect.Set(rank, "m_Type", tag);
            Reflect.Set(rank, "m_BaseValueType", ContextRankBaseValueType.MaxClassLevelWithArchetype);
            Reflect.Set(rank, "m_Progression", progression);
            Reflect.Set(rank, "m_StartLevel", startLevel);
            Reflect.Set(rank, "m_StepLevel", stepLevel);
            Reflect.Set(rank, "Archetype", Reflect.Empty<BlueprintArchetypeReference>());
            Reflect.Set(rank, "m_AdditionalArchetypes", new BlueprintArchetypeReference[0]);
            Reflect.Set(rank, "m_Class", new[]
            {
                Reflect.Ref<BlueprintCharacterClassReference>(Guids.MagusClass),
                Reflect.Ref<BlueprintCharacterClassReference>(Guids.EldritchScionClass)
            });
            return rank;
        }

        private static Condition CasterHasFact(string guid, bool not = false)
        {
            var condition = new ContextConditionCasterHasFact { Not = not };
            Reflect.Set(condition, "m_Fact", Reflect.Ref<BlueprintUnitFactReference>(guid));
            return condition;
        }

        // Длительности заданы в СЕКУНДАХ, а не в раундах, и это принципиально.
        //
        // Раньше и песня, и маркер "уже спели в этом раунде" висели ровно 1 раунд и
        // истекали в один и тот же миг. Получался самоблок: чтобы ПРОДЛИТЬ песню,
        // критовать надо пока она ещё жива, — но тогда жив и маркер, который новую
        // песню запрещает. Диагностика в игре это подтвердила: в момент возврата хода и
        // песня, и маркер висели с остатком 0,0 — гейт срабатывал, проверка навыка
        // не запускалась, и следом всё снималось. Продлить песню было невозможно в
        // принципе, независимо от Заклинательного удара.
        //
        // Лечится это РАЗВЕДЕНИЕМ длительностей, а не удлинением песни:
        //  - маркер (4с) заведомо короче раунда. Внутри одного хода он всё ещё висит
        //    (в пошаговом режиме длительности считаются от TurnStartTime, поэтому все
        //    удары серии видят его на месте) и честно держит лимит "не больше одной
        //    песни за раунд", но к следующему ходу гарантированно истекает и не мешает
        //    продлению;
        //  - песня (7с) — это раунд плюс секунда запаса.
        //
        // Про эту секунду отдельно, её легко "оптимизировать" и сломать дважды.
        // Ровно 6с не годятся: EndTime тогда совпадает с началом следующего хода,
        // условие снятия `EndTime <= currentTime` выполняется, и бафф снимается В
        // НАЧАЛЕ хода — то есть ДО крита. Продлевать становится нечего, каждый раунд
        // накладывается новый бафф, FxOnStart проигрывается заново (кольцо мигает), а
        // союзники теряют бонус в промежутке между началом хода и критом. Секунда
        // запаса оставляет бафф живым к моменту крита, и Stacking=Prolong двигает ему
        // конец вперёд — без мигания и без провала в бонусе.
        //
        // БЫЛО 9с (полтора раунда) — это ошибка, которую пришлось откатывать: песня
        // переживала следующий раунд САМА, продлевать было нечего, проверка навыка не
        // запускалась, и в логе её не появлялось. Пользователь совершенно верно заметил,
        // что проверка должна требоваться и для продления тоже. Не удлинять песню
        // больше раунда с запасом.
        private const float SongDurationSeconds = 7f;
        private const float SungThisRoundFlagSeconds = 4f;

        private static ContextActionApplyBuff ApplyBuff(string buffGuid, bool toCaster = false, float seconds = SongDurationSeconds)
        {
            var action = new ContextActionApplyBuff
            {
                ToCaster = toCaster,
                AsChild = false,
                Permanent = false,
                UseDurationSeconds = true,
                DurationSeconds = seconds,
                // Поле всё равно не должно быть "пустым мусором": ContextActionApplyBuff
                // читает DurationValue только при UseDurationSeconds == false, но
                // оставлять его недозаполненным — ровно та привычка, на которой мы уже
                // ловили NullReferenceException в других компонентах.
                DurationValue = new ContextDurationValue
                {
                    Rate = DurationRate.Rounds,
                    DiceType = DiceType.Zero,
                    DiceCountValue = 0,
                    BonusValue = 1
                }
            };
            Reflect.Set(action, "m_Buff", Reflect.Ref<BlueprintBuffReference>(buffGuid));
            return action;
        }

        private static ContextActionRemoveBuff RemoveBuff(string buffGuid)
        {
            var action = new ContextActionRemoveBuff();
            Reflect.Set(action, "m_Buff", Reflect.Ref<BlueprintBuffReference>(buffGuid));
            return action;
        }

        private static AddContextStatBonus StatBonusFromRank(ModifierDescriptor descriptor, StatType stat, AbilityRankType rankTag)
        {
            return new AddContextStatBonus
            {
                Descriptor = descriptor,
                Stat = stat,
                Multiplier = 1,
                Value = new ContextValue { ValueType = ContextValueType.Rank, ValueRank = rankTag }
            };
        }

        // m_Icon у предметов/фактов — прямая ссылка на UnityEngine.Sprite, а не
        // строковый BlueprintReference. Сначала пробуем свою иконку из Assets, и только
        // если её нет или файл не прочитался — одалживаем уже загруженную у ванильного
        // блюпринта (резолвить guid+fileid вручную не нужно и не стоит).
        private static Sprite ItemIcon(string blueprintGuid, string assetFile = null)
        {
            return ModIcons.Load(assetFile)
                   ?? ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintGuid)?.Icon;
        }

        private static Sprite FactIcon(string blueprintGuid, string assetFile = null)
        {
            return ModIcons.Load(assetFile)
                   ?? ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(blueprintGuid)?.Icon;
        }

        private static AddContextStatBonus StatBonusFlat(ModifierDescriptor descriptor, StatType stat, int amount)
        {
            return new AddContextStatBonus
            {
                Descriptor = descriptor,
                Stat = stat,
                Multiplier = 1,
                Value = amount
            };
        }
    }
}
