// Run with: node --test Tests/UI/attribute-mapping-editor.test.cjs
// Exercise the actual Vue options from the Razor view without a running store.
const { readFileSync } = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { test } = require('node:test');

const viewPath = process.env.AKENEO_MAPPING_VIEW ||
    path.join(__dirname, '../../Views/AttributeMapping/AttributeMappings.cshtml');
const view = readFileSync(viewPath, 'utf8');
const script = view.match(/<script asp-location="Footer">([\s\S]*?)<\/script>/)[1]
    .replaceAll('@@', '@');

function attribute(code, overrides = {}) {
    return {
        id: 0, code, label: code, group: 'general', groupLabel: 'General',
        valueModeId: 0, isComputed: false, isReferenceEntityAttribute: false,
        attributeTypeId: 10, nopTargetTypeId: '0', nopTargetKey: '',
        nopTargetEntityId: '', entityScopeId: 7, fallbackSources: [],
        availableTargetTypes: [{ value: '0', text: 'Ignore' }, { value: '1', text: 'Product Field' }],
        availableReferenceEntityAttributes: [], ...overrides
    };
}

function reference(overrides = {}) {
    return {
        key: 'general::plant_info::plant', code: 'plant_info', label: 'Plant information',
        group: 'general', groupLabel: 'General', isReferenceEntityAttribute: true,
        referenceEntityCode: 'plant', attributeTypeId: 50,
        availableReferenceEntityAttributes: [
            { value: '', text: 'Select field' }, { value: '@code', text: 'Record code' },
            { value: 'name', text: 'Name' }
        ], ...overrides
    };
}

function editor(overrides = {}) {
    const data = {
        mappings: [attribute('name'), attribute('price', { id: 12, nopTargetTypeId: '1' })],
        referenceEntityAttributes: [reference()],
        entityScopes: { productModel: 1, variantProduct: 2, standaloneProduct: 4, all: 7 },
        valueModes: { singleAttribute: 0, template: 1 },
        targetTypes: { ignore: 0, productField: 1 },
        availableFallbackSources: [{ key: 'name', text: 'Name', akeneoAttributeCode: 'name' }],
        ...overrides
    };
    let app;
    let request;
    vm.runInNewContext(script, {
        document: { getElementById: id => ({ textContent: JSON.stringify(id === 'akeneo-app-data' ? data : {}) }) },
        window: { crypto: { randomUUID: () => 'unique-computed-key' } },
        Vue: { createApp(options) {
            app = options.data();
            app.$nextTick = () => {}; // Scrolling is not part of this editor-state harness.
            for (const [name, method] of Object.entries(options.methods)) app[name] = method.bind(app);
            for (const [name, getter] of Object.entries(options.computed)) {
                Object.defineProperty(app, name, { get: getter.bind(app) });
            }
            return { component() { return this; }, mount() {} };
        } },
        $: { ajax: options => { request = options; } },
        addAntiForgeryToken: data => data,
        setTimeout() {}
    });
    return { app, request: () => request };
}

test('initial list includes saved Ignore and inherited mappings, never unused attributes or empty reference containers', () => {
    const { app } = editor({ mappings: [attribute('unused'), attribute('disabled', { id: 1 }), attribute('global', { id: 2, isInherited: true })] });
    assert.equal(app.mappings.length, 2);
    assert.equal(app.groups.length, 1);
    assert.equal(app.groups[0].entries.length, 2);
    assert.equal(app.isExpanded('General'), true);
    app.toggleGroup('General');
    assert.equal(app.isExpanded('General'), false);
    assert.ok(app.availableSources.some(source => source.code === 'unused'));
    assert.ok(!app.availableSources.some(source => source.code === 'global'));
});

test('add, duplicate prevention and cancel restore the source to the picker', () => {
    const { app } = editor({ selectedFamilyCode: 'trees' });
    app.newSourceCode = 'name';
    app.addMapping();
    const draft = app.mappings.find(mapping => mapping.code === 'name');
    assert.equal(draft.clientAdded, true);
    assert.equal(draft.editing, true);
    assert.equal(draft.akeneoFamilyCode, 'trees');
    assert.ok(!app.availableSources.some(source => source.code === 'name'));
    app.newSourceCode = 'name';
    app.addMapping();
    assert.equal(app.mappings.length, 2);
    app.cancel(draft);
    assert.ok(app.availableSources.some(source => source.code === 'name'));
});

