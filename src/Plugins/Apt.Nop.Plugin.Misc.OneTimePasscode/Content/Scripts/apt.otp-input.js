
(function (global, $) {
  global.apt = global.apt || {};

  var otpInput = global.apt.otpInput || {};

  var otpFieldSelector = null;
  var pasteCallback = null;
  var digitCount = null;

  otpInput.init = function (data) {

    pasteCallback = data.pasteCallback ?? null;
    otpFieldSelector = data.selector || '[otp-field]';
    digitCount = data.digitCount || 6;

    $(apt.otp.parentSelector).on('keydown', otpFieldSelector, onKeyDown);
    $(apt.otp.parentSelector).on('paste', otpFieldSelector, onPaste);

  };

  otpInput.getCode = function (wrapperSelector) {
    let str = '';
    for (let i = 0; i <= 5; i++) {
      str += document.querySelector(`${wrapperSelector} ${otpFieldSelector}[data-index='${i}']`)?.value ?? '';
    }
    return str;
  };

  otpInput.resetCode = function (wrapperSelector) {
    const codeBoxes = getCodeBoxes(wrapperSelector);

    codeBoxes.forEach((el, i) => {
      el.value = '';
      el.classList.remove('filled', 'error');
    });

    codeBoxes[0]?.focus();
  };

  otpInput.focus = function (ix) {

    setTimeout(function () {
      if (!ix || ix <= 0) {
        ix = 0;
      }

      $(apt.otp.parentSelector).find(otpFieldSelector)[ix].focus();
    }, 0);
  }

  otpInput.isComplete = function (wrapperSelector) {
    const codeBoxes = getCodeBoxes(wrapperSelector);
    return codeBoxes.length > 0 && codeBoxes.every(function (el) { return el.value.trim() !== ''; });
  };

  otpInput.getCodeBoxCount = function (wrapperSelector) {
    return getCodeBoxes(wrapperSelector).length;
  };

  otpInput.focusFirstEmpty = function (wrapperSelector) {
    setTimeout(function () {
      const codeBoxes = getCodeBoxes(wrapperSelector);
      var firstEmpty = codeBoxes.find(function (el) { return !el.value; });
      (firstEmpty || codeBoxes[codeBoxes.length - 1])?.focus();
    }, 0);
  };

  function onKeyDown(e) {
    const codeBoxes = getCodeBoxes();
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

    const codeBoxes = getCodeBoxes();
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

    if (digits.length >= codeBoxes.length) {
      if (pasteCallback) {
        pasteCallback();
      }
    }
  }

  function getCodeBoxes(wrapperSelector) {
    return [...document.querySelectorAll(`${wrapperSelector || apt.otp.parentSelector} ${otpFieldSelector}`)];
  }



  global.apt.otpInput = otpInput;
})(window, jQuery);