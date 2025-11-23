<template>
    <fluent-button :appearance="appearance" @click="onclick">
        <slot></slot>
    </fluent-button>
</template>

<script lang="ts" setup>
    import { computed } from 'vue';

    const modelValue = defineModel<Boolean>({ default: false });
    const appearance = computed(() => modelValue.value ? "accent" : "neutral");
    function onclick() {
        modelValue.value = !modelValue.value;
    }
</script>