test('source options include labels, codes and groups without losing template tokens', () => {
    const { app } = editor();
    const option = app.availableSourceOptions.find(source => source.value === 'plant_info');
    assert.match(option.label, /Plant information.*plant_info.*Reference entity/);
    assert.equal(option.hint, 'General');
    assert.ok(app.templateTokenOptions().some(token => token.key === 'name'));
    assert.equal(app.dropdownOptions([], 'Global defaults')[0].value, '');
    assert.equal(app.dropdownOptions([{ key: 'height', text: 'Height' }])[0].hint, '{height}');
});

test('computed mappings can be created without any family attributes', () => {
    const { app } = editor({ mappings: [], referenceEntityAttributes: [] });
    assert.equal(app.groups.length, 0);
    app.newMappingKind = 'computed';
    app.addMapping();
    assert.equal(app.mappings[0].isComputed, true);
    assert.equal(app.mappings[0].editing, true);
    assert.equal(app.groups[0].code, '__computed');
    app.cancel(app.mappings[0]);
    assert.equal(app.groups.length, 0);
});

test('reference containers appear only after adding a field; drafts and exhausted fields cannot be added twice', () => {
    const { app } = editor({ mappings: [] });
    app.newSourceCode = 'plant_info';
    app.addMapping();
    assert.equal(app.groups[0].entries[0].isReferenceEntity, true);
    assert.equal(app.isReferenceExpanded(reference().key), true);
    assert.ok(!app.availableSources.some(source => source.code === 'plant_info'));
    app.cancel(app.mappings[0]);
    assert.equal(app.groups.length, 0);
    assert.ok(app.availableSources.some(source => source.code === 'plant_info'));
    for (const field of ['@code', 'name']) {
        app.newSourceCode = 'plant_info';
        app.addMapping();
        const draft = app.mappings.at(-1);
        draft.id = app.mappings.length;
        draft.clientAdded = false;
        draft.editing = false;
        draft.referenceEntityAttributeCode = field;
    }
    assert.equal(app.availableSources.length, 0);
    assert.equal(app.canAddReferenceMapping(app.groups[0].entries[0]), false);
});

test('delete removes a saved row; re-adding uses clean settings', () => {
    const { app, request } = editor();
    app.deletePersistedMapping(app.mappings[0]);
    request().success({ success: true });
    assert.equal(app.mappings.length, 0);
    app.newSourceCode = 'price';
    app.addMapping();
    assert.equal(app.mappings[0].id, 0);
    assert.equal(app.mappings[0].nopTargetTypeId, '0');
    assert.equal(app.mappings[0].clientAdded, true);
});

test('deleting a family override restores its global mapping instead of exposing a duplicate source', () => {
    const { app, request } = editor({ selectedFamilyCode: 'trees' });
    app.deletePersistedMapping(app.mappings[0]);
    request().success({ success: true, replacementMapping: { id: 7, isInherited: true, nopTargetTypeId: '1', nopTargetKey: 'Price' } });
    assert.equal(app.mappings.length, 1);
    assert.equal(app.mappings[0].id, 7);
    assert.equal(app.mappings[0].isInherited, true);
    assert.equal(app.canDeleteMapping(app.mappings[0]), false);
    assert.ok(!app.availableSources.some(source => source.code === 'price'));
});

test('save sends the selected source and family, then retains the saved row', () => {
    const { app, request } = editor({ selectedFamilyCode: 'trees' });
    app.newSourceCode = 'name';
    app.addMapping();
    const draft = app.mappings.at(-1);
    draft.draft.nopTargetTypeId = '1';
    draft.draft.nopTargetKey = 'Name';
    app.save(draft);
    assert.equal(request().data.AkeneoAttributeCode, 'name');
    assert.equal(request().data.AkeneoFamilyCode, 'trees');
    assert.equal(request().data.NopTargetKey, 'Name');
    request().success({ success: true, id: 19, mappingKey: 'name' });
    assert.equal(draft.id, 19);
    assert.equal(draft.clientAdded, false);
    assert.equal(draft.editing, false);
    assert.ok(!app.availableSources.some(source => source.code === 'name'));
});

