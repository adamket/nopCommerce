(function (window, $) {
  'use strict';

  class TopicsPlusAdmin {
    constructor(options) {
      this.options = Object.assign({
        previewModalSelector: '#topic-revision-preview-modal',
        previewFrameSelector: '#topic-revision-preview-frame',
        previewUrlTemplate: '/apt/topics-plus/preview/{id}',
        previewTitleTemplate: 'Topic revision created on {createdOn}',
        revertConfirmButtonSelector: '#topic-revision-revert-confirm-button',
        topicId: null
      }, options || {});

      this.topicId = this.options.topicId;
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
            data-topic-id="${row.TopicId}"
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

      $('#topic-revision-revert-confirm-button-action-confirmation-submit-button')
        .on('click', (event) => {
          const yesButton = event.currentTarget;

          if (yesButton.dataset.loading === 'true') {
            event.preventDefault();
            return;
          }

          this.setButtonLoading(yesButton, true);
          this.confirmRevertRevision(yesButton);
        });
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

    confirmRevertRevision(confirmButton) {
      const revisionId = this.pendingRevertRevisionId;
      const button = this.pendingRevertButton;
      const topicId = this.topicId;

      if (!revisionId || !button) {
        this.setButtonLoading(confirmButton, false);
        return;
      }

      $.ajax({
        url: `/admin/apt/topics-plus/revert/`,
        type: 'POST',
        data: addAntiForgeryToken({
          topicRevisionId: revisionId,
          topicId: topicId
        })
      }).done(function (response) {
        if (response.success) {
          location.reload();
          return;
        }

        this.setButtonLoading(confirmButton, false);
      }.bind(this)).fail(function () {
        this.setButtonLoading(confirmButton, false);
      }.bind(this));
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

  window.TopicsPlusAdmin = TopicsPlusAdmin;

})(window, window.jQuery);