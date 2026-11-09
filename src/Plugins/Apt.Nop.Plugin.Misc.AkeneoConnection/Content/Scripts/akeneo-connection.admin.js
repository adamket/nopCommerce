// Akeneo views provide their own loading feedback for admin requests.
if (document.querySelector('.akeneo-connection')) {
  document.getElementById('ajaxBusy')?.remove();
}

HTMLElement.prototype.loading = function (isLoading, options) {
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

// New log messages can use "product ID 123". Keep named products and lists readable in older logs.
window.AkeneoProductLog = (function () {
    function productReferenceParts(message) {
        message = message || '';
        var references = /\b(?:product\s+'(?:[^'\n]|'(?=\w))*'\s+\(ID\s+([1-9]\d*)(?=\))|(?:product\s+(?:IDs?\s*[:#]?\s*|#)|nopProductId\s*[:=]\s*)([1-9]\d*(?:,\s*[1-9]\d*)*))(?![\w-])/gi;
        var parts = [];
        var cursor = 0;
        for (var match of message.matchAll(references)) {
            var ids = match[1] || match[2];
            var start = match.index + match[0].length - ids.length;
            for (var number of ids.matchAll(/\d+/g)) {
                if (Number(number[0]) > 2147483647) continue;
                var offset = start + number.index;
                parts.push({ text: message.slice(cursor, offset) });
                parts.push({ text: number[0], productId: number[0] });
                cursor = offset + number[0].length;
            }
        }
        parts.push({ text: message.slice(cursor) });
        return parts;
    }

    const component = {
        props: ['message', 'productEditBase', 'parts'],
        render: function () {
            const parts = this.parts || productReferenceParts(this.message);
            return Vue.h('span', parts.map(part => part.productId
                ? Vue.h('a', {
                    href: this.productEditBase + part.productId,
                    target: '_blank',
                    rel: 'noopener noreferrer'
                }, part.text)
                : part.text));
        }
    };
    return { component, productReferenceParts };
})();
