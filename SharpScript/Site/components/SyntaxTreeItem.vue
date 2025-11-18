<template>
    <fluent-tree-item>
        <span :class="`ast-item-wrap ast-item-${item.type}`" ref="titleElementRef">
            <span class="ast-item-type" :title="item.type"></span>
            <span v-if="item.property" class="ast-item-property">{{ item.property }}:</span>
            <span v-if="item.value" class="ast-inline-value" v-html="renderValue(item.value, item.type)"></span>
            <span class="ast-item-kind">{{ item.kind }}</span>
        </span>
        <SyntaxTreeItem v-if="item.children && item.children.length" v-for="child in item.children" :item="child" />
    </fluent-tree-item>
</template>

<script lang="ts">
    import type { PropType } from "vue";
    import type { AstItemMap, AstItemAll } from "sharp-script";

    export default {
        name: "SyntaxTreeItem",
        props: {
            item: {
                type: Object as PropType<AstItemAll>,
                required: true
            }
        },
        methods: {
            escapeCommon(value: string) {
                return value
                    .replace('\r', '\\r')
                    .replace('\n', '\\n')
                    .replace('\t', '\\t');
            },
            escapeTrivia(value: string) {
                return this.escapeCommon(value)
                    .replace(/(^ +| +$)/g, (_, $1) => $1.length > 1 ? `<space:${$1.length}>` : "<space>");
            },
            renderValue(value: string, type: keyof AstItemMap) {
                if (typeof value !== "string") { return `${value}`; }
                else if (type === "trivia") { return this.escapeTrivia(value); }
                else { return this.escapeCommon(value); }
            }
        }
    };
</script>

<style lang="scss" scoped>
    fluent-tree-item {
        cursor: inherit;

        &::part(positioning-region),
        :deep(.positioning-region) {
            background: none;
        }
    }

    .ast-item-wrap {
        display: flex;
        align-items: center;
        padding-top: 2px;
        padding-bottom: 2px;
        padding-right: 6px;
    }

    .ast-item-property {
        margin-right: 5px;
    }

    .ast-item-type {
        display: block;
        width: 20px;
        height: 20px;
        margin-right: 5px;

        .ast-item-node & {
            background-image: url("data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0.5%200.5%2020%2020'%3E%3Cpath%20fill='%234684ee'%20stroke='%23f6f6f6'%20d='M4%204h12v4H4zm0%204h12v4H4zm0%204h12v4H4z'/%3E%3C/svg%3E");
        }

        .ast-item-token & {
            background-image: url("data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0.5%200.5%2020%2020'%3E%3Cpath%20fill='%23eeb046'%20stroke='%23f6f6f6'%20d='M4%208h12v4H4z'/%3E%3C/svg%3E");
        }

        .ast-item-trivia & {
            background-image: url("data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0.5%200.5%2020%2020'%3E%3Cpath%20d='M5.5%207.1V11h9V7.1'%20stroke='%23f6f6f6'%20stroke-width='4'%20fill='none'/%3E%3Cpath%20d='M5.5%208v3h9V8'%20stroke='%23666'%20stroke-width='2'%20fill='none'/%3E%3C/svg%3E");
        }

        .ast-item-value & {
            background-image: url("data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0.5%200.5%2020%2020'%3E%3Cpath%20fill='%23eeb046'%20stroke='%23f6f6f6'%20d='M8%208h4v4H8z'/%3E%3C/svg%3E");
        }

        .ast-item-operation & {
            background-image: url("data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0.5%200.5%2020%2020'%3E%3Ccircle%20cx='10'%20cy='10'%20r='4.23'%20stroke='%23f6f6f6'%20stroke-width='4.5'%20fill='none'/%3E%3Ccircle%20cx='10'%20cy='10'%20r='4.23'%20stroke='%23888'%20stroke-width='2.5'%20fill='none'/%3E%3C/svg%3E");
        }

        .ast-item-property-only & {
            background-image: url("data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0.5%200.5%2020%2020'%3E%3Ccircle%20cx='10'%20cy='10'%20r='2'%20fill='%23888'%20stroke='%23f6f6f6'%20stroke-width='1.1'/%3E%3C/svg%3E");
        }
    }

    .ast-item-trivia .ast-inline-value {
        display: inline-block;
        min-width: 5em;
    }

    .ast-inline-value+.ast-item-kind {
        display: inline-block;
        color: var(--neutral-foreground-hint);
        font-style: italic;
        margin-left: 5px;
    }

    .ast-item-operation {
        font-style: italic;
    }
</style>