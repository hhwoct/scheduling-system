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

/* ============ 全局表格吸附 ============ */
/* 所有 el-table：表体内部滚动，表头始终可见（显式设置 height/max-height 的表格仍以自身为准） */
.el-table__body-wrapper {
  max-height: calc(100vh - 240px);
  overflow-y: auto;
}
</style>
