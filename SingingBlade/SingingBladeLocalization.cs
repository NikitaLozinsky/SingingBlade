using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;

namespace SingingBlade
{
    // Ключи строк, которые лежат в Localization.json рядом со сборкой.
    internal static class L
    {
        public const string ItemName = "SingingBlade.Item.Name";
        public const string ItemDescription = "SingingBlade.Item.Description";
        public const string AbilityName = "SingingBlade.Ability.Name";
        public const string AbilityDescription = "SingingBlade.Ability.Description";
        public const string SongBuffName = "SingingBlade.SongBuff.Name";
        public const string SongBuffDescription = "SingingBlade.SongBuff.Description";
        public const string SongBuffEmpoweredName = "SingingBlade.SongBuffEmpowered.Name";
        public const string SongBuffEmpoweredDescription = "SingingBlade.SongBuffEmpowered.Description";
        public const string EnchantmentName = "SingingBlade.Enchantment.Name";
        public const string EnchantmentDescription = "SingingBlade.Enchantment.Description";
        public const string ItemFlavorText = "SingingBlade.Item.FlavorText";
        public const string SungThisRoundFlagName = "SingingBlade.SungThisRoundFlag.Name";
        public const string SungThisRoundFlagDescription = "SingingBlade.SungThisRoundFlag.Description";
    }

    internal static class SingingBladeLocalization
    {
        private const string FallbackLocale = "ruRU";

        private static Dictionary<string, Dictionary<string, string>> _stringsByLocale;

        // Подмешиваем свои строки в любой языковой пак, который грузит игра —
        // и при старте, и при переключении языка в настройках.
        public static void Apply(LocalizationPack pack, Locale locale)
        {
            var strings = GetStrings();
            if (strings == null) return;

            if (!strings.TryGetValue(locale.ToString(), out var localeStrings))
            {
                strings.TryGetValue(FallbackLocale, out localeStrings);
            }

            if (localeStrings == null) return;

            foreach (var pair in localeStrings)
            {
                pack.PutString(pair.Key, pair.Value);
            }
        }

        // Создаёт LocalizedString, указывающую на наш ключ (без Shared-строки —
        // так же, как это сделано у ванильных уникальных предметов вроде Faith Bearer).
        public static LocalizedString CreateString(string key)
        {
            return new LocalizedString { Key = key };
        }

        // Поля записи, которые не являются кодом локали (остальные ключи каждого
        // объекта в "LocalizedStrings" — это коды локалей: ruRU, enGB, ...).
        private static readonly HashSet<string> NonLocaleFields = new HashSet<string> { "Key", "SimpleName", "ProcessTemplates" };

        // Разбирает формат { "LocalizedStrings": [ { "Key": "...", "ruRU": "...", "enGB": "...", ... }, ... ] }
        // (тот же, что использует мод AbilityPanelResize) и сворачивает его в
        // {локаль: {ключ: текст}} — дальше используется как и раньше.
        private static Dictionary<string, Dictionary<string, string>> GetStrings()
        {
            if (_stringsByLocale != null) return _stringsByLocale;

            _stringsByLocale = new Dictionary<string, Dictionary<string, string>>();

            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            var path = Path.Combine(dir, "Localization.json");
            if (!File.Exists(path)) return _stringsByLocale;

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) return _stringsByLocale;
            if (!root.TryGetValue("LocalizedStrings", out var entriesObj)) return _stringsByLocale;
            if (!(entriesObj is List<object> entries)) return _stringsByLocale;

            foreach (var entryObj in entries)
            {
                if (!(entryObj is Dictionary<string, object> entry)) continue;
                if (!entry.TryGetValue("Key", out var keyObj) || !(keyObj is string key)) continue;

                foreach (var field in entry)
                {
                    if (NonLocaleFields.Contains(field.Key)) continue;
                    if (!(field.Value is string text)) continue;

                    if (!_stringsByLocale.TryGetValue(field.Key, out var localeMap))
                    {
                        localeMap = new Dictionary<string, string>();
                        _stringsByLocale[field.Key] = localeMap;
                    }
                    localeMap[key] = text;
                }
            }

            return _stringsByLocale;
        }
    }
}
