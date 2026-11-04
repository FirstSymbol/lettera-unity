#if !LETTERA_NO_MATH
namespace FirstSymbol.Lettera
{
    internal sealed partial class LetteraMathParser
    {
        // Inside \ce or \pu: chemistry's arrows keep a reaction's length.
        int m_Chemistry;

        // \ce{...} and \pu{...}: rewritten into this notation and read in place, into the same tree, under the same limits.
        int Chemistry(bool quantity, int position)
        {
            string content = ReadRawGroup();
            if (Failed || content == null)
            {
                m_Tree.ErrorPosition = position;
                return -1;
            }
            string name = quantity ? "\\pu" : "\\ce";
            string source = LetteraChemistry.Translate(content, quantity, out string error, out int errorAt);
            if (source == null)
            {
                m_At = position;
                Fail(name + ": " + error);
                return -1;
            }
            if (!Enter()) return -1;
            string outer = m_Source;
            int resume = m_At;
            bool inTable = m_InTable;
            MathAlphabet alphabet = m_Alphabet;
            m_Source = source;
            m_At = 0;
            m_InTable = false;
            m_Alphabet = MathAlphabet.Default;
            m_Chemistry++;
            int list = ParseList(EndOfSource);
            if (!Failed && m_At < m_Source.Length) Fail("Something here cannot be read.");
            m_Chemistry--;
            m_Source = outer;
            m_InTable = inTable;
            m_Alphabet = alphabet;
            Leave();
            if (Failed)
            {
                // The place is the command's in what was written, and not one in the rewriting.
                if (!m_Tree.Error.StartsWith("\\")) m_Tree.Error = name + ": " + m_Tree.Error;
                m_Tree.ErrorPosition = position;
                m_At = m_Source.Length;
                return list;
            }
            m_At = resume;
            return list;
        }
    }
}
#endif
