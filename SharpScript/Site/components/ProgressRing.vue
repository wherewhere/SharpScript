<template>
    <svg class="refresh-ring" width="16" height="16" viewBox="0 0 16 16">
        <circle class="indeterminate-indicator" cx="8px" cy="8px" r="7px"></circle>
    </svg>
</template>

<style lang="scss" scoped>
    @use "../styles/theme";
    @use "../styles/colors";

    $progress-ring-foreground-theme: colors.$accent-fill-color-default;
    $progress-ring-background-theme: colors.$control-fill-color-transparent;

    .indeterminate-indicator {
        fill: none;
        stroke-width: 2px;
        stroke-linecap: round;
        transform-origin: 50% 50%;
        transform: rotate(-90deg);
        transition: 0.2s ease-in-out;
        animation: 2s linear 0s infinite normal none spin-infinite;

        @include theme.auto-theme {
            stroke: theme.themed($progress-ring-foreground-theme);
        }
    }

    @keyframes spin-infinite {
        0% {
            stroke-dasharray: 0.01px, 43.97px;
            transform: rotate(0deg);
        }

        50% {
            stroke-dasharray: 21.99px, 21.99px;
            transform: rotate(450deg);
        }

        100% {
            stroke-dasharray: 0.01px, 43.97px;
            transform: rotate(1080deg);
        }
    }
</style>