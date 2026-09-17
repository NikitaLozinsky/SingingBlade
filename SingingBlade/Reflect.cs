using System;
using System.Reflection;
using Kingmaker.Blueprints;

namespace SingingBlade
{
    // Большинство полей компонентов и блюпринтов в игре объявлены как приватные
    // [SerializeField] без публичного сеттера (доступ только через .Get()-геттеры) —
    // это нормальная схема сериализации Unity, а не недосмотр разработчиков.
    // Раз мы строим блюпринты в рантайме напрямую через `new`, а не через JSON-десериализацию,
    // единственный рабочий способ выставить такие поля — точечная рефлексия.
    internal static class Reflect
    {
        public static void Set(object target, string fieldName, object value)
        {
            var type = target.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }

            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, fieldName);

            field.SetValue(target, value);
        }

        // Строит BlueprintXxxReference на блюпринт (свой новый или уже существующий в игре) по его GUID.
        public static TRef Ref<TRef>(string guid) where TRef : BlueprintReferenceBase, new()
        {
            var reference = new TRef();
            reference.ReadGuidFromGuid(BlueprintGuid.Parse(guid));
            return reference;
        }

        // "Пустая" типизированная ссылка — используется там, где поле-ссылка обязано
        // быть не-null (иначе движок упадёт на Xxx.Get() внутри самого компонента),
        // но реального ограничения нам не нужно (например Archetype в ContextRankConfig).
        public static TRef Empty<TRef>() where TRef : BlueprintReferenceBase, new()
        {
            var reference = new TRef();
            reference.ReadGuidFromGuid(BlueprintGuid.Empty);
            return reference;
        }
    }
}
