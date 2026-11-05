using UnityEngine;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>A project's component that needs world text by name, so that a conversion to Canvas text must refuse.</summary>
    [RequireComponent(typeof(LetteraWorldText))]
    public class SwapNeedsWorldText : MonoBehaviour
    {
    }
}
