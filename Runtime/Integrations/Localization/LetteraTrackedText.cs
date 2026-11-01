using System;
using UnityEngine.Localization;
using UnityEngine.Localization.PropertyVariants;
using UnityEngine.Localization.PropertyVariants.TrackedObjects;

namespace FirstSymbol.Lettera.Localization
{
    /// <summary>What a Game Object Localizer's variants of a text need: Unity writes the fields through JSON with no setter, and the text then looks at what changed.</summary>
    public abstract class LetteraTrackedTextBase : JsonSerializerTrackedObject
    {
        protected override void PostApplyTrackedProperties()
        {
            if (Target is ITextComponent text) text.SharedEngine.SyncSerializedFields();
            if (Target is UnityEngine.UI.Graphic graphic) graphic.SetAllDirty();
            base.PostApplyTrackedProperties();
        }
    }

    /// <summary>Text, font, stack, size, colour, alignment and padding of a Canvas text, per locale.</summary>
    [Serializable]
    [DisplayName("Lettera Text")]
    [CustomTrackedObject(typeof(LetteraText), true)]
    public class LetteraTrackedText : LetteraTrackedTextBase
    {
    }

    /// <summary>The same for a world text.</summary>
    [Serializable]
    [DisplayName("Lettera World Text")]
    [CustomTrackedObject(typeof(LetteraWorldText), true)]
    public class LetteraTrackedWorldText : LetteraTrackedTextBase
    {
    }
}
