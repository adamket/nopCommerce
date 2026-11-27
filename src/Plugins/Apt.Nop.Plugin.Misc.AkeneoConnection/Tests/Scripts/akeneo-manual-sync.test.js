const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function harness(responses) {
    const calls = [], timers = [];
    function deferred() {
        let state, value;
        const success = [], failure = [];
        const promise = {
            always(callback) { promise.done(callback); promise.fail(callback); return promise; },
            done(callback) { if (state === 'done') callback(value); else success.push(callback); return promise; },
            fail(callback) { if (state === 'fail') callback(value); else failure.push(callback); return promise; }
        };
        return {
            resolve(result) { state = 'done'; value = result; success.forEach(callback => callback(result)); return this; },
            reject(error) { state = 'fail'; value = error; failure.forEach(callback => callback(error)); return this; },
            promise() { return promise; }
        };
    }
    const jQuery = {
        Deferred: deferred,
        ajax(options) {
            calls.push(options);
            const response = responses.shift();
            assert.ok(response, 'unexpected extra request');
            const request = deferred();
            return (response.error ? request.reject(response.error) : request.resolve(response)).promise();
        }
    };
    const context = { window: {}, jQuery, setTimeout(callback) { timers.push(callback); } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../Content/Scripts/akeneo-manual-sync.js'), 'utf8'), context);
    return { client: context.window.AkeneoManualSync, calls, tick() { assert.ok(timers.length); timers.shift()(); } };
}

test('queued and running responses keep the UI pending until the final result', () => {
    const setup = harness([
        { queued: true, statusUrl: '/status/job', message: 'queued' },
        { pending: true, message: 'running' },
        { success: true, counts: { updated: 17 } }
    ]);
    const progress = [];
    let result;
    setup.client.post({ type: 'POST', url: '/run' }, message => progress.push(message)).done(response => result = response);
    assert.equal(result, undefined);
    setup.tick();
    assert.equal(result, undefined);
    setup.tick();
    assert.equal(result.counts.updated, 17);
    assert.deepEqual(progress, ['queued', 'running']);
    assert.deepEqual(setup.calls.map(call => call.type), ['POST', 'GET', 'GET']);
});

test('transient polling failure retries status without submitting another sync', () => {
    const setup = harness([
        { queued: true, statusUrl: '/status/job' },
        { error: { status: 503 } },
        { success: true }
    ]);
    let result;
    setup.client.post({ type: 'POST', url: '/run' }).done(response => result = response).fail(() => assert.fail('unexpected rejection'));
    setup.tick();
    assert.equal(result, undefined);
    setup.tick();
    assert.equal(result.success, true);
    assert.equal(setup.calls.filter(call => call.type === 'POST').length, 1);
});

test('a recorded server-side failure preserves its diagnostic response', () => {
    const failure = { status: 500, responseJSON: { message: 'Akeneo request failed', errors: ['diagnostic detail'] } };
    const setup = harness([{ queued: true, statusUrl: '/status/job' }, { error: failure }]);
    let result;
    setup.client.post({ type: 'POST', url: '/run' }).fail(response => result = response);
    setup.tick();
    assert.equal(result, failure);
});

function profilePage(setup) {
    const view = fs.readFileSync(path.join(__dirname, '../../Views/SyncProfile/CreateOrUpdate.cshtml'), 'utf8');
    const methods = view.slice(view.indexOf('                reconnectSync: function'), view.indexOf('                loadLock: function'));
    const page = vm.runInNewContext('({' + methods + '})', { AkeneoManualSync: setup.client });
    Object.assign(page, {
        urls: { latestSync: '/profile/7/latest' }, busy: false, runResult: null,
        loadLock() { page.lockRefreshed = true; }
    });
    return page;
}

test('refresh discovers the active profile job and pins subsequent polls to that job', () => {
    const setup = harness([
        { pending: true, statusUrl: '/status/job-7', message: 'running' },
        { pending: true, message: 'still running' },
        { success: true, message: 'finished', counts: { updated: 17 }, warnings: ['review this'], errors: [] }
    ]);
    const page = profilePage(setup);
    page.reconnectSync();
    assert.equal(page.busy, 'run');
    assert.equal(page.runResult.cls, 'alert-info');
    setup.tick();
    assert.equal(page.busy, 'run');
    setup.tick();
    assert.equal(page.busy, false);
    assert.equal(page.runResult.cls, 'alert-success');
    assert.equal(page.runResult.counts.updated, 17);
    assert.equal(page.runResult.warnings[0], 'review this');
    assert.equal(page.lockRefreshed, true);
    assert.deepEqual(setup.calls.map(call => call.url), ['/profile/7/latest', '/status/job-7', '/status/job-7']);
    assert.ok(setup.calls.every(call => call.type === 'GET'));
});

test('refresh after completion immediately displays the retained result', () => {
    const setup = harness([{ success: true, message: 'already finished', counts: { updated: 3 } }]);
    const page = profilePage(setup);
    page.reconnectSync();
    assert.equal(page.busy, false);
    assert.equal(page.runResult.message, 'already finished');
    assert.equal(page.runResult.counts.updated, 3);
    assert.equal(setup.calls.length, 1);
});

test('a profile with no background job stays idle without a completion message', () => {
    const setup = harness([{ idle: true }]);
    const page = profilePage(setup);
    page.reconnectSync();
    assert.equal(page.busy, false);
    assert.equal(page.runResult, null);
    assert.equal(setup.calls.length, 1);
});

test('a refreshed page shows cancellation and failure details', () => {
    for (const response of [
        { canceled: true, message: 'canceled by shutdown', errors: ['original exception'] },
        { error: { status: 500, responseJSON: { message: 'failed', errors: ['original failure'] } } }
    ]) {
        const setup = harness([response]);
        const page = profilePage(setup);
        page.reconnectSync();
        assert.equal(page.busy, false);
        assert.equal(page.runResult.cls, response.canceled ? 'alert-warning' : 'alert-danger');
        assert.equal(page.runResult.errors[0], response.canceled ? 'original exception' : 'original failure');
    }
});

test('creating a profile does not attempt reconnection', () => {
    const setup = harness([]);
    const page = profilePage(setup);
    page.urls.latestSync = null;
    page.reconnectSync();
    assert.equal(page.busy, false);
    assert.equal(setup.calls.length, 0);
});
