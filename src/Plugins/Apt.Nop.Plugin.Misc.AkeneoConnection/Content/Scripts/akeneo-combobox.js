/*
 * <akeneo-combobox> - Akeneo-style combo box for the Akeneo Connection admin pages.
 *
 * Register on a Vue app:
 *   app.component('akeneo-combobox', window.AkeneoCombobox);
 *
 * Use:
 *   <akeneo-combobox v-model="familyCodes"
 *                    v-bind:options="families"          // [{ value, label, hint? }]
 *                    v-bind:disabled="busy"
 *                    placeholder="All families · type to filter"
 *                    add-placeholder="Add a family…"
 *                    item-name="family"></akeneo-combobox>
 *
 * Props:
 *   modelValue      Array of selected values (multiple) or a single value / null.
 *   options         [{ value, label, hint }]. hint is shown in monospace on the right
 *                   and defaults to the value; pass hint: '' to hide it.
 *   multiple        Default true. When false, picking an option replaces the
 *                   selection and closes the menu.
 *   disabled        Disables the whole control.
 *   placeholder     Shown when nothing is selected.
 *   addPlaceholder  Shown when something is selected (multiple only).
 *   itemName        Used in accessible labels ("Remove family", "Clear all families").
 *   emptyText       Shown when there are no options at all.
 *   allowCustom     Multiple only. Lets the user add typed values that are not in
 *                   options (Enter or comma), e.g. free-form codes.
 */
