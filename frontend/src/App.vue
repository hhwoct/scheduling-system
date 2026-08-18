<template>
  <!-- 不在这里加 key，避免整个布局（含 sidebar）每次路由都重新挂载导致菜单重新展开 -->
  <router-view />
</template>

<script setup>
</script>

<style>
html, body, #app {
  height: 100%;
  margin: 0;
  padding: 0;
  background-color: #f5f7fa;
}
</style>
