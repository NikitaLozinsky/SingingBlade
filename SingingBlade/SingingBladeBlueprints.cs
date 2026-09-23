using System.Collections.Generic;
using System.Reflection;
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
    // Собирает блюпринты мода напрямую в рантайме (не через JSON-редактор) и
    // регистрирует их в ResourcesLibrary.BlueprintsCache. Структура скопирована
    // с уникального оружия "Faith Bearer" (Item -> Enchantment с крит-триггером
    // -> Ability с AoE-эффектом), но:
    //  - при крите не срабатывает автоматически, а требует успешной проверки
    //    Подвижности (DC 40, не больше раза за раунд);
    //  - вместо лечения союзников — форк "Песни отваги" барда, масштабируемый по
    //    уровню Магуса (и класса, и архетипа Eldritch Scion — см. ниже).
    //
    // Урон по врагам (стихийное эхо от Arcane Pool) — БЫЛ в ранней версии мода,
    // УБРАН по прямой просьбе пользователя: он задевал союзных, но неподконтрольных
    // игроку NPC (ContextConditionIsAlly не распознавал их как "своих"), плюс с ним
    // была связана потиковая AreaEffect-зона (см. GUID EchoAreaGuid/EchoAreaBuffGuid
    // в Guids.cs — больше не используются, оставлены закомментированными, чтобы не
    // переиспользовать случайно), которая крашилась на каждый тик игры из-за пустых
    // ActionList-полей в AbilityAreaEffectRunAction и мешала баффу союзникам корректно
    // сниматься. См. историю в CLAUDE.md, если понадобится восстановить похожую механику —
    // в следующий раз стоит сразу учесть оба урока.
    public static partial class SingingBladeBlueprints
    {
        // Радиус зоны песни вокруг исполнителя.
        //
        // Ровно 50 футов, и это НЕ произвольное число: движок НЕ масштабирует Fx зоны
        // под её Size (AreaEffectView.SpawnFxs() просто спавнит префаб как есть, а
        // механический радиус задаётся отдельно — ScriptZoneCylinder.Radius =
        // blueprint.Size.Meters). Кольцо InspireCourageAreaFx, которое мы одолжили,
        // нарисовано дизайнерами под 50-футовую зону: ВСЕ ванильные зоны с этим Fx
        // (InspireCourageArea, FakeInspireCourage, InspireTranquility,
        // BeastTamerInspireFerocity, DLC3_InspireCourage, Aranka_Area) имеют Size = 50.
        // При 30 футах, как было раньше, кольцо рисовалось заметно больше зоны, и
        // союзник внутри видимого круга мог не получать бафф.
        // Если когда-нибудь захочется другой радиус — менять вместе с Fx, иначе
        // картинка снова разойдётся с механикой.
        private const float SongRadiusFeet = 50f;

        public static void Create()
        {
            var songBuff = BuildSongBuff(Guids.SongBuffGuid, "SingingBladeSongBuff", empowered: false);
            var songBuffEmpowered = BuildSongBuff(Guids.SongBuffEmpoweredGuid, "SingingBladeSongBuffEmpowered", empowered: true);
            var songArea = BuildSongArea();
            var songAureole = BuildSongAureole();
            var sungThisRoundFlag = BuildSungThisRoundFlag();
            var cutTheSkiesBuff = BuildCutTheSkiesBuff();
            var reachStretchBuff = BuildReachStretchBuff();
            var cutTheSkiesAbility = BuildCutTheSkiesAbility(
                Guids.CutTheSkiesAbilityGuid, Guids.ArcanePoolResource,
                "SingingBladeCutTheSkiesAbility");
            var cutTheSkiesAbilityEldritch = BuildCutTheSkiesAbility(
                Guids.CutTheSkiesAbilityEldritchGuid, Guids.EldritchPoolResource,
                "SingingBladeCutTheSkiesAbilityEldritch");
            var ability = BuildAbility();
            var stormEnchantment = BuildStormEnchantment();
            var stormFeature = BuildStormFeature();
            var stormPetFeature = BuildStormPetFeature();
            var enchantment = BuildEnchantment();
            var item = BuildItem();

            Register(songBuff);
            Register(songBuffEmpowered);
            Register(songArea);
            Register(songAureole);
            Register(sungThisRoundFlag);
            Register(cutTheSkiesBuff);
            Register(reachStretchBuff);
            // ОБА варианта способности регистрируются всегда — под резерв Магуса и под
            // резерв Наследника. Ровно этой строки когда-то и не хватало: блюпринт
            // собирался методом BuildCutTheSkiesAbility, но в кэш не попадал, выдача
            // молча не состоялась, и в Player.log не было ни одной строки мода.
            Register(cutTheSkiesAbility);
            Register(cutTheSkiesAbilityEldritch);
            Register(ability);
            // Порядок внутри Register не важен (ссылки резолвятся лениво, по GUID), но
            // зарегистрированы должны быть ВСЕ три: фича носителя ссылается на фичу Айву,
            // а зачарование висит на предмете.
            Register(stormEnchantment);
            Register(stormFeature);
            Register(stormPetFeature);
            Register(enchantment);
            Register(item);
        }

        private static void Register(BlueprintScriptableObject blueprint)
        {
            // Имена компонентам — ДО всего остального, см. NameElements: без них игра
            // не может сохраниться.
            NameElements(blueprint);

            // OnEnable проставляет OwnerBlueprint у компонентов — то же самое, что
            // BlueprintsCache.Load() делает для блюпринтов, прочитанных из pack-файла.
            blueprint.OnEnable();
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(blueprint.AssetGuid, blueprint);
        }

        // Раздаёт имена компонентам блюпринта и вложенным в них действиям и условиям.
        //
        // ЭТО НЕ КОСМЕТИКА — без имён ИГРА НЕ СОХРАНЯЕТСЯ. `EntityFact` сериализует свои
        // компоненты словарём, ключ которого — имя компонента блюпринта:
        //     [JsonProperty(PropertyName = "Components")]
        //     private Dictionary<string, EntityFactComponent> ComponentsDictionary =>
        //         Components.ToDictionary(i => i.SourceBlueprintComponentName, i => i);
        // а `SourceBlueprintComponentName` — это ровно `component.name`. У блюпринтов из
        // JSON имя есть всегда ("$AddInitiatorAttackWithWeaponTrigger$c1894e60-..."), а у
        // собранных через `new` оно остаётся C#-null, и `ToDictionary` падает с
        // "Value cannot be null. Parameter name: key". Сохранение при этом не просто
        // ругается в лог, а ОБРЫВАЕТСЯ: игрок видит окно SAVINGERROR и не может сохраниться.
        //
        // Побочно имена чинят ещё одну тонкость: `ModifiableValue.AddModifierUnique`
        // различает модификаторы по паре (факт, ИМЯ компонента). С null-именами два разных
        // компонента одного факта считались бы одним и тем же, и второй бонус молча
        // не применился бы.
        //
        // Имя должно быть СТАБИЛЬНЫМ между запусками: по нему сохранённые данные компонента
        // находят свой компонент при загрузке. Поэтому не Guid.NewGuid(), как в
        // Element.CreateInstance, а детерминированная пара "имя блюпринта + индекс".
        // Следствие: менять ПОРЯДОК компонентов в уже вышедшем блюпринте — значит терять
        // сохранённые данные этих компонентов (не критично, но знать стоит).
        private static void NameElements(BlueprintScriptableObject blueprint)
        {
            var components = blueprint.ComponentsArray;
            if (components == null) return;

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;

                if (string.IsNullOrEmpty(component.name))
                {
                    component.name = "$" + component.GetType().Name + "$" + blueprint.name + "$" + i;
                }

                NameNested(blueprint, component, component.name);
            }
        }

        // Обходит поля объекта и именует вложенные действия и условия (ActionList,
        // ConditionsChecker и всё, что внутри них). Обход намеренно УЗКИЙ — только эти два
        // контейнера и сами Element'ы: шире было бы легко уйти в граф блюпринтов и
        // зациклиться. Элементам движок сам раздаёт имена вида "$Тип$guid"
        // (Element.CreateInstance), но только когда создаёт их сам; наши, собранные через
        // new, тоже должны быть подписаны.
        private static void NameNested(BlueprintScriptableObject blueprint, object owner, string prefix)
        {
            if (owner == null) return;

            var fields = owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public
                                                   | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                var value = field.GetValue(owner);
                if (value == null) continue;

                if (value is ActionList actions)
                {
                    NameAll(blueprint, actions.Actions, prefix + "$" + field.Name);
                }
                else if (value is ConditionsChecker conditions)
                {
                    NameAll(blueprint, conditions.Conditions, prefix + "$" + field.Name);
                }
            }
        }

        private static void NameAll(BlueprintScriptableObject blueprint, Element[] elements, string prefix)
        {
            if (elements == null) return;

            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                if (element == null) continue;

                if (string.IsNullOrEmpty(element.name))
                {
                    element.name = "$" + element.GetType().Name + "$" + prefix + "$" + i;
                }

                // Регистрируем элемент в блюпринте — ровно то, что при чтении .jbp делает
                // Element.OnDeserialized (Json.BlueprintBeingRead.Data.AddToElementsList).
                // Помимо самого списка это проставляет элементу Owner, а он нужен: метод
                // Element.LogError читает Owner.name БЕЗ проверки на null, и у наших
                // действий любая жалоба на неверные данные превращалась бы в
                // NullReferenceException вместо внятной строки в логе.
                if (element.Owner == null) blueprint.AddToElementsList(element);

                // Внутри действия может лежать ещё один ActionList (Conditional, проверка
                // навыка с ветками Success/Failure) — спускаемся дальше.
                NameNested(blueprint, element, element.name);
            }
        }

    }
}
