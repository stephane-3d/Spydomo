(function () {
    const state = { mountedIds: new Set(), unmounts: new Map() };

    async function ensureClerkLoaded() {
        if (!window.Clerk || !Clerk.load) {
            console.error("Clerk JS not loaded on page.");
            return false;
        }
        await Clerk.load();
        return true;
    }

    function getOrCreateHost(el) {
        // If a host exists, reuse it; otherwise create a fresh one
        let host = el.querySelector("[data-clerk-host='1']");
        if (!host) {
            host = document.createElement("div");
            host.setAttribute("data-clerk-host", "1");
            el.innerHTML = "";
            el.appendChild(host);
        }
        return host;
    }

    function canRemount(elementId) {
        // If the DOM for this id is gone or no host exists, allow remount even if we cached it
        const el = document.getElementById(elementId);
        if (!el) return true;
        const hasHost = !!el.querySelector("[data-clerk-host='1']");
        return !hasHost;
    }

    window.clerkInterop = {
        mountSignUp: async (elementId, afterSignUpUrl, afterSignInUrl) => {
            if (!(await ensureClerkLoaded())) return;
            const el = document.getElementById(elementId);
            if (!el) return;

            if (state.mountedIds.has(elementId) && !canRemount(elementId)) return;

            // If we think it's mounted but DOM was removed, clear the stale cache
            if (state.mountedIds.has(elementId) && canRemount(elementId)) {
                const u = state.unmounts.get(elementId);
                if (typeof u === "function") try { u(); } catch { }
                state.unmounts.delete(elementId);
                state.mountedIds.delete(elementId);
            }

            const host = getOrCreateHost(el);
            let unmount;
            if (Clerk.user) {
                unmount = Clerk.mountUserButton(host);
            } else {
                unmount = Clerk.mountSignUp(host, { afterSignUpUrl, afterSignInUrl });
            }

            state.mountedIds.add(elementId);
            state.unmounts.set(elementId, unmount);
        },

        mountSignIn: async (elementId, afterSignInUrl) => {
            if (!(await ensureClerkLoaded())) return;
            const el = document.getElementById(elementId);
            if (!el) return;

            if (state.mountedIds.has(elementId) && !canRemount(elementId)) return;

            if (state.mountedIds.has(elementId) && canRemount(elementId)) {
                const u = state.unmounts.get(elementId);
                if (typeof u === "function") try { u(); } catch { }
                state.unmounts.delete(elementId);
                state.mountedIds.delete(elementId);
            }

            const host = getOrCreateHost(el);
            const unmount = Clerk.user
                ? Clerk.mountUserButton(host)
                : Clerk.mountSignIn(host, { afterSignInUrl });

            state.mountedIds.add(elementId);
            state.unmounts.set(elementId, unmount);
        },

        unmount: (elementId) => {
            const u = state.unmounts.get(elementId);
            if (typeof u === "function") {
                try { u(); } catch { }
            }
            state.unmounts.delete(elementId);
            state.mountedIds.delete(elementId);
        },

        // ✅ Fully defensive version – will never throw on getToken
        getSessionToken: async () => {
            const clerk = window.Clerk;

            // 1) Clerk not present at all
            if (!clerk) {
                console.warn("Clerk is not available on window.");
                return null;
            }

            // 2) Ensure Clerk is loaded
            try {
                if (typeof clerk.load === "function") {
                    await clerk.load();
                }
            } catch (e) {
                console.error("Error calling clerk.load()", e);
                return null;
            }

            // 3) No active session → user not logged in
            const session = clerk.session;
            if (!session) {
                return null;
            }

            if (typeof session.getToken !== "function") {
                console.warn("session.getToken is not a function.");
                return null;
            }

            try {
                // If you use a template, you can adapt:
                // const token = await session.getToken({ template: "spydomo" });
                const token = await session.getToken();
                return token ?? null;
            } catch (e) {
                console.error("Error while calling session.getToken()", e);
                return null;
            }
        },

        getCurrentUser: async () => {
            if (!(await ensureClerkLoaded())) return null;

            const user = Clerk.user;
            return user ? {
                id: user.id,
                emailAddress: user.primaryEmailAddress?.emailAddress ?? "",
                fullName: `${user.firstName || ""} ${user.lastName || ""}`.trim()
            } : null;
        },

        getUserId: () => (window.Clerk && Clerk.user ? Clerk.user.id : ""),

        logout: async () => {
            if (!(await ensureClerkLoaded())) return;
            if (Clerk.session) {
                await Clerk.signOut({ redirectUrl: '/app/login' });
            }
        }
    };
})();




/*window.clerkInterop = {
    mountSignUp: async (elementId, afterSignUpUrl, afterSignInUrl) => {
        await Clerk.load();

        const el = document.getElementById(elementId);
        if (Clerk.user) {
            el.innerHTML = `<div id="user-button"></div>`;
            Clerk.mountUserButton(document.getElementById('user-button'));
        } else {
            el.innerHTML = `<div id="sign-up"></div>`;
            Clerk.mountSignUp(document.getElementById('sign-up'), {
                afterSignUpUrl,
                afterSignInUrl
            });
        }
    },

    mountSignIn: async (elementId, afterSignInUrl) => {
        await Clerk.load();

        const el = document.getElementById(elementId);
        if (Clerk.user) {
            el.innerHTML = `<div id="user-button"></div>`;
            Clerk.mountUserButton(document.getElementById('user-button'));
        } else {
            el.innerHTML = `<div id="sign-in"></div>`;
            Clerk.mountSignIn(document.getElementById('sign-in'), {
                afterSignInUrl
            });
        }
    },

    getSessionToken: async () => {
        await Clerk.load();
        return await Clerk.session.getToken();
    },

    getCurrentUser: async () => {
        await Clerk.load();
        const user = await Clerk.user;
        return {
            id: user.id,
            emailAddress: user.primaryEmailAddress.emailAddress,
            fullName: `${user.firstName || ""} ${user.lastName || ""}`.trim()
        };
    },

    getUserId: () => {
        return Clerk.user?.id || "";
    },

    logout: async () => {
        await Clerk.load();
        if (Clerk.session) {
            await Clerk.signOut({ redirectUrl: '/app/login' });
        }
    }
};
*/