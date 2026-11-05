package com.firstsymbol.lettera;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipDescription;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.graphics.Rect;
import android.os.Build;
import android.provider.Settings;
import android.text.InputType;
import android.view.ActionMode;
import android.view.KeyEvent;
import android.view.Menu;
import android.view.MenuItem;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.widget.FrameLayout;
import android.widget.Magnifier;
import java.util.List;
import java.util.concurrent.ConcurrentLinkedQueue;

/** What C# calls. It maps platform calls to events and back, and decides nothing. Everything that touches a view runs on the UI thread. */
public final class LetteraBridge {
    private static final LetteraMirrorCore core = new LetteraMirrorCore();
    private static final ConcurrentLinkedQueue<Integer> menuChoices = new ConcurrentLinkedQueue<>();
    private static LetteraHiddenField field;
    private static Magnifier magnifier;
    private static ActionMode actionMode;

    private LetteraBridge() { }

    public static String next() { return core.next(); }

    public static boolean isGameActivity(Activity activity) { return !activity.getClass().getName().endsWith("UnityPlayerActivity"); }

    public static void open(final Activity activity, final String text, final int start, final int end, final int revision, final int inputType, final int imeOptions, final String autofill) {
        activity.runOnUiThread(() -> {
            core.setRevision(revision);
            core.setReported(text);
            if (field == null) {
                field = new LetteraHiddenField(activity, core);
                field.setAlpha(0f);
                FrameLayout.LayoutParams layout = new FrameLayout.LayoutParams(1, 1);
                ViewGroup content = activity.findViewById(android.R.id.content);
                content.addView(field, layout);
                field.setOnEditorActionListener((view, action, event) -> {
                    core.verb(verbOf(action));
                    return true;
                });
                field.setOnKeyListener((view, code, event) -> {
                    if (event.getAction() != KeyEvent.ACTION_DOWN) return false;
                    if (code == KeyEvent.KEYCODE_BACK) {
                        core.verb("done");
                        return false;
                    }
                    int mapped = mapKey(code);
                    if (mapped == 0) return false;
                    core.key(mapped, (event.isShiftPressed() ? 1 : 0) | (event.isCtrlPressed() ? 2 : 0) | (event.isAltPressed() ? 4 : 0), event.getRepeatCount() > 0);
                    return true;
                });
            }
            field.pushing = true;
            field.setInputType(inputType);
            field.setImeOptions(imeOptions);
            if (Build.VERSION.SDK_INT >= 26 && autofill != null && !autofill.isEmpty()) field.setAutofillHints(autofill);
            field.setText(text);
            field.setSelection(clamp(start, text), clamp(end, text));
            field.pushing = false;
            field.requestFocus();
            InputMethodManager keyboard = (InputMethodManager) activity.getSystemService(Context.INPUT_METHOD_SERVICE);
            keyboard.showSoftInput(field, 0);
        });
    }

    private static int clamp(int at, String text) { return Math.max(0, Math.min(at, text.length())); }

    public static void push(Activity activity, final String text, final int start, final int end, final int revision) {
        activity.runOnUiThread(() -> {
            if (field == null) return;
            core.setRevision(revision);
            core.setReported(text);
            field.pushing = true;
            if (!text.contentEquals(field.getText())) field.setText(text);
            field.setSelection(clamp(start, text), clamp(end, text));
            field.pushing = false;
        });
    }

