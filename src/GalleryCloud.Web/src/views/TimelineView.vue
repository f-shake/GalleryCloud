<script setup lang="ts">
import { ref, onMounted, watch, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useTimeline, type RowItem } from '../composables/useTimeline'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'
import TimeScrubber from '../components/timeline/TimeScrubber.vue'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'

const viewStore = usePhotoViewStore()
const { columns, groupLevel, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()
const tl = useTimeline()

const containerRef = ref<HTMLElement | null>(null)
const rows = ref<RowItem[]>([])
const ready = ref(false)

// Mobile: auto-hide scrubber, show on scroll
const showScrubber = ref(window.innerWidth > 767)
let scrubberTimer: ReturnType<typeof setTimeout> | null = null

function onTlScroll() {
  updateHeader()
  if (window.innerWidth <= 767) {
    showScrubber.value = true
    if (scrubberTimer) clearTimeout(scrubberTimer)
    scrubberTimer = setTimeout(() => { showScrubber.value = false }, 1200)
  }
}

const CELL_GAP = 4 // horizontal gap between cells in the grid
const ROW_GAP = 4  // vertical gap between rows (padding-bottom on each grid)

function estimateRowSize() {
  if (!containerRef.value || columns.value < 1) return 100
  const w = containerRef.value.clientWidth
  // Cell width accounts for horizontal gaps, then add vertical spacing
  return Math.floor((w - (columns.value - 1) * CELL_GAP) / columns.value) + ROW_GAP
}

const currentHeader = ref('')

function updateHeader() {
  const st = containerRef.value?.scrollTop ?? 0
  // Don't show sticky header when scrolled near top — the in-line header is visible
  if (st <= 8) { currentHeader.value = ''; return }
  const items = virtualizer.value?.getVirtualItems() ?? []
  for (const item of items) {
    if (item.start <= st && item.end >= st) {
      const r = rows.value[item.index]
      if (r?.type === 'header') {
        // Header is visible in-line, use previous group for sticky
        for (let i = item.index - 1; i >= 0; i--) {
          const prev: any = rows.value[i]
          if (prev?.type === 'header') { currentHeader.value = prev.label; break }
        }
        if (!currentHeader.value) currentHeader.value = '' // still at the very first header
        break
      }
      if (r?.type === 'row') {
        for (let i = item.index; i >= 0; i--) {
          const prev: any = rows.value[i]
          if (prev?.type === 'header') { currentHeader.value = prev.label; break }
        }
        break
      }
    }
  }
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

// Year positions for the scrubber: year → ratio of total scroll height
const yearPositions = ref<{ year: number; ratio: number }[]>([])

function computeYearPositions() {
  const positions: { year: number; offset: number }[] = []
  let acc = 0
  let lastYear = -1

  for (const r of rows.value) {
    if (r.type === 'header') {
      const yr = parseInt(r.date.substring(0, 4))
      if (yr > 0 && yr !== lastYear) {
        lastYear = yr
        positions.push({ year: yr, offset: acc })
      }
    } else if (r.type === 'row') {
      // Flat mode: detect year transitions from photo dates
      for (const p of r.photos) {
        if (p.takenAt) {
          const yr = new Date(p.takenAt).getFullYear()
          if (yr > 0 && yr !== lastYear) {
            lastYear = yr
            positions.push({ year: yr, offset: acc })
          }
        }
      }
    }
    acc += r.type === 'header' ? 46 : estimateRowSize()
  }

  const totalH = acc || 1
  yearPositions.value = positions.map(p => ({
    year: p.year,
    ratio: p.offset / totalH,
  }))
}

async function rebuildRows() {
  rows.value = tl.buildRows(columns.value, groupLevel.value)
  computeYearPositions()
  await nextTick()
  virtualizer.value?.measure()
}

watch([columns, groupLevel], rebuildRows)

onMounted(async () => {
  await tl.init()
  await rebuildRows()
  ready.value = true
})

function onPhotoClick(photoId: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(photoId, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src, tl.allItems.value)
}

function onJumpToDate(dateStr: string) {
  const target = dateStr.substring(0, 10)
  let idx = -1
  // Rows are newest→oldest (descending). Find first item at or before target date.
  for (let i = 0; i < rows.value.length; i++) {
    const r = rows.value[i]
    if (r.type === 'header' && r.date <= target) { idx = i; break }
    if (r.type === 'row' && r.photos[0]?.takenAt) {
      if (r.photos[0].takenAt.substring(0, 10) <= target) { idx = i; break }
    }
  }
  if (idx < 0 && rows.value.length > 0) idx = 0
  if (idx >= 0) virtualizer.value?.scrollToIndex(idx, { align: 'start' })
}

// Map any scrollTop to a date using all loaded items (works for unrendered positions too)
const _dateCache = new Map<number, string>()
function getDateAtScrollTop(scrollTop: number): string {
  const totalH = virtualizer.value?.getTotalSize() ?? 0
  if (totalH <= 0 || tl.allItems.value.length === 0) return ''
  const ratio = Math.max(0, Math.min(1, scrollTop / totalH))
  const bucket = Math.floor(ratio * 1000)
  const cached = _dateCache.get(bucket)
  if (cached !== undefined) return cached
  const idx = Math.floor(ratio * (tl.allItems.value.length - 1))
  const item = tl.allItems.value[idx]
  const result = item?.takenAt?.substring(0, 10) ?? ''
  _dateCache.set(bucket, result)
  return result
}

function getScrollTop() { return containerRef.value?.scrollTop ?? 0 }
function getTotalHeight() { return virtualizer.value?.getTotalSize() ?? 0 }

// Pinch zoom
let pinchStart = 0, pinchEnd = 0
function onTouchStart(e: TouchEvent) {
  if (e.touches.length === 2) {
    pinchStart = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY)
    pinchEnd = pinchStart
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2 && pinchStart > 0) { e.preventDefault(); pinchEnd = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY) }
}
function onTouchEnd() {
  if (pinchStart > 0 && Math.abs(pinchEnd - pinchStart) > 20) { if (pinchEnd > pinchStart) zoomIn(); else zoomOut() }
  pinchStart = 0; pinchEnd = 0
}
</script>

