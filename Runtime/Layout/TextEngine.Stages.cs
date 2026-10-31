using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera
{
    // Effects that ask for it get called at the stages of a rebuild, and each frame for a pose. Others pay one count check per pass.
    internal sealed partial class TextEngine
    {
        readonly List<(LetteraEffect effect, int start, int end)> m_PoseHooks = new List<(LetteraEffect, int, int)>();

        void RunPoseHooks() { }
    }
}
