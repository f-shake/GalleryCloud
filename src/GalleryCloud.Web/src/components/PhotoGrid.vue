<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { thumbUrl } from '../composables/useThumbnailUrl'

interface GridPhoto { id: string; [key: string]: any }
interface Group { label?: string; photos: GridPhoto[] }

const props = withDefaults(defineProps<{
  photos?: GridPhoto[]
  groups?: Group[]
  columns: number
}>(), {
  photos: () => [],
  groups: undefined,
})

defineEmits<{ 'photo-click': [id: string, event: MouseEvent] }>()

const CELL_GAP = 4
const ROW_GAP = 4
const containerRef = ref<HTMLElement | null>(null)
const containerWidth = ref(0)

// Build flat rows from props (header + photo rows)
const rows = computed(() => {
  const result: { type: 'header' | 'row'; label?: string; photos: GridPhoto[] }[] = []
  const groups = props.groups ?? [{ photos: props.photos }]
  const cols = props.columns || 1

  for (const g of groups) {
    if (g.label) {
      result.push({ type: 'header', label: g.label, photos: [] })
    }
    for (let i = 0; i < g.photos.length; i += cols) {
      result.push({ type: 'row', photos: g.photos.slice(i, i + cols) })
    }
  }
  return result
})

function estimateRowSize() {
  if (!containerWidth.value || props.columns < 1) return 100
  return Math.floor((containerWidth.value - (props.columns - 1) * CELL_GAP) / props.columns) + ROW_GAP
}

const virtualizer = useVirtualizer({
  get count() { return rows.value.length },
  getScrollElement: () => containerRef.value,
  estimateSize: (i: number) => {
    const r = rows.value[i]
    return r?.type === 'header' ? 46 : estimateRowSize()
  },
  overscan: 2,
})

// Measure container width on mount and resize
onMounted(() => {
  measureWidth()
  window.addEventListener('resize', onResize)
})
function onResize() { measureWidth() }
function measureWidth() {
  if (containerRef.value) {
    const w = containerRef.value.clientWidth
    if (w !== containerWidth.value) {
      containerWidth.value = w
      nextTick(() => virtualizer.value?.measure())
    }
  }
}

watch(() => props.columns, () => {
  nextTick(() => virtualizer.value?.measure())
})
</script>

<template>
  <div ref="containerRef" class="pg-virt">
    <div :style="{ height: virtualizer.getTotalSize() + 'px', position: 'relative' }">
      <div
        v-for="vItem in virtualizer.getVirtualItems()"
        :key="'v' + vItem.index"
        :style="{
          position: 'absolute',
          top: 0,
          left: 0,
          width: '100%',
          height: vItem.size + 'px',
          transform: `translateY(${vItem.start}px)`,
        }"
      >
        <template v-if="rows[vItem.index]?.type === 'header'">
          <div style="padding:6px 16px 4px 16px">
            <el-tag type="info" size="large">{{ rows[vItem.index].label }}</el-tag>
          </div>
        </template>
        <template v-else>
          <div
            :style="{
              display: 'grid',
              gridTemplateColumns: `repeat(${columns}, 1fr)`,
              gap: '4px',
              paddingBottom: '4px',
            }"
          >
            <div
              v-for="p in (rows[vItem.index]?.photos || [])"
              :key="p.id"
              class="thumb-cell"
              @click="(e: MouseEvent) => $emit('photo-click', p.id, e)"
            >
              <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style>
.pg-virt {
  height: 100%;
  overflow-y: auto;
}
.pg-virt::-webkit-scrollbar { display: none; }
.pg-virt { scrollbar-width: none; }
</style>
