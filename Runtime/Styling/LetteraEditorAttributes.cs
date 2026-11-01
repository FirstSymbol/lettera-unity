using System;

namespace FirstSymbol.Lettera
{
    /// <summary>A field a new style shows straight away in the inspector. The others wait behind + Parameter until they differ from their default.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LetteraEssentialAttribute : Attribute { }

    /// <summary>Where a custom effect stands in the style picker, and the words that find it.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class LetteraPickerAttribute : Attribute
    {
        public string Group { get; }
        public string Words { get; }

        public LetteraPickerAttribute(string group, string words = "")
        {
            Group = group;
            Words = words;
        }
    }
}
