using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraPathRoot { StreamingAssets, PersistentData, Data, TemporaryCache, Absolute }

    /// <summary>Told to a source while it loads.</summary>
    public sealed class LetteraLoadContext
    {
        /// <summary>From 0 to 1, as far as the source can say.</summary>
        public float Progress { get; set; }

        /// <summary>How long a source may hold a frame before it should give the frame back, in milliseconds.</summary>
        public float SliceMs { get; set; } = 4;
    }

    /// <summary>Where a document comes from. Subclass it for your own: it shows in the loader's picker.</summary>
    [Serializable]
    public abstract class LetteraTextSource
    {
        /// <summary>True when there is something to load. A source with no path or asset loads nothing, and that is no failure.</summary>
        public abstract bool HasContent { get; }

        /// <summary>
        /// Loads over as many frames as it needs. Yield to give a frame back, and set <paramref name="done"/> at the end.
        /// Throw to fail: the reason reaches <see cref="LetteraSourceLoader.Failed"/>, and the text keeps what it showed.
        /// </summary>
        public abstract IEnumerator Load(LetteraLoadContext context, CancellationToken cancel, Action<LetteraRope> done);
    }

    /// <summary>A file, under one of the places a player can reach.</summary>
    [Serializable]
    public class LetteraPathSource : LetteraTextSource
    {
        public LetteraPathRoot Root;
        public string Path = "";
        public LetteraFileEncoding Encoding;
        public LetteraFileBackend Backend;

        public override bool HasContent => !string.IsNullOrEmpty(Path);

        public string FullPath
        {
            get
            {
                switch (Root)
                {
                    case LetteraPathRoot.StreamingAssets: return Combine(Application.streamingAssetsPath);
                    case LetteraPathRoot.PersistentData: return Combine(Application.persistentDataPath);
                    case LetteraPathRoot.Data: return Combine(Application.dataPath);
                    case LetteraPathRoot.TemporaryCache: return Combine(Application.temporaryCachePath);
                    default: return Path;
                }
            }
        }

        string Combine(string root) => root.TrimEnd('/', '\\') + "/" + Path.TrimStart('/', '\\');

        /// <summary>Inside an APK and on the web, streaming assets are addresses, not files.</summary>
        public bool IsUrl => FullPath.Contains("://");

        public override IEnumerator Load(LetteraLoadContext context, CancellationToken cancel, Action<LetteraRope> done)
        {
            if (IsUrl)
            {
                IEnumerator url = LetteraUrlLoading.Fetch(FullPath, null, context, cancel, done);
                while (url.MoveNext()) yield return url.Current;
                yield break;
            }
            yield return null;
            cancel.ThrowIfCancellationRequested();
            done(LetteraFileSource.Load(FullPath, new LetteraFileOptions { Encoding = Encoding, Backend = Backend }));
        }
    }

    /// <summary>A text asset, for documents up to a few megabytes.</summary>
    [Serializable]
    public class LetteraTextAssetSource : LetteraTextSource
    {
        public TextAsset Asset;

        public override bool HasContent => Asset != null;

        public override IEnumerator Load(LetteraLoadContext context, CancellationToken cancel, Action<LetteraRope> done)
        {
            yield return null;
            cancel.ThrowIfCancellationRequested();
            done(LetteraRope.FromUtf8(Asset.bytes));
        }
    }

    /// <summary>A file on the network. It needs Unity's web request module, and says so if that is off.</summary>
    [Serializable]
    public class LetteraUrlTextSource : LetteraTextSource
    {
        public string Url = "";

        public override bool HasContent => !string.IsNullOrEmpty(Url);

        public override IEnumerator Load(LetteraLoadContext context, CancellationToken cancel, Action<LetteraRope> done) => LetteraUrlLoading.Fetch(Url, null, context, cancel, done);
    }

    internal static class LetteraUrlLoading
    {
        internal static IEnumerator Fetch(string url, string[] headers, LetteraLoadContext context, CancellationToken cancel, Action<LetteraRope> done)
        {
#if LETTERA_WEBREQUEST
            using UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(url);
            UnityEngine.Networking.UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancel.IsCancellationRequested)
                {
                    request.Abort();
                    cancel.ThrowIfCancellationRequested();
                }
                context.Progress = request.downloadProgress;
                yield return null;
            }
            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success) throw new IOException($"Lettera: could not load '{url}': {request.error}");
            done(LetteraRope.FromUtf8(request.downloadHandler.data));
