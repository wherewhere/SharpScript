<template>
    <div>
        <slot></slot>
    </div>
</template>

<script lang="ts">
    import type { PropType } from "vue";
    import type { FoundationElement } from "@microsoft/fast-foundation"
    export default {
        name: "ValueChangeHost",
        props: {
            eventName: {
                type: String as PropType<keyof HTMLElementEventMap>,
                required: true
            },
            valueName: {
                type: String as PropType<keyof HTMLElementEventMap | string>,
                required: true
            },
            modelValue: String as PropType<string | number | boolean>
        },
        emits: ["update:modelValue"],
        watch: {
            eventName(newValue: keyof HTMLElementEventMap, oldValue: keyof HTMLElementEventMap) {
                if (newValue !== oldValue) {
                    const $el = this.$el;
                    if ($el instanceof HTMLElement) {
                        const element = $el.children[0];
                        if (element instanceof HTMLElement) {
                            if (oldValue) {
                                element.removeEventListener(oldValue, this.onValueChanged);
                            }
                            if (newValue) {
                                element.addEventListener(newValue, this.onValueChanged);
                            }
                        }
                    }
                }
            },
            modelValue(newValue: String, oldValue: String) {
                if (newValue !== oldValue) {
                    const valueName = this.valueName;
                    if (valueName) {
                        const $el = this.$el;
                        if ($el instanceof HTMLElement) {
                            const element = $el.children[0];
                            if (element instanceof HTMLElement) {
                                (element as any)[valueName] = newValue;
                            }
                        }
                    }
                }
            }
        },
        methods: {
            registerEvent(eventName: keyof HTMLElementEventMap, valueName: keyof HTMLElement) {
                const $el = this.$el;
                if ($el instanceof HTMLElement) {
                    const element = $el.children[0] as FoundationElement;
                    if (element instanceof HTMLElement) {
                        const modelValue = this.modelValue;
                        if (modelValue === undefined) {
                            this.$emit("update:modelValue", element[valueName]);
                        }
                        else {
                            (element as any)[valueName] = modelValue;
                        }
                        element.addEventListener(eventName, this.onValueChanged);
                    }
                }
            },
            onValueChanged<K extends keyof HTMLElementEventMap>(event: HTMLElementEventMap[K]) {
                const target = event.target as FoundationElement;
                if (target instanceof HTMLElement) {
                    this.$emit("update:modelValue", target[this.valueName as keyof HTMLElement]);
                }
            }
        },
        mounted() {
            const { eventName, valueName } = this;
            if (eventName && valueName) {
                this.registerEvent(eventName, valueName as keyof HTMLElement);
            }
        }
    };
</script>