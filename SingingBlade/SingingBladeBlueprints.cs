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
            // OnEnable проставляет OwnerBlueprint у компонентов — то же самое, что
            // BlueprintsCache.Load() делает для блюпринтов, прочитанных из pack-файла.
            blueprint.OnEnable();
            ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(blueprint.AssetGuid, blueprint);
        }

    }
}
