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
    public static class SingingBladeBlueprints
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
            var sustainedNoteBuff = BuildSustainedNoteBuff();
            var sustainedNoteToggle = BuildSustainedNoteToggle();
            var ability = BuildAbility();
            var enchantment = BuildEnchantment();
            var item = BuildItem();

            Register(songBuff);
            Register(songBuffEmpowered);
            Register(songArea);
            Register(songAureole);
            Register(sungThisRoundFlag);
            Register(sustainedNoteBuff);
            Register(sustainedNoteToggle);
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
            // Своя картинка здесь НЕ используется сознательно: предмет переиспользует
            // визуал "Несущего веру" целиком — и модель, и иконку, — чтобы они не
            // разъезжались между собой. Свои иконки только у баффов и способностей.
            Reflect.Set(item, "m_Icon", ItemIcon(Guids.FaithBearerItem));
            Reflect.Set(item, "m_Cost", 100000);
            Reflect.Set(item, "m_Weight", 4.0f);
            Reflect.Set(item, "m_Type", Reflect.Ref<BlueprintWeaponTypeReference>(Guids.ScimitarWeaponType));
            Reflect.Set(item, "m_Size", Size.Medium);
            // Пустая (но НЕ null!) ссылка на "предмет одежды" персонажа. Мы его не
            // используем — визуал клинка целиком берётся из m_VisualParameters, — но
            // BlueprintItemEquipment.EquipmentEntity дереференсит поле БЕЗ проверки
            // (m_EquipmentEntity.Get(), не ?.Get()), а у блюпринтов из JSON ссылка
            // всегда создана. В рантайме поле оставалось C#-null, и открытие панели
            // настройки внешности в инвентаре роняло UI: ItemEntity.CanChangeColor()
            // -> BlueprintItemEquipment.get_EquipmentEntity -> NullReferenceException
            // (видно в Player.log, CharacterVisualSettingsVM.CreateItemsColorSelectors).
            // Четвёртый случай одной и той же категории — см. правило в CLAUDE.md.
            Reflect.Set(item, "m_EquipmentEntity", Reflect.Empty<KingmakerEquipmentEntityReference>());
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
            // Короче раунда — иначе маркер сам себе мешает (см. ApplyBuff).
            var markSungThisRound = ApplyBuff(Guids.SungThisRoundFlagGuid, toCaster: true, seconds: SungThisRoundFlagSeconds);

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

            // Переключатель "Долгая нота" здесь НЕ выдаётся, хотя штатный способ был
            // бы именно таким: AddUnitFeatureEquipment -> скрытая фича -> AddFacts.
            // Схема верная (так устроены ванильные LordProtectorEnchant и
            // ProtectionFromEvil), но у нас рвалась молча: на блюпринте зачарования
            // оба компонента присутствовали, а до персонажа не доезжала ни фича, ни
            // переключатель — и ни одного исключения мода в логе. Заменено на прямую
            // выдачу из кода по событию смены экипировки (SustainedNoteGrant):
            // одно звено вместо четырёх, и его состояние видно в диагностике.

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
        // "Долгая нота": переключатель проведения лучевых заклинаний через клинок
        // ----------------------------------------------------------------

        // Бафф "режим включён". Обязателен сам по себе: BlueprintActivatableAbility
        // требует непустой m_Buff. Заодно по нему рантайм-код понимает, включён ли режим
        // (см. SustainedNote.IsModeActive).
        private static BlueprintBuff BuildSustainedNoteBuff()
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.SustainedNoteBuffGuid);
            buff.name = "SingingBladeSustainedNoteBuff";
            buff.ComponentsArray = new BlueprintComponent[0];

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(L.SustainedNoteName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(L.SustainedNoteDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.SpellStrikeAbility, ModIcons.SustainedNote));

            // Оба PrefabLink обязаны быть не null — иначе Buff.OnRemove() падает и
            // иконка баффа навсегда залипает в панели (подробный разбор в BuildSongBuff).
            buff.FxOnStart = new PrefabLink();
            buff.FxOnRemove = new PrefabLink();
            buff.ResourceAssetIds = new string[0];

            return buff;
        }

        // Сам переключатель. СОЗНАТЕЛЬНО БЕЗ RestrictionHasFact(SongAureole) — переключатель
        // "липкий": однажды включённый, он остаётся включённым и просто ничего не делает,
        // пока песнь молчит. Первая версия вешала сюда ограничение, и движок честно гасил
        // тумблер каждый раз, когда песнь смолкала (ActivatableAbility: "!IsAvailableByRestrictions"),
        // то есть практически каждый раунд без крита — в бою это заставляло щёлкать тумблер
        // без конца. Проверку "песнь звучит" делает только рантайм в момент каста
        // (SustainedNote.IsModeActive), и это единственное место, где она действительно нужна.
        private static BlueprintActivatableAbility BuildSustainedNoteToggle()
        {
            var toggle = new BlueprintActivatableAbility
            {
                Group = ActivatableAbilityGroup.None,
                WeightInGroup = 1,
                IsOnByDefault = false,
                // Не гасим по окончании боя: тумблер липкий, игрок сам решает, когда его
                // выключить (см. комментарий выше).
                DeactivateIfCombatEnded = false,
                DeactivateAfterFirstRound = false,
                DeactivateImmediately = true,
                IsTargeted = false,
                DeactivateIfOwnerDisabled = true,
                DeactivateIfOwnerUnconscious = true,
                OnlyInCombat = false,
                DoNotTurnOffOnRest = false,
                ActionBarAutoFillIgnored = false,
                HiddenInUI = false,
                // Immediately + Free: включение/выключение не тратит действие,
                // как и требовалось.
                ActivationType = AbilityActivationType.Immediately,
                ResourceAssetIds = new string[0]
            };
            toggle.AssetGuid = BlueprintGuid.Parse(Guids.SustainedNoteToggleGuid);
            toggle.name = "SingingBladeSustainedNoteToggle";
            toggle.ComponentsArray = new BlueprintComponent[0];

            Reflect.Set(toggle, "m_Buff", Reflect.Ref<BlueprintBuffReference>(Guids.SustainedNoteBuffGuid));
            Reflect.Set(toggle, "m_ActivateWithUnitCommand", Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Free);
            Reflect.Set(toggle, "m_DisplayName", SingingBladeLocalization.CreateString(L.SustainedNoteName));
            Reflect.Set(toggle, "m_Description", SingingBladeLocalization.CreateString(L.SustainedNoteDescription));
            Reflect.Set(toggle, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(toggle, "m_Icon", FactIcon(Guids.SpellStrikeAbility, ModIcons.SustainedNote));

            return toggle;
        }

        // Активируемая способность "Долгая нота" для режима ДИСТАНЦИОННОГО УДАРА
        // (SustainedNote.RangedStrike). Быстрое действие, стоит одно очко Мистического
        // резерва, на RangedStrikeDurationSeconds вешает на магуса SustainedNoteBuff.
        //
        // Форма скопирована с ванильной магусовской арканы "Мистическая точность"
        // (ArcaneAccuracyAbility в Classes/Magus/Arcanas): тот же Type = Extraordinary,
        // Range = Personal, ActionType = Swift и тот же компонент AbilityResourceLogic на
        // ресурс ArcanePoolResourse. Это ровно тот способ, которым ваниль берёт плату из
        // Мистического резерва, — своего изобретать не нужно.
        //
        // В режиме переключателя (RangedStrike = false) блюпринт всё равно создаётся и
        // регистрируется, но персонажу не выдаётся (см. SustainedNote.RefreshToggle):
        // так переключение режима остаётся вопросом одного флага, без возни с GUID.
        private static BlueprintAbility BuildSustainedNoteAbility()
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
                ActionType = Kingmaker.UnitLogic.Commands.Base.UnitCommand.CommandType.Swift
            };
            ability.AssetGuid = BlueprintGuid.Parse(Guids.SustainedNoteAbilityGuid);
            ability.name = "SingingBladeSustainedNoteAbility";

            Reflect.Set(ability, "m_DisplayName", SingingBladeLocalization.CreateString(L.SustainedNoteName));
            Reflect.Set(ability, "m_Description", SingingBladeLocalization.CreateString(L.SustainedNoteDescription));
            Reflect.Set(ability, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(ability, "m_Icon", FactIcon(Guids.SpellStrikeAbility, ModIcons.SustainedNote));

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
                        ApplyBuff(Guids.SustainedNoteBuffGuid, toCaster: true,
                                  seconds: SustainedNote.RangedStrikeDurationSeconds)
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
                        Reflect.Ref<BlueprintAbilityResourceReference>(Guids.ArcanePoolResource));
            Reflect.Set(resource, "m_IsSpendResource", true);

            ability.ComponentsArray = new BlueprintComponent[] { runAction, resource };

            return ability;
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
