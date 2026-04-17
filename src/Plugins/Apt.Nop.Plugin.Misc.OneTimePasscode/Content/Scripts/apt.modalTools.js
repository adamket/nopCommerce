// ModalTools modal library
// Usage:
//   ModalTools.showModal('#demoModal')
//   ModalTools.hideModal('#demoModal')
//   $(document).trigger('mt-open:#demoModal')
// Events emitted:
//   $(document).on('mt-opened:#demoModal', function (e, modalEl) { ... })
// Markup:
//   <div data-mt-modal id="demoModal"> ... </div>
// Optional modal attributes:
//   data-mt-no-backdrop-close   => clicking outside does not close modal
// Close triggers:
//   data-mt-close on a button OR click on backdrop OR Escape key

(function (global) {
  const ModalTools = {};
  const state = {
    openModals: new Set(),
    lastFocusedByModalId: new Map(),
    boundOpenEvents: new Set(),
    focusableSelector:
      'a[href], area[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  };

  function resolveModal(modalSelectorOrEl) {
    if (!modalSelectorOrEl) throw new Error("ModalTools: modalSelector is required.");
    if (modalSelectorOrEl instanceof Element) return modalSelectorOrEl;
    const el = document.querySelector(modalSelectorOrEl);
    if (!el) throw new Error(`ModalTools: modal not found for selector: ${modalSelectorOrEl}`);
    return el;
  }

  function isOpen(modalEl) {
    return modalEl.classList.contains("is-open");
  }

  function ensureInBody(modalEl) {
    if (modalEl.parentElement !== document.body) {
      document.body.appendChild(modalEl);
    }
  }

  function ensureModalRootClass(modalEl) {
    modalEl.classList.add("mt-overlay");
  }

  function ensureModalAccessibility(modalEl) {
    modalEl.setAttribute("aria-hidden", isOpen(modalEl) ? "false" : "true");
    modalEl.setAttribute("role", modalEl.getAttribute("role") || "dialog");
    modalEl.setAttribute("aria-modal", "true");
  }

  function setAria(modalEl, open) {
    modalEl.setAttribute("aria-hidden", open ? "false" : "true");
  }

  function lockScrollIfNeeded() {
    const hasOpenModals = state.openModals.size > 0;

    document.documentElement.classList.toggle("mt-modal-open", hasOpenModals);
    document.body.classList.toggle("mt-modal-open", hasOpenModals);

    document.documentElement.classList.toggle("mt-scroll-lock", hasOpenModals);
    document.body.classList.toggle("mt-scroll-lock", hasOpenModals);
  }

  function getModalId(modalEl) {
    return modalEl.id || modalEl.getAttribute("data-mt-id") || null;
  }

  function getModalSelector(modalEl) {
    const modalId = modalEl.id;
    if (modalId) return "#" + modalId;

    const dataId = modalEl.getAttribute("data-mt-id");
    if (dataId) return '[data-mt-id="' + dataId + '"]';

    return null;
  }

  function shouldCloseOnBackdrop(modalEl) {
    return !modalEl.hasAttribute("data-mt-no-backdrop-close");
  }

  function emitModalEvent(prefix, modalEl) {
    const selector = getModalSelector(modalEl);
    if (!selector) return;

    const eventName = prefix + ":" + selector;

    if (global.jQuery) {
      global.jQuery(document).trigger(eventName, [modalEl]);
      return;
    }

    document.dispatchEvent(new CustomEvent(eventName, {
      detail: { modal: modalEl }
    }));
  }

  function focusFirst(modalEl) {
    const focusables = Array.from(
      modalEl.querySelectorAll(state.focusableSelector)
    ).filter(el => el.offsetParent !== null); // only visible elements

    // Prefer inputs specifically
    const inputs = Array.from(
      modalEl.querySelectorAll('input, select, textarea')
    ).filter(el => el.offsetParent !== null);

    if (inputs.length) {
      inputs[0].focus();
      return;
    }

    if (focusables.length > 0) {
      focusables[0].focus();
      return;
    }

    const windowEl = modalEl.querySelector(".mt-window") || modalEl;
    windowEl.setAttribute("tabindex", "-1");
    windowEl.focus();
  }

  function trapTabKey(modalEl, event) {
    if (event.key !== "Tab") return;

    const focusables = Array.from(modalEl.querySelectorAll(state.focusableSelector))
      .filter(el => el.offsetParent !== null);

    if (focusables.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = document.activeElement;

    if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    } else if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    }
  }

  function onOverlayMouseDown(e) {
    const overlay = e.currentTarget;
    if (e.target === overlay && shouldCloseOnBackdrop(overlay)) {
      ModalTools.hideModal(overlay);
    }
  }

  function onOverlayClick(e) {
    const overlay = e.currentTarget;
    const closeBtn = e.target.closest("[data-mt-close]");
    if (closeBtn) {
      e.preventDefault();
      ModalTools.hideModal(overlay);
    }
  }

  function onKeyDown(e) {
    if (e.key === "Escape") {
      const top = ModalTools.getTopModal();
      if (top) ModalTools.hideModal(top);
      return;
    }

    const top = ModalTools.getTopModal();
    if (top) trapTabKey(top, e);
  }

  function attachListeners(modalEl) {
    modalEl.addEventListener("mousedown", onOverlayMouseDown);
    modalEl.addEventListener("click", onOverlayClick);
  }

  function detachListeners(modalEl) {
    modalEl.removeEventListener("mousedown", onOverlayMouseDown);
    modalEl.removeEventListener("click", onOverlayClick);
  }

  function bindOpenEvent(modalEl) {
    const selector = getModalSelector(modalEl);
    if (!selector) return;

    const eventName = "mt-open:" + selector;
    if (state.boundOpenEvents.has(eventName)) return;

    state.boundOpenEvents.add(eventName);

    if (global.jQuery) {
      global.jQuery(document).on(eventName, function () {
        ModalTools.showModal(modalEl);
      });
    } else {
      document.addEventListener(eventName, function () {
        ModalTools.showModal(modalEl);
      });
    }
  }

  function prepareModal(modalEl) {
    ensureModalRootClass(modalEl);
    ensureModalAccessibility(modalEl);
    bindOpenEvent(modalEl);
  }

  function autoBindOpenedEvents() {
    const modals = document.querySelectorAll("[data-mt-modal]");
    modals.forEach(prepareModal);
  }

  ModalTools.showModal = function (modalSelectorOrEl, options) {
    const modalEl = resolveModal(modalSelectorOrEl);
    if (isOpen(modalEl)) return;

    ensureInBody(modalEl);
    prepareModal(modalEl);

    const opts = Object.assign(
      {
        focus: true,
        closeOthers: false,
      },
      options || {}
    );

    if (opts.closeOthers) {
      Array.from(state.openModals).forEach(m => ModalTools.hideModal(m));
    }

    const modalId = getModalId(modalEl);
    if (modalId) state.lastFocusedByModalId.set(modalId, document.activeElement);

    attachListeners(modalEl);
    setAria(modalEl, true);
    state.openModals.add(modalEl);
    lockScrollIfNeeded();

    if (state.openModals.size === 1) {
      document.addEventListener("keydown", onKeyDown);
    }

    emitModalEvent("mt-begin-open", modalEl);

    // Ensure the browser sees the initial hidden state before opening.
    modalEl.classList.remove("is-open");
    void modalEl.offsetWidth;

    requestAnimationFrame(() => {
      modalEl.classList.add("is-open");

      if (opts.focus) {
        requestAnimationFrame(() => focusFirst(modalEl));
      }

      emitModalEvent("mt-opened", modalEl);
    });
  };

  ModalTools.hideModal = function (modalSelectorOrEl) {
    const modalEl = resolveModal(modalSelectorOrEl);
    if (!isOpen(modalEl)) return;

    detachListeners(modalEl);
    modalEl.classList.remove("is-open");
    setAria(modalEl, false);
    state.openModals.delete(modalEl);
    lockScrollIfNeeded();

    if (state.openModals.size === 0) {
      document.removeEventListener("keydown", onKeyDown);
    }

    const modalId = getModalId(modalEl);
    const last = modalId ? state.lastFocusedByModalId.get(modalId) : null;
    if (last && typeof last.focus === "function") {
      setTimeout(() => last.focus(), 0);
    }
  };

  ModalTools.toggleModal = function (modalSelectorOrEl, options) {
    const modalEl = resolveModal(modalSelectorOrEl);
    if (isOpen(modalEl)) ModalTools.hideModal(modalEl);
    else ModalTools.showModal(modalEl, options);
  };

  ModalTools.getTopModal = function () {
    const arr = Array.from(state.openModals);
    return arr.length ? arr[arr.length - 1] : null;
  };

  document.addEventListener("click", function (e) {
    const openBtn = e.target.closest("[data-mt-open]");
    if (!openBtn) return;

    const sel = openBtn.getAttribute("data-mt-open");
    if (!sel) return;

    e.preventDefault();
    ModalTools.showModal(sel);
  });

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", autoBindOpenedEvents);
  } else {
    autoBindOpenedEvents();
  }

  global.ModalTools = ModalTools;
})(window);