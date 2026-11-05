using UnityEngine;
using UnityEngine.Events;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>Holds references the way a project's own script would, for the conversion tests.</summary>
    public class SwapReferenceHolder : MonoBehaviour
    {
        public Component AnyText;
        public LetteraText CanvasOnly;
        public LetteraSelectable Selectable;
        public LetteraSequencer Sequencer;
        public UnityEvent Changed = new UnityEvent();
    }
}
