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
      parentElement: otp.parentSelector,
      otpStepContainer: ".otp-step-container",
      emailInput: "[data-otp-email]",
      sendButton: "[data-otp-send]",
      verifyButton: "[data-otp-verify]",
      messageContainer: "[data-otp-message]",
      submitButton: "[data-otp-submit]",
      backToStep1Button: "[data-otp-back]",
      wrapperElement: ".otp-container",
      standardLoginEmailInput: "#Email",
      step1Container: ".otp-step-1",
      step2Container: ".otp-step-2"

    }
  };

  var state = {
    email: null
  }

  otp.init = function (options) {
    settings = $.extend(true, {}, settings, options || {});

    $(document).on("click", settings.selectors.sendButton, function (e) {
      e.preventDefault();
      var isResend = $(this).data('otp-resend') != null;
      otp.sendOtp(e.target, isResend, $(this).data('otp-generate'));
    });

    $(document).on("click", settings.selectors.submitButton, function (e) {
      e.preventDefault();
      otp.verifyOtp();
    });

    $(document).on('input', settings.selectors.emailInput, function (e) {
      state.email = $(this).val();
    });

    $(document).on('mt-begin-open:#apt-otp-modal', function (e) {
      var currentEmail = $(settings.selectors.standardLoginEmailInput).val();
      $(settings.selectors.emailInput).val(currentEmail);
      state.email = currentEmail;
    });

    $(document).on('click', settings.selectors.backToStep1Button, function () {

      var $stepContainer = $(`${settings.selectors.parentElement} ${settings.selectors.otpStepContainer}`);
      $stepContainer.find(settings.selectors.step2Container).remove();
      $stepContainer.find(settings.selectors.step1Container).show();

      $stepContainer.html($stepContainer.data('page-1'));
    });
  };


  otp.sendOtp = async function (btn, resend, generateCode) {

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
      var response = await postJson(settings.sendUrl, { email: state.email, generateOtp: generateCode });
      if (!response.success) {

        if (resend && response.prematureOtpRequest && response.canResendInSeconds) {
          apt.shared.loading(btn, false);
          setCountdown(btn, response.canResendInSeconds);
        }

        apt.otp.showOtpValidation(response.message || "Invalid email", resend ? settings.selectors.step2Container : settings.selectors.step1Container);
        return;
      }

      if (response.success) {
        if (resend) {
          apt.otpInput.resetCode(document.querySelector(settings.selectors.step2Container));
          if (response.canResendInSeconds) {
            apt.shared.loading(btn, false);
            setCountdown(btn, response.canResendInSeconds);

            apt.otp.showOtpSuccess(settings.localeStrings.resendSuccessMessage);
          }
          return;
        }

        var $otpStepContainer = $(`${settings.selectors.parentElement} .otp-step-container`);

        var $step2Container = $otpStepContainer.find(settings.selectors.step2Container);
        if (!$step2Container.length) {
          $otpStepContainer.append('<div class="otp-step-2"></div>')
        }

        $otpStepContainer.find(settings.selectors.step1Container).hide();
        $otpStepContainer.find(settings.selectors.step2Container).empty().append(response.markup).show();
        apt.otpInput.focus();

        return;
      }
    } catch (e) {
      apt.otp.showOtpValidation(settings.localeStrings.generalErrorMessage, resend ? settings.selectors.step2Container : settings.selectors.step1Container);
    } finally {
      apt.shared.loading(btn, false);
    }
  };

  otp.verifyOtp = async function () {
    var btn = document.querySelector(settings.selectors.submitButton);

    if (apt.shared.loading(btn)) {
      return;
    }

    var otp = apt.otpInput.getCode(document.querySelector(settings.selectors.step2Container));

    if (otp.length < 6) {
      apt.otp.showOtpValidation(settings.localeStrings.incompleteCodeErrorMessage, settings.selectors.step2Container); //TODO NEEDS SR
      apt.otpInput.focusFirstEmpty(document.querySelector(settings.selectors.step2Container));
      return;
    }

    apt.shared.loading(btn, true);

    try {
      var response = await postJson(settings.verifyUrl, { otp: otp, email: state.email });
      if (response.success) {
        location.href = response.returnUrl || '/';
        return;
      }

      apt.otp.showOtpValidation(response.message || "Invalid code.", settings.selectors.step2Container);
      apt.shared.loading(btn, false);

    } catch (e) {
      apt.shared.loading(btn, false);
      apt.otp.showOtpValidation(settings.localeStrings.generalErrorMessage, settings.selectors.step2Container); 
    }
  };


  otp.showOtpSuccess = function (message, containerSelector) {
    setTimeout(function () {
      var flag = (containerSelector ? document.querySelector(containerSelector) : document).querySelector('.otp-success-flag');
      flag.textContent = message;
      flag.classList.add('is-visible');
    }, 0);
  }

  otp.showOtpValidation = function (message, containerSelector) {
    setTimeout(function () {
      var flag = (containerSelector ? document.querySelector(containerSelector) : document).querySelector('.otp-error-flag');
      flag.textContent = message;
      flag.classList.add('is-visible');
    }, 0);
  }

  otp.hideOtpValidation = function () {
    var flags = document.querySelectorAll('.otp-error-flag, .otp-success-flag');

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