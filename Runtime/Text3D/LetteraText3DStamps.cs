#if UNITY_EDITOR
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public partial class LetteraText3D : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            m_Engine?.FlushPendingSource();
            LetteraStamps.Save(this, ref LetteraSerializedVersion);
        }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraSolidLook : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }
}
#endif
