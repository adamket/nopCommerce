(function (global, $) {
  global.apt = global.apt || {};

  var otp = global.apt.otp || {};

  otp.parentSelector = ".apt-otp";

  var settings = {
    sendUrl: "/apt/request-otp",
    verifyUrl: "/apt/otp-login",
    localeStrings: {
      resendTryAgainErrorMessage: "Can resend again in {0}s",
      emailRequiredErrorMessage: "Email is required.",
      resendSuccessMessage: "Sent!  Please allow a few minutes for the email to arrive.",
      generalErrorMessage: "An unexpected error occurred.  Please try again or contact support.",
      incompleteCodeErrorMessage: "Please ensure entire code is filled in."
    },
    selectors: {
      //add [otp-modal]?
      //change otp-send / bypass send naming
      standardLoginEmailInput: "#Email",
      parentElement: otp.parentSelector,
      otpStepContainer: "[otp-step-container]",
      emailInput: "[otp-email]",
      sendButton: "[otp-send]",
      resendButton: "[otp-resend]",
      errorFlag: "[otp-error-flag]",
      successFlag: "[otp-success-flag]",
      verifyButton: "[otp-verify]",
      messageContainer: "[otp-message]",
      submitButton: "[otp-submit]",
      backToStep1Button: "[otp-back]",
      step1Container: "[otp-step-1]",
      step2Container: "[otp-step-2]",
      showModalButton: "[otp-show-modal]"
    }
  };

  var state = {
    email: null
  }

  otp.init = function (options) {
    settings = $.extend(true, {}, settings, options || {});

    $(document).on("click", `${settings.selectors.step1Container} ${settings.selectors.sendButton}`, function (e) {
      e.preventDefault();
      var bypassSend = $(this).data('otp-bypass') === true;
      otp.sendOtp(e.target, false, bypassSend);
    });

    $(document).on("click", `${settings.selectors.step2Container} ${settings.selectors.resendButton}`, function (e) {
      e.preventDefault();
      otp.sendOtp(e.target, true, false);
    });

    $(document).on("click", `${settings.selectors.step2Container} ${settings.selectors.submitButton}`, function (e) {
      e.preventDefault();
      otp.verifyOtp();
    });

    $(document).on('input', settings.selectors.emailInput, function (e) {
      state.email = $(this).val();
    });

    $(document).on('click', settings.selectors.showModalButton, function (e) {
      e.preventDefault();
      ModalTools.showModal('[otp-modal]');
    });

    $(document).on('mt-begin-open:[otp-modal]', function (e) {
      var currentEmail = $(settings.selectors.standardLoginEmailInput).val();
      $(settings.selectors.emailInput).val(currentEmail);
      state.email = currentEmail;
    });

    $(document).on('click', settings.selectors.backToStep1Button, function () {

      var $stepContainer = $(`${settings.selectors.parentElement} ${settings.selectors.otpStepContainer}`);
      $stepContainer.find(settings.selectors.step2Container).empty().hide();
      $stepContainer.find(settings.selectors.step1Container).show();

      $stepContainer.html($stepContainer.data('page-1'));
    });
  };


  otp.sendOtp = async function (btn, isResend, bypassSend) {

    if (!state.email || !state.email.trim()) {
      apt.otp.showOtpValidation(settings.localeStrings.emailRequiredErrorMessage, settings.selectors.step1Container);
      document.querySelector(settings.selectors.emailInput).focus();
      return;
    }

    if (apt.shared.loading(btn) || btn.dataset.countdown) {
      return;
    }

    apt.shared.loading(btn, true);

    try {
      var response = await postJson(settings.sendUrl, { email: state.email, bypassSend: bypassSend });
      if (!response.success) {

        if (isResend && response.prematureOtpRequest && response.canResendInSeconds) {
          apt.shared.loading(btn, false);
          setCountdown(btn, response.canResendInSeconds);
        }

        apt.otp.showOtpValidation(response.message, isResend ? settings.selectors.step2Container : settings.selectors.step1Container);
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

        var $otpStepContainer = $(`${settings.selectors.parentElement} ${settings.selectors.otpStepContainer}`);

        var $step2Container = $otpStepContainer.find(settings.selectors.step2Container);
        if (!$step2Container.length) {
          $otpStepContainer.append('<div class="otp-step-2" ' + settings.selectors.step2Container + '></div>')
        }

        $otpStepContainer.find(settings.selectors.step1Container).hide();
        $otpStepContainer.find(settings.selectors.step2Container).empty().append(response.markup).show();
        apt.otpInput.focus();

        return;
      }
    } catch (e) {
      console.error(e);
      apt.otp.showOtpValidation(settings.localeStrings.generalErrorMessage, isResend ? settings.selectors.step2Container : settings.selectors.step1Container);
    } finally {
      apt.shared.loading(btn, false);
    }
  };

  otp.verifyOtp = async function () {
    var btn = document.querySelector(settings.selectors.submitButton);

    if (apt.shared.loading(btn)) {
      return;
    }

    var otp = apt.otpInput.getCode(settings.selectors.step2Container);

    var inputsLength = apt.otpInput.getCodeBoxCount();
    if (otp.length < inputsLength) {
      apt.otpInput.focusFirstEmpty();
      apt.otp.showOtpValidation(settings.localeStrings.incompleteCodeErrorMessage, settings.selectors.step2Container);

      return;
    }

    apt.shared.loading(btn, true);

    try {
      var response = await postJson(settings.verifyUrl, { otp: otp, email: state.email });
      if (response.success) {
        location.href = response.returnUrl || '/';
        return;
      }

      apt.otp.showOtpValidation(response.message, settings.selectors.step2Container);
      apt.shared.loading(btn, false);

    } catch (e) {
      console.error(e);
      apt.shared.loading(btn, false);
      apt.otp.showOtpValidation(settings.localeStrings.generalErrorMessage, settings.selectors.step2Container);
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


  global.apt.otp = otp;
})(window, jQuery);