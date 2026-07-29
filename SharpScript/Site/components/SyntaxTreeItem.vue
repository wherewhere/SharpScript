<template>
    <li :class="{ collapsed, leaf }">
        <span :class="['ast-item-wrap', `ast-item-${item.type}`, 'clickable']" @click="onclick">
            <span v-if="!leaf" class="expand-collapse-chevron clickable">
                <ChevronRight12Regular class="collapsed-glyph" />
                <ChevronDown12Regular class="expanded-glyph" />
            </span>
            <span class="ast-item-type clickable" :title="item.type"></span>
            <span v-if="item.property" class="ast-item-property">{{ item.property }}:</span>
            <span v-if="item.value" class="ast-inline-value" v-html="renderValue(item.value, item.type)"></span>
            <span class="ast-item-kind">{{ item.kind }}</span>
        </span>
        <ol v-if="!leaf" v-show="!collapsed">
            <SyntaxTreeItem v-for="child in item.children" :item="child" />
        </ol>
    </li>
</template>

<script generic="T extends AstItemAll" lang="ts" setup>
    import "../types";
    import { computed, shallowRef } from "vue";
    import type { AstItemMap, AstItemAll } from "sharp-script";
    import ChevronRight12Regular from "@fluentui/svg-icons/icons/chevron_right_12_regular.svg?component";
    import ChevronDown12Regular from "@fluentui/svg-icons/icons/chevron_down_12_regular.svg?component";

    const { item } = defineProps<{
        item: T;
    }>();

    const collapsed = shallowRef(true);
    const leaf = computed(() => !item.children?.length);

    function escapeCommon(value: string) {
        return value
            .replace('\r', '\\r')
            .replace('\n', '\\n')
            .replace('\t', '\\t');
    };

    function escapeTrivia(value: string) {
        return escapeCommon(value)
            .replace(/(^ +| +$)/g, (_, $1) => $1.length > 1 ? `<space:${$1.length}>` : "<space>");
    };

    function renderValue(value: string, type: keyof AstItemMap) {
        if (typeof value !== "string") { return `${value}`; }
        else if (type === "trivia") { return escapeTrivia(value); }
        else { return escapeCommon(value); }
    }

    function onclick(event: PointerEvent) {
        event.preventDefault();
        const target = event.target;
        if (target instanceof HTMLSpanElement && !target.classList.contains("clickable")) { return; }
        collapsed.value = !collapsed.value;
    }
</script>

<style lang="scss" scoped>
    @use "../styles/theme";
    @use "../styles/colors";

    $tree-view-item-background: colors.$subtle-fill-color-transparent;
    $tree-view-item-background-pointer-over: colors.$subtle-fill-color-secondary;
    $tree-view-item-background-pressed: colors.$subtle-fill-color-tertiary;
    $tree-view-item-background-disabled: colors.$subtle-fill-color-disabled;

    $tree-view-item-foreground: colors.$text-fill-color-primary;
    $tree-view-item-foreground-pointer-over: colors.$text-fill-color-primary;
    $tree-view-item-foreground-pressed: colors.$text-fill-color-secondary;
    $tree-view-item-foreground-disabled: colors.$text-fill-color-disabled;

    li {
        list-style: none;
        cursor: auto;

        .expand-collapse-chevron {
            width: 20px;
            height: 20px;
            margin-right: 5px;
            display: flex;
            justify-content: center;
            align-items: center;

            .collapsed-glyph,
            .expanded-glyph {
                fill: currentColor;
            }

            .collapsed-glyph {
                display: none;
            }
        }

        &.collapsed {
            .expanded-glyph {
                display: none;
            }

            .collapsed-glyph {
                display: inline;
            }
        }

        &:not(.leaf)>.ast-item-wrap {
            cursor: pointer;
        }

        span:not(.clickable) {
            cursor: auto;
        }

        .ast-item-wrap {
            display: flex;
            align-items: center;
            padding-top: 2px;
            padding-bottom: 2px;
            padding-right: 6px;
            border-radius: 4px;
            transition: background-color colors.$control-faster-animation-duration ease-in-out;

            @include theme.auto-theme {
                background: theme.themed($tree-view-item-background);
                color: theme.themed($tree-view-item-foreground);

                &:not(:disabled):hover {
                    background: theme.themed($tree-view-item-background-pointer-over);
                    color: theme.themed($tree-view-item-foreground-pointer-over);
                }

                &:not(:disabled):active {
                    background: theme.themed($tree-view-item-background-pressed);
                    color: theme.themed($tree-view-item-foreground-pressed);
                }

                &:disabled {
                    background: theme.themed($tree-view-item-background-disabled);
                    color: theme.themed($tree-view-item-foreground-disabled);
                }
            }

            .ast-item-property {
                margin-right: 5px;
            }
        }

        ol {
            margin: 0;
            padding-left: 25px;
        }
    }

    .ast-item-type {
        display: block;
        width: 20px;
        height: 20px;
        margin-left: 1px;
        margin-right: 5px;

        .ast-item-node & {
            background-image: url("../assets/node.svg");
        }

        .ast-item-token & {
            background-image: url("../assets/token.svg");
        }

        .ast-item-trivia & {
            background-image: url("../assets/trivia.svg");
        }

        .ast-item-value & {
            background-image: url("../assets/value.svg");
        }

        .ast-item-operation & {
            background-image: url("../assets/operation.svg");
        }

        .ast-item-property-only & {
            background-image: url("../assets/property.svg");
        }
    }

    .ast-item-trivia .ast-inline-value {
        display: inline-block;
        min-width: 5em;
    }

    .ast-inline-value+.ast-item-kind {
        display: inline-block;
        font-style: italic;
        margin-left: 5px;

        @include theme.auto-theme {
            color: theme.themed(colors.$text-fill-color-secondary);
        }
    }

    .ast-item-operation {
        font-style: italic;
    }
</style>