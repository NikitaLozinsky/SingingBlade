using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SingingBlade
{
    // Загрузка собственных иконок мода из папки Assets рядом со сборкой.
    //
    // Раньше все иконки одалживались у ванильных блюпринтов через .Icon (см. хелперы
    // ItemIcon/FactIcon) — это было надёжно, но из-за этого предмет, способность и
    // баффы мода выглядели как чужие вещи. Свои PNG грузим руками: m_Icon у
    // BlueprintItem/BlueprintUnitFact — это прямая ссылка на UnityEngine.Sprite,
    // так что достаточно собрать Sprite в рантайме, никакой возни с ассет-бандлами.
    //
    // Если файла нет или он битый — возвращаем null, и вызывающий код откатывается
    // на одолженную ванильную иконку. Отсутствие картинки не должно ронять мод.
    internal static class ModIcons
    {
        // Только баффы и способности. У САМОГО меча иконку не подменяем: он намеренно
        // переиспользует иконку и модель ванильного "Несущего веру" (см. BuildItem).
        public const string Song = "BladeSong.png";

        // Способность «Дотянуться до звёзд» и её бафф. Крылатый клинок в молниях — по
        // образу из истории клинка: заклинание, пойманное на лету и вогнанное остриём.
        // Запасные, сейчас не используются: SustainedNote.png и SustainedNoteAlt.png
        // (иконки прежнего названия "Долгая нота"), BladeSongChoir.png, SpellOnEdge.png,
        // BladeReticle.png. Имена файлов НЕ переименовывались вместе с кодом — это
        // просто картинки в Assets.
        public const string ReachForStars = "FlySword.png";

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Load(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (Cache.TryGetValue(fileName, out var cached)) return cached;

            Sprite sprite = null;
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                var path = Path.Combine(Path.Combine(dir, "Assets"), fileName);
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    // Размер (2,2) — заглушка: LoadImage сам пересоздаёт текстуру под
                    // реальные размеры PNG.
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(bytes))
                    {
                        texture.name = "SingingBlade_" + fileName;
                        sprite = Sprite.Create(
                            texture,
                            new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f));
                        sprite.name = texture.name;
                    }
                }
            }
            catch (System.Exception e)
            {
                Main.LogError("ModIcons.Load(" + fileName + ")", e);
                sprite = null;
            }

            Cache[fileName] = sprite;
            return sprite;
        }
    }
}
