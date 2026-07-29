<template>
    <option>
        <slot></slot>
    </option>
</template>

<style lang="scss" scoped>
    @use "../styles/theme";
    @use "../styles/colors";

    $combobox-item-background: colors.$subtle-fill-color-transparent;
    $combobox-item-background-pressed: colors.$subtle-fill-color-tertiary;
    $combobox-item-background-pointer-over: colors.$subtle-fill-color-secondary;
    $combobox-item-background-disabled: colors.$subtle-fill-color-disabled;
    $combobox-item-background-selected: colors.$subtle-fill-color-secondary;
    $combobox-item-background-selected-unfocused: colors.$subtle-fill-color-tertiary;
    $combobox-item-background-selected-pressed: colors.$subtle-fill-color-secondary;
    $combobox-item-background-selected-pointer-over: colors.$subtle-fill-color-tertiary;
    $combobox-item-background-selected-disabled: colors.$subtle-fill-color-secondary;

    $combobox-item-pill-fill: colors.$accent-fill-color-default;
    $combobox-item-pill-height: 16px;
    $combobox-item-pill-width: 3px;
    $combobox-item-pill-min-scale: 0.625;
    $combobox-item-pill-corner-radius: 1.5px;

    $combobox-item-scale-animation-duration: colors.$control-fast-animation-duration;

    @supports (appearance: base-select) {
        option {
            position: relative;
            min-block-size: auto;

            @include theme.auto-theme {
                background: theme.themed($combobox-item-background);

                &:not(:disabled):hover {
                    background: theme.themed($combobox-item-background-pointer-over);
                }

                &:not(:disabled):active {
                    background: theme.themed($combobox-item-background-pressed);
                }

                &:disabled {
                    background: theme.themed($combobox-item-background-disabled);
                }

                &:not(:disabled):checked {
                    background: theme.themed($combobox-item-background-selected);
                }

                &:not(:disabled):checked:hover {
                    background: theme.themed($combobox-item-background-selected-pointer-over);
                }

                &:not(:disabled):checked:active {
                    background: theme.themed($combobox-item-background-selected-pressed);
                }

                &:disabled:checked {
                    background: theme.themed($combobox-item-background-selected-disabled);
                }
            }

            &::checkmark {
                content: '';
                position: absolute;
                left: 0;
                height: $combobox-item-pill-height;
                width: $combobox-item-pill-width;
                border-radius: $combobox-item-pill-corner-radius;
                transition: height $combobox-item-scale-animation-duration cubic-bezier(colors.$control-fast-out-slow-in-key-spline);

                @include theme.auto-theme {
                    background: theme.themed($combobox-item-pill-fill);
                }
            }

            &:not(:disabled):active {
                &::checkmark {
                    height: $combobox-item-pill-height * $combobox-item-pill-min-scale;
                }
            }
        }
    }
</style>