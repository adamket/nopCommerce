(function (global, $) {
  global.apt = global.apt || {};

  var otp = global.apt.otp || {};
  var isInitialized = false;

  otp.parentSelector = "[apt-otp]";

  var settings = {
    sendUrl: "/apt/request-otp",
    verifyUrl: "/apt/validate-otp",
    localeStrings: {
      resendTryAgainErrorMessage: "Can resend again in {0}s",
      emailRequiredErrorMessage: "Email is required.",
      resendSuccessMessage: "Sent!  Please allow a few minutes for the email to arrive.",
      generalErrorMessage: "An unexpected error occurred.  Please try again or contact support.",
      incompleteCodeErrorMessage: "Please ensure entire code is filled in."
    },
    selectors: {
      parentElement: otp.parentSelector,
      standardLoginEmailInput: "#Email",
      modalWrapper: "[otp-modal-windows]",
      requestOtpModal: "[otp-request-modal]",
      validateOtpModal: "[otp-validate-modal]",
      emailInput: "[otp-email-field]",
      sendButton: "[otp-send-otp-btn]",
      resendButton: "[otp-resend-btn]",
      errorFlag: "[otp-error-flag]",
      successFlag: "[otp-success-flag]",
      loginButton: "[otp-login-btn]",
      backToOtpRequestButton: "[otp-back-btn]",
      showModalButton: "[otp-show-modal]",
      bypassSendButton: "[otp-bypass-send-btn]",

    },
    callbacks: {
      onOtpRequested: null,
      onOtpValidated: null
    }
  };

  var state = {
    email: null
  }

  otp.init = function (options) {



    settings = $.extend(true, {}, settings, options || {});

    if (isInitialized) {
      console.log("OTP js has already been initialized.");
      return;
    }

    $(document).on("click", `${settings.selectors.requestOtpModal} ${settings.selectors.sendButton}`, function (e) {
      e.preventDefault();
      otp.requestOtp(e.target, false, false);
    });

    $(document).on("click", `${settings.selectors.requestOtpModal} ${settings.selectors.bypassSendButton}`, function (e) {
      e.preventDefault();
      otp.requestOtp(e.target, false, true);
    });

    $(document).on("click", `${settings.selectors.validateOtpModal} ${settings.selectors.resendButton}`, function (e) {
      e.preventDefault();
      otp.requestOtp(e.target, true, false);
    });

    $(document).on("click", `${settings.selectors.validateOtpModal} ${settings.selectors.loginButton}`, function (e) {
      e.preventDefault();
      otp.verifyOtp();
    });

    $(document).on('input', `${settings.selectors.requestOtpModal} ${settings.selectors.emailInput}`, function (e) {
      state.email = $(this).val();
    });

    $(document).on('click', settings.selectors.showModalButton, function (e) {
      e.preventDefault();
      ModalTools.showModal(settings.selectors.requestOtpModal, {
        closeOthers: true
      });
    });

    $(document).on('mt-begin-open:' + settings.selectors.requestOtpModal, function (e) {
      var currentEmail = $(settings.selectors.standardLoginEmailInput).val();
      $(settings.selectors.emailInput).val(currentEmail);
      state.email = currentEmail;
    });

    $(document).on('click', settings.selectors.backToOtpRequestButton, function () {
      ModalTools.showModal(settings.selectors.requestOtpModal, {
        closeOthers: true
      });
    });

    isInitialized = true;
  };

  otp.requestOtp = async function (btn, isResend, bypassSend) {

    var emailInput = document.querySelector(settings.selectors.emailInput);
    state.email = emailInput?.value || state.email;

    if (!state.email || !state.email.trim()) {
      apt.otp.showOtpValidation(settings.localeStrings.emailRequiredErrorMessage, settings.selectors.requestOtpModal);
      document.querySelector(settings.selectors.emailInput).focus();
      return;
    }

    if (apt.shared.loading(btn) || btn.dataset.countdown) {
      return;
    }

    apt.shared.loading(btn, true);
    const payload = { email: state.email, bypassSend: bypassSend };
    try {

      const response = await postJson(settings.sendUrl, payload);
      runCallback("onOtpRequested", response, payload);

      if (!response.success) {

        if (isResend && response.prematureOtpRequest && response.canResendInSeconds) {
          apt.shared.loading(btn, false);
          setCountdown(btn, response.canResendInSeconds);
        }

        apt.otp.showOtpValidation(response.message || settings.localeStrings.generalErrorMessage, isResend ? settings.selectors.validateOtpModal : settings.selectors.requestOtpModal);
        return;
      }

      if (response.success) {
        if (isResend) {
          apt.otpInput.resetCode();
          if (response.canResendInSeconds) {
            apt.shared.loading(btn, false);
            setCountdown(btn, response.canResendInSeconds);

            apt.otp.showOtpSuccess(settings.localeStrings.resendSuccessMessage);
          }
          return;
        }

        var validateModal = document.querySelector(settings.selectors.validateOtpModal);
        if (!validateModal) {
          console.error("OTP validate modal container not found.");
          return;
        }

        validateModal.innerHTML = response.markup;

        ModalTools.showModal(settings.selectors.validateOtpModal, {
          closeOthers: true
        });

        return;
      }
    } catch (e) {
      console.error(e);
      apt.otp.showOtpValidation(settings.localeStrings.generalErrorMessage, isResend ? settings.selectors.validateOtpModal : settings.selectors.requestOtpModal);
    } finally {
      apt.shared.loading(btn, false);
    }
  };

  otp.verifyOtp = async function () {
    var btn = document.querySelector(settings.selectors.loginButton);

    if (apt.shared.loading(btn)) {
      return;
    }

    var otp = apt.otpInput.getCode(settings.selectors.validateOtpModal);

    var inputsLength = apt.otpInput.getCodeBoxCount();
    if (otp.length < inputsLength) {
      apt.otpInput.focusFirstEmpty();
      apt.otp.showOtpValidation(settings.localeStrings.incompleteCodeErrorMessage, settings.selectors.validateOtpModal);

      return;
    }

    apt.shared.loading(btn, true);

    try {
      const payload = { otp: otp, email: state.email, redirectUrlPath: apt.shared.getQueryParam('returnUrl') };
      const response = await postJson(settings.verifyUrl, payload);
      runCallback("onOtpValidated", response, payload);

      if (response.success) {
        location.href = response.redirectUrl || '/';
        return;
      }

      apt.otp.showOtpValidation(response.message || settings.localeStrings.generalErrorMessage, settings.selectors.validateOtpModal);
      apt.shared.loading(btn, false);

    } catch (e) {
      console.error(e);
      apt.shared.loading(btn, false);
      apt.otp.showOtpValidation(settings.localeStrings.generalErrorMessage, settings.selectors.validateOtpModal);
    }
  };


  otp.showOtpSuccess = function (message, containerSelector) {
    setTimeout(function () {
      var flag = (containerSelector ? document.querySelector(containerSelector) : document).querySelector(settings.selectors.successFlag);
      flag.textContent = message;
      flag.classList.add('is-visible');
    }, 0);
  }

  otp.showOtpValidation = function (message, containerSelector) {
    setTimeout(function () {
      var flag = (containerSelector ? document.querySelector(containerSelector) : document).querySelector(settings.selectors.errorFlag);
      flag.textContent = message;
      flag.classList.add('is-visible');
    }, 0);
  }

  otp.hideOtpValidation = function () {
    var flags = document.querySelectorAll(`${settings.selectors.successFlag}, ${settings.selectors.errorFlag}`);

    for (var i = 0; i < flags.length; ++i) {
      var flag = flags[i];
      flag.classList.remove('is-visible');
    }
  }

  $(document).on('click', function (e) {
    otp.hideOtpValidation();
  });

  function setCountdown(btn, sendAgainSeconds) {
    var originalBtnContent = btn.innerHTML;
    btn.dataset.countdown = "true";

    apt.shared.runCountdown(sendAgainSeconds,
      (s) => {
        btn.innerHTML = apt.shared.formatString(settings.localeStrings.resendTryAgainErrorMessage, s);
      },
      () => {
        btn.innerHTML =
          originalBtnContent;
        delete btn.dataset.countdown;
      }
    );
    return;
  }

  function postJson(url, data) {
    return $.ajax({
      url: url,
      type: "POST",
      data: addAntiForgeryToken(data)
    });
  }

  function runCallback(name, ...args) {
    const cb = settings.callbacks?.[name];
    if (typeof cb === "function") {
      try {
        cb(...args);
      } catch (e) {
        console.error(e);
      }
    }
  }

  global.apt.otp = otp;
})(window, jQuery);