    public static void close(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (field == null) return;
            InputMethodManager keyboard = (InputMethodManager) activity.getSystemService(Context.INPUT_METHOD_SERVICE);
            keyboard.hideSoftInputFromWindow(field.getWindowToken(), 0);
            ViewGroup parent = (ViewGroup) field.getParent();
            if (parent != null) parent.removeView(field);
            field = null;
            View surface = activity.getWindow().getDecorView();
            surface.requestFocus();
        });
    }

    /** Pixels, top left. Only so that a candidate strip or a magnifier knows where the caret stands. */
    public static void place(Activity activity, final int x, final int y) {
        activity.runOnUiThread(() -> {
            if (field == null) return;
            field.setX(x);
            field.setY(y);
        });
    }

    private static String verbOf(int action) {
        switch (action) {
            case EditorInfo.IME_ACTION_NEXT: return "next";
            case EditorInfo.IME_ACTION_PREVIOUS: return "previous";
            case EditorInfo.IME_ACTION_DONE: return "done";
            case EditorInfo.IME_ACTION_GO:
            case EditorInfo.IME_ACTION_SEARCH:
            case EditorInfo.IME_ACTION_SEND: return "submit";
            default: return "return";
        }
    }

    /** The same numbers as LetteraKeyCode, in C#. */
    private static int mapKey(int code) {
        switch (code) {
            case KeyEvent.KEYCODE_DPAD_LEFT: return 7;
            case KeyEvent.KEYCODE_DPAD_RIGHT: return 8;
            case KeyEvent.KEYCODE_DPAD_UP: return 9;
            case KeyEvent.KEYCODE_DPAD_DOWN: return 10;
            case KeyEvent.KEYCODE_MOVE_HOME: return 11;
            case KeyEvent.KEYCODE_MOVE_END: return 12;
            case KeyEvent.KEYCODE_PAGE_UP: return 13;
            case KeyEvent.KEYCODE_PAGE_DOWN: return 14;
            case KeyEvent.KEYCODE_TAB: return 5;
            case KeyEvent.KEYCODE_ESCAPE: return 6;
            default: return 0;
        }
    }

    /** Input type from plain numbers, so the mapping can be tested in C# against these constants. */
    public static int constant(String name) {
        switch (name) {
            case "TYPE_CLASS_TEXT": return InputType.TYPE_CLASS_TEXT;
            case "TYPE_CLASS_NUMBER": return InputType.TYPE_CLASS_NUMBER;
            case "TYPE_CLASS_PHONE": return InputType.TYPE_CLASS_PHONE;
            case "TYPE_TEXT_VARIATION_PASSWORD": return InputType.TYPE_TEXT_VARIATION_PASSWORD;
            case "TYPE_TEXT_VARIATION_URI": return InputType.TYPE_TEXT_VARIATION_URI;
            case "TYPE_TEXT_VARIATION_EMAIL_ADDRESS": return InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS;
            case "TYPE_TEXT_FLAG_MULTI_LINE": return InputType.TYPE_TEXT_FLAG_MULTI_LINE;
            default: return 0;
        }
    }

    public static boolean reducedMotion(Activity activity) {
        return Settings.Global.getFloat(activity.getContentResolver(), Settings.Global.ANIMATOR_DURATION_SCALE, 1f) == 0f;
    }

    // ------------------------------------------------------------ clipboard

    public static boolean clipWrite(Activity activity, String plain, String html) {
        ClipboardManager manager = (ClipboardManager) activity.getSystemService(Context.CLIPBOARD_SERVICE);
        if (manager == null) return false;
        manager.setPrimaryClip(html != null && !html.isEmpty() ? ClipData.newHtmlText("Lettera", plain, html) : ClipData.newPlainText("Lettera", plain));
        return true;
    }

    /** Whether something is there, without reading it: reading is what shows the system's notice. */
    public static int clipProbe(Activity activity) {
        ClipboardManager manager = (ClipboardManager) activity.getSystemService(Context.CLIPBOARD_SERVICE);
        if (manager == null) return 0;
        if (!manager.hasPrimaryClip()) return 1;
        ClipDescription description = manager.getPrimaryClipDescription();
        if (description == null) return 0;
        if (description.hasMimeType(ClipDescription.MIMETYPE_TEXT_HTML)) return 3;
        if (description.hasMimeType(ClipDescription.MIMETYPE_TEXT_PLAIN)) return 2;
        if (description.hasMimeType("image/*")) return 4;
        return 0;
    }

    /** Only ever called for an explicit paste. */
    public static String clipRead(Activity activity, boolean html) {
        ClipboardManager manager = (ClipboardManager) activity.getSystemService(Context.CLIPBOARD_SERVICE);
        if (manager == null || !manager.hasPrimaryClip()) return null;
        ClipData clip = manager.getPrimaryClip();
        if (clip == null || clip.getItemCount() == 0) return null;
        ClipData.Item item = clip.getItemAt(0);
        if (html) return item.getHtmlText();
        CharSequence text = item.coerceToText(activity);
        return text != null ? text.toString() : null;
    }

    // ------------------------------------------------------------ the system's menu

    /** Where other apps' entries go in the system's menu: after all of ours. */
    private static final int LATER = 1000;

    /** Counts menus shown, on the UI thread, so that a menu no longer shown is not heard. */
    private static volatile int menuGeneration;

    /** Our entries' ids, apart from any the system or another app uses, Menu.NONE among them. */
    private static final int FIRST_ID = 0x4C450000;

    public static boolean showMenu(final Activity activity,final int left, final int top, final int right, final int bottom, final String[] titles, final boolean[] enabled) {
        if (Build.VERSION.SDK_INT < 23) return false;
        // Only the menu shown last speaks. A menu that ends after a choice says so too, and that word, left in the queue, would read to the
        // next menu as its own dismissal: the next choice would then go unheard. Both happen here, before the UI thread runs, since C# reads
        // the queue from its first frame on.
        final int generation = ++menuGeneration;
        menuChoices.clear();
        activity.runOnUiThread(() -> {
            if (generation != menuGeneration) return;
            if (actionMode != null) {
                ActionMode old = actionMode;
                actionMode = null;
                old.finish();
            }
            View anchor = field != null ? field : activity.getWindow().getDecorView();
            actionMode = anchor.startActionMode(new ActionMode.Callback2() {
                @Override public boolean onCreateActionMode(ActionMode mode, Menu menu) {
                    for (int i = 0; i < titles.length; i++) menu.add(Menu.NONE, FIRST_ID + i, i, titles[i]).setEnabled(enabled[i]);
                    return true;
                }
                // The system adds other apps' entries before ours. Those that only start an app go after ours, so that Paste and Copy come
                // first as in a plain field; an entry the system handles some other way is left where it is.
                @Override public boolean onPrepareActionMode(ActionMode mode, Menu menu) {
                    boolean moved = false;
                    for (int i = menu.size() - 1; i >= 0; i--) {
                        MenuItem item = menu.getItem(i);
                        int id = item.getItemId();
                        if (isOurs(id) || item.getIntent() == null || item.getOrder() >= LATER) continue;
                        CharSequence title = item.getTitle();
                        Intent intent = item.getIntent();
                        android.graphics.drawable.Drawable icon = item.getIcon();
                        int group = item.getGroupId(), order = item.getOrder();
                        menu.removeItem(id);
                        menu.add(group, id, LATER + order, title).setIntent(intent).setIcon(icon);
                        moved = true;
                    }
                    return moved;
                }
                private boolean isOurs(int id) { return id >= FIRST_ID && id < FIRST_ID + titles.length; }
                @Override public boolean onActionItemClicked(ActionMode mode, MenuItem item) {
                    // Another app's entry is the system's to run, never one of ours by its number.
                    if (!isOurs(item.getItemId())) return false;
                    if (generation == menuGeneration) menuChoices.add(item.getItemId() - FIRST_ID);
                    mode.finish();
                    return true;
                }
                @Override public void onDestroyActionMode(ActionMode mode) {
                    if (generation != menuGeneration) return;
                    actionMode = null;
                    menuChoices.add(-1);
                }
                // The rect comes in the game view's pixels, and Android wants it in the anchor's own: the anchor is a one-pixel field at the caret,
                // so without this the menu stood off the screen.
                @Override public void onGetContentRect(ActionMode mode, View view, Rect out) {
                    int[] game = new int[2], at = new int[2];
                    View content = activity.findViewById(android.R.id.content);
                    if (content != null) content.getLocationOnScreen(game);
                    view.getLocationOnScreen(at);
                    int dx = game[0] - at[0], dy = game[1] - at[1];
                    out.set(left + dx, top + dy, right + dx, bottom + dy);
                }
            }, ActionMode.TYPE_FLOATING);
            if (actionMode == null) {
                android.util.Log.w("Lettera", "The system gave no floating menu");
                menuChoices.add(-1);
            }
        });
        return true;
    }

    public static void hideMenu(Activity activity) {
        activity.runOnUiThread(() -> {
            if (actionMode != null) actionMode.finish();
        });
    }

    /** What was chosen: an entry, -1 when the menu went away, or -2 for nothing yet. */
    public static int nextMenuChoice() {
        Integer value = menuChoices.poll();
        return value != null ? value : -2;
    }

    /** Apps that offer to work on selected text. */
    public static String[] textApps(Activity activity) {
        Intent intent = new Intent(Intent.ACTION_PROCESS_TEXT).setType("text/plain");
        PackageManager packages = activity.getPackageManager();
        List<ResolveInfo> found = packages.queryIntentActivities(intent, 0);
        String[] labels = new String[found.size()];
        for (int i = 0; i < labels.length; i++) labels[i] = found.get(i).loadLabel(packages).toString();
        return labels;
    }

    public static void share(Activity activity, String text) {
        Intent send = new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text);
        activity.startActivity(Intent.createChooser(send, null));
    }

    // ------------------------------------------------------------ the system's loupe

    public static boolean loupeShow(final Activity activity, final float x, final float y) {
        if (Build.VERSION.SDK_INT < 28) return false;
        activity.runOnUiThread(() -> {
            // The game draws into a surface of its own, which a loupe on the window cannot see: it showed an empty plate. A loupe on that
            // surface magnifies what the game drew. Positions come in the game view's pixels, which is that surface's.
            if (magnifier == null) {
                View content = activity.findViewById(android.R.id.content);
                View surface = findSurface(content);
                magnifier = new Magnifier(surface != null ? surface : activity.getWindow().getDecorView());
                magnifierOnSurface = surface != null;
            }
            float dx = 0, dy = 0;
            if (!magnifierOnSurface) {
                int[] game = new int[2], window = new int[2];
                View content = activity.findViewById(android.R.id.content);
                if (content != null) content.getLocationOnScreen(game);
                activity.getWindow().getDecorView().getLocationOnScreen(window);
                dx = game[0] - window[0];
                dy = game[1] - window[1];
            }
            magnifier.show(x + dx, y + dy);
        });
        return true;
    }

    private static boolean magnifierOnSurface;

    private static View findSurface(View view) {
        if (view instanceof android.view.SurfaceView) return view;
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) {
                View found = findSurface(group.getChildAt(i));
                if (found != null) return found;
            }
        }
        return null;
    }

    public static void loupeHide(Activity activity) {
        activity.runOnUiThread(() -> {
            if (magnifier != null) magnifier.dismiss();
            magnifier = null;
        });
    }
}
