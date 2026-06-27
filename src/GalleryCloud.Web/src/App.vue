<script setup lang="ts">
import { useDark } from './composables/useDark'
useDark()
</script>

<template>
  <router-view />
</template>

<style>
/* Global thumbnail styles — prevent broken image icons */
.thumb-cell {
  cursor: pointer; overflow: hidden; border-radius: 4px;
  background: var(--el-fill-color-light); aspect-ratio: 1;
}
.thumb-img {
  width: 100%; height: 100%; object-fit: cover; display: block;
}
/* Hide empty/broken images — show the thumb-cell background instead */
.thumb-img:not([src]),
.thumb-img[src=""],
.thumb-img[style*="display: none"] {
  visibility: hidden;
}
</style>
