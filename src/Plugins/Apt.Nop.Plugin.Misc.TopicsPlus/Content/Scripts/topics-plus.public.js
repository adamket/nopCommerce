// /Plugins/Misc.TopicsPlus/Content/scripts/public.js
(function (window) {
  "use strict";

  const TopicsPlusPublic = {
    async fetchRequest({
      url,
      method = "GET",
      headers = {},
      body = null,
      timeout = 15000
    }) {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), timeout);

      try {
        let finalBody = null;
        const finalHeaders = {
          "X-Requested-With": "XMLHttpRequest",
          ...headers
        };

        if (body && method.toUpperCase() !== "GET") {
          if (body instanceof FormData) {
            finalBody = body;
          } else {
            finalBody = new URLSearchParams(body);
            finalHeaders["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8";
          }
        }

        const response = await fetch(url, {
          method,
          headers: finalHeaders,
          body: finalBody,
          signal: controller.signal
        });

        if (!response.ok) {
          const contentType = response.headers.get("content-type") || "";
          const errorBody = contentType.includes("application/json")
            ? await response.json()
            : await response.text();

          throw new Error(`Request failed (${response.status}): ${JSON.stringify(errorBody)}`);
        }

        if (response.status === 204) {
          return null;
        }

        const contentType = response.headers.get("content-type") || "";

        return contentType.includes("application/json")
          ? await response.json()
          : await response.text();
      } catch (error) {
        if (error.name === "AbortError") {
          console.error("TopicsPlus request timed out");
        } else {
          console.error("TopicsPlus request error:", error);
        }

        throw error;
      } finally {
        clearTimeout(timeoutId);
      }
    },

    getTopicId(editor) {
      return editor.element.closest("[data-topic-id]")?.dataset.topicId;
    },

    initInlineTopicEditor({
      titleSelector,
      bodySelector,
      sharedHighlightParentSelector,
      updateUrl = "/apt/topics-plus/update-topic",
      successMessage = "Saved!"
    }) {
      if (!window.InlineEditorLib?.InlineEditor) {
        console.warn("TopicsPlus: InlineEditorLib was not found.");
        return;
      }

      const [titleEditor] = window.InlineEditorLib.InlineEditor.createAll(titleSelector, {
        showToolbar: false,
        showSaveButton: false,
        getTopicId: TopicsPlusPublic.getTopicId
      });

      window.InlineEditorLib.InlineEditor.createAll(bodySelector, {
        sharedHighlightParentSelector,
        getTopicId: TopicsPlusPublic.getTopicId,

        onSave: async function (bodyEditor) {
          const topicId = bodyEditor.getTopicId();

          const requestBody = {
            topicId,
            title: titleEditor ? (titleEditor.element.textContent || "").trim() : "",
            content: bodyEditor.getHTML()
          };

          const response = await TopicsPlusPublic.fetchRequest({
            url: updateUrl,
            method: "POST",
            body: addAntiForgeryToken(requestBody)
          });

          if (!response.success) {
            bodyEditor.showError(response.message || "An error occurred");
            return;
          }

          if (titleEditor) {
            titleEditor.markSaved();
          }

          bodyEditor.markSaved();
          bodyEditor.showSuccess(successMessage, 3000);
        }
      });
    }
  };

  window.TopicsPlusPublic = TopicsPlusPublic;
  window.fetchRequest = TopicsPlusPublic.fetchRequest;
})(window);