test('computed condition saves, reloads into a draft, and survives cancel', () => {
    const { app, request } = editor({ mappings: [], referenceEntityAttributes: [] });
    app.newMappingKind = 'computed';
    app.addMapping();
    const row = app.mappings.at(-1);
    row.draft.name = 'Publication';
    row.draft.nopTargetTypeId = '1';
    row.draft.nopTargetKey = 'Published';
    assert.equal(row.draft.applyWhenEnabled, false);
    row.draft.applyWhenEnabled = true;
    row.draft.applyWhen = 'sync_status == published OR sync_status == unpublished';
    app.save(row);
    assert.equal(request().data.ApplyWhen, row.draft.applyWhen);
    request().success({ success: true, id: 42, mappingKey: 'publication' });
    assert.equal(row.applyWhen, 'sync_status == published OR sync_status == unpublished');
    app.edit(row);
    assert.equal(row.draft.applyWhen, row.applyWhen);
    assert.equal(row.draft.applyWhenEnabled, true);
    row.draft.applyWhen = 'sync_status != hold';
    app.cancel(row);
    assert.equal(row.applyWhen, 'sync_status == published OR sync_status == unpublished');
});

test('saving an unchecked condition clears it and reopening stays unchecked', () => {
    const { app, request } = editor({ mappings: [attribute('computed', {
        id: 42, isComputed: true, valueModeId: 1, applyWhen: 'sync_status != hold', nopTargetTypeId: '1'
    })] });
    const row = app.mappings[0];
    app.edit(row);
    assert.equal(row.draft.applyWhenEnabled, true);
    row.draft.applyWhenEnabled = false;
    app.cancel(row);
    app.edit(row);
    assert.equal(row.draft.applyWhenEnabled, true);
    row.draft.applyWhenEnabled = false;
    app.save(row);
    assert.equal(request().data.ApplyWhen, '');
    request().success({ success: true, id: 42 });
    assert.equal(row.applyWhen, '');
    assert.equal(row.draft.applyWhen, '');
    app.edit(row);
    assert.equal(row.draft.applyWhenEnabled, false);
});

test('null and whitespace conditions start unchecked', () => {
    for (const applyWhen of [null, '', '  \t  ']) {
        const { app } = editor({ mappings: [attribute('computed', { id: 42, isComputed: true, valueModeId: 1, applyWhen })] });
        const row = app.mappings[0];
        assert.equal(row.draft.applyWhenEnabled, false);
        app.edit(row);
        assert.equal(row.draft.applyWhenEnabled, false);
    }
});

test('inherited computed mapping retains its condition when restoring the global mapping', () => {
    const { app, request } = editor({ selectedFamilyCode: 'trees', mappings: [
        attribute('computed', { id: 2, mappingKey: 'publication', isComputed: true, valueModeId: 1, name: 'Publication' })
    ] });
    app.deletePersistedMapping(app.mappings[0]);
    request().success({ success: true, replacementMapping: {
        id: 1, isInherited: true, isComputed: true, valueModeId: 1, mappingKey: 'publication',
        name: 'Publication', valueTemplate: '{sync_status == published ? true : false}',
        applyWhen: 'sync_status != hold', nopTargetTypeId: '1', nopTargetKey: 'Published'
    } });
    const row = app.mappings[0];
    app.edit(row);
    assert.equal(row.draft.applyWhen, 'sync_status != hold');
    assert.equal(row.draft.applyWhenEnabled, true);
});

test('deleted Akeneo attributes remain deletable but cannot be edited or offered as new sources', () => {
    const { app, request } = editor({ mappings: [attribute('removed_attribute', {
        id: 42, mappingKey: 'removed_attribute', isMissingSource: true, nopTargetTypeId: '1'
    })] });
    const row = app.mappings[0];
    assert.equal(app.statusFor(row).text, 'Missing source');
    assert.equal(app.canDeleteMapping(row), true);
    app.edit(row);
    assert.equal(row.editing, false);
    app.deletePersistedMapping(row);
    assert.equal(request().data.id, 42);
    request().success({ success: true });
    assert.equal(app.mappings.length, 0);
    assert.equal(app.availableSources.some(source => source.code === 'removed_attribute'), false);
});

test('removing an orphaned family override retains the orphaned inherited mapping', () => {
    const { app, request } = editor({ selectedFamilyCode: 'trees', mappings: [attribute('removed_attribute', {
        id: 42, isMissingSource: true, nopTargetTypeId: '1'
    })] });
    const row = app.mappings[0];
    app.deletePersistedMapping(row);
    request().success({ success: true, replacementMapping: {
        id: 10, isInherited: true, nopTargetTypeId: '1', nopTargetKey: 'Published'
    } });
    assert.equal(row.isMissingSource, true);
    assert.equal(app.canDeleteMapping(row), false);
    assert.equal(app.statusFor(row).text, 'Missing source');
});
