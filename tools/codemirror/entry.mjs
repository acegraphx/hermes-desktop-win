import { EditorState, Compartment } from "@codemirror/state";
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection, dropCursor, rectangularSelection, crosshairCursor } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { searchKeymap, highlightSelectionMatches } from "@codemirror/search";
import { syntaxHighlighting, defaultHighlightStyle, indentOnInput, bracketMatching, foldGutter, foldKeymap } from "@codemirror/language";
import { markdown, markdownLanguage } from "@codemirror/lang-markdown";
import { oneDark } from "@codemirror/theme-one-dark";

const themeCompartment = new Compartment();
const readOnlyCompartment = new Compartment();

const lightTheme = EditorView.theme({
    "&": {
        color: "#202020",
        backgroundColor: "#FAFAFA",
        height: "100%",
        fontSize: "13px"
    },
    ".cm-content": {
        fontFamily: "Consolas, 'Cascadia Code', 'Fira Code', monospace",
        caretColor: "#1565C0"
    },
    ".cm-cursor, .cm-dropCursor": { borderLeftColor: "#1565C0" },
    "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection": {
        backgroundColor: "rgba(21,101,192,0.18)"
    },
    ".cm-gutters": {
        backgroundColor: "#F2F2F2",
        color: "#888",
        border: "none"
    },
    ".cm-activeLine": { backgroundColor: "rgba(21,101,192,0.06)" },
    ".cm-activeLineGutter": { backgroundColor: "rgba(21,101,192,0.10)" },
    ".cm-scroller::-webkit-scrollbar": { width: "6px", height: "6px" },
    ".cm-scroller::-webkit-scrollbar-thumb": { background: "#C1C1C1", borderRadius: "3px" }
}, { dark: false });

const darkExtras = EditorView.theme({
    "&": { fontSize: "13px" },
    ".cm-content": {
        fontFamily: "Consolas, 'Cascadia Code', 'Fira Code', monospace"
    },
    ".cm-scroller::-webkit-scrollbar": { width: "6px", height: "6px" },
    ".cm-scroller::-webkit-scrollbar-thumb": { background: "#3A3D44", borderRadius: "3px" }
}, { dark: true });

let view = null;
let suppressChangeEvents = false;

function postMessage(payload) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(payload);
    }
}

function buildExtensions(isDark, readOnly) {
    return [
        lineNumbers(),
        highlightActiveLineGutter(),
        history(),
        foldGutter(),
        drawSelection(),
        dropCursor(),
        EditorState.allowMultipleSelections.of(true),
        indentOnInput(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        bracketMatching(),
        rectangularSelection(),
        crosshairCursor(),
        highlightActiveLine(),
        highlightSelectionMatches(),
        keymap.of([
            ...defaultKeymap,
            ...historyKeymap,
            ...searchKeymap,
            ...foldKeymap,
            indentWithTab,
            {
                key: "Mod-s",
                run: () => { postMessage({ type: "save" }); return true; }
            }
        ]),
        markdown({ base: markdownLanguage }),
        EditorView.lineWrapping,
        themeCompartment.of(isDark ? [oneDark, darkExtras] : [lightTheme]),
        readOnlyCompartment.of(EditorView.editable.of(!readOnly)),
        EditorView.updateListener.of(update => {
            if (update.docChanged && !suppressChangeEvents) {
                const text = update.state.doc.toString();
                postMessage({ type: "change", text });
            }
        }),
        EditorView.domEventHandlers({
            click: (event, v) => {
                // Detect click on a wikilink token: scan a small window around the click position.
                const pos = v.posAtDOM(event.target);
                if (pos == null) return false;
                const line = v.state.doc.lineAt(pos);
                const text = line.text;
                const offsetInLine = pos - line.from;
                // Find [[...]] containing offsetInLine.
                const re = /\[\[([^\]\|\n]+?)(?:\|[^\]\n]+?)?\]\]/g;
                let m;
                while ((m = re.exec(text)) !== null) {
                    if (m.index <= offsetInLine && offsetInLine <= m.index + m[0].length) {
                        if (event.ctrlKey || event.metaKey) {
                            postMessage({ type: "wikilink", target: m[1].trim() });
                            return true;
                        }
                        break;
                    }
                }
                return false;
            }
        })
    ];
}

window.editorInit = function(initialBase64, isDark, readOnly) {
    const text = decodeURIComponent(escape(atob(initialBase64 || "")));
    const state = EditorState.create({
        doc: text,
        extensions: buildExtensions(!!isDark, !!readOnly)
    });
    if (view) view.destroy();
    view = new EditorView({
        state,
        parent: document.getElementById("editor")
    });
    postMessage({ type: "ready" });
};

window.editorSetContent = function(base64) {
    if (!view) return;
    const text = decodeURIComponent(escape(atob(base64 || "")));
    suppressChangeEvents = true;
    try {
        view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: text }
        });
    } finally {
        suppressChangeEvents = false;
    }
};

window.editorSetTheme = function(isDark) {
    if (!view) return;
    view.dispatch({
        effects: themeCompartment.reconfigure(isDark ? [oneDark, darkExtras] : [lightTheme])
    });
};

window.editorSetReadOnly = function(readOnly) {
    if (!view) return;
    view.dispatch({
        effects: readOnlyCompartment.reconfigure(EditorView.editable.of(!readOnly))
    });
};

window.editorFocus = function() { if (view) view.focus(); };
