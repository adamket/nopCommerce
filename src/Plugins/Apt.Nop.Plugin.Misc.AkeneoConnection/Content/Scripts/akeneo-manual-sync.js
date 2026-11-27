(function (global, $) {
    'use strict';

    // Polling observes a hosted job; stopping polling never cancels the job.
    function watch(statusUrl, onProgress, initialDelay) {
        var completion = $.Deferred();
        var failures = 0;

        function poll() {
            $.ajax({ type: 'GET', url: statusUrl, dataType: 'json', cache: false })
                .done(function (response) {
                    failures = 0;
                    if (response.pending) {
                        // The profile lookup discovers a job once. Follow that job's URL from
                        // then on so another run cannot replace it halfway through polling.
                        statusUrl = response.statusUrl || statusUrl;
                        if (onProgress) onProgress(response.message);
                        setTimeout(poll, 2000);
                    } else {
                        completion.resolve(response);
                    }
                })
                .fail(function (xhr) {
                    // Brief transport failures do not mean the sync itself failed.
                    if ((xhr.status === 0 || xhr.status === 502 || xhr.status === 503 || xhr.status === 504) && ++failures < 4) {
                        if (onProgress) onProgress('Reconnecting to check background sync status...');
                        setTimeout(poll, 5000);
                        return;
                    }
                    if (!xhr.responseJSON) {
                        xhr.responseJSON = { message: 'Unable to check the background sync. It may still be running; check sync history before starting another run.' };
                    }
                    completion.reject(xhr);
                });
        }

        if (initialDelay) setTimeout(poll, initialDelay);
        else poll();
        return completion.promise();
    }

    global.AkeneoManualSync = {
        watch: watch,
        post: function (options, onProgress) {
            var completion = $.Deferred();
            $.ajax(options).done(function (response) {
                if (!response.queued) {
                    completion.resolve(response);
                    return;
                }
                if (onProgress) onProgress(response.message);
                watch(response.statusUrl, onProgress, 1000)
                    .done(function (result) { completion.resolve(result); })
                    .fail(function (xhr) { completion.reject(xhr); });
            }).fail(function (xhr) {
                if (!xhr.responseJSON) {
                    xhr.responseJSON = { message: 'Unable to confirm whether the sync was queued. Check sync history before trying again.' };
                }
                completion.reject(xhr);
            });
            return completion.promise();
        }
    };
})(window, jQuery);