#else
            yield return null;
            throw new NotSupportedException($"Lettera: loading '{url}' needs Unity's Web Request module, which this project has turned off.");
#endif
        }
    }

    /// <summary>Loads a source into the text on this object. A load that fails leaves the text as it was.</summary>
    [AddComponentMenu("Lettera/Source Loader")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class LetteraSourceLoader : MonoBehaviour, IMotionClient
    {
        [SerializeReference] LetteraTextSource m_Source;
        [SerializeField] bool m_LoadOnEnable = true;

        IEnumerator m_Running;
        CancellationTokenSource m_Cancel;
        LetteraLoadContext m_Context;
        LetteraRope m_Result;
        bool m_InHub;

        public LetteraTextSource Source { get => m_Source; set => m_Source = value; }
        public bool LoadOnEnable { get => m_LoadOnEnable; set => m_LoadOnEnable = value; }
        public bool IsLoading => m_Running != null;
        public float Progress => m_Context?.Progress ?? 0;
        public string LastError { get; private set; }

        public event Action Loaded;
        public event Action<string> Failed;

        void OnEnable()
        {
            if (m_LoadOnEnable) Reload();
        }

        void OnDisable() => Cancel();

        /// <summary>Loads again. A load still running is cancelled, and only the last one reports.</summary>
        public void Reload()
        {
            Cancel();
            if (m_Source == null || !m_Source.HasContent) return;
            LastError = null;
            m_Cancel = new CancellationTokenSource();
            m_Context = new LetteraLoadContext();
            m_Result = null;
            m_Running = m_Source.Load(m_Context, m_Cancel.Token, rope => m_Result = rope);
            if (!m_InHub)
            {
                m_InHub = true;
                LetteraMotionHub.Add(this);
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.update -= StepInEditor;
                UnityEditor.EditorApplication.update += StepInEditor;
            }
#endif
        }

        /// <summary>Stops a running load. Nothing is called afterwards, and what it had read so far is let go.</summary>
        public void Cancel()
        {
            if (m_Running == null) return;
            m_Cancel?.Cancel();
            m_Cancel?.Dispose();
            m_Cancel = null;
            m_Running = null;
            m_Result?.Dispose();
            m_Result = null;
        }

#if UNITY_EDITOR
        void StepInEditor()
        {
            if (this == null || !Step())
                UnityEditor.EditorApplication.update -= StepInEditor;
        }
#endif

        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            m_InHub = this != null && Step();
            return m_InHub;
        }

        /// <summary>For tests: takes one step of a running load.</summary>
        internal bool Step()
        {
            if (m_Running == null) return false;
            bool more;
            try
            {
                more = m_Running.MoveNext();
            }
            catch (OperationCanceledException)
            {
                m_Running = null;
                return false;
            }
            catch (Exception e)
            {
                m_Running = null;
                LastError = e.Message;
                Failed?.Invoke(e.Message);
                return false;
            }
            if (more) return true;
            m_Running = null;
            Finish();
            return false;
        }

        void Finish()
        {
            LetteraRope rope = m_Result;
            m_Result = null;
            if (rope == null) return;
            m_Context.Progress = 1;
            var canvas = GetComponent<LetteraText>();
            if (canvas != null) canvas.OpenDocument(rope, true);
            else
            {
                var world = GetComponent<LetteraWorldText>();
                if (world != null) world.OpenDocument(rope, true);
                else rope.Dispose();
            }
            Loaded?.Invoke();
        }
    }
}
