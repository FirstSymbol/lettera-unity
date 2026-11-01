using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// A very long document in a scrolling view, built from code: only what is near the view is ever laid out, and a scroll inside that is no rebuild.
    /// It also searches, and jumps to what it found. Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraDocumentsSample : MonoBehaviour
    {
        [Tooltip("How many lines the generated document has.")]
        public int Lines = 200000;

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                System.Type module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) events.AddComponent(module);
                else events.AddComponent<StandaloneInputModule>();
            }

            // The scroller is the viewport, and the text fills it. Neither grows to the document's size.
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(canvasObject.transform, false);
            var viewRect = (RectTransform)view.transform;
            viewRect.anchorMin = new Vector2(0.1f, 0.1f);
            viewRect.anchorMax = new Vector2(0.9f, 0.9f);
            viewRect.sizeDelta = Vector2.zero;
            view.AddComponent<Image>().color = new Color(0, 0, 0, 0.04f);

            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(view.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textObject.AddComponent<LetteraText>();
            text.FontSize = 18;

            // A tag opened on the first line stays in force thousands of lines later, wherever the view is.
            var body = new StringBuilder("<color=#2b6cb0>");
            for (int i = 0; i < Lines; i++) body.Append("Line ").Append(i).Append(": a document this long is no harder than a short one.\n");
            LetteraRope rope = LetteraRope.FromString(body.ToString());

            // A file works the same way, and is not even decoded up front:
            // LetteraRope rope = LetteraFileSource.Load(path);
            // Or a Source Loader component, for a file, a text asset or an address, set up in the inspector.

            text.OpenDocument(rope, takeOwnership: true);
            view.AddComponent<LetteraDocumentScroller>().Text = text;

            // Search reads through the document a slice at a time, so a frame stays short.
            var search = new LetteraDocumentSearch(rope) { Query = "Line " + Lines / 2 + ":" };
            var hits = new List<LetteraSearchHit>();
            search.Start();
            while (search.Step(4, hits)) { }
            if (hits.Count > 0) text.View.ScrollToChar(hits[0].Start, LetteraRevealAlign.Center);
        }
    }
}
