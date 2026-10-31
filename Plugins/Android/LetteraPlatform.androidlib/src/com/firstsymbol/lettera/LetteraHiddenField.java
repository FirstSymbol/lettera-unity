package com.firstsymbol.lettera;

import android.content.Context;
import android.text.Editable;
import android.text.InputFilter;
import android.text.Spanned;
import android.text.TextWatcher;
import android.view.inputmethod.BaseInputConnection;
import android.widget.EditText;

/** The system's own text control, for a field drawn by Lettera. It holds no rule: all it does is say what happened. */
final class LetteraHiddenField extends EditText {
    final LetteraMirrorCore core;
    boolean pushing;
    private int composingStart = -1, composingEnd = -1;

    LetteraHiddenField(Context context, LetteraMirrorCore core) {
        super(context);
        this.core = core;
        setFilters(new InputFilter[] { (source, start, end, dest, dstart, dend) -> {
            if (!pushing && !(source instanceof Spanned && isComposing())) core.replace(dstart, dend, source.subSequence(start, end).toString());
            return null;
        } });
        addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { }
            @Override public void afterTextChanged(Editable s) {
                if (pushing) return;
                reportComposition(s);
                if (!isComposing()) core.afterChange(s.toString(), getSelectionStart());
            }
        });
    }

    private boolean isComposing() { return composingStart >= 0; }

    private void reportComposition(Editable text) {
        int start = BaseInputConnection.getComposingSpanStart(text), end = BaseInputConnection.getComposingSpanEnd(text);
        if (start >= 0 && end > start) {
            composingStart = start;
            composingEnd = end;
            core.compose(start, end, text.subSequence(start, end).toString(), Math.max(0, getSelectionStart() - start));
        } else if (composingStart >= 0) {
            composingStart = composingEnd = -1;
            core.composeEnd(false);
        }
    }

    @Override protected void onSelectionChanged(int start, int end) {
        super.onSelectionChanged(start, end);
        // Called from the parent's constructor, before there is a core.
        if (core != null && !pushing && !isComposing()) core.select(start, end);
    }
}
