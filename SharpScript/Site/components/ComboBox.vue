<template>
    <div class="combobox" :class="className" :style="style" :width="width" :height="height">
        <select v-bind="$attrs" v-model="model">
            <slot></slot>
        </select>
        <ChevronDown12Regular class="drop-down-glyph" />
    </div>
</template>

<script generic="T" lang="ts" setup>
    import "../types";
    import { ClassValue, StyleValue } from "vue";
    import ChevronDown12Regular from "@fluentui/svg-icons/icons/chevron_down_12_regular.svg?component";

    const {
        class: className,
        style,
        width,
        height
    } = defineProps<{
        class?: ClassValue,
        style?: StyleValue,
        width?: number,
        height?: number
    }>();
    const model = defineModel<T>();
</script>

<style lang="scss" scoped>
    @use "../styles/theme";
    @use "../styles/colors";

    $combobox-dropdown-glyph-foreground: colors.$text-fill-color-secondary;
    $combobox-dropdown-glyph-foreground-disabled: colors.$text-fill-color-disabled;
    $combobox-dropdown-foreground: colors.$text-fill-color-primary;
    $combobox-dropdown-background: colors.$solid-background-fill-color-tertiary;
    $combobox-dropdown-border: colors.$surface-stroke-color-flyout;

    $combobox-padding: 6px 34px 6px 11px;
    $combobox-dropdown-border-thickness: 1px;
    $combobox-dropdown-content-margin: 0 4px;
    $combobox-dropdown-button-background-corner-radius: 4px;

    $max-dropdown-height: 504px;

    .combobox {
        position: relative;

        select {
            width: 100%;
            height: 100%;
            appearance: none;
            padding: $combobox-padding;

            +.drop-down-glyph {
                position: absolute;
                right: 14px;
                top: calc(50% - 6px);
                fill: currentColor;
                pointer-events: none;

                @include theme.auto-theme {
                    color: theme.themed($combobox-dropdown-glyph-foreground);
                }
            }

            @supports (appearance: base-select) {
                appearance: base-select;
                justify-content: space-between;
                align-items: center;

                &::picker-icon {
                    display: none;
                }

                &::picker(select) {
                    opacity: 0;
                    appearance: base-select;
                    max-height: $max-dropdown-height;
                    border-radius: colors.$overlay-corner-radius;
                    padding: $combobox-dropdown-content-margin;
                    box-shadow: 0 0 16px rgba(0, 0, 0, .14);
                    transition: opacity colors.$control-faster-animation-duration linear;

                    @include theme.auto-theme {
                        color: theme.themed($combobox-dropdown-foreground);
                        background: theme.themed($combobox-dropdown-background);
                        border: $combobox-dropdown-border-thickness solid theme.themed($combobox-dropdown-border);
                    }
                }

                &:open::picker(select) {
                    opacity: 1;

                    @starting-style {
                        opacity: 0;
                    }
                }
            }

            &:disabled {
                +.drop-down-glyph {
                    @include theme.auto-theme {
                        color: theme.themed($combobox-dropdown-glyph-foreground-disabled);
                    }
                }
            }
        }
    }
</style>