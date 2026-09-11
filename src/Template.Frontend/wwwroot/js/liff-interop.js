// LIFF interop. Kept in a file (not inline in index.html) because the CloudFront CSP allows only
// script-src 'self' - an inline block would be blocked (SPEC 4 / 10.1).
//
// getFreshIdToken is the crux of the two-token design (SPEC 3.2): the LINE ID token lives one hour
// and is not refreshed in place, so once it is near expiry the only recovery is logout() then
// login(). Skipping the logout() leaves the expired token cached and every later call returns the
// same stale value. The app exchanges this ID token for its own JWT once at startup and never
// relies on it again, so this path runs rarely.
window.liffInterop = {
    liffId: null,

    // Returns true when LIFF is ready and the user is logged in; returns false while navigating to
    // the LINE login page (the caller should stop and let the redirect happen).
    init: async function (liffId) {
        this.liffId = liffId;
        await liff.init({ liffId: liffId });
        if (!liff.isLoggedIn()) {
            liff.login({ redirectUri: location.href });
            return false;
        }
        return true;
    },

    // Always returns a *valid* ID token, or null when it had to trigger a re-login (page navigates).
    getFreshIdToken: function () {
        const decoded = liff.getDecodedIDToken();
        if (!decoded || decoded.exp * 1000 < Date.now() + 60000) {
            liff.logout();
            liff.login({ redirectUri: location.href });
            return null;
        }
        return liff.getIDToken();
    },

    getProfile: async function () {
        const p = await liff.getProfile();
        return {
            userId: p.userId,
            displayName: p.displayName,
            pictureUrl: p.pictureUrl,
            statusMessage: p.statusMessage
        };
    },

    getEnvironment: function () {
        return {
            os: liff.getOS(),
            language: liff.getLanguage(),
            liffVersion: liff.getVersion(),
            lineVersion: liff.getLineVersion(),
            isInClient: liff.isInClient()
        };
    },

    logout: function () {
        liff.logout();
    },

    closeWindow: function () {
        if (liff.isInClient()) {
            liff.closeWindow();
        }
    }
};
