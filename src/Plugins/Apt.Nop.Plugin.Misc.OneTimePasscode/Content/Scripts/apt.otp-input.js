
(function (global, $) {
  global.apt = global.apt || {};

  var otpInput = global.apt.otpInput || {};

  var otpFieldSelector = null;
  var codeBoxes = [];
  var pasteCallback = null;

  otpInput.init = function (data) {

    pasteCallback = data.pasteCallback ?? null;
    otpFieldSelector = data.selector || '.otp-field';

    $(document).on('keydown', otpFieldSelector, onKeyDown);
    $(document).on('paste', otpFieldSelector, onPaste);

  };

  otpInput.getCode = function (wrapper) {
    let str = '';
    for (let i = 0; i <= 5; i++) {
      str += wrapper.querySelector(otpFieldSelector + `[data-index='${i}']`)?.value ?? '';
    }
    return str;
  };

  otpInput.resetCode = function (wrapper) {
    const boxes = wrapper.querySelectorAll(otpFieldSelector);

    boxes.forEach((el, i) => {
      el.value = '';
      el.classList.remove('filled', 'error');
    });

    boxes[0]?.focus();
  };

  otpInput.focus = function (ix) {

    setTimeout(function () {
      if (!ix || ix <= 0) {
        ix = 0;
      }

      $(otpFieldSelector)[ix].focus();
    }, 0);
  }

  function onKeyDown(e) {
    const codeBoxes = [...document.querySelectorAll(otpFieldSelector)];
    const input = e.target;
    const i = codeBoxes.indexOf(input);
    const isCtrlOrCmd = e.ctrlKey || e.metaKey;

    // Allow copy/paste shortcuts
    if (isCtrlOrCmd && (e.key === 'c' || e.key === 'v')) return;

    // Allow tab navigation between OTP boxes
    if (e.key === 'Tab') {
      if (e.shiftKey) {
        if (i > 0) {
          e.preventDefault();
          codeBoxes[i - 1]?.focus();
        }
        return;
      }

      if (i < codeBoxes.length - 1) {
        e.preventDefault();
        codeBoxes[i + 1]?.focus();
      }

      // Let normal tab behavior continue from the last box
      return;
    }

    // Arrow key navigation
    if (e.key === 'ArrowLeft') {
      e.preventDefault();
      codeBoxes[i - 1]?.focus();
      return;
    }

    if (e.key === 'ArrowRight') {
      e.preventDefault();
      codeBoxes[i + 1]?.focus();
      return;
    }

    // Backspace: clear current, move back if already empty
    if (e.key === 'Backspace') {
      e.preventDefault();
      if (input.value) {
        input.value = '';
        input.classList.remove('filled');
      } else if (i > 0) {
        codeBoxes[i - 1].focus();
      }
      return;
    }

    // Only allow digit input (0-9)
    if (/^\d$/.test(e.key)) {
      e.preventDefault();
      input.value = e.key;
      input.classList.add('filled');
      codeBoxes[i + 1]?.focus();
      return;
    }

    // Allow a few useful keys
    if (['Delete', 'Home', 'End'].includes(e.key)) {
      return;
    }

    // Block everything else
    e.preventDefault();
  }

  function onPaste(e) {
    e.preventDefault();

    const codeBoxes = [...document.querySelectorAll(otpFieldSelector)];
    const clipboardData = e.clipboardData || e.originalEvent?.clipboardData;

    if (!clipboardData) return;

    const digits = (clipboardData.getData('text') || '')
      .replace(/\D/g, '')
      .split('');

    const startIndex = codeBoxes.indexOf(e.target);

    digits.forEach((digit, offset) => {
      const box = codeBoxes[startIndex + offset];
      if (box) {
        box.value = digit;
        box.classList.add('filled');
      }
    });

    const nextEmpty = codeBoxes.find((box) => !box.value);
    (nextEmpty || codeBoxes[codeBoxes.length - 1])?.focus();

    if (digits.length >= 6) {
      if (pasteCallback) {
        pasteCallback();
      }
    }
  }

  global.apt.otpInput = otpInput;
})(window, jQuery);