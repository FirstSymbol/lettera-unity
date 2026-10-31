package com.firstsymbol.lettera;

import java.util.concurrent.ConcurrentLinkedQueue;

/** What a text control reports, as lines the C# side reads once a frame. Nothing here touches Android, so it can run on a plain JVM. */
public final class LetteraMirrorCore {
    private final ConcurrentLinkedQueue<String> queue = new ConcurrentLinkedQueue<>();
    private volatile int revision;
    private String reported = "";

    public void setRevision(int value) { revision = value; }

    public void setReported(String text) { reported = text == null ? "" : text; }

    public String next() { return queue.poll(); }

    public static String escape(String text) {
        if (text == null) return "";
        StringBuilder out = new StringBuilder(text.length() + 4);
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            switch (c) {
                case '\\': out.append("\\\\"); break;
                case '\t': out.append("\\t"); break;
                case '\n': out.append("\\n"); break;
                case '\r': out.append("\\r"); break;
                default: out.append(c);
            }
        }
        return out.toString();
    }

    private void line(char kind, int a, int b, int caret, int flag, int key, int modifiers, String text) {
        queue.add(kind + "\t" + revision + "\t" + a + "\t" + b + "\t" + caret + "\t" + flag + "\t" + key + "\t" + modifiers + "\t" + escape(text));
    }

    /** An exact edit, as a filter sees it. The text it leaves is remembered for the comparison after. */
    public void replace(int start, int end, String text) {
        String safe = text == null ? "" : text;
        int from = Math.max(0, Math.min(start, reported.length())), to = Math.max(from, Math.min(end, reported.length()));
        reported = reported.substring(0, from) + safe + reported.substring(to);
        line('R', start, end, -1, 0, 0, 0, safe);
    }

    /** What the control holds after a change. Only what no filter reported goes out, as the whole text. */
    public void afterChange(String now, int caret) {
        String text = now == null ? "" : now;
        if (text.equals(reported)) return;
        reported = text;
        line('W', -1, -1, caret, 0, 0, 0, text);
    }

    public void compose(int start, int end, String text, int caret) { line('C', start, end, caret, 0, 0, 0, text); }
    public void composeEnd(boolean commit) { line('E', -1, -1, -1, commit ? 1 : 0, 0, 0, ""); }
    public void select(int start, int end) { line('S', start, end, -1, 0, 0, 0, ""); }
    public void verb(String name) { line('V', -1, -1, -1, 0, 0, 0, name); }
    public void key(int code, int modifiers, boolean repeat) { line('K', -1, -1, -1, repeat ? 1 : 0, code, modifiers, ""); }
    public void keyboardFrame(int x, int y, int width, int height) { line('F', x, y, width, height, 0, 0, ""); }
    public void closed() { line('X', -1, -1, -1, 0, 0, 0, ""); }

    /** What a plain JVM can check. */
    public static void main(String[] args) {
        LetteraMirrorCore core = new LetteraMirrorCore();
        core.setRevision(3);
        core.setReported("abc");
        core.replace(1, 2, "XY");
        expect(core.next(), "R\t3\t1\t2\t-1\t0\t0\t0\tXY");
        core.afterChange("aXYc", 3);
        expect(core.next(), null);
        core.afterChange("aXYcd", 5);
        expect(core.next(), "W\t3\t-1\t-1\t5\t0\t0\t0\taXYcd");
        core.verb("submit");
        expect(core.next(), "V\t3\t-1\t-1\t-1\t0\t0\t0\tsubmit");
        expect(escape("a\tb\nc\\"), "a\\tb\\nc\\\\");
        core.key(7, 2, true);
        expect(core.next(), "K\t3\t-1\t-1\t-1\t1\t7\t2\t");
        core.composeEnd(true);
        expect(core.next(), "E\t3\t-1\t-1\t-1\t1\t0\t0\t");
        System.out.println("LETTERA_JVM_OK");
    }

    private static void expect(String got, String want) {
        boolean same = got == null ? want == null : got.equals(want);
        if (!same) throw new AssertionError("got [" + got + "] want [" + want + "]");
    }
}
