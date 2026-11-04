using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Runs <see cref="LetteraStartUp"/>'s steps in a player's first frames, before their canvas update, and goes when they are done.</summary>
    [AddComponentMenu("")]
    internal sealed class LetteraStartUpDriver : MonoBehaviour
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        void LateUpdate()
        {
            if (!LetteraStartUp.Tick()) Destroy(gameObject);
        }
#endif
    }
}
