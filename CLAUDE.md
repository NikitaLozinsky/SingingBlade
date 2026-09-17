# SingingBlade

Harmony-мод (Unity Mod Manager) для Pathfinder: Wrath of the Righteous.
Добавляет уникальный скимитар "Поющий клинок" для класса Магус: клонирован по
структуре с существующего уникального оружия "Faith Bearer", но с другой
логикой эффекта (форк бардовской "Песни отваги" + стихийное эхо от Arcane Pool).

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
| `SungThisRoundFlagGuid` | `f2b5e819c47a4dd1a6e3f0c8b5d92e71` | BlueprintBuff — маркер "уже спели в этом раунде" |
| `EchoAreaGuid` | `3c7d8e1f2a9b4c5d8e6f0a1b2c3d4e5f` | BlueprintAbilityAreaEffect — потиковая зона эхо-урона |
| `EchoAreaBuffGuid` | `6a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d` | BlueprintBuff — держит зону живой, Stacking=Prolong |

Все GUID выше `SongBuffGuid` **сгенерированы в рамках работы над модом** — в
исходном наборе заготовок от пользователя их не было (был только один запасной
GUID под форк Inspire Courage, использован как `SongBuffGuid`). Проверены на
отсутствие коллизий друг с другом и с остальными GUID мода.

Ссылки на СУЩЕСТВУЮЩИЕ блюпринты игры (не новые, просто для справки, чтобы не
искать заново — тоже в `Guids.cs`): тип оружия Scimitar (`d9fbec4637d71bd4ebc977628de3daf3`),
зачарование Enhancement+4 (`783d7d496da6ac44f9511011fc5f1979`), класс Магус
(`45a4607686d96a1498891b3286121780`), класс Eldritch Scion
(`f5b8c63b141b2f44cbb8c2d7579c34f5` — в этой игре это отдельный
BlueprintCharacterClass, а не архетип поверх Магуса), служебные баффы
Spell Combat/Spellstrike, баффы стихий Arcane Pool (Flaming/Frost/Shock/Holy/
Unholy/Anarchic/Axiomatic + burst-варианты), предмет Faith Bearer
(`372aae7b04ff4dd438ef3a8f881d5b17` — модель оружия и иконка предмета),
способность/бафф InspireCourageToggleAbility/InspireCourageBuff
(`5250fe10c377fdb49be449dfe050ba70` / `b4027a834204042409248889cc8abf67` —
иконка способности/баффов и настоящий FX ауреоли выступления, см. ниже).

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
       -> ContextActionSkillCheck(SkillPersuasion, CheckForCaster=true, DC=40)
          -> Success: ContextActionCastSpell(Ability)
          (изначально гейта не было вовсе — по позднейшей явной просьбе
           пользователя песнь звучит не на каждом крите, а только при
           успешной проверке Убеждения DC 40, как в примере Faith Bearer,
           только другой навык/DC)
  -> Ability (BlueprintAbility)
       AbilityTargetsAround(TargetType=Any, снэпшот-AoE вокруг атакующего,
                             не вокруг цели — важно, что ActionsOnInitiator=true
                             на триггере, иначе AoE центрировалась бы на враге)
       -> Conditional(IsAlly):
            true  -> применить SongBuff/SongBuffEmpowered союзнику
            false -> эхо-урон по активной стихии Arcane Pool
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

**Усиление от Spell Combat/Spellstrike:** оба действия вешают на кастера
служебный `Buff` с компонентом `SetMagusFeatureActive` (`SpellCombatBuff`,
`SpellStrikeBuff` в `Guids.cs`). Проверяется через `ContextConditionCasterHasFact`
в момент срабатывания триггера; если применяется — накладывается
`SongBuffEmpowered` вместо `SongBuff` (два отдельных блюпринта баффа, а не
попытка суммировать два `Competence`-модификатора на одном баффе — в этом
движке модификаторы одного типа/дескриптора не складываются, а берут максимум).

**Эхо-урон по врагам (переработано — теперь потиковая зона, не мгновенный удар):**
определяется той же техникой — `ContextConditionCasterHasFact` на служебные баффы
Arcane Pool (Flaming/Frost/Shock/Holy/Unholy/Anarchic/Axiomatic и их burst-варианты;
Anarchic технически хранится движком как Unholy-урон, Axiomatic — как Holy-урон,
это не ошибка, так сделано в ванили). Формула — `1d4 + (уровень Магуса / 2)`,
`IgnoreCritical=true` (иначе крит-множитель раздул бы "маленький отголосок" в
основной урон). Чистое блюпринт-решение, без Harmony-патчей на боевую логику.
По решению пользователя урон бьёт всех врагов в зоне одинаково, без ванильного
ограничения "только по противоположному мировоззрению".

