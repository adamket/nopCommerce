(function (global, $) {
  global.apt = global.apt || {};

  var otp = apt.opt || {}
  var settings = {
    sendUrl: "/apt/request-otp",
    verifyUrl: "/apt/otp-login",
    localeStrings: {
      resendTryAgainErrorMessage: "Can resend again in {0}s",
      emailRequiredErrorMessage: "Email is required.",
    },
    selectors: {
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

    $(document).on('mt-begin-open:#otp', function (e, modalEl) {
      var currentEmail = $(settings.selectors.standardLoginEmailInput).val();
      $(settings.selectors.emailInput).val(currentEmail);
      state.email = currentEmail;
      //apt.otp.hideOtpValidation();
    });

    $(document).on('click', settings.selectors.backToStep1Button, function () {

      var $modalBodyEl = $(".mt-body"); //make some modal.getContent function
      $modalBodyEl.find('.otp-step-2').remove();
      $modalBodyEl.find('.otp-step-1').show();

      $modalBodyEl.html($modalBodyEl.data('page-1'));
    });
  };


  otp.sendOtp = async function (btn, resend, generateCode) {

    if (!state.email || !state.email.trim()) {
      apt.otp.showOtpValidation(settings.localeStrings.emailRequiredErrorMessage, settings.selectors.step1Container);
      return;
    }

    if (btn.loading() || btn.dataset.countdown) {
      return;
    }

    btn.loading(true);

    try {
      var response = await postJson(settings.sendUrl, { email: state.email, generateOtp: generateCode });
      if (!response.success) {

        if (resend && response.prematureOtpRequest && response.canResendInSeconds) {
          btn.loading(false);
          setCountdown(btn, response.canResendInSeconds);
        }

        apt.otp.showOtpValidation(response.message || "Invalid email", resend ? settings.selectors.step2Container : settings.selectors.step1Container);
        return;
      }

      if (response.success) {
        if (resend) {
          apt.otpInput.resetCode(document.querySelector(settings.selectors.step2Container));
          if (response.canResendInSeconds) {
            btn.loading(false);
            setCountdown(btn, response.canResendInSeconds);
          }
          return;
        }

        var $modalBodyEl = $(".mt-body");

        var $step2Container = $modalBodyEl.find('.otp-step-2');
        if (!$step2Container.length) {
          $modalBodyEl.append('<div class="otp-step-2"></div>')
        }

        $modalBodyEl.find(".otp-step-1").hide();
        $modalBodyEl.find('.otp-step-2').empty().append(response.markup).show();
        apt.otpInput.focus();

        return;
      } 
    } catch (e) {
      //  setMessage("Unable to send the code right now.", true);
    } finally {
      btn.loading(false);
    }
  };

  otp.verifyOtp = async function () {


    var btn = document.querySelector(settings.selectors.submitButton);

    if (btn.loading()) {
      return;
    }

    var otp = apt.otpInput.getCode(document.querySelector(settings.selectors.step2Container));

    if (otp.length < 6) {
      apt.otp.showOtpValidation("Please ensure all values are entered.", settings.selectors.step2Container);


      return;
    }

    btn.loading(true);

    try {
      var response = await postJson(settings.verifyUrl, { otp: otp, email: state.email });
      if (response.success) {
        location.href = response.returnUrl || '/';
        return;
      }

      apt.otp.showOtpValidation(response.message || "Invalid code.", settings.selectors.step2Container);
      btn.loading(false);

    } catch (e) {
      setMessage("Unable to process the code right now.", true);
      btn.loading(false);
      apt.otp.showOtpValidation("An error occurred.", settings.selectors.step2Container);
    }
  };


  otp.showOtpValidation = function (message, containerSelector) {
    setTimeout(function () {
      var flag = (containerSelector ? document.querySelector(containerSelector) : document).querySelector('.otp-validation-flag');
      flag.textContent = message;
      flag.classList.add('is-visible');
    }, 0);
  }

  otp.hideOtpValidation = function () {
    var flags = document.querySelectorAll('.otp-validation-flag');

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

    runCountdown(sendAgainSeconds,
      (s) => {
        btn.innerHTML = String.format(settings.localeStrings.resendTryAgainErrorMessage, s);
         /* `${settings.localeStrings.resendTryAgainMessage} ${s}s`;*/
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