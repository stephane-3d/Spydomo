window.spydomoCopyText = async function (text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (_) {
        try {
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.focus(); ta.select();
            const ok = document.execCommand('copy');
            document.body.removeChild(ta);
            return ok;
        } catch {
            return false;
        }
    }
};

window.spydomoNav = (() => {
    const qs = (s) => document.querySelector(s);

    function setOpen(open) {
        const nav = qs("#primary-nav");
        const btn = qs(".nav-toggle");
        const backdrop = qs(".nav-backdrop");
        if (!nav || !btn || !backdrop) return;

        nav.classList.toggle("is-open", open);
        backdrop.classList.toggle("is-open", open);
        btn.setAttribute("aria-expanded", open ? "true" : "false");
    }

    function toggle() {
        const nav = qs("#primary-nav");
        setOpen(!(nav && nav.classList.contains("is-open")));
    }

    function close() { setOpen(false); }

    // Close on Esc
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") close();
    });

    return { toggle, close };
})();

window.spydomoCopyToClipboard = async (text) => {
    // Modern approach
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return;
    }

    // Fallback (older browsers / non-secure contexts)
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    ta.style.top = "-9999px";
    document.body.appendChild(ta);
    ta.focus();
    ta.select();

    const ok = document.execCommand("copy");
    document.body.removeChild(ta);

    if (!ok) throw new Error("execCommand(copy) failed");
};

window.spydomoDownloadTextFile = (fileName, content) => {
    const blob = new Blob([content], { type: "application/json;charset=utf-8" });
    const url = URL.createObjectURL(blob);

    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();

    URL.revokeObjectURL(url);
};

