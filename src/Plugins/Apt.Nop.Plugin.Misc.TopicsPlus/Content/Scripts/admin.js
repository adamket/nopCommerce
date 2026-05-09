(function (window, $) {
  'use strict';

  class TopicRevisionAdmin {
    constructor(options) {
      this.options = Object.assign({
        previewModalSelector: '#topic-revision-preview-modal',
        previewFrameSelector: '#topic-revision-preview-frame',
        previewUrlTemplate: '/apt/topics-plus/preview/{id}',
        previewTitleTemplate: 'Topic revision created on {createdOn}',
        revertConfirmButtonSelector: '#topic-revision-revert-confirm-button'
      }, options || {});
      this.pendingRevertRevisionId = null;
      this.pendingRevertButton = null;
      this.currentPreviewToken = 0;
      this.activePreviewButton = null;
    }

    renderViewCol(data, type, row, meta) {
      const createdOn = this.escapeAttribute(row.CreatedOn || '');

      return `<button type="button"
        class="btn btn-primary topic-revision-preview-button"
        data-topic-revision-preview
        data-revision-id="${row.Id}"
        data-created-on="${createdOn}">
    <i class="fas fa-eye"></i>
    <span>View</span>
</button>`;
    }

    renderRevertCol(data, type, row, meta) {

      if (row.HideRevertButton) {
        return '';
      }

        return `  <button type="button"
            class="btn btn-default topic-revision-revert-button"
            data-topic-revision-revert
            data-revision-id="${row.Id}">
        <i class="fas fa-history"></i>
        <span>Revert</span>
    </button></div>`;
    }

    init() {
      document.addEventListener('click', (event) => {
        const previewButton = event.target.closest('[data-topic-revision-preview]');
        if (previewButton) {
          event.preventDefault();

          const revisionId = previewButton.dataset.revisionId;
          const createdOn = previewButton.dataset.createdOn;

          this.openPreview(revisionId, createdOn, previewButton);
          return;
        }

        const revertButton = event.target.closest('[data-topic-revision-revert]');
        if (revertButton) {
          event.preventDefault();

          const revisionId = revertButton.dataset.revisionId;
          this.revertRevision(revisionId, revertButton);
        }
      });

      const confirmButton = document.querySelector(this.options.revertConfirmButtonSelector);

      if (confirmButton) {
        confirmButton.addEventListener('click', (event) => {
          if (confirmButton.dataset.confirmed !== 'true') {
            return;
          }

          event.preventDefault();

          confirmButton.dataset.confirmed = 'false';

          this.confirmRevertRevision();
        });
      }
    }

    openPreview(revisionId, createdOn, button) {
      const iframe = document.querySelector(this.options.previewFrameSelector);
      const modal = document.querySelector(this.options.previewModalSelector);

      if (!iframe || !modal) {
        return;
      }

      const token = ++this.currentPreviewToken;

      if (this.activePreviewButton) {
        this.setButtonLoading(this.activePreviewButton, false);
      }

      this.activePreviewButton = button;
      this.setButtonLoading(button, true);

      const modalTitle = modal.querySelector('.modal-title');
      if (modalTitle) {
        modalTitle.textContent = this.options.previewTitleTemplate.replace('{createdOn}', createdOn || '');
      }

      iframe.onload = () => {
        if (token !== this.currentPreviewToken) {
          return;
        }

        this.setButtonLoading(this.activePreviewButton, false);
        this.activePreviewButton = null;

        $(this.options.previewModalSelector).modal('show');
      };

      iframe.src = this.options.previewUrlTemplate.replace('{id}', encodeURIComponent(revisionId));
    }

    revertRevision(revisionId, button) {
      this.pendingRevertRevisionId = revisionId;
      this.pendingRevertButton = button;

      const confirmButton = document.querySelector(this.options.revertConfirmButtonSelector);

      if (!confirmButton) {
        return;
      }

      confirmButton.click();
    }

    setButtonLoading(button, isLoading) {
      if (!button) {
        return;
      }

      if (isLoading) {
        if (button.dataset.loading === 'true') {
          return;
        }

        button.dataset.loading = 'true';
        button.dataset.originalHtml = button.innerHTML;

        const width = button.offsetWidth;
        const height = button.offsetHeight;

        button.style.width = width + 'px';
        button.style.height = height + 'px';

        button.disabled = true;
        button.classList.add('loading');

        button.innerHTML = `
                    <span class="spinner-border spinner-border-sm"
                          role="status"
                          aria-hidden="true"></span>
                `;
      } else {
        if (button.dataset.loading !== 'true') {
          return;
        }

        button.innerHTML = button.dataset.originalHtml || 'View';

        button.style.width = '';
        button.style.height = '';

        button.disabled = false;
        button.classList.remove('loading');

        delete button.dataset.originalHtml;
        delete button.dataset.loading;
      }
    }

    escapeAttribute(value) {
      return String(value)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
    }
  }

  window.TopicRevisionAdmin = TopicRevisionAdmin;

})(window, window.jQuery);