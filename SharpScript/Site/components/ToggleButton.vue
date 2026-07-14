<template>
    <button :class="{ accent: model }" @click="onclick">
        <slot></slot>
    </button>
</template>

<script lang="ts" setup>
    const model = defineModel<Boolean>({ default: () => false });
    function onclick() {
        model.value = !model.value;
    }
</script>