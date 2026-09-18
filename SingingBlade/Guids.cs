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

        // Скрытый бафф-носитель визуальной ауреоли, только на самого исполнителя.
        // Нужен отдельным блюпринтом потому, что FxOnStart спавнится на ВЛАДЕЛЬЦЕ баффа
        // (Buff.TrySpawnParticleEffect -> FxHelper.SpawnFxOnUnit(prefab, Owner.Unit.View)):
        // пока кольцо висело на SongBuff/SongBuffEmpowered, его получал каждый союзник в
        // радиусе, и при кучном строе кольца накладывались друг на друга в "плотную" ауру.
        // Новый GUID (не было в исходном списке заготовок).
        public const string SongAureoleGuid = "77f30b64eaad418cb5d2343c5ff8e93b";

        // Существующие блюпринты игры, на которые мы ссылаемся, не создавая заново.
        public const string ScimitarWeaponType = "d9fbec4637d71bd4ebc977628de3daf3";
        public const string Enhancement4Enchantment = "783d7d496da6ac44f9511011fc5f1979";

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

        // Служебный маркер-бафф "уже спели в этом раунде" — не даёт песне срабатывать
        // больше одного раза за раунд даже при нескольких критах в серии ударов.
        // Новый GUID (не было в исходном списке заготовок).
        public const string SungThisRoundFlagGuid = "f2b5e819c47a4dd1a6e3f0c8b5d92e71";

        public const string MagusClass = "45a4607686d96a1498891b3286121780";
        public const string EldritchScionClass = "f5b8c63b141b2f44cbb8c2d7579c34f5";

        public const string SpellCombatBuff = "91e4b45ab5f29574aa1fb41da4bbdcf2";
        public const string SpellStrikeBuff = "06e0c9887eb1724409977dac7168bfd7";

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
