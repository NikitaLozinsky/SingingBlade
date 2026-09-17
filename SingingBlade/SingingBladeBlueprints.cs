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
using Kingmaker.Enums.Damage;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.AreaEffects;
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
    //  - без skill-check гейта на триггере (эффект срабатывает автоматически на крите);
    //  - вместо лечения союзников — форк "Песни отваги" барда, масштабируемый по
    //    уровню Магуса (и класса, и архетипа Eldritch Scion — см. ниже);
    //  - плюс урон по врагам в той же зоне стихией текущего зачарования Arcane Pool.
    public static class SingingBladeBlueprints
    {
        // Радиус эффекта (в футах) — чуть больше, чем у Faith Bearer (15), т.к. эффект
        // двойной (баф союзникам + урон врагам), а не только лечение.
        private const float EchoRadiusFeet = 30f;

        public static void Create()
        {
            var songBuff = BuildSongBuff(Guids.SongBuffGuid, "SingingBladeSongBuff", empowered: false);
            var songBuffEmpowered = BuildSongBuff(Guids.SongBuffEmpoweredGuid, "SingingBladeSongBuffEmpowered", empowered: true);
            var sungThisRoundFlag = BuildSungThisRoundFlag();
            var echoArea = BuildEchoArea();
            var echoAreaBuff = BuildEchoAreaBuff();
            var ability = BuildAbility();
            var enchantment = BuildEnchantment();
            var item = BuildItem();

            Register(songBuff);
            Register(songBuffEmpowered);
            Register(sungThisRoundFlag);
            Register(echoArea);
            Register(echoAreaBuff);
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
            // каждой попытке) — неудачная проверка Убеждения не тратит "лимит раунда",
            // так что следующий крит в этой же серии ударов ещё может спеть успешно.
            var markSungThisRound = ApplyBuff(Guids.SungThisRoundFlagGuid, toCaster: true);

            // Визуальная ауреоль/кольцо выступления — РАЗОВЫЙ спавн Fx на кастере,
            // а не AddAreaEffect (тот вариант уже приводил к перманентному баффу и
            // дублям в панели — см. историю в CLAUDE.md). FxOnStart на самом SongBuff
            // даёт только всплеск на союзнике-получателе, а не кольцо вокруг исполнителя,
            // поэтому кольцо добавляем отдельно, здесь.
            var singFx = new ContextActionSpawnFx { PrefabLink = new PrefabLink { AssetId = Guids.InspireCourageAreaFx } };

            // Зона эхо-урона по врагам — накладываем на кастера при каждой успешной песне.
            // StackingType.Prolong у EchoAreaBuff (см. BuildEchoAreaBuff) означает, что
            // повторный успех ПРОДЛЕВАЕТ уже активную зону, а не создаёт вторую поверх неё.
            var extendEchoArea = ApplyBuff(Guids.EchoAreaBuffGuid, toCaster: true);

            // Проверка Убеждения (DC 40) — песнь звучит не на каждом крите, а только при
            // успехе. CheckForCaster=true: триггер уже выполняется в контексте атакующего
            // (см. ActionsOnInitiator ниже), поэтому проверяем именно его навык, а не цели.
            var singChance = new ContextActionSkillCheck
            {
                Stat = StatType.SkillPersuasion,
                CheckForCaster = true,
                UseCustomDC = true,
                CustomDC = 40,
                Success = new ActionList { Actions = new GameAction[] { castAbility, markSungThisRound, singFx, extendEchoArea } },
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

            // Враги в способности больше не обрабатываются — эхо-урон переехал в
            // EchoAreaBuff/EchoArea (см. BuildEchoAreaBuff/BuildEchoArea): бьёт не мгновенно
            // в момент крита, а со следующего раунда, пока зона активна. Поэтому таргетимся
            // сразу только на союзников.
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
            // и SongBuffEmpowered это РАЗНЫЕ блюпринты, StackingType.Replace сама по себе
            // замещает только одинаковые блюпринты. Без явного взаимного удаления на цели
            // могли одновременно висеть оба (например, если в одном раунде спели без
            // спелл-комбата, а в следующем — с ним) — внешне выглядит как "дублирующиеся"
            // иконки песни в панели баффов, т.к. у обоих один и тот же одолженный значок.
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

        // Небольшой урон стихией текущего зачарования Arcane Pool. Формула 1d4 + (уровень Магуса / 2) —
        // это заметно меньше, чем урон самих заклинаний Магуса (обычно несколько кубиков высокого номинала
        // плюс модификатор характеристики), поэтому это именно "отголосок", а не замена боевого урона.
        // Определяем активное зачарование через ContextConditionCasterHasFact на служебный Buff, который
        // Arcane Pool вешает на кастера при выборе стихии (AddBondProperty) — чистое блюпринт-решение,
        // без Harmony-патчей: тот же механизм, которым игра сама отслеживает Spell Combat/Spellstrike.
        private static GameAction ElementalEcho(DamageEnergyType energy, params string[] buffGuids)
        {
            var conditions = new Condition[buffGuids.Length];
            for (var i = 0; i < buffGuids.Length; i++)
            {
                conditions[i] = CasterHasFact(buffGuids[i]);
            }

            var deal = new ContextActionDealDamage
            {
                DamageType = new DamageTypeDescription { Type = DamageType.Energy, Energy = energy },
                Value = new ContextDiceValue
                {
                    DiceType = DiceType.D4,
                    DiceCountValue = 1,
                    BonusValue = new ContextValue { ValueType = ContextValueType.Rank, ValueRank = AbilityRankType.DamageBonus }
                },
                IgnoreCritical = true
            };

            return new Conditional
            {
                ConditionsChecker = new ConditionsChecker { Operation = Operation.Or, Conditions = conditions },
                IfTrue = new ActionList { Actions = new GameAction[] { deal } },
                IfFalse = new ActionList()
            };
        }

        // ----------------------------------------------------------------
        // Зона "Отголоска" — потиковый эхо-урон по врагам со следующего раунда
        // ----------------------------------------------------------------

        // BlueprintAbilityAreaEffect: раз в раунд (Round-действие, не сразу при спавне)
        // бьёт врагов внутри радиуса стихией текущего зачарования Arcane Pool — то есть
        // ПЕРВЫЙ тик приходится на следующий раунд после успешной песни, а не на сам крит.
        // m_TargetType оставлен на дефолте (Any) — фильтрация "враг/не враг" сделана явно
        // внутри Round-действия через ContextConditionIsAlly{Not=true}, как и у ElementalEcho.
        private static BlueprintAbilityAreaEffect BuildEchoArea()
        {
            var area = new BlueprintAbilityAreaEffect
            {
                AffectEnemies = true,
                AggroEnemies = true,
                AffectDead = false,
                IgnoreSleepingUnits = false,
                Shape = AreaEffectShape.Cylinder,
                Size = new Feet(EchoRadiusFeet),
                CanBeUsedInTacticalCombat = false
            };
            area.AssetGuid = BlueprintGuid.Parse(Guids.EchoAreaGuid);
            area.name = "SingingBladeEchoArea";

            var enemyOnly = new Conditional
            {
                Comment = "Только враги получают потиковый эхо-урон",
                ConditionsChecker = new ConditionsChecker
                {
                    Operation = Operation.And,
                    Conditions = new Condition[] { new ContextConditionIsAlly { Not = true } }
                },
                IfTrue = new ActionList
                {
                    Actions = new GameAction[]
                    {
                        ElementalEcho(DamageEnergyType.Fire, Guids.ArcaneFlamingBuff, Guids.ArcaneFlamingBurstBuff),
                        ElementalEcho(DamageEnergyType.Cold, Guids.ArcaneFrostBuff, Guids.ArcaneIcyBurstBuff),
                        ElementalEcho(DamageEnergyType.Electricity, Guids.ArcaneShockBuff, Guids.ArcaneShockingBurstBuff),
                        ElementalEcho(DamageEnergyType.Holy, Guids.ArcaneHolyBuff, Guids.ArcaneAxiomaticBuff),
                        ElementalEcho(DamageEnergyType.Unholy, Guids.ArcaneUnholyBuff, Guids.ArcaneAnarchicBuff)
                    }
                },
                IfFalse = new ActionList()
            };

            var runAction = new AbilityAreaEffectRunAction
            {
                Round = new ActionList { Actions = new GameAction[] { enemyOnly } }
            };

            area.ComponentsArray = new BlueprintComponent[] { runAction };

            return area;
        }

        // BlueprintBuff, который держит зону живой на кастере. Stacking=Prolong — ключевое:
        // повторная успешная песня ПРОДЛЕВАЕТ существующую зону (максимум из старого и нового
        // времени окончания), а не создаёт поверх неё вторую — иначе враги получали бы эхо
        // от нескольких наложившихся зон сразу. ContextRankConfig обязателен здесь же (а не
        // только на способности) — Round-действия зоны выполняются в MechanicsContext ЭТОГО
        // баффа, а не исходной способности, так что Rank для ElementalEcho должен быть виден
        // именно тут.
        private static BlueprintBuff BuildEchoAreaBuff()
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Prolong,
                Frequency = DurationRate.Rounds
            };
            buff.AssetGuid = BlueprintGuid.Parse(Guids.EchoAreaBuffGuid);
            buff.name = "SingingBladeEchoAreaBuff";

            var addArea = new AddAreaEffect();
            Reflect.Set(addArea, "m_AreaEffect", Reflect.Ref<BlueprintAbilityAreaEffectReference>(Guids.EchoAreaGuid));

            buff.ComponentsArray = new BlueprintComponent[]
            {
                MagusLevelRank(AbilityRankType.DamageBonus, ContextRankProgression.Div2, startLevel: 0, stepLevel: 0),
                addArea
            };

            Reflect.Set(buff, "m_DisplayName", SingingBladeLocalization.CreateString(L.EchoAreaBuffName));
            Reflect.Set(buff, "m_Description", SingingBladeLocalization.CreateString(L.EchoAreaBuffDescription));
            Reflect.Set(buff, "m_DescriptionShort", new LocalizedString());
            Reflect.Set(buff, "m_Icon", FactIcon(Guids.InspireCourageToggleAbility));

            return buff;
        }

        // ----------------------------------------------------------------
        // Бафф союзникам ("Песнь клинка") — форк Inspire Courage под Магуса
        // ----------------------------------------------------------------

        private static BlueprintBuff BuildSongBuff(string guid, string name, bool empowered)
        {
            var buff = new BlueprintBuff
            {
                Stacking = StackingType.Replace,
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
            // Визуальная вспышка при активации — тот же FX, что у настоящей Inspire Courage,
            // но напрямую на нашем баффе (публичное поле, без реального InspireCourageBuff
            // и его AreaEffect-хвоста — см. комментарий у Guids.InspireCourageBuffFx).
            buff.FxOnStart = new PrefabLink { AssetId = Guids.InspireCourageBuffFx };

            return buff;
        }

        // Служебный маркер "уже спели в этом раунде" — чистый флаг без механического
        // эффекта, 1 раунд длительности (естественно сгорает к следующему раунду).
        // Не скрываем через m_Flags.HiddenInUi намеренно: это приватный вложенный enum
        // в BlueprintBuff, рефлексия в него ради чисто косметического скрытия одной
        // маленькой иконки не стоит усложнения — вместо этого дали ему осмысленные
        // имя/описание, чтобы иконка сама объясняла себя, если игрок её заметит.
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
