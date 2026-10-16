/*
 * Toast: shows a server-rendered .dg-toast[data-autoshow] pill (e.g. "Added
 * to cart" after the add-to-cart POST redirect) and hides it after a moment.
 *
 * The pill holds the only "View cart" link, so the auto-hide must not take it away
 * while the visitor is reading or reaching for it (WCAG 2.2.1 Timing Adjustable):
 * hovering or focusing anything inside it cancels the timer for good, and a close
 * button is always available.
 *
 * The cookie bar is fixed to the same corner of the viewport and is taller before consent is
 * given, where it would hide the pill completely. Its height is published as
 * --consent-bar-height so the pill can sit above it rather than behind it.
 */
(() => {
    "use strict";

    const HIDE_DELAY = 4000;

    const toast = document.querySelector(".dg-toast[data-autoshow]");
    if (!toast) {
        return;
    }

    const consentBar = document.getElementById("consent");

    const publishConsentBarHeight = () => {
        document.documentElement.style.setProperty("--consent-bar-height", (consentBar?.offsetHeight ?? 0) + "px");
    };

    window.addEventListener("resize", publishConsentBarHeight);

    let timer;
    let cancelled = false;

    const hide = () => {
        toast.classList.remove("show");
    };

    const scheduleHide = () => {
        if (cancelled) {
            return;
        }
        window.clearTimeout(timer);
        timer = window.setTimeout(hide, HIDE_DELAY);
    };

    const cancelHide = () => {
        cancelled = true;
        window.clearTimeout(timer);
    };

    toast.addEventListener("mouseenter", cancelHide);
    toast.addEventListener("focusin", cancelHide);

    const close = toast.querySelector("[data-toast-close]");
    if (close) {
        close.addEventListener("click", () => {
            cancelHide();
            hide();
        });
    }

    requestAnimationFrame(() => {
        publishConsentBarHeight();
        toast.classList.add("show");
    });
    scheduleHide();
})();
