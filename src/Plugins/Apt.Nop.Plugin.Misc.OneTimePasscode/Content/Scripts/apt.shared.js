document.addEventListener('keydown', function (e) {
  if (e.target.matches('input[data-enter-clicks]') && e.key === 'Enter') {
    e.preventDefault();
    const buttonId = e.target.dataset.enterClicks;

    e.target.blur();
    document.getElementById(buttonId)?.click();
  }
});

const LOADING_ATTR = 'data-loading';
const ORIGINAL_HTML_ATTR = 'data-original-html';

HTMLElement.prototype.loading = function (isLoading) {
  var element = this;

  if (isLoading === undefined) {
    return element.hasAttribute(LOADING_ATTR);
  }

  if (isLoading) {
    const { width, height } = this.getBoundingClientRect();
    this.setAttribute(ORIGINAL_HTML_ATTR, this.innerHTML);
    this.style.width = `${width}px`;
    this.style.height = `${height}px`;
    this.style.display = 'flex';
    this.style.alignItems = 'center';
    this.style.justifyContent = 'center';
    this.setAttribute(LOADING_ATTR, '');
    this.disabled = true;
    this.innerHTML = `
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
    const originalHTML = this.getAttribute(ORIGINAL_HTML_ATTR);
    if (originalHTML !== null) this.innerHTML = originalHTML;
    this.style.width = '';
    this.style.height = '';
    this.style.display = '';
    this.style.alignItems = '';
    this.style.justifyContent = '';
    this.removeAttribute(LOADING_ATTR);
    this.removeAttribute(ORIGINAL_HTML_ATTR);
    this.disabled = false;
  }

  return this;
}


function runCountdown(durationSeconds, callback, done) {
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
}

if (!String.format) {
  String.format = function (str, ...args) {
    return str.replace(/\{(\d+)\}/g, (match, index) => {
      return args[index] !== undefined ? args[index] : match;
    });
  };
}