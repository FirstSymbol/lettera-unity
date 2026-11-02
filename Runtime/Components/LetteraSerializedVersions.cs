using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// The format saved objects are written in. Each text, style and asset holds it as <c>LetteraSerializedVersion</c>, and 0 means saved before there was such a field.
    /// It lets a later version upgrade from a known point: see <c>LetteraUpgrades.Register</c>, in the editor. A build only carries the number.
    /// </summary>
    public static class LetteraSerializedVersions
    {
        public const int Current = 1;
    }

#if UNITY_EDITOR
    internal interface ILetteraStamped
    {
        int Version { get; set; }
    }

    /// <summary>
    /// Who is older than <see cref="LetteraSerializedVersions.Current"/>. Unity keeps a field's initial value when a file lacks the field, so a stamp can only be written when saving,
    /// and an object that came from an older file must not get one before its upgrades ran. Unity may call these from any thread.
    /// </summary>
    internal static class LetteraStamps
    {
        static readonly object s_Lock = new object();
        // By reference: a Unity object still loading has no instance id to compare by.
        sealed class Same : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object a, object b) => ReferenceEquals(a, b);
            int IEqualityComparer<object>.GetHashCode(object a) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
        }

        static readonly HashSet<object> s_Older = new HashSet<object>(new Same());
        static readonly List<ILetteraStamped> s_Queue = new List<ILetteraStamped>();
        static volatile bool s_Queued;

        internal static bool Queued => s_Queued;

        internal static void Load(ILetteraStamped target, int version)
        {
            if (version >= LetteraSerializedVersions.Current) return;
            lock (s_Lock)
            {
                if (!s_Older.Add(target)) return;
                s_Queue.Add(target);
                s_Queued = true;
            }
        }

        internal static void Save(ILetteraStamped target, ref int version)
        {
            if (version == LetteraSerializedVersions.Current) return;
            lock (s_Lock)
                if (!s_Older.Contains(target))
                    version = LetteraSerializedVersions.Current;
        }

        internal static ILetteraStamped[] Take()
        {
            lock (s_Lock)
            {
                ILetteraStamped[] taken = s_Queue.ToArray();
                s_Queue.Clear();
                s_Queued = false;
                return taken;
            }
        }

        /// <summary>Its upgrades ran, so saving it writes the stamp.</summary>
        internal static void Done(ILetteraStamped target)
        {
            lock (s_Lock) s_Older.Remove(target);
        }
    }

    public partial class LetteraFontAsset : ILetteraStamped
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
    }

    public partial class LetteraSystemFont : ISerializationCallbackReceiver
    {
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraFontVariant : ISerializationCallbackReceiver
    {
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraText : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // A field's edit in play is written out before anything reads the saved text.
            m_Engine?.FlushPendingSource();
            LetteraStamps.Save(this, ref LetteraSerializedVersion);
        }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraWorldText : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // A field's edit in play is written out before anything reads the saved text.
            m_Engine?.FlushPendingSource();
            LetteraStamps.Save(this, ref LetteraSerializedVersion);
        }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraStyle : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraSettings : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraWordDictionary : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraFieldPreset : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraFontStack : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraInlineLibrary : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraRangeTopic : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraEntranceLibrary : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraEffectPreset : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraPaintLibrary : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }

    public partial class LetteraStylePreset : ILetteraStamped, ISerializationCallbackReceiver
    {
        int ILetteraStamped.Version { get => LetteraSerializedVersion; set => LetteraSerializedVersion = value; }
        void ISerializationCallbackReceiver.OnBeforeSerialize() => LetteraStamps.Save(this, ref LetteraSerializedVersion);
        void ISerializationCallbackReceiver.OnAfterDeserialize() => LetteraStamps.Load(this, LetteraSerializedVersion);
    }
#endif
}
