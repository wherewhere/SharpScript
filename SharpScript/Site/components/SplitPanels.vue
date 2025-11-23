<template>
    <div class="split-panels" :direction="direction" ref="root">
        <div class="slotted slot1">
            <slot name="panel1"></slot>
        </div>
        <div class="median" ref="median" @pointerdown="pointerdown">
            <span class="handle"></span>
        </div>
        <div v-if="!collapsed" class="slotted slot2">
            <slot name="panel2"></slot>
        </div>
    </div>
</template>

<script lang="ts" setup>
    import { onMounted, shallowRef, useTemplateRef, watch } from 'vue';

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
                    height: 16px;
                    margin: 2px 0;
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
                    width: 16px;
                    margin: 0 2px;
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
        background: var(--neutral-fill-input-rest);
        border: calc(var(--stroke-width) * 1px) solid var(--neutral-stroke-layer-rest);
        border-radius: calc(var(--control-corner-radius) * 1px);
        display: inline-flex;
        align-items: center;
        justify-content: center;

        span.handle {
            border: 1px solid var(--neutral-stroke-strong-rest);
            border-radius: 1px;
        }

        &:hover {
            background: var(--neutral-fill-input-hover);

            span.handle {
                border: 1px solid var(--neutral-stroke-strong-hover);
            }
        }

        &:active {
            background: var(--neutral-fill-input-active);

            span.handle {
                border: 1px solid var(--neutral-stroke-strong-active);
            }
        }

        &:focus {
            background: var(--neutral-fill-input-focus);

            span.handle {
                border: 1px solid var(--neutral-stroke-strong-focus);
            }
        }
    }

    .slotted {
        overflow: auto;
    }
</style>