(function () {
    var instanceCounter = 0;

    window.AkeneoCombobox = {
        name: 'AkeneoCombobox',
        props: {
            modelValue: { default: function () { return []; } },
            options: { type: Array, default: function () { return []; } },
            multiple: { type: Boolean, default: true },
            disabled: { type: Boolean, default: false },
            placeholder: { type: String, default: 'Select · type to filter' },
            addPlaceholder: { type: String, default: 'Add…' },
            itemName: { type: String, default: 'item' },
            emptyText: { type: String, default: 'No options are available.' },
            allowCustom: { type: Boolean, default: false }
        },
        emits: ['update:modelValue', 'change'],
        data: function () {
            instanceCounter++;

            return {
                uid: 'akeneo-combobox-' + instanceCounter,
                search: '',
                open: false,
                activeIndex: 0
            };
        },
        computed: {
            selected: function () {
                if (this.multiple)
                    return Array.isArray(this.modelValue) ? this.modelValue : [];

                return this.modelValue === null || this.modelValue === undefined || this.modelValue === ''
                    ? []
                    : [this.modelValue];
            },
            filtered: function () {
                var term = this.search.trim().toLowerCase();

                if (!term)
                    return this.options;

                return this.options.filter(function (option) {
                    return String(option.label || '').toLowerCase().indexOf(term) >= 0 ||
                        String(option.value || '').toLowerCase().indexOf(term) >= 0;
                });
            },
            inputPlaceholder: function () {
                return this.selected.length && this.multiple
                    ? this.addPlaceholder
                    : this.selected.length
                        ? ''
                        : this.placeholder;
            },
            /** The typed value that would be added as a custom entry, or ''. */
            customCandidate: function () {
                if (!this.allowCustom || !this.multiple)
                    return '';

                var term = this.search.trim();

                if (!term)
                    return '';

                var lower = term.toLowerCase();
                var matchesOption = this.options.some(function (option) {
                    return String(option.value).toLowerCase() === lower;
                });
                var alreadySelected = this.selected.some(function (value) {
                    return String(value).toLowerCase() === lower;
                });

                return matchesOption || alreadySelected ? '' : term;
            },
            activeDescendant: function () {
                return this.open && this.filtered.length
                    ? this.optionId(this.activeIndex)
                    : null;
            }
        },
        mounted: function () {
            this.onDocumentMouseDown = function (event) {
                if (this.open && !this.$el.contains(event.target))
                    this.close();
            }.bind(this);

            document.addEventListener('mousedown', this.onDocumentMouseDown);
        },
        beforeUnmount: function () {
            document.removeEventListener('mousedown', this.onDocumentMouseDown);
        },
        methods: {
            optionId: function (index) {
                return this.uid + '-option-' + index;
            },
            labelFor: function (value) {
                var option = this.options.find(function (item) { return item.value === value; });
                return option ? option.label : value;
            },
            hintFor: function (option) {
                return option.hint === undefined ? option.value : option.hint;
            },
            isSelected: function (value) {
                return this.selected.indexOf(value) >= 0;
            },
            emit: function (selected) {
                var value = this.multiple
                    ? selected
                    : (selected.length ? selected[0] : null);

                this.$emit('update:modelValue', value);
                this.$emit('change', value);
            },
            focusInput: function () {
                if (this.disabled)
                    return;

                this.$refs.input.focus();
                this.openMenu();
            },
            openMenu: function () {
                if (this.disabled || this.open)
                    return;

                this.open = true;
                this.activeIndex = 0;
            },
            close: function () {
                this.open = false;
                this.search = '';
            },
            toggleMenu: function () {
                if (this.open) {
                    this.close();
                    return;
                }

                this.focusInput();
            },
            onInput: function () {
                this.activeIndex = 0;
                this.open = true;
            },
            toggle: function (value) {
                if (this.disabled)
                    return;

                if (!this.multiple) {
                    this.emit([value]);
                    this.close();
                    return;
                }

                var selected = this.selected.slice();
                var index = selected.indexOf(value);

                if (index >= 0)
                    selected.splice(index, 1);
                else
                    selected.push(value);

                this.emit(selected);

                // Like Akeneo: keep the menu open for more picks, but reset the
                // filter and keep the picked option highlighted.
                if (this.search) {
                    this.search = '';
                    this.activeIndex = Math.max(0, this.options.findIndex(function (option) {
                        return option.value === value;
                    }));
                    this.scrollActiveIntoView();
                }
            },
            addCustom: function () {
                var value = this.customCandidate;

                if (!value || this.disabled)
                    return;

                this.emit(this.selected.concat([value]));
                this.search = '';
                this.activeIndex = 0;
            },
            remove: function (value) {
                this.emit(this.selected.filter(function (item) { return item !== value; }));
            },
            clear: function () {
                this.emit([]);
                this.$refs.input.focus();
            },
            moveActive: function (offset) {
                var count = this.filtered.length;

                if (!count)
                    return;

                this.activeIndex = (this.activeIndex + offset + count) % count;
                this.scrollActiveIntoView();
            },
            scrollActiveIntoView: function () {
                this.$nextTick(function () {
                    var option = document.getElementById(this.optionId(this.activeIndex));

                    if (option)
                        option.scrollIntoView({ block: 'nearest' });
                }.bind(this));
            },
            onKeydown: function (event) {
                switch (event.key) {
                    case 'ArrowDown':
                    case 'ArrowUp':
                        event.preventDefault();

                        if (!this.open)
                            this.openMenu();
                        else
                            this.moveActive(event.key === 'ArrowDown' ? 1 : -1);
                        break;

                    case 'Enter':
                        event.preventDefault();

                        // With nothing matching the typed text, Enter adds it.
                        if (this.customCandidate && !this.filtered.length)
                            this.addCustom();
                        else if (this.open && this.filtered[this.activeIndex])
                            this.toggle(this.filtered[this.activeIndex].value);
                        else
                            this.openMenu();
                        break;

                    case ',':
                        if (this.customCandidate) {
                            event.preventDefault();
                            this.addCustom();
                        }
                        break;

                    case 'Escape':
                        if (this.open) {
                            event.preventDefault();
                            this.close();
                        }
                        break;

                    case 'Tab':
                        this.close();
                        break;

                    case 'Backspace':
                        if (!this.search && this.selected.length)
                            this.remove(this.selected[this.selected.length - 1]);
                        break;
                }
            }
        },
        template: `
<div class="akeneo-combobox"
     v-bind:class="{ 'is-open': open, 'is-disabled': disabled, 'is-single': !multiple }">
    <div class="akeneo-combobox-control"
         v-on:mousedown.self.prevent="focusInput">
        <span class="akeneo-combobox-chip"
              v-for="value in selected"
              v-bind:key="value"
              v-bind:title="value">
            <span class="akeneo-combobox-chip-text">{{ labelFor(value) }}</span>
            <button type="button"
                    class="akeneo-combobox-chip-remove"
                    v-bind:aria-label="'Remove ' + itemName + ' ' + labelFor(value)"
                    v-bind:disabled="disabled"
                    v-on:mousedown.prevent
                    v-on:click.stop="remove(value)"></button>
        </span>

        <input ref="input"
               type="text"
               class="akeneo-combobox-input"
               role="combobox"
               aria-autocomplete="list"
               v-bind:aria-controls="uid + '-listbox'"
               v-bind:aria-expanded="open ? 'true' : 'false'"
               v-bind:aria-activedescendant="activeDescendant"
               v-bind:placeholder="inputPlaceholder"
               v-bind:disabled="disabled"
               v-model="search"
               v-on:focus="openMenu"
               v-on:click="openMenu"
               v-on:input="onInput"
               v-on:keydown="onKeydown" />

        <button type="button"
                class="akeneo-combobox-clear"
                v-bind:title="'Clear all ' + itemName + 's'"
                v-bind:aria-label="'Clear all ' + itemName + 's'"
                v-if="selected.length && !disabled"
                v-on:mousedown.prevent
                v-on:click.stop="clear">
        </button>

        <i class="fas fa-chevron-down akeneo-combobox-caret"
           aria-hidden="true"
           v-on:mousedown.prevent="toggleMenu"></i>
    </div>

    <ul class="akeneo-combobox-menu"
        role="listbox"
        v-bind:id="uid + '-listbox'"
        v-bind:aria-multiselectable="multiple ? 'true' : 'false'"
        v-show="open">
        <li v-for="(option, index) in filtered"
            v-bind:key="option.value"
            v-bind:id="optionId(index)"
            role="option"
            class="akeneo-combobox-option"
            v-bind:aria-selected="isSelected(option.value) ? 'true' : 'false'"
            v-bind:class="{ 'is-active': index === activeIndex, 'is-selected': isSelected(option.value) }"
            v-on:mousedown.prevent="toggle(option.value)"
            v-on:mouseenter="activeIndex = index">
            <span class="akeneo-combobox-check" aria-hidden="true"></span>
            <span class="akeneo-combobox-option-label">{{ option.label }}</span>
            <span class="akeneo-combobox-option-code" v-if="hintFor(option)">{{ hintFor(option) }}</span>
        </li>
        <li class="akeneo-combobox-option akeneo-combobox-add"
            v-if="customCandidate"
            v-on:mousedown.prevent="addCustom">
            <i class="fas fa-plus" aria-hidden="true"></i>
            <span class="akeneo-combobox-option-label">Add “{{ customCandidate }}”</span>
            <span class="akeneo-combobox-option-code">Enter</span>
        </li>
        <li class="akeneo-combobox-empty" v-if="!filtered.length && !customCandidate">
            <template v-if="options.length && search">No match for “{{ search }}”.</template>
            <template v-else>{{ emptyText }}</template>
        </li>
    </ul>
</div>`
    };
})();
