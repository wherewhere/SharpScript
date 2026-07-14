<template>
    <div class="split-panels" :direction="direction" ref="root">
        <div class="slotted slot1">
            <slot name="panel1"></slot>
        </div>
        <div class="median" ref="median" @pointerdown="pointerdown">
            <span class="handle" role="button"></span>
        </div>
        <div v-if="!collapsed" class="slotted slot2">
            <slot name="panel2"></slot>
        </div>
    </div>
</template>

<script lang="ts" setup>
    import { onMounted, shallowRef, useTemplateRef, watch } from "vue";

    const { direction = "row", collapsed, barsize = 8, barhandle, slot1minsize, slot2minsize } = defineProps<{
        direction?: "row" | "column";
        collapsed?: boolean;
        barsize?: number;
        barhandle?: boolean;
        slot1minsize?: number;
        slot2minsize?: number;
    }>();

    const root = useTemplateRef("root");
    const median = useTemplateRef("median");
    const emit = defineEmits<{
        (e: "splittercollapsed", payload: { collapsed: boolean }): void;
        (e: "splitterresized", payload: { panel1size: number, panel2size: number }): void;
    }>();

    const isResizing = shallowRef(false);

    watch(
        isResizing,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                if (newValue) {
                    root.value!.setAttribute("resizing", '');
                } else {
                    root.value!.style.userSelect = '';
                    root.value!.style.cursor = '';
                    root.value!.removeAttribute("resizing");
                }
            }
        }
    );
    watch(
        () => direction,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                root.value!.style.gridTemplateRows = '';
                root.value!.style.gridTemplateColumns = '';
                updateBarSizeStyle();
            }
        });
    watch(
        () => collapsed,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                const realValue = newValue !== null && newValue !== undefined && newValue !== false;
                if (realValue) {
                    root.value!.setAttribute("collapsed", '');
                } else {
                    root.value!.removeAttribute("collapsed");
                }
                emit("splittercollapsed", { collapsed: realValue });
            }
        });
    watch(
        () => barsize,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                updateBarSizeStyle();
            }
        });
    watch(
        () => barhandle,
        (newValue, oldValue) => {
            if (newValue !== oldValue) {
                const realValue = newValue !== null && newValue !== undefined && newValue !== false;
                if (realValue) {
                    root.value!.removeAttribute("no-barhandle");
                } else {
                    root.value!.setAttribute("no-barhandle", '');
                }
            }
        });

    let slot1size = 0, slot2size = 0, totalsize = 0, left = 0, right = 0, top = 0;

    function pointerdown() {
        isResizing.value = true;
        const clientRect = root.value!.getBoundingClientRect();
        left = clientRect.x;
        right = clientRect.right;
        top = clientRect.y;
        totalsize = direction === "row" ? clientRect.width : clientRect.height;

        root.value!.addEventListener("pointermove", resizeDrag);
        root.value!.addEventListener("pointerup", pointerup);
        root.value!.addEventListener("touchmove", touchmove);
        root.value!.addEventListener("touchend", pointerup);
    }

    function pointerup() {
        isResizing.value = false;
        emit("splitterresized", { panel1size: slot1size, panel2size: slot2size });
        root.value!.removeEventListener("pointermove", resizeDrag);
        root.value!.removeEventListener("pointerup", pointerup);
        root.value!.removeEventListener("touchmove", touchmove);
        root.value!.removeEventListener("touchend", pointerup);
    }

    function touchmove(e: TouchEvent) {
        if (e.touches.length) {
            const { clientX, clientY } = e.touches[0];
            resizeDrag({ clientX, clientY });
        }
    }

    function resizeDrag(e: { clientX: number, clientY: number }) {
        if (direction === "row") {
            const newMedianStart = (document.body.dir === '' || document.body.dir === "ltr") ? (e.clientX - left) : (right - e.clientX);
            const median = barsize;

            slot1size = Math.floor(newMedianStart - (median / 2));
            slot2size = Math.floor(root.value!.clientWidth - slot1size - (median / 2));

            let min1size = ensurevalue(slot1minsize);
            if (slot1size < min1size) {
                slot1size = Math.floor(min1size);
                slot2size = Math.floor(root.value!.clientWidth - slot1size - (median / 2));
            }
            let min2size = ensurevalue(slot2minsize);
            if (slot2size < min2size) {
                slot2size = Math.floor(min2size);
                slot1size = Math.floor(root.value!.clientWidth - slot2size - (median / 2));
            }

            const totalSize = slot1size + slot2size - median;
            let slot1fraction = (slot1size / totalSize).toFixed(2);
            let slot2fraction = (slot2size / totalSize).toFixed(2);
            root.value!.style.gridTemplateColumns = `${slot1fraction}fr ${median}px ${slot2fraction}fr`;
        }
        if (direction === "column") {
            const newMedianTop = e.clientY - top;
            const median = barsize;

            slot1size = Math.floor(newMedianTop - (median / 2));
            slot2size = Math.floor(root.value!.clientHeight - slot1size - (median / 2));

            let min1size = ensurevalue(slot1minsize);
            if (slot1size < min1size) {
                slot1size = Math.floor(min1size);
                slot2size = Math.floor(root.value!.clientHeight - slot1size - (median / 2));
            }
            let min2size = ensurevalue(slot2minsize);
            if (slot2size < min2size) {
                slot2size = Math.floor(min2size);
                slot1size = Math.floor(root.value!.clientHeight - slot2size - (median / 2));
            }

            const totalSize = slot1size + slot2size - median;
            let slot1fraction = (slot1size / totalSize).toFixed(2);
            let slot2fraction = (slot2size / totalSize).toFixed(2);

            root.value!.style.gridTemplateRows = `${slot1fraction}fr ${median}px ${slot2fraction}fr`;
        }
    }

    function updateBarSizeStyle() {
        if (median.value && median.value.style) {
            if (direction === "row") {
                median.value.style.inlineSize = `${barsize}px`;
                median.value.style.blockSize = '';
            }
            else {
                median.value.style.blockSize = `${barsize}px`;
                median.value.style.inlineSize = '';
            }
        }
    }

    function ensurevalue(value: string | number | any) {
        if (!value) { return 0; }

        value = value.trim().toLowerCase();

        if (value.endsWith('%')) { return totalsize * parseFloat(value) / 100; }

        if (value.endsWith("px")) { return parseFloat(value); }

        if (value.endsWith("fr")) { return totalsize * parseFloat(value); }

        return 0;
    }

    onMounted(updateBarSizeStyle);
