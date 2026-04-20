(function (global, $) {
  global.apt = global.apt || {};
  var shared = global.apt.shared || {};

  const LOADING_ATTR = 'data-loading';
  const ORIGINAL_HTML_ATTR = 'data-original-html';

  shared.loading = function (element, isLoading) {
    if (isLoading === undefined) {
      return element.hasAttribute(LOADING_ATTR);
    }

    if (isLoading) {
      const { width, height } = element.getBoundingClientRect();
      element.setAttribute(ORIGINAL_HTML_ATTR, element.innerHTML);
      element.style.width = `${width}px`;
      element.style.height = `${height}px`;
      element.style.display = 'flex';
      element.style.alignItems = 'center';
      element.style.justifyContent = 'center';
      element.setAttribute(LOADING_ATTR, '');
      element.disabled = true;
      element.innerHTML = `
  <svg
    class="spinner-svg"
    width="28"
    height="28"
    viewBox="0 0 24 24"
    xmlns="http://www.w3.org/2000/svg"
    aria-hidden="true"
  >
    <circle
      class="spinner-track"
      cx="12"
      cy="12"
      r="9"
      fill="none"
      stroke="currentColor"
      stroke-width="3.5"
    />
    <circle
      class="spinner-arc"
      cx="12"
      cy="12"
      r="9"
      fill="none"
      stroke="currentColor"
      stroke-width="3.5"
      stroke-linecap="round"
    />
  </svg>
`;
    } else {
      const originalHTML = element.getAttribute(ORIGINAL_HTML_ATTR);
      if (originalHTML !== null) element.innerHTML = originalHTML;
      element.style.width = '';
      element.style.height = '';
      element.style.display = '';
      element.style.alignItems = '';
      element.style.justifyContent = '';
      element.removeAttribute(LOADING_ATTR);
      element.removeAttribute(ORIGINAL_HTML_ATTR);
      element.disabled = false;
    }

    return element;
  };

  shared.runCountdown = function (durationSeconds, callback, done) {
    const end = Date.now() + durationSeconds * 1000;
    function tick() {
      const remaining = Math.max(0, Math.ceil((end - Date.now()) / 1000));
      callback(remaining);
      if (remaining > 0) {
        setTimeout(tick, 250);
      } else if (done) {
        done();
      }
    }
    tick();
  };

  shared.formatString = function (str, ...args) {
    return str.replace(/\{(\d+)\}/g, function (match, index) {
      return args[index] !== undefined ? args[index] : match;
    });
  };

  document.addEventListener('keydown', function (e) {
    if (e.target.matches('input[data-enter-clicks]') && e.key === 'Enter') {
      e.preventDefault();
      const buttonId = e.target.dataset.enterClicks;
      e.target.blur();
      document.getElementById(buttonId)?.click();
    }
  });

  global.apt.shared = shared;
})(window, jQuery);