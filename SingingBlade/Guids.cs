namespace SingingBlade
{
    // GUID'ы новых блюпринтов мода. Зафиксированы, чтобы не разъезжаться между сессиями работы.
    internal static class Guids
    {
        public const string ItemGuid = "c82273d736bd4e38a149cde7e2ad71ca";
        public const string EnchantmentGuid = "e82b16df3c954b379095340068686479";
        public const string AbilityGuid = "dd08fab975744ab39979ee126fd81db2";
        public const string SongBuffGuid = "b46ca77c9bcd480c92f13e72e4415a47";

        // Единственный GUID, которого не было в исходном списке заготовок (там было выделено
        // на один запасной GUID под форк Inspire Courage) — для "усиленного" варианта песни
        // потребовался ещё один отдельный BlueprintBuff, поэтому сгенерирован новый GUID.
        public const string SongBuffEmpoweredGuid = "a1c4f9e26b7d4c3d8f0a5e6b7c8d9e0f";

        // Зона песни (BlueprintAbilityAreaEffect). Вешается на исполнителя через
        // AddAreaEffect на SongAureole и раздаёт/снимает бафф песни по входу-выходу
        // из радиуса. Новый GUID.
        public const string SongAreaGuid = "d165fa02e71646d39acd7972e6297626";

        // Скрытый бафф-носитель визуальной ауреоли, только на самого исполнителя.
        // Нужен отдельным блюпринтом потому, что FxOnStart спавнится на ВЛАДЕЛЬЦЕ баффа
        // (Buff.TrySpawnParticleEffect -> FxHelper.SpawnFxOnUnit(prefab, Owner.Unit.View)):
        // пока кольцо висело на SongBuff/SongBuffEmpowered, его получал каждый союзник в
        // радиусе, и при кучном строе кольца накладывались друг на друга в "плотную" ауру.
        // Новый GUID (не было в исходном списке заготовок).
        public const string SongAureoleGuid = "77f30b64eaad418cb5d2343c5ff8e93b";

        // Существующие блюпринты игры, на которые мы ссылаемся, не создавая заново.
        public const string ScimitarWeaponType = "d9fbec4637d71bd4ebc977628de3daf3";
        // Ванильное +5 (Weapons/Enchantments/Enhancement5.jbp, WeaponEnhancementBonus = 5).
        // До 2026-09-26 клинок был +4 (Enhancement4, 783d7d496da6ac44f9511011fc5f1979).
        public const string Enhancement5Enchantment = "bdba267e951851449af552aa9f9e3992";

        // Модель "Несущего веру" (уникальный скимитар) — переиспользуем её визуал
        // для Поющего клинка вместо дефолтной модели базового типа Scimitar.
        public const string FaithBearerWeaponModel = "64e3b5cf96061074d91f704b6681f70e";
        public const string FaithBearerWeaponSheathModel = "729eb31cff4df02419fa151bcd2049c4";

        // Сам предмет "Несущий веру" — берём у него иконку для нашего скимитара.
        public const string FaithBearerItem = "372aae7b04ff4dd438ef3a8f881d5b17";

        // Настоящая бардовская "Песнь отваги" — берём иконку для наших способности/баффов.
        public const string InspireCourageToggleAbility = "5250fe10c377fdb49be449dfe050ba70";

        // FxOnStart самого InspireCourageBuff — скромная вспышка на КОНКРЕТНОМ получателе
        // выступления. Ровно для этого и используется: FxOnStart у SongBuff/SongBuffEmpowered,
        // то есть у каждого союзника, услышавшего песню.
        public const string InspireCourageBuffFx = "9353083f430e5a44a8e9d6e26faec248";

        // Fx самой InspireCourageArea (кольцо/ауреоль вокруг выступающего). Прошёл три
        // итерации, обе неудачные стоит помнить:
        //  1) разовый ContextActionSpawnFx в BuildEnchantment — этот Fx рассчитан на
        //     ПОСТОЯННО включённую area-effect зону со своим контроллером жизненного цикла
        //     (спавн на активации / уничтожение на ForceEnd()), поэтому разовый спавн без
        //     владеющего баффа никем не уничтожался и кольцо оставалось навсегда;
        //  2) FxOnStart у SongBuff/SongBuffEmpowered — очистка заработала, но FxOnStart
        //     спавнится на ВЛАДЕЛЬЦЕ баффа, а его получают все союзники в радиусе: при
        //     кучном строе кольца накладывались друг на друга в "плотную" ауру.
        // Сейчас — FxOnStart отдельного скрытого SongAureole, который вешается только на
        // самого исполнителя (toCaster): ровно одно кольцо, и штатная очистка баффом.
        public const string InspireCourageAreaFx = "2f93a2909cb766f4d961aee34a3c84c2";

        // --- «Дотянуться до звёзд»: проведение лучевых заклинаний через удар клинком ---
        // БОЛЬШЕ НЕ ИСПОЛЬЗУЕТСЯ. Была скрытой фичей-переносчиком в штатной цепочке
        // "зачарование -> AddUnitFeatureEquipment -> фича -> AddFacts -> переключатель".
        // Цепочка рвалась молча (компоненты на блюпринте есть, до персонажа не доезжает
        // ничего, в логе ни одного исключения), поэтому заменена прямой выдачей из кода —
        // см. ReachForStarsGrant. Блюпринт с этим GUID больше не создаётся; сам GUID
        // НЕ переиспользовать под другое, чтобы не путать историю сессий.
        public const string ReachForStarsFeatureGuid = "bf4bc6de66d24b339410d508379c2d9a";
        // ОТРАБОТАННЫЙ GUID. Был активируемым переключателем старой схемы
        // «подбежать и ударить» (флаг RangedStrike = false). Схема удалена целиком
        // 2026-09-23: режим дистанционного удара работает и делает её ненужной, а
        // поддерживать две ветки одной механики дороже, чем иметь одну. Блюпринт с этим
        // GUID больше не создаётся; сам GUID НЕ переиспользовать под другое.
        // Разбор трёх слоёв пошаговой механики, на которых та схема ломалась, сохранён
        // в CLAUDE.md — он полезен сам по себе, независимо от удалённого кода.
        // public const string ReachForStarsToggleGuid = "66a55d4b4da64eb9879ef66cca96d6a5";
        // Бафф "переключатель включён" — обязательное поле m_Buff у активируемой способности,
        // заодно по нему код проверяет, включён ли режим.
        public const string ReachForStarsBuffGuid = "aec52e83d3404b649be6446df6e9867c";

        // Активируемая способность «Дотянуться до звёзд» в режиме ДИСТАНЦИОННОГО УДАРА
        // (ReachForStars.RangedStrike): быстрое действие, тратит очко Мистического
        // резерва, вешает на исполнителя ReachForStarsBuff. Новый GUID, сгенерирован
        // 2026-09-21, проверен на отсутствие коллизий с остальными GUID мода.
        // В режиме переключателя (старая схема) НЕ используется.
        public const string ReachForStarsAbilityGuid = "001d4da034f243508388779003d19645";

        // ТО ЖЕ САМОЕ, но с платой из резерва Чародейского наследника.
        //
        // Зачем второй блюпринт: компонент AbilityResourceLogic умеет ровно ОДИН
        // m_RequiredResource, а у Магуса и у Наследника резервы — РАЗНЫЕ блюпринты
        // (см. ArcanePoolResource / EldritchPoolResource ниже). Ваниль решает это так же —
        // дублированием способности под каждый резерв (ArcaneWeaponSwitchAbility против
        // EldritchWeaponSwitchAbility). Какой из двух выдать, решает ReachForStars
        // .RefreshToggle по тому, какой резерв реально есть у персонажа.
        // GUID сгенерирован 2026-09-21, проверен на отсутствие коллизий.
        public const string ReachForStarsAbilityEldritchGuid = "01553f94337d4ad199c3ad3ea882c09d";

        // --- «Гроза Элизиума»: молния на крит + гром/молния на естественных атаках ---
        // Аналог ванильного CallLightningCritical (зачарование скимитара "Гнев медвежьего
        // бога", ea4da1b2cf1db1147b9e9974135d43ad), переписанный с медведей на путь Азаты
        // по просьбе пользователя 2026-09-22. Три новых GUID, проверены на коллизии.
        public const string StormEnchantmentGuid = "06db45fc46fd4901a68e7d642c645271";
        // Скрытая фича носителя: его естественные атаки и удары без оружия.
        public const string StormFeatureGuid = "37bcde342e2a49cbaa21b8034996bbfe";
        // То же самое для Айву — её выдаёт AddFeatureToPet из фичи носителя.
        public const string StormPetFeatureGuid = "716210dc387e4cac9d12e8083e487d16";

        // Бафф-«растяжка» досягаемости под дальность заклинания.
        //
        // Раньше эта добавка вешалась ПРЯМО на стат Reach отдельным модификатором, и это
        // была настоящая мина: ModifiableValue сериализует все свои модификаторы в сейв
        // (свойство PersistentModifierList), у модификатора без фактов-источника нет
        // владельца, который снял бы его при загрузке, — то есть сохранение в момент между
        // кастом и ударом давало ВЕЧНЫЙ бонус к досягаемости, снять который нечем.
        // Теперь это обычный скрытый бафф на один раунд с компонентом
        // AddStatBonus(Reach, 1) и РАНГОМ, равным нужному числу футов (Buff.SetRank):
        // сохраняется штатно, истекает сам, худший случай — лишний раунд досягаемости.
        public const string ReachStretchBuffGuid = "461a9737e1c54034b605cc2b72351090";

        // Ванильные ссылки для «Грозы Элизиума».
        // Грохочущий взрыв (ThunderingBurst) — обычное зачарование, WeaponEnergyBurst
        // со звуковым уроном d8. Просто вешается на предмет, своего аналога не нужно.
        public const string ThunderingBurstEnchantment = "83bd616525288b34a8f34976b2759ea1";
        // Заклинание "Вызов грозы" — то же самое, что бьёт в ванильном оригинале.
        // Берём его целиком: свой ContextActionDealDamage дал бы тот же урон, но без
        // готового Fx удара молнии и без строки в боевом логе.
        public const string CallLightningStormAbility = "cad052ef098f9f247ab73ae4c37ac687";
        // "Первое вознесение" Азаты (скрытая фича, выдающая способность «Орудия свободы»).
        // По ней проверяем, явил ли носитель природу азаты: это замена ванильному
        // условию "превратился в медведя".
        public const string AzataFirstAscensionFeature = "d2cfbbb941e07b04299b617017e369f1";

        // Мистический резерв магуса (BlueprintAbilityResource) — из него способность «Дотянуться до звёзд»
        // берёт плату. Тот же ресурс, что тратят ванильные арканы вроде Мистической
        // точности (ArcaneAccuracyAbility, компонент AbilityResourceLogic).
        public const string ArcanePoolResource = "effc3e386331f864e9e06d19dc218b37";

        // Резерв Чародейского наследника — ОТДЕЛЬНЫЙ блюпринт, а не тот же самый.
        // Архетип EldritchScionArchetype на 1 уровне в RemoveFeatures убирает магусовскую
        // ArcanePoolFeature (3ce9bb90749c21249adc639031d5eed1) и в AddFeatures ставит
        // EldritchPoolFeature (95e04a9e86aa9e64dad7122625b79c62), а та через
        // AddAbilityResources выдаёт ИМЕННО этот ресурс. Отдельный класс
        // EldritchScionClass делает то же самое. Поэтому у Наследника магусовского
        // ArcanePoolResource нет вовсе — счётчик на способности показывал бы 0,
        // а игра писала бы "нет ресурсов" (ровно этот баг и был).
        // Архетип Armored Battlemage, наоборот, свой ресурс НЕ заводит — у него
        // ArmoredBattlemageArcanePoolFeature ссылается на тот же магусовский ресурс,
        // так что третий вариант способности не нужен.
        public const string EldritchPoolResource = "17b6158d363e4844fa073483eb2655f8";

        // Книги заклинаний Магуса и Чародейского наследника. Нужны, чтобы определить
        // "своё" заклинание САМОСТОЯТЕЛЬНО, а не через UnitPartMagus.IsSpellFromMagusSpellList:
        // тот резолвит книгу строго через BlueprintRoot.SystemMechanics.MagusClass, и на
        // ОТДЕЛЬНОМ классе Чародейского наследника (EldritchScionClass, см. ниже) вернул бы
        // null со всеми вытекающими. Обе книги указывают на один и тот же спеллбук-блюпринт
        // и у класса-наследника, и у архетипа поверх Магуса — поэтому проверка по книге
        // покрывает оба способа быть Наследником.
        public const string MagusSpellbook = "5d8d04e76dff6c5439de99af0d57be63";
        public const string EldritchScionSpellbook = "e2763fbfdb91920458c4686c3e7ed085";

        // Список заклинаний Магуса. ОСНОВНАЯ проверка "своё заклинание" идёт по нему,
        // а не по книге: и книга Магуса, и книга Чародейского наследника ссылаются на
        // ОДИН И ТОТ ЖЕ список (проверено в .jbp обоих спеллбуков), а сам список не
        // зависит от того, из какой книги заклинание в итоге кастуется. Это важно для
        // модов вроде MythicMagicMayhem, которые сливают книги: проверка по книге там
        // перестала бы узнавать заклинание, а проверка по списку продолжит работать.
        public const string MagusSpellList = "4d72e1e7bd6bc4f4caaea7aa43a14639";

        // "Орудия свободы" — мифическая способность Азаты (первое вознесение).
        // Не заклинание и не из книги магуса: живёт в окне способностей, Type =
        // Supernatural, плата из собственного ресурса. Но по механике это настоящий луч
        // (AbilityDeliverProjectile -> RayItem -> RayType, AttackType = RangedTouch),
        // поэтому проводится через клинок ровно так же, как Обжигающий луч.
        // Добавлена в список ReachForStars.ExtraDeliverableAbilities по просьбе
        // пользователя 2026-09-22.
        public const string AzataFirstAscensionAbility = "5d5f0c9b274bab44eb01272c8fcf251d";

        // Служебный маркер-бафф "уже спели в этом раунде" — не даёт песне срабатывать
        // больше одного раза за раунд даже при нескольких критах в серии ударов.
        // Новый GUID (не было в исходном списке заготовок).
        public const string SungThisRoundFlagGuid = "f2b5e819c47a4dd1a6e3f0c8b5d92e71";

        public const string MagusClass = "45a4607686d96a1498891b3286121780";
        public const string EldritchScionClass = "f5b8c63b141b2f44cbb8c2d7579c34f5";

        public const string SpellCombatBuff = "91e4b45ab5f29574aa1fb41da4bbdcf2";
        public const string SpellStrikeBuff = "06e0c9887eb1724409977dac7168bfd7";

        // Сами способности Магуса (не баффы) — берём иконку "Удара заклинателя"
        // для нашей способности «Дотянуться до звёзд»: она читается игроком как родственная механика.
        public const string SpellCombatAbility = "8898a573e8a8a184b8186dbc3a26da74";
        public const string SpellStrikeAbility = "e958891ef90f7e142a941c06c811181e";

        // ОТКЛЮЧЕНО по прямой просьбе пользователя (эхо-урон по врагам задевал союзных,
        // но неподконтрольных игроку NPC, плюс потиковая AreaEffect-зона ниже крашилась
        // каждый тик игры и не давала баффу союзникам корректно сниматься — см. историю
        // в CLAUDE.md). GUID'ы НЕ переиспользовать под другое в будущем, чтобы не
        // рассинхронизировать историю сессий, если механику когда-нибудь вернут:
        // EchoAreaGuid       = "3c7d8e1f2a9b4c5d8e6f0a1b2c3d4e5f"  (BlueprintAbilityAreaEffect)
        // EchoAreaBuffGuid   = "6a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d"  (BlueprintBuff, Stacking=Prolong)
        //
        // Временные зачарования Мистического резерва (Arcane Pool) — тоже были нужны только
        // для эхо-урона, сейчас не используются нигде. Ссылки на существующие ванильные
        // блюпринты (не наши GUID), оставлены как справка на случай, если понадобятся снова:
        // ArcaneFlamingBuff       = "32e17840df49fbd48b835d080f5673a4"
        // ArcaneFlamingBurstBuff  = "bca914e2b1814ff886c7a91de104fd46"
        // ArcaneFrostBuff         = "39f8c2ca61fa4bb419b13813001125ce"
        // ArcaneIcyBurstBuff      = "85d5bfd7c0f54adb82444877df1712b0"
        // ArcaneShockBuff         = "5b76e44a1ed84704e858c38e7e97e7f2"
        // ArcaneShockingBurstBuff = "0bacae88449140bdb5368354ffdd410a"
        // ArcaneHolyBuff          = "ac667a37ec216d0418ea483c02ff871e"
        // ArcaneAxiomaticBuff     = "df4537fb64116694d82a6736940542b7"
        // ArcaneUnholyBuff        = "9140db37a973e6543b5906d6da5780f7"
        // ArcaneAnarchicBuff      = "962970e58fb7fbe41a7efc1c907ebe49"
    }
}
