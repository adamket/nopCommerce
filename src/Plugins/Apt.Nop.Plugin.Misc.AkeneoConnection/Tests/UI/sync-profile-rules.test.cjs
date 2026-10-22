// Run with: node --test Tests/UI/sync-profile-rules.test.cjs
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { test } = require('node:test');

const view = fs.readFileSync(process.env.AKENEO_PROFILE_VIEW ||
    path.join(__dirname, '../../Views/SyncProfile/CreateOrUpdate.cshtml'), 'utf8');
const script = view.match(/<script asp-location="Footer">([\s\S]*?)<\/script>/)[1];

function editor(form = {}) {
    const data = { id: 7, savedEnabled: true,
        form: { name: 'Store', enabled: true, skipRules: [], ...form },
        options: {}, updatedFilterModeIds: {}, urls: {} };
    let app;
    vm.runInNewContext(script, {
        document: { getElementById: () => ({ textContent: JSON.stringify(data) }) },
        window: {}, addAntiForgeryToken: data => data,
        Vue: { createApp(options) {
            app = options.data();
            for (const [key, method] of Object.entries(options.methods)) app[key] = method.bind(app);
            for (const [key, getter] of Object.entries(options.computed))
                Object.defineProperty(app, key, { get: getter.bind(app) });
            return { component() {}, mount() {} };
        } }
    });
    return app;
}

test('adding a general skip rule marks form dirty and posts the entered values', () => {
    const app = editor();
    assert.equal(app.isDirty, false);
    app.addSkipRule();
    assert.equal(app.form.skipRules[0].attributeCode, '');
    assert.equal(app.form.skipRules[0].value, '');
    app.form.skipRules[0].attributeCode = 'approval';
    app.form.skipRules[0].value = 'pending';
    assert.equal(app.isDirty, true);
    const posted = app.buildPostData();
    assert.deepEqual(JSON.parse(posted.SkipRulesJson), [{ attributeCode: 'approval', value: 'pending' }]);
    assert.equal(posted.Id, 7);
    assert.equal('ValueMappingsJson' in posted, false);
});

test('adding a rule preserves existing skip configuration', () => {
    const app = editor({ skipRules: [{ attributeCode: 'approval', value: 'pending' }] });
    app.addSkipRule();
    assert.equal(app.form.skipRules.length, 2);
    assert.equal(app.form.skipRules[0].attributeCode, 'approval');
    assert.equal(app.form.skipRules[0].value, 'pending');
    assert.equal(app.form.skipRules[1].attributeCode, '');
});

test('saved rules round trip and removing the last rule posts an empty array', () => {
    const row = { attributeCode: 'approval', value: 'pending' };
    const app = editor({ skipRules: [row] });
    assert.deepEqual(JSON.parse(app.buildPostData().SkipRulesJson), [row]);
    app.form.skipRules.splice(0, 1);
    assert.equal(app.buildPostData().SkipRulesJson, '[]');
    assert.equal(app.isDirty, true);
});
