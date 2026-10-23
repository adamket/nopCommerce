const { readFileSync } = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { test } = require('node:test');
const componentPath = process.env.AKENEO_COMBOBOX_SCRIPT || path.join(__dirname, '../../Content/Scripts/akeneo-combobox.js');
const listeners = new Map();
const context = { window: { innerWidth: 1000, innerHeight: 700,
    addEventListener() {}, removeEventListener() {}, cancelAnimationFrame() {} },
    document: { addEventListener(name, callback) { listeners.set(name, callback); }, removeEventListener(name) { listeners.delete(name); } } };
vm.runInNewContext(readFileSync(componentPath, 'utf8'), context);
const definition = context.window.AkeneoCombobox;

function combobox(props = {}) {
    const events = [];
    const app = { ...definition.data(), ...Object.fromEntries(Object.entries(definition.props).map(([name, prop]) =>
        [name, typeof prop.default === 'function' ? prop.default() : prop.default])), ...props,
        $emit: (name, value) => events.push({ name, value }),
        $refs: { input: { focus() {} } }, $nextTick() {}
    };
    for (const [name, method] of Object.entries(definition.methods)) app[name] = method.bind(app);
    for (const [name, getter] of Object.entries(definition.computed)) {
        Object.defineProperty(app, name, typeof getter === 'function'
            ? { get: getter.bind(app) }
            : { get: getter.get.bind(app), set: getter.set.bind(app) });
    }
    return { app, events };
}

test('search finds labels and codes, ignoring case, and reports no matches', () => {
    const { app } = combobox({ options: [{ value: 'tree_height', label: 'Mature height' }] });
    for (const term of ['MATURE', 'TREE_HEIGHT']) {
        app.search = term;
        assert.equal(app.filtered.length, 1);
    }
    app.search = 'missing';
    assert.equal(app.filtered.length, 0);
});

test('single selection preserves empty-string choices such as Global defaults', () => {
    const { app, events } = combobox({ multiple: false, modelValue: '', options: [{ value: '', label: 'Global defaults' }] });
    assert.equal(app.inputValue, 'Global defaults');
    app.openMenu();
    assert.equal(app.inputValue, '');
    assert.equal(app.inputPlaceholder, 'Global defaults');
    app.toggle('');
    assert.equal(events.length, 0);
    app.modelValue = 'trees';
    app.toggle('');
    assert.equal(events[0].name, 'update:modelValue');
    assert.equal(events[0].value, '');
    assert.equal(events[1].name, 'change');
    assert.equal(app.open, false);
});

test('required choices cannot be cleared by buttons or backspace', () => {
    const { app, events } = combobox({ multiple: false, clearable: false, modelValue: '0' });
    app.clear();
    app.remove('0');
    app.onKeydown({ key: 'Backspace' });
    assert.equal(events.length, 0);
});

test('keyboard navigation selects the filtered option and Escape closes the menu', () => {
    const { app, events } = combobox({ multiple: false, options: [{value:'name', label:'Name'}, {value:'price', label:'Price'}] });
    const key = name => app.onKeydown({key: name, preventDefault() {}});
    key('ArrowDown');
    key('ArrowDown');
    key('Enter');
    assert.equal(events[0].value, 'price');
    app.openMenu();
    app.inputValue = 'name';
    assert.equal(app.filtered.length, 1);
    key('Escape');
    assert.equal(app.open, false);
    assert.equal(app.search, '');
});

test('existing multi-select users keep removable chips and array emissions', () => {
    const { app, events } = combobox({ modelValue: ['trees'] });
    assert.equal(app.multiple, true);
    assert.equal(app.clearable, true);
    app.toggle('shrubs');
    assert.equal(events[0].value.join(','), 'trees,shrubs');
    app.remove('trees');
    assert.equal(events[2].value.length, 0);
});

test('disabled controls cannot change selection', () => {
    const { app, events } = combobox({ disabled: true, modelValue: ['trees'] });
    app.toggle('shrubs');
    app.clear();
    app.remove('trees');
    app.onKeydown({ key: 'Enter', preventDefault() {} });
    assert.equal(events.length, 0);
});

function positioned(rect, menuHeight = 300) {
    const { app } = combobox();
    app.open = true;
    app.$refs.control = { getBoundingClientRect: () => rect };
    app.$refs.menu = { scrollHeight: menuHeight - 2, contains: target => target === 'option' };
    app.updateMenuPosition();
    return app;
}

test('menu is placed outside clipping containers and flips above a low control', () => {
    const app = positioned({ left: 100, right: 400, top: 600, bottom: 640, width: 300, height: 40 });
    assert.equal(app.menuStyle.top, '294px');
    assert.equal(app.menuStyle.left, '100px');
    assert.equal(app.menuStyle.width, '300px');
    assert.match(definition.template, /<teleport to="body">/);
});

test('menu opens below when it fits and stays within the viewport horizontally', () => {
    const app = positioned({ left: 900, right: 1200, top: 30, bottom: 70, width: 300, height: 40 });
    assert.equal(app.menuStyle.top, '76px');
    assert.equal(app.menuStyle.left, '692px');
});

test('limited space constrains menu height and offscreen anchors close the menu', () => {
    context.window.innerHeight = 400;
    const app = positioned({ left: 100, right: 400, top: 180, bottom: 220, width: 300, height: 40 });
    assert.equal(app.menuStyle.maxHeight, '166px');
    context.window.innerHeight = 700;
    app.$refs.control.getBoundingClientRect = () => ({ left: 100, right: 400, top: -90, bottom: -50, width: 300, height: 40 });
    app.updateMenuPosition();
    assert.equal(app.open, false);
});

test('clicking teleported options keeps the menu open and outside clicks close it', () => {
    const app = positioned({ left: 100, right: 400, top: 30, bottom: 70, width: 300, height: 40 });
    app.$el = { contains: target => target === 'input' };
    definition.mounted.call(app);
    listeners.get('mousedown')({ target: 'option' });
    assert.equal(app.open, true);
    listeners.get('mousedown')({ target: 'outside' });
    assert.equal(app.open, false);
    definition.beforeUnmount.call(app);
    assert.equal(listeners.size, 0);
});
