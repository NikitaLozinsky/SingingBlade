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
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.Enums;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
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
    public static class SingingBladeBlueprints
    {
        // Радиус AoE вокруг атакующего, в котором ищутся союзники для песни.
        private const float EchoRadiusFeet = 30f;

        public static void Create()
        {
            var songBuff = BuildSongBuff(Guids.SongBuffGuid, "SingingBladeSongBuff", empowered: false);
            var songBuffEmpowered = BuildSongBuff(Guids.SongBuffEmpoweredGuid, "SingingBladeSongBuffEmpowered", empowered: true);
            var songAureole = BuildSongAureole();
            var sungThisRoundFlag = BuildSungThisRoundFlag();
            var ability = BuildAbility();
            var enchantment = BuildEnchantment();
            var item = BuildItem();

            Register(songBuff);
            Register(songBuffEmpowered);
            Register(songAureole);
            Register(sungThisRoundFlag);
            Register(ability);
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

        // ----------------------------------------------------------------
        // Предмет
        // ----------------------------------------------------------------

        private static BlueprintItemWeapon BuildItem()
        {
            var item = new BlueprintItemWeapon
            {
                CR = 13,
                Charges = 1,
                SpendCharges = false,
                RestoreChargesOnRest = false,
                CasterLevel = 1,
                SpellLevel = 1,
                DC = 11,
                IsNonRemovable = false,
                KeepInPolymorph = false,
                Double = false,
                CountAsDouble = false
            };
            item.AssetGuid = BlueprintGuid.Parse(Guids.ItemGuid);
            item.name = "SingingBladeItem";

            Reflect.Set(item, "m_DisplayNameText", SingingBladeLocalization.CreateString(L.ItemName));
            Reflect.Set(item, "m_DescriptionText", SingingBladeLocalization.CreateString(L.ItemDescription));
            // Пустые (не null!) LocalizedString — как у ванильных предметов. Если оставить
            // поле C#-null, implicit-конвертация LocalizedString -> string в игре возвращает
            // буквально строку "<null>" (см. Kingmaker.Localization.LocalizedString, операторы
            // implicit operator string), и она показывается в тултипе как есть.
            // Большой поэтичный текст истории клинка — показывается отдельно от описания,
            // по кнопке "Сведения" в инвентаре (как у "Жертвы Роннека" и других легендарок).
            Reflect.Set(item, "m_FlavorText", SingingBladeLocalization.CreateString(L.ItemFlavorText));
            Reflect.Set(item, "m_NonIdentifiedNameText", new LocalizedString());
            Reflect.Set(item, "m_NonIdentifiedDescriptionText", new LocalizedString());
            // Иконка не задавалась вовсе -> движок молча подставлял дефолтную иконку
            // скимитара. Не резолвим Unity Sprite вручную по guid+fileid (для m_Icon
            // это прямая ссылка на объект Sprite, а не строковый BlueprintReference) —
            // проще и надёжнее одолжить уже загруженный Icon у Faith Bearer.
            Reflect.Set(item, "m_Icon", ItemIcon(Guids.FaithBearerItem));
            Reflect.Set(item, "m_Cost", 100000);
            Reflect.Set(item, "m_Weight", 4.0f);
            Reflect.Set(item, "m_Type", Reflect.Ref<BlueprintWeaponTypeReference>(Guids.ScimitarWeaponType));
            Reflect.Set(item, "m_Size", Size.Medium);
            Reflect.Set(item, "m_OverrideDamageDice", false);
            Reflect.Set(item, "m_OverrideDamageType", false);
            Reflect.Set(item, "m_Enchantments", new[]
            {
                Reflect.Ref<BlueprintWeaponEnchantmentReference>(Guids.Enhancement4Enchantment),
                Reflect.Ref<BlueprintWeaponEnchantmentReference>(Guids.EnchantmentGuid)
            });

            // КРИТИЧНО: BlueprintItemWeapon.OnEnableWithLibrary() сам подставляет
            // m_VisualParameters = new WeaponVisualParameters() если поле null, но внутри
            // этого пустого объекта m_Projectiles остаётся C#-null (не пустой массив).
            // RuleAttackWithWeapon.LaunchProjectiles() безусловно читает
            // Weapon.WeaponVisualParameters.Projectiles.Length на КАЖДОЙ атаке этим оружием —
            // и падает с NullReferenceException ДО того, как успевает создать RuleDealDamage.
            // Именно поэтому базовый удар не наносил урон и наш крит-триггер не срабатывал:
            // RuleAttackWithWeapon.OnTrigger падал раньше, чем очередь доходила до damage
            // и до OnEventDidTrigger-подписчиков (см. GameLogFull.txt: "Object reference not
            // set to an instance of an object at WeaponVisualParameters.get_Projectiles()").
            // Задаём m_Projectiles явно пустым массивом, заодно переиспользуем модель
            // уникального скимитара "Несущий веру" вместо дефолтной модели типа Scimitar.
            var visualParameters = new WeaponVisualParameters();
            Reflect.Set(visualParameters, "m_Projectiles", new BlueprintProjectileReference[0]);
            Reflect.Set(visualParameters, "m_WeaponModel", new PrefabLink { AssetId = Guids.FaithBearerWeaponModel });
            Reflect.Set(visualParameters, "m_WeaponSheathModelOverride", new PrefabLink { AssetId = Guids.FaithBearerWeaponSheathModel });
            Reflect.Set(item, "m_VisualParameters", visualParameters);

            return item;
        }

        // ----------------------------------------------------------------
        // Зачарование оружия: крит-триггер (без skill-check гейта)
        // ----------------------------------------------------------------

        private static BlueprintWeaponEnchantment BuildEnchantment()
        {
            var castAbility = new ContextActionCastSpell();
            Reflect.Set(castAbility, "m_Spell", Reflect.Ref<BlueprintAbilityReference>(Guids.AbilityGuid));

            // Отмечаем "спели в этом раунде" ТОЛЬКО при реальном успехе песни (не на
            // каждой попытке) — неудачная проверка Подвижности не тратит "лимит раунда",
            // так что следующий крит в этой же серии ударов ещё может спеть успешно.
            var markSungThisRound = ApplyBuff(Guids.SungThisRoundFlagGuid, toCaster: true);

            // Кольцо-ауреоль — на самого исполнителя, ровно один раз за успешную песню.
            // Именно здесь, а не в BuildAbility: AbilityEffectRunAction выполняется ОТДЕЛЬНО
            // для каждой цели AoE, и наложение "на кастера" оттуда сработало бы по разу на
            // каждого союзника в радиусе. Success-ветка проверки навыка выполняется один раз.
            var spawnAureole = ApplyBuff(Guids.SongAureoleGuid, toCaster: true);

            // Кольцо/ауреоль выступления раньше спавнилось здесь отдельным разовым
            // ContextActionSpawnFx(InspireCourageAreaFx) — но это Fx самой
            // InspireCourageArea, рассчитанный на ПОСТОЯННО включённую area-effect зону
            // с собственным контроллером жизненного цикла (спавн на активации/уничтожение
            // на ForceEnd()). Спавн "в лоб", без владеющего баффа, никем не уничтожается —
            // эффект оставался навсегда (см. историю в CLAUDE.md). Теперь этот же Fx
            // назначен прямо в FxOnStart у SongBuff/SongBuffEmpowered (см. BuildSongBuff) —
            // у PrefabLink там уже есть подтверждённо рабочая очистка вместе со снятием баффа
            // (Buff.TrySpawnParticleEffect/ClearParticleEffect), отдельный экшен тут не нужен.

            // Проверка Подвижности (DC 40) — песнь звучит не на каждом крите, а только при
            // успехе. Навык именно Подвижность, а не Убеждение: песнь рождается не из голоса,
            // а из танца с клинком и поющего рассечённого воздуха (см. описания в
            // Localization.json). CheckForCaster=true: триггер уже выполняется в контексте
            // атакующего (см. ActionsOnInitiator ниже), проверяем его навык, а не цели.
            var singChance = new ContextActionSkillCheck
            {
                Stat = StatType.SkillMobility,
                CheckForCaster = true,
                UseCustomDC = true,
                CustomDC = 40,
                Success = new ActionList { Actions = new GameAction[] { castAbility, markSungThisRound, spawnAureole } },
                Failure = new ActionList()
            };

            // Не больше одной СПЕТОЙ песни за раунд, даже если в серии ударов несколько
            // критов подряд — иначе полноценная атака могла бы наложить бафф/эхо-урон
            // по несколько раз за один раунд.
            var onceThisRoundGuard = new Conditional
            {
                Comment = "Не больше одной песни за раунд",
                ConditionsChecker = new ConditionsChecker
                {
                    Operation = Operation.And,
                    Conditions = new Condition[] { CasterHasFact(Guids.SungThisRoundFlagGuid, not: true) }
                },
                IfTrue = new ActionList { Actions = new GameAction[] { singChance } },
                IfFalse = new ActionList()
            };

            var trigger = new AddInitiatorAttackWithWeaponTrigger
            {
                CriticalHit = true,
                OnlyHit = true,
                // По умолчанию действия триггера выполняются в контексте того, КОГО ударили —
                // тогда область эффекта центрировалась бы на противнике, а не на Магусе. Нам нужно
                // ровно наоборот: песнь звучит от самого атакующего.
                ActionsOnInitiator = true,
                Action = new ActionList { Actions = new GameAction[] { onceThisRoundGuard } }
            };

            var enchantment = new BlueprintWeaponEnchantment();
            enchantment.AssetGuid = BlueprintGuid.Parse(Guids.EnchantmentGuid);
            enchantment.name = "SingingBladeEnchantment";
            Reflect.Set(enchantment, "m_EnchantmentCost", 1);
            Reflect.Set(enchantment, "m_IdentifyDC", 5);
            // m_EnchantName/m_Description непустые -> зачарование само становится записью
            // в списке "Свойства" тултипа предмета (как "Святое оружие" у Faith Bearer) —
            // с собственным именем и попап-описанием по наведению.
            Reflect.Set(enchantment, "m_EnchantName", SingingBladeLocalization.CreateString(L.EnchantmentName));
            Reflect.Set(enchantment, "m_Description", SingingBladeLocalization.CreateString(L.EnchantmentDescription));
            Reflect.Set(enchantment, "m_Prefix", new LocalizedString());
            Reflect.Set(enchantment, "m_Suffix", new LocalizedString());
            enchantment.ComponentsArray = new BlueprintComponent[] { trigger };

            return enchantment;
        }

        // ----------------------------------------------------------------
        // Способность-эффект: AoE вокруг атакующего на крите
        // ----------------------------------------------------------------

        private static BlueprintAbility BuildAbility()
        {
            var ability = new BlueprintAbility
            {
                Type = AbilityType.Special,
                Range = AbilityRange.Custom,
                CustomRange = new Feet(EchoRadiusFeet),
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
            Reflect.Set(ability, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility));

            // Только союзники — урона по врагам больше нет (убран по просьбе пользователя,
            // см. комментарий в шапке файла).
            var targetsAround = new AbilityTargetsAround();
            Reflect.Set(targetsAround, "m_Radius", new Feet(EchoRadiusFeet));
            Reflect.Set(targetsAround, "m_TargetType", TargetType.Ally);
            Reflect.Set(targetsAround, "m_IncludeDead", false);
            Reflect.Set(targetsAround, "m_Condition", new ConditionsChecker { Operation = Operation.And, Conditions = new Condition[0] });
            Reflect.Set(targetsAround, "m_SpreadSpeed", new Feet(EchoRadiusFeet));

            var spellCombatOrStrike = new ConditionsChecker
            {
                Operation = Operation.Or,
                Conditions = new Condition[] { CasterHasFact(Guids.SpellCombatBuff), CasterHasFact(Guids.SpellStrikeBuff) }
            };

            // Сначала снимаем "другой" вариант песни, потом накладываем нужный — SongBuff
            // и SongBuffEmpowered это РАЗНЫЕ блюпринты, а StackingType (хоть Replace, хоть
            // нынешний Prolong) работает только в пределах ОДНОГО блюпринта. Без явного
            // взаимного удаления на цели могли одновременно висеть оба (например, если в
            // одном раунде спели без спелл-комбата, а в следующем — с ним) — внешне выглядит
            // как "дублирующиеся" иконки песни, т.к. значок у обоих один и тот же.
            var allyBranch = new Conditional
            {
                Comment = "Спелл-комбат/спеллстрайк в этом раунде -> усиленная песнь",
                ConditionsChecker = spellCombatOrStrike,
                IfTrue = new ActionList { Actions = new GameAction[] { RemoveBuff(Guids.SongBuffGuid), ApplyBuff(Guids.SongBuffEmpoweredGuid) } },
                IfFalse = new ActionList { Actions = new GameAction[] { RemoveBuff(Guids.SongBuffEmpoweredGuid), ApplyBuff(Guids.SongBuffGuid) } }
            };

            var runAction = new AbilityEffectRunAction
            {
                Actions = new ActionList { Actions = new GameAction[] { allyBranch } }
            };

            ability.ComponentsArray = new BlueprintComponent[] { targetsAround, runAction };

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
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility));
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
            buff.ComponentsArray = new BlueprintComponent[0];

            // Пустые (не null!) LocalizedString — бафф скрыт, но правило "никаких C#-null
            // в полях блюпринта" общее: иначе движок покажет строку "<null>", если до этих
            // полей всё-таки кто-то доберётся (инспектор, отладочный вывод).
            Reflect.Set(buff, "m_DisplayName", new LocalizedString());
            Reflect.Set(buff, "m_Description", new LocalizedString());
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.SetEnumFlag(buff, "m_Flags", 2); // BlueprintBuff.Flags.HiddenInUi

            buff.FxOnStart = new PrefabLink { AssetId = Guids.InspireCourageAreaFx };
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
        // Reflect.SetEnumFlag (берёт Type самого поля и оборачивает rawValue им же).
        private static BlueprintBuff BuildSungThisRoundFlag()
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.SungThisRoundFlagGuid);
            buff.name = "SingingBladeSungThisRoundFlag";
            buff.ComponentsArray = new BlueprintComponent[0];

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(L.SungThisRoundFlagName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(L.SungThisRoundFlagDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility));
            Reflect.SetEnumFlag(buff, "m_Flags", 2); // BlueprintBuff.Flags.HiddenInUi
            // Оба PrefabLink обязаны быть НЕ null — см. подробный комментарий в BuildSongBuff.
            // Маркер снимается каждый раунд, так что без этого он ронял Buff.OnRemove() ровно
            // так же, как и сами баффы песни (просто без видимой иконки — он скрыт из UI).
            buff.FxOnStart = new PrefabLink();
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

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

        private static ContextActionApplyBuff ApplyBuff(string buffGuid, bool toCaster = false)
        {
            var action = new ContextActionApplyBuff
            {
                ToCaster = toCaster,
                AsChild = false,
                Permanent = false,
                UseDurationSeconds = false,
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
        // строковый BlueprintReference, поэтому проще одолжить уже загруженный Icon
        // у существующего ванильного блюпринта, чем резолвить guid+fileid вручную.
        private static Sprite ItemIcon(string blueprintGuid)
        {
            return ResourcesLibrary.TryGetBlueprint<BlueprintItem>(blueprintGuid)?.Icon;
        }

        private static Sprite FactIcon(string blueprintGuid)
        {
            return ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(blueprintGuid)?.Icon;
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
