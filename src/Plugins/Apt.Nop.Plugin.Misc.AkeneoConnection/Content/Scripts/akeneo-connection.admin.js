Element.prototype.loading = function (isLoading, options) {
  options = options || {};

  var element = this;

  if (isLoading === undefined) {
    return element.dataset.loading === 'true';
  }

  if (isLoading === true) {
    if (element.dataset.loading === 'true') {
      return element;
    }

    element.dataset.loading = 'true';
    element.dataset.previousHtml = element.innerHTML;
    element.dataset.previousWidth = element.style.width || '';

    var width = element.offsetWidth;
    var height = options.height || '1rem';
    var spinnerClass = options.spinnerClass || 'spinner-border spinner-border-sm';

    element.style.width = width + 'px';
    element.disabled = true;
    element.classList.add('adf-loading');

    element.innerHTML =
      '<span class="' + spinnerClass + '"' +
      ' role="status"' +
      ' aria-hidden="true"' +
      ' style="width:' + height + ';height:' + height + ';">' +
      '</span>';

    return element;
  }

  if (element.dataset.loading !== 'true') {
    return element;
  }

  element.innerHTML = element.dataset.previousHtml || '';
  element.style.width = element.dataset.previousWidth || '';
  element.disabled = false;
  element.classList.remove('adf-loading');

  delete element.dataset.loading;
  delete element.dataset.previousHtml;
  delete element.dataset.previousWidth;

  return element;
};