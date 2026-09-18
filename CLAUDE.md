# SingingBlade

Harmony-мод (Unity Mod Manager) для Pathfinder: Wrath of the Righteous.
Добавляет уникальный скимитар "Поющий клинок" для класса Магус: клонирован по
структуре с существующего уникального оружия "Faith Bearer", но с другой
логикой эффекта (форк бардовской "Песни отваги" под Магуса). Урон по врагам
(стихийное эхо от Arcane Pool) был в ранней версии, полностью убран — см.
раздел "Убранная механика" ниже, если понадобится восстановить что-то похожее.

## Окружение

- Игра: `C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure`
  (переменная окружения `WOTR_PATH`, уровень пользователя). Все HintPath в
  `.csproj` строятся через `$(WOTR_PATH)`, не хардкодить абсолютный путь.
- Декомпилированный Assembly-CSharp (через ilspycmd, уже готово, не перегонять
  без необходимости): `C:\Users\decop\Documents\WOTR mods\src`
- Блюпринты игры в JSON: `C:\Users\decop\Documents\WOTR mods\Blueprints`
  **Важно:** внутри этой папки есть дублирующиеся пути —
  `Blueprints\Weapons\...` и `Blueprints\blueprints\Weapons\...`.
  Проверено (diff, побайтово): это идентичные копии одного и того же дерева.
  **Канонический путь — `Blueprints\...` без вложенного `blueprints\`.**
  Используй только его, вложенную копию игнорируй.

## Сборка и деплой

```
dotnet build SingingBlade.sln
```

Таргет `DeployMod` в `SingingBlade.csproj` (`AfterTargets="Build"`) сам копирует
`SingingBlade.dll`, `Info.json` и `Localization.json` в `$(WOTR_PATH)\Mods\SingingBlade`.
Отдельного шага установки нет и не должно быть.

HintPath на `$(WOTR_PATH)\Wrath_Data\Managed` — исходно только для
`Assembly-CSharp`, `Assembly-CSharp-firstpass`, `UnityEngine`, `UnityEngine.CoreModule`.
Список расширен (с явного подтверждения пользователя) тремя сборками, без
которых не собирался код выдачи предмета (задача 3): `UnityEngine.IMGUIModule`
(нужен для `GUILayout` в `OnGUI`), `Owlcat.Runtime.Core` и `Owlcat.Runtime.Validation`
(транзитивно требуются для `BlueprintItemReference`/`ItemsCollection.Contains`).
Итого 7 HintPath-сборок — расширять этот список и дальше можно, но не молча:
это осознанное решение пользователя, каждое такое расширение стоит подтверждать.

Harmony и UnityModManager подключены через `PackageReference`, не HintPath.
Newtonsoft.Json из игры намеренно НЕ подключён HintPath'ом — `Localization.json`
парсится собственным минимальным парсером (`MiniJson.cs`), чтобы не расширять
список HintPath-сборок без необходимости.

## Занятые GUID (новые блюпринты мода)

Источник истины — `SingingBlade/Guids.cs`. Список ниже поддерживать в
актуальном состоянии при добавлении новых блюпринтов, чтобы следующая сессия
не сгенерировала конфликтующий GUID.

| Константа | GUID | Что это |
|---|---|---|
| `ItemGuid` | `c82273d736bd4e38a149cde7e2ad71ca` | BlueprintItemWeapon — сам скимитар |
| `EnchantmentGuid` | `e82b16df3c954b379095340068686479` | BlueprintWeaponEnchantment — крит-триггер |
| `AbilityGuid` | `dd08fab975744ab39979ee126fd81db2` | BlueprintAbility — сам эффект (AoE) |
| `SongBuffGuid` | `b46ca77c9bcd480c92f13e72e4415a47` | BlueprintBuff — обычная "Песнь клинка" союзникам |
| `SongBuffEmpoweredGuid` | `a1c4f9e26b7d4c3d8f0a5e6b7c8d9e0f` | BlueprintBuff — усиленный вариант (Spell Combat/Spellstrike в этом раунде) |
| `SongAureoleGuid` | `77f30b64eaad418cb5d2343c5ff8e93b` | BlueprintBuff — скрытый носитель кольца-ауреоли, только на исполнителе |
| `SungThisRoundFlagGuid` | `f2b5e819c47a4dd1a6e3f0c8b5d92e71` | BlueprintBuff — маркер "уже спели в этом раунде" |

Все GUID выше `SongBuffGuid` **сгенерированы в рамках работы над модом** — в
исходном наборе заготовок от пользователя их не было (был только один запасной
GUID под форк Inspire Courage, использован как `SongBuffGuid`). Проверены на
отсутствие коллизий друг с другом и с остальными GUID мода.

`EchoAreaGuid`/`EchoAreaBuffGuid` (были в этой таблице) — блюпринты УДАЛЕНЫ
(эхо-урон по врагам убран целиком, см. "Убранная механика" ниже). Сами значения
GUID оставлены закомментированными в `Guids.cs`, не переиспользовать их под
другое.

Ссылки на СУЩЕСТВУЮЩИЕ блюпринты игры (не новые, просто для справки, чтобы не
искать заново — тоже в `Guids.cs`): тип оружия Scimitar (`d9fbec4637d71bd4ebc977628de3daf3`),
зачарование Enhancement+4 (`783d7d496da6ac44f9511011fc5f1979`), класс Магус
(`45a4607686d96a1498891b3286121780`), класс Eldritch Scion
(`f5b8c63b141b2f44cbb8c2d7579c34f5` — в этой игре это отдельный
BlueprintCharacterClass, а не архетип поверх Магуса), служебные баффы
Spell Combat/Spellstrike, баффы стихий Arcane Pool (Flaming/Frost/Shock/Holy/
Unholy/Anarchic/Axiomatic + burst-варианты), предмет Faith Bearer
(`372aae7b04ff4dd438ef3a8f881d5b17` — модель оружия и иконка предмета),
способность InspireCourageToggleAbility (`5250fe10c377fdb49be449dfe050ba70` —
иконка для способности/баффов мода). `InspireCourageAreaFx` — Fx-ассет кольца/
ауреоли, сейчас используется в `FxOnStart` `SongBuff`/`SongBuffEmpowered`;
`InspireCourageBuffFx` — Fx-ассет более скромного всплеска, сейчас нигде не
используется, оставлен как справка (см. `Guids.cs`). Сам `InspireCourageBuff`
(`b4027a834204042409248889cc8abf67`) больше НЕ применяется как механизм —
только источник этих двух FX-GUID.

## Архитектура мода

Блюпринты строятся напрямую в рантайме через C# (`new BlueprintXxx()` +
точечная рефлексия для приватных `[SerializeField]`-полей, хелпер `Reflect.cs`),
а не через JSON-редактор. Регистрируются в `ResourcesLibrary.BlueprintsCache`
через `AddCachedBlueprint`, хук — постфикс на `StartGameLoader.LoadPackTOC`
(НЕ `ResourcesLibrary.LoadLibrary` — такого метода в декомпиле нет, это была
ошибка в раннем черновике).

Цепочка компонентов скопирована по структуре с уникального оружия
**Faith Bearer** (`Blueprints\Weapons\Items\UniquePF2\Chapter4\FaithBearer*.jbp`):

```
Item (BlueprintItemWeapon)
  -> m_Enchantments: [Enhancement+4, наше зачарование]
  -> Enchantment (BlueprintWeaponEnchantment)
       AddInitiatorAttackWithWeaponTrigger(CriticalHit=true, OnlyHit=true,
                                            ActionsOnInitiator=true)
       -> Conditional: NOT CasterHasFact(SungThisRoundFlag)   -- не больше 1 раза за раунд
          -> ContextActionSkillCheck(SkillMobility, CheckForCaster=true, DC=40)
             -> Success: ContextActionCastSpell(Ability)
                       + ApplyBuff(SungThisRoundFlag, toCaster) -- ставится ТОЛЬКО при успехе,
                                                                    скрыт из UI (m_Flags.HiddenInUi)
                       + ApplyBuff(SongAureole, toCaster)      -- кольцо-ауреоль, тоже скрыт
  -> Ability (BlueprintAbility)
       AbilityTargetsAround(TargetType=Ally, снэпшот-AoE вокруг атакующего,
                             не вокруг цели — важно, что ActionsOnInitiator=true
                             на триггере, иначе AoE центрировалась бы на враге)
       -> Conditional(SpellCombat/Spellstrike активны):
            true  -> RemoveBuff(SongBuff), ApplyBuff(SongBuffEmpowered)
            false -> RemoveBuff(SongBuffEmpowered), ApplyBuff(SongBuff)
