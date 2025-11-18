<template>
    <fluent-button :appearance="appearance" @click="onclick">
        <slot></slot>
    </fluent-button>
</template>

<script lang="ts">
    export default {
        name: "ToggleButton",
        props: {
            modelValue: Boolean
        },
        computed: {
            appearance() {
                return this.modelValue ? "accent" : "neutral";
            }
        },
        emits: ["update:modelValue"],
        methods: {
            onclick() {
                this.$emit("update:modelValue", !this.modelValue);
            }
        }
    };
</script>