const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { test } = require('node:test');

const view = fs.readFileSync(process.env.AKENEO_FAMILY_VIEW || path.join(__dirname, '../../Views/FamilyMapping/CreateOrUpdate.cshtml'), 'utf8');
const enums = {
    AkeneoVariantRelationshipMode: { None: 0, ProductAttributeCombinations: 10, AssociatedToProductAttributeValue: 20, GroupedProducts: 30 },
    AkeneoCombinationAvailabilitySource: { EnabledStatus: 0, PublishedRule: 10 },
    AkeneoSubModelParentLevel: { FamilySetting: 0, MatchingSubModel: 10 }
};
const script = view.match(/<script asp-location="Footer">([\s\S]*?)<\/script>/)[1]
    .replace('@Html.Raw(vueModelJson)', 'initialData')
    .replace(/@\(\(int\)(\w+)\.(\w+)\)/g, (_, type, key) => enums[type][key]);

function editor(overrides = {}) {
    let app;
    vm.runInNewContext(script, {
        initialData: { axisMappings: [], subModelRules: [], variantRelationshipModeId: 10,
            manageVariantsByEnabledStatus: true, combinationAvailabilitySourceId: 0, ...overrides },
        window: { AkeneoCombobox: {} },
        Vue: { createApp(options) {
            app = options.data();
            for (const [key, method] of Object.entries(options.methods)) app[key] = method.bind(app);
            for (const [key, computed] of Object.entries(options.computed)) Object.defineProperty(app, key, typeof computed === "function"
                ? { get: computed.bind(app) }
                : { get: computed.get.bind(app), set: computed.set.bind(app) });
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

test('availability choice maps to the switch and its source', () => {
    const app = editor();
    assert.equal(app.availabilityChoice, 'enabled');
    app.availabilityChoice = 'published';
    assert.equal(app.form.manageVariantsByEnabledStatus, true);
    assert.equal(app.form.combinationAvailabilitySourceId, 10);
    app.availabilityChoice = 'off';
    assert.equal(app.manageVariantsByEnabledStatusValue, 'false');
    assert.equal(app.form.combinationAvailabilitySourceId, 0);
    assert.deepEqual(Array.from(app.availabilityOptions, option => option.value), ['off', 'enabled', 'published']);
});

test('a saved Published rule source shows as the Published rule choice', () => {
    assert.equal(editor({ combinationAvailabilitySourceId: 10 }).availabilityChoice, 'published');
    assert.equal(editor({ manageVariantsByEnabledStatus: false, combinationAvailabilitySourceId: 10 }).availabilityChoice, 'off');
    assert.match(view, /name="CombinationAvailabilitySourceId" v-bind:value="form.combinationAvailabilitySourceId"/);
});