<template>
  <div class="tl-wrap" @touchstart="onTouchStart" @touchmove="onTouchMove" @touchend="onTouchEnd">
    <!-- Toolbar: outside scroll, always visible -->
    <div class="tl-toolbar">
      <PhotoGridToolbar :count="tl.totalPhotos.value" />
    </div>

    <!-- Date header overlay (outside scroll area, doesn't affect virtualizer) -->
    <div v-if="currentHeader" class="tl-overlay-header">
      <el-tag type="info" size="large">{{ currentHeader }}</el-tag>
    </div>

    <!-- Virtual scroller -->
    <div ref="containerRef" class="tl-virt" @scroll="onTlScroll">
      <!-- Virtualized rows -->
      <div :style="{ height: virtualizer.getTotalSize() + 'px', position: 'relative' }">
        <div
          v-for="vItem in virtualizer.getVirtualItems()"
          :key="'v' + vItem.index"
          :style="{ position: 'absolute', top: 0, left: 0, width: '100%', height: vItem.size + 'px', transform: `translateY(${vItem.start}px)` }"
        >
          <template v-if="(rows[vItem.index] as RowItem).type === 'header'">
            <div style="padding:6px 16px 4px 16px">
              <el-tag type="info" size="large">{{ (rows[vItem.index] as any).label }}</el-tag>
            </div>
          </template>
          <template v-else>
            <div :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'4px', paddingBottom:'4px' }">
              <div
                v-for="p in (rows[vItem.index] as any).photos"
                :key="p.id"
                class="thumb-cell"
                @click="onPhotoClick(p.id, $event)"
              >
                <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
              </div>
            </div>
          </template>
        </div>
      </div>
    </div>

    <!-- States: absolute overlay, not in flex flow -->
    <div v-if="!ready && tl.loading" class="tl-state-overlay"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
    <div v-else-if="isScanning && rows.length === 0" class="tl-state-overlay" style="color:var(--el-text-color-secondary)">扫描进行中...</div>
    <div v-else-if="tl.error" class="tl-state-overlay" style="color:var(--el-color-danger)">{{ tl.error }}</div>
  </div>

  <TimeScrubber
    :total-items="tl.totalPhotos.value"
    :earliest-year="tl.earliestYear.value"
    :latest-year="tl.latestYear.value"
    :year-positions="yearPositions"
    :get-scroll-top="getScrollTop"
    :get-total-height="getTotalHeight"
    :get-date-at="getDateAtScrollTop"
    :on-jump-to-date="onJumpToDate"
    :show-scrubber="showScrubber"
  />
</template>

<style>
@media (max-width: 767px) {
  .tl-wrap { padding: 0 !important; }
}
@media (min-width: 768px) {
  .tl-wrap { padding-right: 52px !important; }
}
.tl-wrap { position: absolute; inset: 0; display: flex; flex-direction: column; }

/* Toolbar */
.tl-toolbar {
  flex-shrink: 0;
  display: flex; align-items: center; gap: 8px;
  padding: 4px 16px;
  background: var(--el-bg-color-page);
}
.tl-toolbar-info { font-size: 13px; color: var(--el-text-color-secondary); }

/* Virtual scroll area */
.tl-virt { flex: 1; overflow-y: auto; }
.tl-virt::-webkit-scrollbar { display: none; }
.tl-virt { scrollbar-width: none; }

/* Loading / error / empty states — absolute overlay, never in flex flow */
.tl-state-overlay {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  z-index: 5; pointer-events: none;
}

/* Date header overlay — outside scroll area, absolute positioned */
.tl-overlay-header {
  position: absolute;
  top: 32px; /* below toolbar */
  left: 0; right: 0;
  z-index: 20;
  padding: 4px 16px;
  background: var(--el-bg-color-page);
  pointer-events: none;
}
</style>
