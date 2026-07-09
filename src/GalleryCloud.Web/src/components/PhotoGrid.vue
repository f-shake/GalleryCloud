<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { HEADER_HEIGHT, estimateGridRowSize } from '../composables/usePhotoGridLayout'

interface GridPhoto { id: string; takenAt?: string | null; [key: string]: any }
interface Group { label?: string; photos: GridPhoto[] }

const props = withDefaults(defineProps<{
  photos?: GridPhoto[]
  groups?: Group[]
  columns: number
  selectionMode?: boolean
  selectedIds?: Set<string>
}>(), {
  photos: () => [],
  groups: undefined,
  selectionMode: false,
  selectedIds: () => new Set(),
})

const emit = defineEmits<{
  'photo-click': [id: string, event: MouseEvent]
  'selection-toggle': [id: string]
}>()

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

const virtualizer = useVirtualizer({
  get count() { return rows.value.length },
  getScrollElement: () => containerRef.value,
  estimateSize: (i: number) => {
    const r = rows.value[i]
    return r?.type === 'header' ? HEADER_HEIGHT : estimateGridRowSize(containerWidth.value, props.columns)
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

// --- Drag-to-select ---
let dragActive = false
let dragEncountered = new Set<string>()

function onPointerDown(e: PointerEvent) {
  if (!props.selectionMode) return
  dragActive = true
  dragEncountered = new Set()
  // Toggle the cell under pointer
  const id = findPhotoIdAtPoint(e.clientX, e.clientY)
  if (id) { dragEncountered.add(id); emit('selection-toggle', id) }
}

function onPointerMove(e: PointerEvent) {
  if (!dragActive || !props.selectionMode) return
  const id = findPhotoIdAtPoint(e.clientX, e.clientY)
  if (id && !dragEncountered.has(id)) {
    dragEncountered.add(id)
    emit('selection-toggle', id)
  }
}

function onPointerUp() {
  dragActive = false
  dragEncountered = new Set()
}

function findPhotoIdAtPoint(clientX: number, clientY: number): string | null {
  const el = containerRef.value
  if (!el) return null
  const rect = el.getBoundingClientRect()
  const scrollTop = el.scrollTop
  const relY = clientY - rect.top + scrollTop // Y relative to scroll content
  const width = containerWidth.value || rect.width
  if (!width) return null
  const cols = props.columns || 1
  const rowH = estimateGridRowSize(width, cols)

  // Find which virtual row this Y falls in
  let accY = 0
  for (let i = 0; i < rows.value.length; i++) {
    const r = rows.value[i]
    const h = r.type === 'header' ? HEADER_HEIGHT : rowH
    if (relY >= accY && relY < accY + h) {
      if (r.type !== 'row') return null
      // Which cell in this row?
      const cellW = (width - (cols - 1) * 4) / cols
      const relX = clientX - rect.left
      const cellIdx = Math.floor(relX / (cellW + 4))
      if (cellIdx >= 0 && cellIdx < r.photos.length) {
        return r.photos[cellIdx].id
      }
      return null
    }
    accY += h
  }
  return null
}
</script>

<template>
  <div
    ref="containerRef"
    class="pg-virt"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
    @pointerleave="onPointerUp"
  >
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
              :class="{ 'thumb-cell--selected': selectionMode && selectedIds.has(p.id) }"
              @click="(e: MouseEvent) => { if (!selectionMode) emit('photo-click', p.id, e) }"
            >
              <div v-if="selectionMode" class="thumb-cell-check" :class="{ 'thumb-cell-check--on': selectedIds.has(p.id) }">
                <svg v-if="selectedIds.has(p.id)" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="5,12 10,17 19,8" />
                </svg>
              </div>
              <img v-lazy-img="p.thumbUrl || thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
              <slot name="cell-footer" :photo="p" />
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
  user-select: none;
}
.pg-virt::-webkit-scrollbar { display: none; }
.pg-virt { scrollbar-width: none; }
.thumb-cell--selected { outline: 3px solid var(--el-color-primary); outline-offset: -3px; border-radius: 4px; }
.thumb-cell { position: relative; }
.thumb-cell-check {
  position: absolute; top: 6px; left: 6px; z-index: 2;
  width: 22px; height: 22px; border-radius: 50%;
  border: 2px solid rgba(255,255,255,0.9);
  background: rgba(0,0,0,0.3);
  display: flex; align-items: center; justify-content: center;
  transition: background .15s;
  pointer-events: none;
}
.thumb-cell-check--on { background: var(--el-color-primary); border-color: var(--el-color-primary); }
</style>
