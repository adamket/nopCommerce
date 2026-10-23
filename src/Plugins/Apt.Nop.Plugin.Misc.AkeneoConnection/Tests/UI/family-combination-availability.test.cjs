const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { test } = require('node:test');

const view = fs.readFileSync(process.env.AKENEO_FAMILY_VIEW || path.join(__dirname, '../../Views/FamilyMapping/CreateOrUpdate.cshtml'), 'utf8');
const enums = { None: 0, ProductAttributeCombinations: 10, AssociatedToProductAttributeValue: 20, GroupedProducts: 30 };
const script = view.match(/<script asp-location="Footer">([\s\S]*?)<\/script>/)[1]
    .replace('@Html.Raw(vueModelJson)', 'initialData')
    .replace(/@\(\(int\)AkeneoVariantRelationshipMode\.(\w+)\)/g, (_, key) => enums[key]);

function editor(overrides = {}) {
    let app;
    vm.runInNewContext(script, {
        initialData: { axisMappings: [], subModelRules: [], variantRelationshipModeId: 10,
            manageVariantsByEnabledStatus: true, ...overrides },
        window: { AkeneoCombobox: {} },
        Vue: { createApp(options) {
            app = options.data();
            for (const [key, method] of Object.entries(options.methods)) app[key] = method.bind(app);
            for (const [key, getter] of Object.entries(options.computed)) Object.defineProperty(app, key, { get: getter.bind(app) });
            return { component() { return this; }, mount() {} };
        } }
    });
    return app;
}

test('family form posts the enabled-status toggle as true or false', () => {
    const app = editor();
    assert.equal(app.manageVariantsByEnabledStatusValue, 'true');
    app.form.manageVariantsByEnabledStatus = false;
    assert.equal(app.manageVariantsByEnabledStatusValue, 'false');
    assert.match(view, /name="ManageVariantsByEnabledStatus" v-bind:value="manageVariantsByEnabledStatusValue"/);
});

test('switching to a different representation clears combination availability', () => {
    const app = editor();
    app.form.variantRelationshipModeId = 30;
    assert.equal(app.isCombinationMode, false);
    assert.equal(app.manageVariantsByEnabledStatusValue, 'false');
});

test('disabled setting stays off', () => {
    const app = editor({ manageVariantsByEnabledStatus: false });
    assert.equal(app.manageVariantsByEnabledStatusValue, 'false');
});

test('family editor offers one toggle without a condition template', () => {
 assert.equal(view.includes('fm-combination-availability'), false);
 assert.equal(view.includes('Available when'), false);
 assert.match(view, /Manage variants by Akeneo enabled status/);
});
