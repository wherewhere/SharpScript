<template />

<script lang="ts" setup>
    import { watchPostEffect } from "vue";

    const { lang, description } = defineProps<{
        lang: string;
        description: string;
    }>();

    watchPostEffect(() => document.documentElement.lang = lang);
    watchPostEffect(() => {
        const meta = document.querySelector("meta[name='description']");
        if (meta instanceof HTMLMetaElement) {
            meta.content = description;
        }
    });
</script>