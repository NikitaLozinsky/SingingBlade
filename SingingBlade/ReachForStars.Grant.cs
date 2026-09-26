using System;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using System.Collections.Generic;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace SingingBlade
{
    internal static partial class ReachForStars
    {
        // ----------------------------------------------------------------
        // Выдача переключателя носителю клинка
        // ----------------------------------------------------------------

        public static void RefreshParty()
        {
            var player = Game.Instance?.Player;
            if (player == null) return;

            foreach (var unit in player.Party)
            {
                RefreshToggle(unit);
            }
        }

        // Держит факт-переключатель в соответствии с тем, в руках ли Поющий клинок.
        public static void RefreshToggle(UnitEntityData unit)
        {
            try
            {
                if (unit?.Descriptor == null) return;

                // Какой из двух вариантов способности выдать, зависит от того, КАКОЙ у
                // персонажа мистический резерв: у Магуса и у Чародейского наследника это
                // разные блюпринты ресурса (см. AbilityForPool).
                BlueprintUnitFact granted = AbilityForPool(unit);
                if (granted == null)
                {
                    // Молчать здесь нельзя: ровно так мод однажды и "сломался" —
                    // блюпринт способности забыли зарегистрировать в
                    // SingingBladeBlueprints.Create(), TryGetBlueprint вернул null, выдача
                    // тихо не состоялась, и в Player.log не было НИ ОДНОЙ строки мода.
                    Main.Log("ReachForStars: блюпринт способности не найден в кэше — " +
                             "выдать нечего (проверь Register(...) в SingingBladeBlueprints.Create)");
                    return;
                }

                // На персонаже мог остаться факт ДРУГОГО варианта — от прошлого режима
                // (переключатель) или от другого резерва (если раньше выдали магусовский
                // вариант, а теперь видно, что резерв у него наследника). Снимаем всё
                // лишнее, иначе в панели висело бы две кнопки, одна из них нерабочая.
                foreach (var other in AllGrantable())
                {
                    if (other == null || other == granted) continue;
                    if (HasUsableFact(unit, other)) unit.Descriptor.RemoveFact(other);
                }

                var shouldHave = HoldsSingingBlade(unit);

                // Скрытая фича «Грозы Элизиума» (гром и молния на естественных атаках
                // носителя и его дракона) выдаётся ровно по тому же признаку и тем же
                // способом — из кода, а не через AddUnitFeatureEquipment на зачаровании:
                // штатная цепочка в этом моде уже рвалась молча (см. CLAUDE.md).
                RefreshWieldedFact(unit, Guids.StormFeatureGuid, shouldHave);

                // НЕ HasFact: способность, снятая с панели быстрого доступа, из фактов не
                // уходит, а только отключается (см. HasUsableFact). HasFact на неё отвечает
                // "да", и выдача считала бы, что всё на месте, — а в игре кнопка серая и на
                // нажатие "Недоступно". Ровно так и сломалось после "Пересоздать клинок".
                var hasIt = HasUsableFact(unit, granted);

                if (shouldHave && !hasIt)
                {
                    // Для отключённой способности это НЕ второй экземпляр: движок находит
                    // существующий факт и включает его обратно
                    // (AbilityCollection.PrepareFactForAttach: TurnOn + TemporarilyDisabled = false).
                    unit.Descriptor.AddFact(granted);
                }
                else if (!shouldHave && hasIt)
                {
                    unit.Descriptor.RemoveFact(granted);
                }

                // Клинок убрали из рук — снимаем и сам режим. Иначе в дистанционном режиме
                // у персонажа осталась бы удлинённая досягаемость (бафф доживает свой раунд
                // сам по себе) уже без Поющего клинка, что выглядит как читерский бонус из ниоткуда.
                if (!shouldHave)
                {
                    var modeBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(Guids.ReachForStarsBuffGuid);
                    if (modeBuff != null && unit.Descriptor.HasFact(modeBuff))
                    {
                        unit.Descriptor.RemoveFact(modeBuff);
                    }

                    // И временную растяжку досягаемости тоже.
                    DropExtendedReach();
                }
            }
            catch (Exception e)
            {
                Main.LogError("ReachForStars.RefreshToggle", e);
            }
        }

        // Какой из двух вариантов способности подходит персонажу.
        //
        // Мистический резерв Магуса и резерв Чародейского наследника — РАЗНЫЕ блюпринты
        // ресурса: архетип Наследника (и его отдельный класс) убирает магусовскую
        // ArcanePoolFeature и ставит свою EldritchPoolFeature. Компонент
        // AbilityResourceLogic знает ровно один ресурс, поэтому вариантов способности два,
        // и выбираем тот, чей резерв у персонажа реально есть.
        //
        // Проверяем НАЛИЧИЕ ресурса, а не его остаток: у персонажа, потратившего резерв
        // до нуля, ресурс в коллекции есть, просто с нулевым Amount.
        //
        // Если резерва нет вовсе (клинок попал к не-магусу), выдаём магусовский вариант:
        // работать он всё равно не будет, но так поведение детерминированное и кнопка
        // честно показывает "нет ресурсов", а не исчезает без объяснений.
        private static BlueprintAbility AbilityForPool(UnitEntityData unit)
        {
            var guid = HasResource(unit, Guids.EldritchPoolResource)
                       && !HasResource(unit, Guids.ArcanePoolResource)
                ? Guids.ReachForStarsAbilityEldritchGuid
                : Guids.ReachForStarsAbilityGuid;

            return ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(guid);
        }

        private static bool HasResource(UnitEntityData unit, string resourceGuid)
        {
            var resource = ResourcesLibrary.TryGetBlueprint<BlueprintAbilityResource>(resourceGuid);
            if (resource == null || unit?.Descriptor?.Resources == null) return false;

            foreach (var owned in unit.Descriptor.Resources)
            {
                if (owned == resource) return true;
            }

            return false;
        }

        // Всё, что мод вообще умеет выдавать носителю клинка. Нужно, чтобы снимать
        // варианты, ставшие неактуальными (смена режима сборки, смена резерва).
        private static IEnumerable<BlueprintUnitFact> AllGrantable()
        {
            foreach (var ability in ModeAbilities()) yield return ability;
        }

        // Держит факт в соответствии с тем, в руках ли клинок. Отдельным методом, потому
        // что таких фактов уже два (способность режима и фича «Грозы Элизиума»), и логика
        // "добавить, если надо; снять, если больше не надо" у них одна и та же.
        private static void RefreshWieldedFact(UnitEntityData unit, string factGuid, bool shouldHave)
        {
            var fact = ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(factGuid);
            if (fact == null)
            {
                Main.Log("ReachForStars: блюпринт " + factGuid + " не найден в кэше — " +
                         "выдавать нечего (проверь Register(...) в SingingBladeBlueprints.Create)");
                return;
            }

            var hasIt = HasUsableFact(unit, fact);
            if (shouldHave && !hasIt) unit.Descriptor.AddFact(fact);
            else if (!shouldHave && hasIt) unit.Descriptor.RemoveFact(fact);
        }

        // Есть ли у персонажа факт В РАБОЧЕМ состоянии.
        //
        // Для способностей это не то же самое, что HasFact. Если способность лежит на панели
        // быстрого доступа, RemoveFact её НЕ удаляет: AbilityCollection.PrepareFactForDetach
        // при HasActionBarSlot делает TurnOff, ставит Data.TemporarilyDisabled = true и
        // оставляет факт на месте — чтобы кнопка на панели пережила, например, снятие
        // предмета и ожила при повторной экипировке. HasFact такую способность видит, а
        // пользоваться ею нельзя (MechanicActionBarSlotAbility.IsPossibleActive ->
        // TemporarilyDisabled, на нажатие игра пишет "Недоступно").
        //
        // Отсюда и второе правило: отключённую способность не "снимаем" повторно — её
        // TurnOff второй раз только пишет в лог "EntityFact.TurnOff: is not turned on".
        internal static bool HasUsableFact(UnitEntityData unit, BlueprintUnitFact fact)
        {
            if (fact == null || unit?.Descriptor == null) return false;

            if (fact is BlueprintAbility ability)
            {
                var owned = unit.Descriptor.Abilities.GetAbility(ability);
                return owned != null && !owned.Data.TemporarilyDisabled;
            }

            return unit.Descriptor.HasFact(fact);
        }

        public static bool HoldsSingingBlade(UnitEntityData unit)
        {
            var item = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(Guids.ItemGuid);
            if (item == null || unit?.Body == null) return false;

            return unit.Body.PrimaryHand?.MaybeWeapon?.Blueprint == item
                || unit.Body.SecondaryHand?.MaybeWeapon?.Blueprint == item;
        }

    }
}