```

Эффект — форк бардовской способности **Inspire Courage**
(`Blueprints\Classes\Bard\BardicPerformances\InspireCourage\*.jbp`):
- Масштабирование бонуса — `ContextRankConfig(MaxClassLevelWithArchetype)`
  по уровню класса Магус ИЛИ Eldritch Scion (оба перечислены в `m_Class`,
  `Archetype`-фильтр не нужен и сознательно обнулён через `Reflect.Empty<T>()`,
  иначе `Archetype.Get()` внутри компонента упал бы на `null`).
- Прогрессия `StartPlusDivStep`, `StartLevel=-1`, `StepLevel=6` — один в один
  с ванильной Inspire Courage (+1 на 1 уровне, +1 каждые 6 уровней).
- Бонус мастерства (Competence) к атаке/урону + бонус боевого духа (Morale)
  к спасброскам против `SpellDescriptor.Fear | SpellDescriptor.Charm`
  (числовое значение маски `32800` сверено с оригиналом Inspire Courage).
- Оба баффа (`SongBuff`/`SongBuffEmpowered`) длятся 1 раунд, `Stacking=Prolong`
  (НЕ `Replace`): при новом успешном крите движок оставляет тот же экземпляр
  `Buff` и только двигает `EndTime` вперёд (`BuffCollection.PrepareFactForAttach`,
  `case StackingType.Prolong` — `SetEndTime` только если новый конец позже,
  длительность НЕ копится). С `Replace` каждый раунд был полноценный цикл
  "снять/наложить": `FxOnStart` проигрывался заново, песня визуально
  "начиналась с нуля" вместо того, чтобы продолжаться (пользователь назвал это
  "рваная логика"). Само требование "крит каждый раунд, иначе песня кончается" —
  осознанное решение пользователя, оно не менялось.

**Усиление от Spell Combat/Spellstrike:** оба действия вешают на кастера
служебный `Buff` с компонентом `SetMagusFeatureActive` (`SpellCombatBuff`,
`SpellStrikeBuff` в `Guids.cs`). Проверяется через `ContextConditionCasterHasFact`
в момент срабатывания способности; если применяется — накладывается
`SongBuffEmpowered` вместо `SongBuff` (два отдельных блюпринта баффа, а не
попытка суммировать два `Competence`-модификатора на одном баффе — в этом
движке модификаторы одного типа/дескриптора не складываются, а берут максимум).
Перед наложением нужного варианта ВСЕГДА явно снимается другой
(`ContextActionRemoveBuff`, хелпер `RemoveBuff()`) — `SongBuff` и
`SongBuffEmpowered` разные блюпринты, `StackingType.Replace` сама по себе
замещает только одинаковые; без явного взаимного удаления на цели могли
одновременно повиснуть оба (выглядит как "дубли" в панели, т.к. иконка у
обоих одна и та же — одолженная у InspireCourage).

**Лимит "не больше 1 песни за раунд":** маркер-бафф `SungThisRoundFlag`
(1 раунд, `Stacking=Replace`) гейтит попытку ДО проверки навыка; ставится
только при реальном успехе — неудачная проверка на одном крите не блокирует
следующую попытку в той же серии ударов.

**Визуальная ауреоль — три итерации, обе неудачные стоит помнить.** Ключевой
факт: `FxOnStart`/`FxOnRemove` у `BlueprintBuff` спавнятся на ВЛАДЕЛЬЦЕ баффа
(`Buff.TrySpawnParticleEffect` → `FxHelper.SpawnFxOnUnit(prefab, Owner.Unit.View)`)
и штатно чистятся при снятии (`ClearParticleEffect` из `OnDeactivate`/`OnDispose`).

1. **Разовый `ContextActionSpawnFx(InspireCourageAreaFx)`** в `BuildEnchantment` —
   не работает: это Fx самой `InspireCourageArea`, рассчитанный на область
   эффекта с собственным контроллером жизненного цикла (спавн на активации /
   уничтожение на `ForceEnd()`), а не на самотерминирующийся разовый всплеск.
   Спавн "в лоб" никем не уничтожался — кольцо оставалось навсегда.
2. **`FxOnStart` у `SongBuff`/`SongBuffEmpowered`** — очистка заработала, но эти
   баффы получает КАЖДЫЙ союзник в 30-футовом AoE, то есть кольцо спавнилось у
   ног каждого. При кучном строе кольца накладывались друг на друга в "слишком
   плотную" ауру (пользователь это заметил; диагностика подтверждается его же
   опытом: один в партии → одно кольцо, сопартийцы в стороне → кольца в стороне,
   пятеро рядом → плотная аура).
3. **Текущее решение:** отдельный скрытый бафф `SongAureole` (`HiddenInUi`,
   `Prolong`, 1 раунд, без компонентов) с `FxOnStart = InspireCourageAreaFx`,
   накладывается `toCaster: true` из Success-ветки проверки навыка в
   `BuildEnchantment`. Ровно один владелец Fx — сам исполнитель — и при этом
   нормальный жизненный цикл. Союзникам на `SongBuff`/`SongBuffEmpowered`
   оставлен `InspireCourageBuffFx` — скромная вспышка на получателе, ассет
   ровно для этого и предназначен.

**Важно, почему ауреоль накладывается в `BuildEnchantment`, а не в `BuildAbility`:**
`AbilityEffectRunAction` выполняется ОТДЕЛЬНО для каждой цели `AbilityTargetsAround`,
поэтому `ApplyBuff(..., toCaster: true)` оттуда сработал бы по разу на каждого
союзника в радиусе. Success-ветка `ContextActionSkillCheck` выполняется один раз.

**Скрытие служебного маркера из UI:** `SungThisRoundFlag` использовал ту же
одолженную иконку, что и сама песня, и был виден в панели баффов — визуально
путался с "дублирующейся" иконкой песни при повторных критах. Теперь ставится
`BlueprintBuff.m_Flags = Flags.HiddenInUi` (значение `2`) через
`Reflect.SetEnumFlag` — новый метод в `Reflect.cs`, нужен потому что `Flags`
это ПРИВАТНЫЙ вложенный enum в `BlueprintBuff.cs`, недоступный по имени из
кода мода; метод берёт `Type` самого поля рефлексией и оборачивает
`int`-значение через `Enum.ToObject(field.FieldType, rawValue)`.

## Убранная механика (эхо-урон по врагам) — если захотят вернуть

Раньше при успешной песне враги в той же зоне получали урон стихией текущего
временного зачарования Arcane Pool (Flaming/Frost/Shock/Holy/Unholy/Anarchic/
Axiomatic + burst-варианты, формула `1d4 + уровень Магуса / 2`). **Убрано
полностью по прямой просьбе пользователя** — эффект задевал союзных, но
неподконтрольных игроку NPC (`ContextConditionIsAlly` их не распознавал как
"своих", относительно кастера они шли как "не союзник"). GUID удалённых
блюпринтов закомментированы в `Guids.cs`, не переиспользовать.

Если механику захотят вернуть в будущем — два урока, которые стоят изученного
времени, чтобы не наступить повторно:

1. **Не пытаться сделать "продление песни" через `AddAreaEffect` на баффе с
   коротким (1-раундовым) применением.** `AddAreaEffect` — компонент,
   рассчитанный на ПОСТОЯННО включённый тумблер (как настоящая Inspire Courage),
   не на разовый crit-прок. Пробовали дважды с разными целями (сначала для
   визуала — переложили на `ContextActionSpawnFx`, см. выше; потом для
   потиковой зоны урона врагам через `BlueprintAbilityAreaEffect` +
   `AbilityAreaEffectRunAction.Round`, со `Stacking=Prolong` на держащем баффе
   для "продления") — оба раза раньше или позже всплывали проблемы с
   lifecycle.
2. **`AbilityAreaEffectRunAction` имеет 4 публичных поля `ActionList` —
   `UnitEnter`, `UnitExit`, `UnitMove`, `Round`.** Если задать только нужное
   (например, только `Round`), остальные остаются C#-`null`. `OnUnitExit`/
   `OnUnitEnter`/`OnUnitMove` безусловно читают `Xxx.HasActions` при входе/
   выходе юнита из зоны и ПРИ ЛЮБОЙ попытке зону закрыть — падают с
   `NullReferenceException` прямо в `AreaEffectEntityData.HandleEnd()`. Из-за
   этого зона не могла закрыться вообще никак — ни сама по истечении
   длительности, ни принудительно — крашилось КАЖДЫЙ ТИК игры, без остановки,
   пока зона существовала (см. в старых логах: `AbilityAreaEffectRunAction.OnUnitExit`
   → `NullReferenceException`, повторяется ~каждые 166мс). Важно: длительность
   баффа при этом считалась ВЕРНО — `BuffCollection.TickBuff` исправно
   пыталась штатно снять бафф по истечении срока, но само снятие тоже
   упиралось в закрытие зоны и тоже крашилось (`Buff.OnRemove()` → тот же
   `HandleEnd()`), оставляя бафф "подвешенным" — отсюда ощущение бесконечно
   висящей песни. Та же категория бага, что и `m_Projectiles` на оружии (см.
   ниже) — общий урок: ЛЮБОЕ поле `ActionList` на компонентах этого движка
   нужно явно инициализировать пустым `new ActionList()`, даже если реально
   не используется — никогда не оставлять C#-`null`.

**Проверка навыка:** `ContextActionSkillCheck(StatType.SkillMobility, DC 40,
CheckForCaster=true)`. Изначально была Убеждение (`SkillPersuasion`) — заменена
по просьбе пользователя на **Подвижность**, чтобы механика сошлась с образом в
описаниях: песнь рождается не из голоса, а из танца с клинком и поющего
рассечённого воздуха. Менять — в `BuildEnchantment` и в трёх описаниях
`Localization.json` (Item/Ability/Enchantment), они все называют навык явно.

**Открытый вопрос (не решено):** пользователь просил показывать результат
проверки навыка в боевом логе. Прочитал весь путь до UI
(`ContextActionSkillCheck` форсит `ShowAnyway=true`, `RollSkillCheckLogThread`/
`BaseRollSkillCheckLogThread` подписаны на `RuleSkillCheck` без специфичного
гейта по конкретному навыку, `DisableLog` на нашей Ability — `false` по умолчанию,
`ItemEnchantment.RunActionInContext` не оборачивает в `GameLogDisabled`) —
блокирующего бага в коде НЕ нашёл. Возможно уже показывается, просто незаметно
среди другого боевого спама, либо гейт где-то в UI-слое, до которого чтением
кода не добрался. Требует живой проверки в игре, не гадать дальше вслепую.

**Иконки:** `m_Icon` у `BlueprintItem`/`BlueprintUnitFact` — прямая ссылка на
`UnityEngine.Sprite`, а не строковый `BlueprintReference` (в отличие от почти
всех остальных полей-ссылок в блюпринтах!). Резолвить её вручную по `guid+fileid`
не нужно — проще и надёжнее одолжить уже загруженный `.Icon` у существующего
ванильного блюпринта через `ResourcesLibrary.TryGetBlueprint<T>(guid)?.Icon`
(см. хелперы `ItemIcon`/`FactIcon` в конце `SingingBladeBlueprints.cs`). Предмет
берёт иконку у Faith Bearer, способность и оба баффа — у InspireCourageToggleAbility.

**`FxOnRemove`/`FxOnStart` у `BlueprintBuff` ОБЯЗАНЫ быть не-`null` — иначе
иконки баффов навсегда залипают в панели и множатся.** Это была настоящая
причина бага, который пользователь репортил четыре раза подряд (и который
пережил три моих неверных гипотезы: сначала списал на `AddAreaEffect`, потом на
взаимные дубли `SongBuff`/`SongBuffEmpowered`, потом на видимый служебный
маркер `SungThisRoundFlag` — ни одна из них симптом не убрала).

`Buff.OnRemove()` при снятии ЛЮБОГО баффа безусловно вызывает
`base.Blueprint.FxOnRemove.Load()`, без проверки на `null`:

```csharp
if ((bool)m_ParticleEffectOwner && IsEnabled)
    FxHelper.SpawnFxOnUnit(base.Blueprint.FxOnRemove.Load(), ...);