</script>

<style lang="scss" scoped>
    @use "../styles/theme";
    @use "../styles/colors";

    $sizer-base-background: colors.$card-background-fill-color-default;
    $sizer-base-background-pointer-over: colors.$control-fill-color-secondary;
    $sizer-base-background-pressed: colors.$control-fill-color-tertiary;
    $sizer-base-background-disabled: colors.$control-fill-color-disabled;
    $sizer-base-border: colors.$card-stroke-color-default;
    $button-border-top-pointer-over: (
        light: colors.$control-stroke-color-default,
        dark: colors.$control-stroke-color-secondary
    );
    $button-border-bottom-pointer-over: (
        light: colors.$control-stroke-color-secondary,
        dark: colors.$control-stroke-color-default
    );
    $sizer-base-foreground: colors.$control-strong-fill-color-default;

    $sizer-base-thumb-height: 24px;
    $sizer-base-thumb-width: 4px;
    $sizer-base-thumb-radius: 2px;
    $sizer-base-padding: 4px;

    div.split-panels {
        display: grid;

        &[resizing] {
            user-select: none;

            &[direction=row] {
                cursor: col-resize;
            }

            &[direction=column] {
                cursor: row-resize;
            }
        }

        &[direction=row] {
            grid-template-columns: var(--first-size, 1fr) max-content var(--second-size, 1fr);

            .median {
                grid-column: 2 / 3;

                &:hover {
                    cursor: col-resize;
                }

                span.handle {
                    height: $sizer-base-thumb-height;
                }
            }

            .slot1 {
                grid-column: 1 / 2;
                grid-row: 1 / 1;
            }

            .slot2 {
                grid-column: 3 / 4;
                grid-row: 1 / 1;
            }
        }

        &[direction=column] {
            grid-template-rows: var(--first-size, 1fr) max-content var(--second-size, 1fr);

            .median {
                grid-row: 2 / 3;

                &:hover {
                    cursor: row-resize;
                }

                span.handle {
                    width: $sizer-base-thumb-height;
                }
            }

            .slot1 {
                grid-row: 1 / 2;
                grid-column: 1 / 1;
            }

            .slot2 {
                grid-row: 3 / 4;
                grid-column: 1 / 1;
            }
        }

        &[collapsed] {
            grid-template-columns: 1fr !important;
            grid-template-rows: none !important;
        }

        &[collapsed] .median {
            display: none;
        }

        &[collapsed] #slot2 {
            display: none;
        }

        &[no-barhandle] .median span.handle {
            display: none;
        }
    }

    .median {
        box-sizing: border-box;
        border-radius: colors.$control-corner-radius;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        transition: background-color colors.$control-faster-animation-duration ease-in-out;

        span.handle {
            border-radius: 1px;
            margin: $sizer-base-padding;
        }

        @include theme.auto-theme {
            background: theme.themed($sizer-base-background);
            border: 1px solid theme.themed($sizer-base-border);

            span.handle {
                border: 1px solid theme.themed($sizer-base-foreground);
            }

            &:not(:disabled):hover {
                background: theme.themed($sizer-base-background-pointer-over);
                border-top: 1px solid theme.themed($button-border-top-pointer-over);
                border-bottom: 1px solid theme.themed($button-border-bottom-pointer-over);
            }

            &:not(:disabled):active {
                background: theme.themed($sizer-base-background-pressed);
            }

            &:disabled {
                background: theme.themed($sizer-base-background-disabled);
            }
        }
    }

    .slotted {
        overflow: auto;
    }
</style>