<template>
    <div class="split-panels" :direction="direction">
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

<script lang="ts">
    import { PropType } from "vue";

    export default {
        name: "SplitPanels",
        props: {
            direction: {
                type: String as PropType<"row" | "column">,
                default: "row"
            },
            collapsed: {
                type: Boolean,
                default: false
            },
            barsize: {
                type: Number,
                default: 8
            },
            barhandle: {
                type: Boolean,
                default: true
            },
            slot1minsize: {
                type: Number,
                default: 0
            },
            slot2minsize: {
                type: Number,
                default: 0
            }
        },
        data() {
            return {
                isResizing: false,
                slot1size: 0,
                slot2size: 0,
                totalsize: 0,
                left: 0,
                right: 0,
                top: 0
            }
        },
        watch: {
            isResizing(newValue: boolean, oldValue: boolean) {
                if (newValue !== oldValue) {
                    if (newValue) {
                        this.$el.setAttribute("resizing", '');
                    } else {
                        this.$el.style.userSelect = '';
                        this.$el.style.cursor = '';
                        this.$el.removeAttribute("resizing");
                    }
                }
            },
            direction(newValue: string, oldValue: string) {
                if (newValue !== oldValue) {
                    this.$el.style.gridTemplateRows = '';
                    this.$el.style.gridTemplateColumns = '';
                    this.updateBarSizeStyle();
                }
            },
            collapsed(newValue: boolean, oldValue: boolean) {
                if (newValue !== oldValue) {
                    const realValue = newValue !== null && newValue !== undefined && newValue !== false;
                    if (realValue) {
                        this.$el.setAttribute("collapsed", '');
                    } else {
                        this.$el.removeAttribute("collapsed");
                    }
                    this.$emit("splittercollapsed", { collapsed: realValue });
                }
            },
            barsize(newValue: number, oldValue: number) {
                if (newValue !== oldValue) {
                    this.updateBarSizeStyle();
                }
            },
            barhandle(newValue: boolean, oldValue: boolean) {
                if (newValue !== oldValue) {
                    const realValue = newValue !== null && newValue !== undefined && newValue !== false;
                    if (realValue) {
                        this.$el.removeAttribute("no-barhandle");
                    } else {
                        this.$el.setAttribute("no-barhandle", '');
                    }
                }
            }
        },
        emits: ["splittercollapsed", "splitterresized"],
        methods: {
            pointerdown() {
                this.isResizing = true;
                const clientRect = this.$el.getBoundingClientRect();
                this.left = clientRect.x;
                this.right = clientRect.right;
                this.top = clientRect.y;
                this.totalsize = this.direction === "row" ? clientRect.width : clientRect.height;

                this.$el.addEventListener("pointermove", this.resizeDrag);
                this.$el.addEventListener("pointerup", this.pointerup);
                this.$el.addEventListener("touchmove", this.touchmove);
                this.$el.addEventListener("touchend", this.pointerup);
            },
            pointerup() {
                this.isResizing = false;
                this.$emit("splitterresized", { panel1size: this.slot1size, panel2size: this.slot2size });
                this.$el.removeEventListener("pointermove", this.resizeDrag);
                this.$el.removeEventListener("pointerup", this.pointerup);
                this.$el.removeEventListener("touchmove", this.touchmove);
                this.$el.removeEventListener("touchend", this.pointerup);
            },
            touchmove(e: TouchEvent) {
                if (e.touches.length) {
                    const { clientX, clientY } = e.touches[0];
                    this.resizeDrag({ clientX, clientY });
                }
            },
            resizeDrag(e: { clientX: number, clientY: number }) {
                if (this.direction === "row") {
                    const newMedianStart = (document.body.dir === '' || document.body.dir === "ltr") ? (e.clientX - this.left) : (this.right - e.clientX);
                    const median = this.barsize;

                    this.slot1size = Math.floor(newMedianStart - (median / 2));
                    this.slot2size = Math.floor(this.$el.clientWidth - this.slot1size - (median / 2));

                    let min1size = this.ensurevalue(this.slot1minsize);
                    if (this.slot1size < min1size) {
                        this.slot1size = Math.floor(min1size);
                        this.slot2size = Math.floor(this.$el.clientWidth - this.slot1size - (median / 2));
                    }
                    let min2size = this.ensurevalue(this.slot2minsize);
                    if (this.slot2size < min2size) {
                        this.slot2size = Math.floor(min2size);
                        this.slot1size = Math.floor(this.$el.clientWidth - this.slot2size - (median / 2));
                    }

                    const totalSize = this.slot1size + this.slot2size - median;
                    let slot1fraction = (this.slot1size / totalSize).toFixed(2);
                    let slot2fraction = (this.slot2size / totalSize).toFixed(2);
                    this.$el.style.gridTemplateColumns = `${slot1fraction}fr ${median}px ${slot2fraction}fr`;
                }
                if (this.direction === "column") {
                    const newMedianTop = e.clientY - this.top;
                    const median = this.barsize;

                    this.slot1size = Math.floor(newMedianTop - (median / 2));
                    this.slot2size = Math.floor(this.$el.clientHeight - this.slot1size - (median / 2));

                    let min1size = this.ensurevalue(this.slot1minsize);
                    if (this.slot1size < min1size) {
                        this.slot1size = Math.floor(min1size);
                        this.slot2size = Math.floor(this.$el.clientHeight - this.slot1size - (median / 2));
                    }
                    let min2size = this.ensurevalue(this.slot2minsize);
                    if (this.slot2size < min2size) {
                        this.slot2size = Math.floor(min2size);
                        this.slot1size = Math.floor(this.$el.clientHeight - this.slot2size - (median / 2));
                    }

                    const totalSize = this.slot1size + this.slot2size - median;
                    let slot1fraction = (this.slot1size / totalSize).toFixed(2);
                    let slot2fraction = (this.slot2size / totalSize).toFixed(2);

                    this.$el.style.gridTemplateRows = `${slot1fraction}fr ${median}px ${slot2fraction}fr`;
                }
            },
            updateBarSizeStyle() {
                let median = this.$refs.median as HTMLDivElement | null;

                if (median && median.style) {
                    if (this.direction === "row") {
                        median.style.inlineSize = `${this.barsize}px`;
                        median.style.blockSize = '';
                    }
                    else {
                        median.style.blockSize = `${this.barsize}px`;
                        median.style.inlineSize = '';
                    }
                }
            },
            ensurevalue(value: string | number | any) {
                if (!value) { return 0; }

                value = value.trim().toLowerCase();

                if (value.endsWith('%')) { return this.totalsize * parseFloat(value) / 100; }

                if (value.endsWith("px")) { return parseFloat(value); }

                if (value.endsWith("fr")) { return this.totalsize * parseFloat(value); }

                return 0;
            }
        },
        mounted() {
            this.updateBarSizeStyle();
        },
    };
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