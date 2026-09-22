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
        // Способность-эффект: AoE вокруг атакующего на крите
        // ----------------------------------------------------------------

        private static BlueprintAbility BuildAbility()
        {
            var ability = new BlueprintAbility
            {
                Type = AbilityType.Special,
                Range = AbilityRange.Custom,
                CustomRange = new Feet(SongRadiusFeet),
                CanTargetPoint = true,
                CanTargetFriends = true,
                CanTargetEnemies = false,
                CanTargetSelf = true,
                SpellResistance = false,
                NotOffensive = false,
                Hidden = true,
                ActionBarAutoFillIgnored = true,
                EffectOnAlly = AbilityEffectOnUnit.Helpful,
                EffectOnEnemy = AbilityEffectOnUnit.Harmful,
                ActionType = Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Free
            };
            ability.AssetGuid = BlueprintGuid.Parse(Guids.AbilityGuid);
            ability.name = "SingingBladeAbility";

            Reflect.Set(ability, "m_DisplayName", SingingBladeLocalization.CreateString(L.AbilityName));
            Reflect.Set(ability, "m_Description", SingingBladeLocalization.CreateString(L.AbilityDescription));
            // Пустая (не null) — как и у m_EnchantName выше, иначе "<null>" в UI.
            Reflect.Set(ability, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(ability, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility, ModIcons.Song));

            // Пустые (не null!) контейнеры. Способность показывается игроку разве что
            // строкой в боевом логе, но правило мода одно для всех блюпринтов: у собранного
            // через new ничего не должно остаться C#-null (см. CLAUDE.md). LocalizedString
            // при показе даёт буквальное "<null>", массив ресурсов читает ResourcesPreload.
            ability.LocalizedDuration = new LocalizedString();
            ability.LocalizedSavingThrow = new LocalizedString();
            ability.ResourceAssetIds = new string[0];

            // Способность больше НЕ раздаёт баффы союзникам. Раньше здесь стоял
            // AbilityTargetsAround — снимок союзников в радиусе на момент срабатывания,
            // из-за которого вошедший в радиус позже ничего не получал, а вышедший
            // уносил бафф с собой. Теперь раздачей занимается зона (BuildSongArea),
            // которая живёт на исполнителе, пока висит SongAureole.
            //
            // Сама способность оставлена ради строки "творит заклинание" в боевом
            // логе — это наглядный признак того, что песнь действительно зазвучала.
            // Эффекта у неё нет, поэтому ActionList пустой, но НЕ null: пустые
            // ActionList в этом движке безопасны, а вот C#-null роняет компоненты.
            var runAction = new AbilityEffectRunAction
            {
                Actions = new ActionList { Actions = new GameAction[0] }
            };

            ability.ComponentsArray = new BlueprintComponent[] { runAction };

            return ability;
        }

        // ----------------------------------------------------------------
        // Бафф союзникам ("Песнь клинка") — форк Inspire Courage под Магуса
        // ----------------------------------------------------------------

        private static BlueprintBuff BuildSongBuff(string guid, string name, bool empowered)
        {
            var buff = new BlueprintBuff
            {
                // Prolong, а не Replace: при новом успешном крите песня ПРОДЛЕВАЕТСЯ —
                // движок оставляет тот же самый экземпляр Buff и просто двигает EndTime
                // вперёд (см. BuffCollection.PrepareFactForAttach, case StackingType.Prolong:
                // SetEndTime только если новый конец позже старого, длительность не копится).
                // При Replace старый бафф снимался и накладывался новый — то есть каждый раунд
                // это был отдельный цикл "снять/наложить": FxOnStart проигрывался заново
                // (песня визуально "начиналась с нуля", а не продолжалась) и иконка в панели
                // успевала мигнуть. Prolong убирает эту рваность.
                Stacking = StackingType.Prolong,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(guid);
            buff.name = name;

            var rankTag = AbilityRankType.Default;

            var components = new List<BlueprintComponent>
            {
                // Та же прогрессия, что и у ванильной Inspire Courage: +1 на 1 уровне,
                // +1 каждые 6 уровней — но считаем по уровню класса Магус (и Eldritch Scion).
                MagusLevelRank(rankTag, ContextRankProgression.StartPlusDivStep, startLevel: -1, stepLevel: 6),
                StatBonusFromRank(ModifierDescriptor.Competence, StatType.AdditionalAttackBonus, rankTag),
                StatBonusFromRank(ModifierDescriptor.Competence, StatType.AdditionalDamage, rankTag),
                new SavingThrowContextBonusAgainstDescriptor
                {
                    SpellDescriptor = SpellDescriptor.Fear | SpellDescriptor.Charm,
                    ModifierDescriptor = ModifierDescriptor.Morale,
                    Value = new ContextValue { ValueType = ContextValueType.Rank, ValueRank = rankTag }
                }
            };

            if (empowered)
            {
                // Дополнительный гарантированный +1: UntypedStackable специально не конфликтует
                // с Competence-бонусом выше (бонусы одного типа в этом движке не суммируются,
                // берётся только больший) — а мы хотим именно "плюс сверху", а не "выбрать большее".
                components.Add(StatBonusFlat(ModifierDescriptor.UntypedStackable, StatType.AdditionalAttackBonus, 1));
                components.Add(StatBonusFlat(ModifierDescriptor.UntypedStackable, StatType.AdditionalDamage, 1));
            }

            buff.ComponentsArray = components.ToArray();

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(empowered ? L.SongBuffEmpoweredName : L.SongBuffName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(empowered ? L.SongBuffEmpoweredDescription : L.SongBuffDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility, ModIcons.Song));
            // Скромная вспышка на самом получателе песни. Кольцо-ауреоль сюда вешать НЕЛЬЗЯ:
            // FxOnStart спавнится на владельце баффа (Buff.TrySpawnParticleEffect ->
            // FxHelper.SpawnFxOnUnit(prefab, Owner.Unit.View)), а этот бафф получает каждый
            // союзник в радиусе — при кучном строе кольца накладывались друг на друга в
            // "плотную" ауру. Кольцо теперь на отдельном SongAureole, только на исполнителе.
            buff.FxOnStart = new PrefabLink { AssetId = Guids.InspireCourageBuffFx };
            // КРИТИЧНО (причина бага с вечными и множащимися иконками в панели баффов):
            // Buff.OnRemove() при снятии ЛЮБОГО баффа безусловно вызывает
            // base.Blueprint.FxOnRemove.Load() — без проверки на null. У блюпринтов,
            // прочитанных из JSON, PrefabLink всегда создан (пусть и с пустым AssetId),
            // а мы строим блюпринт в рантайме, и поле оставалось C#-null -> NullReferenceException.
            // Исключение ловится и ГЛОТАЕТСЯ в EntityFactsManager.DelegateOnFactWillDetach
            // (try/catch + лог), поэтому игра не падала — но в BuffCollection.OnFactWillDetach
            // строка EventBus.RaiseEvent(h => h.HandleBuffDidRemoved(fact)) идёт ПОСЛЕ
            // fact.OnRemove() и уже не выполнялась. UI (UnitBuffPartVM) не получал события
            // снятия -> BuffVM с иконкой навсегда оставалась в панели, а каждая следующая
            // песня добавляла ещё одну. При этом модификаторы снимались нормально (они
            // обрабатываются в OnRemove ДО падения), отсюда и "иконка висит, а эффекта нет".
            // Пустой PrefabLink безопасен: WeakResourceLink.Load() возвращает null при пустом
            // AssetId, а FxHelper.SpawnFxOnUnit(null, ...) отсекается проверкой `if ((bool)prefab`.
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

        // ----------------------------------------------------------------
        // Зона песни: бафф выдаётся по нахождению в радиусе, а не снимком
        // ----------------------------------------------------------------

        // Раньше песня раздавалась СНИМКОМ: AbilityTargetsAround собирал союзников в
        // радиусе в момент срабатывания и вешал им бафф на фиксированный срок. Кто
        // вошёл в радиус позже — не получал ничего, кто вышел — уносил бафф с собой.
        // Теперь это настоящая аура: зона висит на исполнителе, AbilityAreaEffectBuff
        // сам вешает бафф на входе и снимает на выходе.
        //
        // ВАЖНО, почему на этот раз area effect безопасен (дважды обжигались):
        //  - предыдущий крах давал НЕ сам AddAreaEffect, а компонент
        //    AbilityAreaEffectRunAction: у него четыре публичных поля ActionList, и
        //    незаполненные роняли AreaEffectEntityData.HandleEnd() каждый тик, из-за
        //    чего зона не закрывалась в принципе. У AbilityAreaEffectBuff полей
        //    ActionList НЕТ вовсе — вход/выход он обрабатывает сам;
        //  - единственное поле, которое обязано быть не-null, это Condition:
        //    IsConditionPassed() вызывает Condition.Check() без проверки. Пустой
        //    ConditionsChecker задан у всех трёх компонентов явно.
        //
        // Структура скопирована с ванильной InspireCourageArea: там ровно так же —
        // несколько AbilityAreaEffectBuff с разными условиями выбирают, какой из
        // вариантов баффа повесить, а кольцо лежит в поле Fx самой зоны.
        private static BlueprintAbilityAreaEffect BuildSongArea()
        {
            var area = new BlueprintAbilityAreaEffect
            {
                SpellResistance = false,
                AffectEnemies = false,
                AggroEnemies = false,
                AffectDead = false,
                IgnoreSleepingUnits = false,
                Shape = AreaEffectShape.Cylinder,
                Size = new Feet(SongRadiusFeet),
                CanBeUsedInTacticalCombat = false,
                // Кольцо выступления живёт ЗДЕСЬ, а не в FxOnStart баффа. Так это
                // устроено у ванильной InspireCourageArea, и так у него правильный
                // жизненный цикл: появляется вместе с зоной, исчезает вместе с ней.
                Fx = new PrefabLink { AssetId = Guids.InspireCourageAreaFx }
            };
            area.AssetGuid = BlueprintGuid.Parse(Guids.SongAreaGuid);
            area.name = "SingingBladeSongArea";
            // ВНИМАНИЕ, грабли: у BlueprintAbilityAreaEffect поле m_TargetType имеет тип
            // ПРИВАТНОГО ВЛОЖЕННОГО enum'а BlueprintAbilityAreaEffect.TargetType (Any=0,
            // Ally=1, Enemy=2), а по имени TargetType из usings этого файла виден совсем
            // другой enum — Kingmaker.UnitLogic.Abilities.Components.TargetType (Enemy=0,
            // Ally=1, Any=2), тот самый, что нужен AbilityTargetsAround. Reflect.Set с ним
            // КОМПИЛИРУЕТСЯ (аргумент object), но в рантайме FieldInfo.SetValue бросает
            // ArgumentException прямо внутри постфикса LoadPackTOC — и игра навсегда
            // повисает на 70% загрузки. Поэтому только сырое значение через SetEnum.
            // Совпадение Ally=1 в обоих enum'ах случайное, порядок членов разный.
            Reflect.SetEnum(area, "m_TargetType", 1); // TargetType.Ally

            Reflect.Set(area, "m_Tags", AreaEffectTags.None);
            Reflect.Set(area, "m_AllowNonContextActions", false);
            Reflect.Set(area, "m_SizeInCells", 0);
            Reflect.Set(area, "m_TickRoundAfterSpawn", false);

            // Два варианта, взаимоисключающие по условию. "Усиленная при Боевом
            // заклинании ИЛИ Заклинательном ударе" — это ОДИН компонент с Operation.Or,
            // а не два с And: ConditionsChecker.Check честно умеет обе операции.
            // Разносить по двум компонентам нельзя — каждый раунд они затирали бы друг
            // друга: OnRound идёт по компонентам по порядку, и тот, чьё условие сейчас
            // не выполнено, делает TryRemoveBuff ровно того баффа, который только что
            // повесил (или сейчас повесит) соседний. Союзники каждый раунд получали бы
            // снятие и повторное наложение — с перезапуском FxOnStart на каждом.
            area.ComponentsArray = new BlueprintComponent[]
            {
                AreaBuff(Guids.SongBuffEmpoweredGuid, Operation.Or,
                    CasterHasFact(Guids.SpellCombatBuff),
                    CasterHasFact(Guids.SpellStrikeBuff)),
                AreaBuff(Guids.SongBuffGuid, Operation.And,
                    CasterHasFact(Guids.SpellCombatBuff, not: true),
                    CasterHasFact(Guids.SpellStrikeBuff, not: true))
            };

            return area;
        }

        private static AbilityAreaEffectBuff AreaBuff(string buffGuid, Operation operation, params Condition[] conditions)
        {
            var component = new AbilityAreaEffectBuff
            {
                // Пересчитывать каждый раунд обязательно: состояние Боевого заклинания
                // и Заклинательного удара у исполнителя меняется между раундами, а зона
                // при продлении песни НЕ пересоздаётся (Stacking=Prolong оставляет тот
                // же бафф-носитель). Без пересчёта союзник так и остался бы с тем
                // вариантом, который был в момент его входа в радиус.
                CheckConditionEveryRound = true,
                Condition = new ConditionsChecker
                {
                    Operation = operation,
                    Conditions = conditions
                }
            };
            Reflect.Set(component, "m_Buff", Reflect.Ref<BlueprintBuffReference>(buffGuid));
            return component;
        }

        // Чисто визуальный бафф-носитель кольца/ауреоли выступления. Механического эффекта
        // нет вообще — нужен только затем, чтобы у Fx был владелец с нормальным жизненным
        // циклом (спавн в Buff.TrySpawnParticleEffect, уничтожение в Buff.ClearParticleEffect),
        // и чтобы этот владелец был РОВНО ОДИН — сам исполнитель (накладывается toCaster),
        // а не каждый союзник в радиусе, как было, пока кольцо висело на SongBuff.
        // Скрыт из UI: собственной иконки у него нет и в панели баффов ему делать нечего.
        private static BlueprintBuff BuildSongAureole()
        {
            var buff = new BlueprintBuff
            {
                // Prolong — по той же причине, что и у самой песни: продлённое выступление
                // не должно перезапускать Fx (иначе кольцо мигало бы каждый раунд).
                Stacking = StackingType.Prolong,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.SongAureoleGuid);
            buff.name = "SingingBladeSongAureole";

            // Этот бафф теперь не просто носитель картинки, а носитель ЗОНЫ песни:
            // пока он висит на исполнителе, вокруг него живёт аура, раздающая бафф
            // союзникам по входу в радиус. Истёк (песню не продлили) — AddAreaEffect
            // в OnDeactivate делает ForceEnd(), зона закрывается, баффы снимаются со
            // всех разом.
            var areaEffect = new AddAreaEffect();
            Reflect.Set(areaEffect, "m_AreaEffect", Reflect.Ref<BlueprintAbilityAreaEffectReference>(Guids.SongAreaGuid));
            buff.ComponentsArray = new BlueprintComponent[] { areaEffect };

            // Пустые (не null!) LocalizedString — бафф скрыт, но правило "никаких C#-null
            // в полях блюпринта" общее: иначе движок покажет строку "<null>", если до этих
            // полей всё-таки кто-то доберётся (инспектор, отладочный вывод).
            Reflect.Set(buff, "m_DisplayName", new LocalizedString());
            Reflect.Set(buff, "m_Description", new LocalizedString());
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.SetEnum(buff, "m_Flags", 2); // BlueprintBuff.Flags.HiddenInUi

            // Кольцо переехало в Fx самой зоны (см. BuildSongArea) — там у него
            // правильный жизненный цикл, завязанный на существование зоны. Здесь
            // FxOnStart остаётся пустым, но НЕ null: Buff.OnRemove() безусловно
            // дёргает FxOnRemove.Load(), и C#-null там роняет снятие баффа.
            buff.FxOnStart = new PrefabLink();
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

        // Служебный маркер "уже спели в этом раунде" — чистый флаг без механического
        // эффекта, 1 раунд длительности (естественно сгорает к следующему раунду).
        // Раньше был виден в панели баффов с той же одолженной иконкой, что и сама
        // песня, — визуально выглядело как "дублирующиеся" иконки песни при повторных
        // критах. Прячем через BlueprintBuff.m_Flags = Flags.HiddenInUi (значение 2):
        // это приватный вложенный enum, поэтому ссылаемся на тип не по имени, а через
        // Reflect.SetEnum (берёт Type самого поля и оборачивает rawValue им же).
        private static BlueprintBuff BuildSungThisRoundFlag()
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.SungThisRoundFlagGuid);
            buff.name = "SingingBladeSungThisRoundFlag";
            // RemoveWhenCombatEnded: маркер не должен доживать до следующего боя.
            // В логе это было отчётливо видно — проверка навыка отсутствовала ровно
            // на ПЕРВОМ крите каждого нового боя, а внутри боя шла на каждом крите:
            // маркер (4 секунды) не успевал истечь, пока игрок добегал до следующей
            // пачки, и гейт съедал первую песню нового боя.
            buff.ComponentsArray = new BlueprintComponent[] { new RemoveWhenCombatEnded() };

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(L.SungThisRoundFlagName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(L.SungThisRoundFlagDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility, ModIcons.Song));
            Reflect.SetEnum(buff, "m_Flags", 2); // BlueprintBuff.Flags.HiddenInUi
            // Оба PrefabLink обязаны быть НЕ null — см. подробный комментарий в BuildSongBuff.
            // Маркер снимается каждый раунд, так что без этого он ронял Buff.OnRemove() ровно
            // так же, как и сами баффы песни (просто без видимой иконки — он скрыт из UI).
            buff.FxOnStart = new PrefabLink();
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

    }
}
