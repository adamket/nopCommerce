(function (global, $) {
  global.apt = global.apt || {};

  var otp = apt.opt || {}
  var settings = {
    sendUrl: "/apt/request-otp",
    verifyUrl: "/apt/otp-login",
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
      otp.sendOtp();
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


  otp.sendOtp = async function () {

    if (!state.email) {
      apt.otp.showOtpValidation("Please enter a valid email address.", settings.selectors.step1Container);
      return;
    }

    var btn = document.querySelector(settings.selectors.sendButton);
    if (btn.loading()) {
      return;
    }


    btn.loading(true);

    try {
      var response = await postJson(settings.sendUrl, { email: state.email });
      if (response.success) {

        var $modalBodyEl = $(".mt-body");
        $modalBodyEl.find(".otp-step-1").hide();

        $modalBodyEl.append(response.markup);

        apt.otpInput.focus();

        return;
      } else {
        apt.otp.showOtpValidation(response.message || "Invalid email", settings.selectors.step1Container);
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

    var otp = apt.otpInput.getCode(document.querySelector(settings.selectors.wrapperElement));

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


  function postJson(url, data) {
    return $.ajax({
      url: url,
      type: "POST",
      data: addAntiForgeryToken(data)
    });
  }


  global.apt.otp = otp;
})(window, jQuery);