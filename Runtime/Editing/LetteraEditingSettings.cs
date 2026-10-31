using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Whose menu shows.</summary>
    public enum LetteraMenuChoice { LetteraDrawn, System }

    /// <summary>Whose loupe shows.</summary>
    public enum LetteraLoupeChoice { MeshLoupe, System }

    public enum LetteraDeleteBackMode { Cluster, Codepoint }

    /// <summary>The selection and editing section of the project settings.</summary>
    [Serializable]
    public class LetteraEditingSettings
    {
        [Header("Caret")]
        [Min(0)] public float CaretBlinkInterval = 0.53f;
        [Tooltip("After this long without input the caret stops blinking and stays on.")]
        [Min(0)] public float CaretBlinkTimeout = 20f;
        [Tooltip("Percent of the line height.")]
        [Min(0)] public float CaretWidthPercent = 6f;

        [Header("History")]
        [Min(0)] public float UndoMergeWindow = 0.5f;
        [Tooltip("Bytes of stored text; 0 means no limit.")]
        [Min(0)] public int UndoMemoryLimit = 1024 * 1024;
        public LetteraDeleteBackMode DeleteBackMode;

        [Header("Gestures")]
        [Min(0)] public float MultiClickSeconds = 0.5f;
        [Min(0)] public float MultiClickSlop = 8f;
        [Min(0)] public float LongPressSeconds = 0.5f;
        [Min(0)] public float DragSlop = 10f;
        [Min(0)] public float MultiTapSeconds = 0.3f;
        [Min(0)] public float MultiTapSlop = 100f;

        [Header("Look")]
        public Color DefaultHighlightColor = new Color(0.2f, 0.565f, 1f, 0.4f);
        public LetteraImeLook ImeLook = new LetteraImeLook();

        [Header("Clipboard")]
        [Min(0)] public int PasteHtmlMaxBytes = 4 * 1024 * 1024;

        [Header("Commands")]
        [Tooltip("What a text's context menu offers when its list names the project's.")]
        [SerializeReference] public List<LetteraEditCommand> Commands = LetteraEditCommand.DefaultProjectList();

        [Header("Prefabs")]
        public LetteraMenuPresenter MenuPrefab;

        [Tooltip("Whose menu shows on Android. System needs the System Menu gate on, and runs Lettera's commands all the same.")]
        public LetteraMenuChoice AndroidMenu;

        [Tooltip("Whose menu shows on iOS. System needs the System Menu gate on, and iOS 16.")]
        public LetteraMenuChoice IosMenu;

        [Tooltip("Whose loupe shows on a phone. System needs the System Loupe gate on, and Android 9 or iOS 17.")]
        public LetteraLoupeChoice Loupe;
        public GameObject HandlesPrefab;
        public GameObject SelectablePrefab;
        public GameObject EditablePrefab;
        public GameObject InputFieldPrefab;
        public GameObject TextAreaPrefab;
        public GameObject PasswordFieldPrefab;
        public GameObject DropdownPrefab;

        /// <summary>A length in density-independent units, in pixels.</summary>
        public static float Dp(float value) => Screen.dpi > 0 ? value * Screen.dpi / 160f : value;
    }

    /// <summary>The underlines of a composition.</summary>
    [Serializable]
    public class LetteraImeLook
    {
        public Color Color = new Color(1, 1, 1, 0.9f);
        [Min(0)] public float Thickness = 1f;
        [Min(0)] public float ActiveThickness = 2f;
    }
}