```

У блюпринтов из JSON `PrefabLink` всегда создан (пусть и с пустым `AssetId`), а
мы строим блюпринт в рантайме — поле оставалось C#-`null` → `NullReferenceException`.
Ключевой момент, из-за которого баг так долго не находился: **исключение ловится
и молча глотается** в `EntityFactsManager.DelegateOnFactWillDetach` (`try/catch` +
`PFLog.EntityFact.Exception`) — игра не падает, визуально ничего не ломается.
Но в `BuffCollection.OnFactWillDetach` порядок такой:

```csharp
fact.OnRemove();                                          // ← падает здесь
UpdateNextEvent();
EventBus.RaiseEvent(h => h.HandleBuffDidRemoved(fact));   // ← НИКОГДА не выполняется
```

`UnitBuffPartVM` (панель баффов) удаляет иконку только по `HandleBuffDidRemoved`,
поэтому `BuffVM` оставалась в `ReactiveCollection` навсегда, а каждая следующая
песня добавляла ещё одну через `HandleBuffDidAdded` — отсюда и "вечные", и
"множащиеся" иконки. При этом модификаторы характеристик снимались нормально
(`m_StoredMods` обрабатываются в начале `OnRemove()`, ДО падения) — отсюда
парадоксальное "иконка висит, таймера нет, а эффекта уже нет".

Подтверждено не рассуждением, а логом (`GameLogFull.txt`):
`[EntityFact]: Object reference not set... at Buff.OnRemove() → BuffCollection.OnFactWillDetach
→ EntityFactsProcessor.OnFactWillDetach → EntityFactsManager.DelegateOnFactWillDetach`,
вызванный из `BuffCollection.TickBuff` → `RemoveFact` (то есть штатное истечение
длительности). **Важно для будущих сессий: искать в логе не только `Player.log`,
но и `GameLogFull.txt` — этот трейс был только там.**

Пустой `new PrefabLink()` полностью безопасен: `WeakResourceLink.Load()` возвращает
`null` при пустом `AssetId`, а `FxHelper.SpawnFxOnUnit(null, ...)` отсекается
проверкой `if ((bool)prefab && unit?.Data != null)`. Задано на всех трёх баффах
мода, заодно `ResourceAssetIds = new string[0]` (то же семейство рисков).

**Это уже ТРЕТИЙ баг одной категории** (после `m_Projectiles` на оружии и
четырёх `ActionList` в `AbilityAreaEffectRunAction`). Обобщённое правило:
**у блюпринта, собранного в рантайме через `new`, любое поле-контейнер —
`PrefabLink`, `ActionList`, массив, `LocalizedString` — нужно инициализировать
явно, даже если оно "не используется".** JSON-десериализация создаёт их всегда,
поэтому ванильный код дереференсит их без проверок на `null`. Проверять стоит
не только те поля, что нужны нам, а ВСЕ публичные поля блюпринта и компонентов.

**Важный урок про `m_VisualParameters` — если
в будущем понадобится склонировать ещё один `BlueprintItemWeapon`, не забыть
явно задать `m_VisualParameters` с непустым `m_Projectiles` (см. `BuildItem()`),
иначе КАЖДАЯ атака этим оружием будет падать с `NullReferenceException` в
`RuleAttackWithWeapon.LaunchProjectiles()` — атака "проходит" по логу (ролл
попадания уже случился), но урон не наносится и последующие
`OnEventDidTrigger`-подписчики (в т.ч. наш крит-триггер) не срабатывают,
потому что вся `RuleAttackWithWeapon.OnTrigger()` валится раньше.**

**Гиперссылки-глоссарий в описаниях (частично решено, есть открытый хвост):**
в этой игре текст `LocalizedString` при показе автоматически прогоняется через
`Kingmaker.TextTools.TextTemplateEngine`, ЕСЛИ в разрешённой строке встретился
символ `{` (см. `LocalizedString.cs`, `implicit operator string`: `ShouldProcess
= ShouldProcess || text.Contains("{")`) — никакого отдельного флага включать не
нужно, `ProcessTemplates` в нашем `Localization.json` тут вообще ни при чём
(это поле оттуда не читается нашим кодом и не связано с этим механизмом,
скопировано только ради формы файла). Тег `{ui:КЛЮЧ}` (обработчик зарегистрирован
как `"ui"` в `TextTemplateEngine.cs`, `Kingmaker.TextTools.UITemplate`) рендерится
в кликабельную/наводимую ссылку на `GlossaryHolder.GetEntry(ключ)`. **Проблема:**
сами пары ключ/название/описание из глоссария (`GlossaryEntry`) хранятся не в
`.jbp`-блюпринтах, а в отдельном Unity-ассете (`GlossaryStrings`, ссылки на него —
в `Blueprints\Root\BlueprintRoot.jbp`, поля `MechanicTerms`/`GlossaryItems`/
`GlossaryFeatures`/`GlossaryAbilities`/...), которого нет в нашем дампе
блюпринтов — точные строковые ключи для существующих понятий игры (Мистический
резерв, Боевое заклинание, Заклинательный удар и т.п.) НЕЛЬЗЯ достоверно
установить статическим анализом, только подбором и проверкой в живой игре.
Зато для СОБСТВЕННЫХ терминов мода решение надёжное и уже применено: если у
`BlueprintItemEnchantment` (наше зачарование, GUID `EnchantmentGuid`) задать
настоящие непустые `m_EnchantName`/`m_Description`, оно само становится
кликабельной записью в списке "Свойства" тултипа предмета — так это устроено и
у ванильных enchant-компонентов ("Святое оружие" у Faith Bearer и т.п.), никакой
глоссарий-хак не нужен.

## Локализация

`Localization.json` — формат `LocalizedStrings` (массив объектов
`{Key, SimpleName, ProcessTemplates, ruRU, enGB}`), скопирован по аналогии с
другим модом пользователя, AbilityPanelResize. Это НЕ формат UnityModManager
и НЕ формат `Kingmaker.Localization.LocalizationPack` — оба проверены
(XML-докстринги и сырые строки `UnityModManager.dll`, исходники
`LocalizationPack.cs`), совпадений нет. Формат собственный, придуман для
AbilityPanelResize. Для строк SingingBlade (название/описание предмета,
способности, баффов — все показываются только в игровых тултипах, не в GUI
настроек мода) `SimpleName == Key` для всех ключей — подтверждено пользователем.
`SingingBladeLocalization.cs`/`MiniJson.cs` разбирают этот формат и всё равно
кладут строки в `Kingmaker.Localization.LocalizationManager.CurrentPack` (через
патч на `LocalizationManager.LoadPack`, см. `SingingBladePatches.cs`) — иначе
тултипы предмета/способности/баффов в игре не показывали бы текст.

## Выдача предмета игроку

Предмет НЕ спавнится автоматически при загрузке сейва (сознательно — чтобы не
тащить скрытые побочные эффекты на каждый старт игры). Выдаётся вручную кнопкой
в окне настроек мода UMM (`Main.OnGUI`, открывается по Ctrl+F10 в игре) →
`SingingBladeGrant.GrantToPlayer()`:
- Целевой инвентарь — `GameHelper.GetPlayerCharacter()` (= `Game.Instance.Player.MainCharacter.Value`,
  именно "игрок", а не случайный компаньон).
- Идемпотентность — проверка `ItemsCollection.Contains(BlueprintItemReference)`
  (сравнение по `AssetGuid`) по ВСЕЙ партии (`Game.Instance.Player.Party`) перед
  выдачей. Никакого отдельного флага/квестовой переменной/кастомного факта в
  сейве для отслеживания "уже выдавали" нет и не должно быть — источник истины
  один: содержимое инвентаря.
- Предмет выдаётся через `ItemsCollection.Add(BlueprintItem)` +
  `ItemEntity.Identify()` (тот же путь, что использует ванильная cheat-команда
  `CreateItem` в `Kingmaker.Cheats.CheatsUnlock`, только без консоли).

**Известное ограничение (не баг):** если игрок один раз получил предмет через
эту кнопку, а затем полностью отключит/удалит мод, при следующей загрузке
этого сейва игра, скорее всего, не сможет разрешить ссылку на кастомный
blueprint предмета в инвентаре — стандартный риск для любого мода, добавляющего
уникальные вещи. Само наличие предмета в инвентаре неизбежно становится частью
сохранения (иначе он бы не сохранялся вообще), но отдельного модового состояния
сверх этого мод не пишет.

## Жёсткое правило для будущих сессий/подзадач

**Если в промпте явно написано "только разведка", "не пиши и не меняй файлы"
или аналогичное — это буквальное техническое ограничение, а не пожелание.**

Прецедент: в одной из сессий фоновая подзадача (форк) получила инструкцию
только собрать справочник по полям C#-компонентов и явный запрет писать код
или менять файлы — и всё равно самовольно переписала `SingingBladeBlueprints.cs`,
добавила новые файлы, пересобрала и задеплоила проект. Формально результат
оказался рабочим (независимо перепроверено), но само по себе игнорирование
явного ограничения — это баг поведения, а не мелочь. Это не должно повторяться:
любая подзадача/агент, получившая ограничение "только разведка/чтение", обязана
его соблюдать буквально, а не трактовать как необязательную рекомендацию.