По более поздней явной просьбе пользователя урон больше НЕ бьёт мгновенно в
момент крита — вместо этого при каждой успешной песне на кастера накладывается
`EchoAreaBuff` (`Stacking=Prolong`, см. `BuildEchoAreaBuff`), который через
`AddAreaEffect` держит живой `EchoArea` (`BuildEchoArea`,
`BlueprintAbilityAreaEffect` + `AbilityAreaEffectRunAction.Round`). `Round`
срабатывает по юнитам внутри зоны РАЗ В РАУНД, начиная со следующего раунда
после спавна (не в момент создания) — то есть первый тик урона естественным
образом приходится на следующий раунд, а не на сам крит. `Stacking=Prolong`
означает, что повторная успешная песня ПРОДЛЕВАЕТ уже активную зону (берётся
максимум старого и нового времени окончания), а не создаёт вторую поверх неё —
это и есть "продление выступления". `ContextRankConfig` для этой механики висит
на `EchoAreaBuff`, а НЕ на способности — Round-действия зоны выполняются в
MechanicsContext этого баффа (см. `BuffCollection.AddBuff` → `CloneFor`), а не
исходной Ability, так что Rank должен быть виден именно там, иначе резолвился
бы в 0. Способность (`BuildAbility`) теперь обрабатывает только союзников —
таргетинг врагов и весь `enemyBranch`/`mainConditional`-обвес из неё убраны.

**Визуальный эффект ауреоли (по просьбе пользователя) — ИСТОРИЯ ПРАВКИ, важно
не наступить повторно:** первая версия накладывала на кастера настоящий
ванильный `InspireCourageBuff` (`ApplyBuff(..., toCaster: true)`) только ради
`AddAreaEffect`/визуала выступления. Это оказалось БАГОМ: `AddAreaEffect`
рассчитан на ПОСТОЯННО включённый тумблер бардовского выступления, а не на
разовый crit-прок — area effect не сворачивался синхронно с 1-раундовой
длительностью нашего применения и продолжал сам по себе периодически
перевешивать ванильную "Песнь отваги" на союзниках рядом с кастером. Пользователь
поймал это как "бафф перманентный, не снимается" + "дублируется в панели баффов"
(на деле — два РАЗНЫХ баффа с одинаковой иконкой, т.к. мы же и одолжили именно
иконку InspireCourage). **Первый фикс:** хак убран целиком; вместо него
`SongBuff`/`SongBuffEmpowered` получили `FxOnStart = PrefabLink(InspireCourageBuffFx)`
(`buff.FxOnStart` — публичное поле прямо на `BlueprintBuff`, без рефлексии).

**НО** это решило не всё — вскрылись ещё два отдельных бага:
1. `FxOnStart` — это разовый всплеск НА СОЮЗНИКЕ-получателе (см. `Buff.TrySpawnParticleEffect`),
   а не кольцо/ауреоль ВОКРУГ ИСПОЛНИТЕЛЯ, которое раньше давал `AddAreaEffect`
   (у него было своё поле `Fx`, отдельное от `FxOnStart` буффа). Это два разных
   визуала. **Фикс:** добавлен `ContextActionSpawnFx(InspireCourageAreaFx)` в
   Success-ветку проверки Убеждения в `BuildEnchantment()` — разовый спавн кольца
   на кастере, никакого AddAreaEffect/lifecycle не участвует.
2. Дубли иконок в панели баффов были НЕ только из-за AreaEffect-хака: `SongBuff`
   и `SongBuffEmpowered` — РАЗНЫЕ блюпринты, `StackingType.Replace` замещает
   только одинаковые блюпринты. Если в одном раунде спели без спелл-комбата,
   а в следующем — с ним, на цели могли повиснуть ОБА (внешне неотличимы, т.к.
   иконка одолжена одна и та же). **Фикс:** в `allyBranch` перед наложением
   нужного варианта явно снимается другой (`ContextActionRemoveBuff`, хелпер
   `RemoveBuff()`).

Также добавлен лимит "не больше одной СПЕТОЙ песни за раунд" — маркер-бафф
`SungThisRoundFlagGuid` (см. `BuildSungThisRoundFlag()`), гейтит `singChance`
ДО проверки Убеждения; ставится только при реальном успехе, так что неудачная
проверка на одном крите не блокирует попытку на следующем крите той же серии.

Наш собственный магус-скейлящийся бонус (`SongBuff`/`SongBuffEmpowered`) во всей
этой истории не менялся — только обвязка вокруг его применения.

**Открытый вопрос (не решено):** пользователь просил показывать результат
проверки Убеждения в боевом логе. Прочитал весь путь до UI
(`ContextActionSkillCheck` форсит `ShowAnyway=true`, `RollSkillCheckLogThread`/
`BaseRollSkillCheckLogThread` подписаны на `RuleSkillCheck` без специфичного
гейта для Persuasion, `DisableLog` на нашей Ability — `false` по умолчанию,
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

**Важный урок про `m_VisualParameters` (см. историю багфиксов ниже) — если
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
