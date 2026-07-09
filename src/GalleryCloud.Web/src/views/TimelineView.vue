<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useSelectionStore } from '../stores/selectionStore'
import { useTimeline, type RowItem } from '../composables/useTimeline'
import { HEADER_HEIGHT, estimateGridRowSize } from '../composables/usePhotoGridLayout'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick } from '../composables/usePhotoClick'
import { useLongPressSelection } from '../composables/useLongPressSelection'
import TimeScrubber from '../components/timeline/TimeScrubber.vue'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import BatchToolbar from '../components/BatchToolbar.vue'

const { columns, groupLevel, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()
const tl = useTimeline()
const selStore = useSelectionStore()
const { onPhotoClick } = usePhotoClick(() => tl.allLoadedItems.value)
const { onTouchStart: onLpTouchStart, onTouchMove: onLpTouchMove, onTouchEnd: onLpTouchEnd } = useLongPressSelection()

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

function estimateRowSize() {
  if (!containerRef.value || columns.value < 1) return 100
  return estimateGridRowSize(containerRef.value.clientWidth, columns.value)
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
    return r?.type === 'header' ? HEADER_HEIGHT : estimateRowSize()
  },
  overscan: 2,
})

// Year positions for the scrubber: year → ratio of total scroll height
const yearPositions = ref<{ year: number; ratio: number }[]>([])
const yearPhotoCounts = ref<number[]>([])

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
        if (p.takenAtDate) {
          const yr = Math.floor(p.takenAtDate / 10000)
          if (yr > 0 && yr !== lastYear) {
            lastYear = yr
            positions.push({ year: yr, offset: acc })
          }
        }
      }
    }
    acc += r.type === 'header' ? HEADER_HEIGHT : estimateRowSize()
  }

  const totalH = acc || 1
  yearPositions.value = positions.map(p => ({
    year: p.year,
    ratio: p.offset / totalH,
  }))

  // Count photos per year from density data (not loaded items)
  const counts: number[] = new Array(positions.length).fill(0)
  for (const dd of tl.dayDensities.value) {
    const yr = parseInt(dd.date.substring(0, 4))
    for (let i = 0; i < positions.length; i++) {
      if (yr === positions[i].year) { counts[i] += dd.count; break }
    }
  }
  yearPhotoCounts.value = counts
  _dateCache.clear()
}

async function rebuildRows() {
  rows.value = tl.buildRows(columns.value, groupLevel.value)
  computeYearPositions()
  await nextTick()
  virtualizer.value?.measure()
}

// 视口驱动：加载可见日的照片 IDs
function loadVisibleDays() {
  const vItems = virtualizer.value?.getVirtualItems()
  if (!vItems) return
  const datesNeeded = new Set<string>()
  for (const vItem of vItems) {
    const row = rows.value[vItem.index]
    if (row?.type === 'row') {
      for (const dk of row.dayKeys) {
        if (dk !== '__null__') datesNeeded.add(dk)
      }
    }
  }
  for (const date of datesNeeded) {
    tl.loadDayIds(date)
  }
}

// 监听虚拟项变化 → 加载可见日
watch(
  () => virtualizer.value?.getVirtualItems().map(i => i.index),
  () => { loadVisibleDays() },
  { flush: 'post' }
)

// 监听 dayDensities 加载完成 → 重建行（只有 loaded 变化时才重建）
watch(
  () => tl.dayDensities.value.map(d => d.loaded),
  async (curr, prev) => {
    if (!prev) return // 初始值，跳过
    for (let i = 0; i < curr.length; i++) {
      if (curr[i] && !prev[i]) { await rebuildRows(); return }
    }
  },
)

watch([columns, groupLevel], rebuildRows)

// Sync loaded items to selection store for date-range selection
watch(() => tl.allLoadedItems.value, (val) => {
  selStore.setViewPhotos(val.map(p => {
    // Convert YYYYMMDD integer to date string for date range filtering
    let takenAt: string | null = null
    if (p.takenAtDate) {
      const s = String(p.takenAtDate)
      takenAt = `${s.substring(0, 4)}-${s.substring(4, 6)}-${s.substring(6, 8)}`
    }
    return { id: p.id, takenAt }
  }))
}, { immediate: true, deep: true })

function onTlResize() { virtualizer.value?.measure() }

onMounted(async () => {
  await tl.init()
  await tl.loadNullDateIds()
  await rebuildRows()
  ready.value = true
  loadVisibleDays()
  window.addEventListener('resize', onTlResize)
  window.addEventListener('photo-hidden', onPhotoHidden)
})
onUnmounted(() => {
  window.removeEventListener('resize', onTlResize)
  window.removeEventListener('photo-hidden', onPhotoHidden)
})

function onJumpToDate(dateStr: string) {
  const target = dateStr.substring(0, 10)
  let idx = -1
  // Rows are newest→oldest (descending). Find first item at or before target date.
  for (let i = 0; i < rows.value.length; i++) {
    const r = rows.value[i]
    if (r.type === 'header' && r.date <= target) { idx = i; break }
    if (r.type === 'row' && r.photos[0]?.takenAtDate) {
      const d = String(r.photos[0].takenAtDate)
      if (`${d.substring(0, 4)}-${d.substring(4, 6)}-${d.substring(6, 8)}` <= target) { idx = i; break }
    }
  }
  if (idx < 0 && rows.value.length > 0) idx = 0
  if (idx >= 0) virtualizer.value?.scrollToIndex(idx, { align: 'start' })
}

/** 该日期的所有照片是否已全选 */
function isDayAllSelected(date: string): boolean {
  // 优先检查 timeline 已加载的数据
  const dd = tl.dayDensities.value.find(d => d.date === date)
  if (dd?.ids?.length) {
    return dd.ids.every(p => selStore.selectedIds.has(p.id))
  }
  // 兜底：用 viewPhotos（可能被服务端全选填充过）
  const dayPhotos = selStore.viewPhotos.filter((p: { id: string; takenAt: string | null }) => p.takenAt?.startsWith(date))
  if (dayPhotos.length > 0) {
    return dayPhotos.every((p: { id: string; takenAt: string | null }) => selStore.selectedIds.has(p.id))
  }
  return false
}

/** 选择/取消选择指定日期的所有照片 */
async function selectDay(date: string) {
  await tl.loadDayIds(date)
  const dd = tl.dayDensities.value.find(d => d.date === date)
  if (!dd?.ids?.length) return
  if (!selStore.enabled) selStore.enable('timeline')

  const dayIds = dd.ids.map(p => p.id)
  const allSelected = dayIds.every(id => selStore.selectedIds.has(id))

  // 基于当前已选，追加或移除本日，不丢失其他天的选择
  const newSet = new Set(selStore.selectedIds)
  if (allSelected) {
    for (const id of dayIds) newSet.delete(id)
  } else {
    for (const id of dayIds) newSet.add(id)
  }
  selStore.selectAll(Array.from(newSet))
}

/** 批量隐藏后刷新 timeline */
async function onBatchHide(_hiddenIds: string[]) {
  await tl.init()
  await tl.loadNullDateIds()
  await rebuildRows()
  loadVisibleDays()
}

/** 单张隐藏后刷新 timeline */
async function onPhotoHidden(e: Event) {
  const id = (e as CustomEvent).detail.id
  // 从已加载的照片中移除
  const idx = tl.allLoadedItems.value.findIndex(p => p.id === id)
  if (idx >= 0) {
    tl.allLoadedItems.value.splice(idx, 1)
    await rebuildRows()
    loadVisibleDays()
  }
}

// Map any scrollTop to a date using daily density data
const _dateCache = new Map<number, string>()
function getDateAtScrollTop(scrollTop: number): string {
  const totalH = virtualizer.value?.getTotalSize() ?? 0
  const ys = yearPositions.value
  const counts = yearPhotoCounts.value
  const densities = tl.dayDensities.value
  if (totalH <= 0 || ys.length === 0 || counts.length === 0 || densities.length === 0) return ''
  const ratio = Math.max(0, Math.min(1, scrollTop / totalH))

  // Find which year section this scrollTop falls in
  let yearIdx = ys.length - 1
  for (let i = 0; i < ys.length; i++) {
    const nextRatio = i + 1 < ys.length ? ys[i + 1].ratio : 1
    if (ratio >= ys[i].ratio && ratio < nextRatio) { yearIdx = i; break }
  }

  // Cache by bucket
  const bucket = Math.floor(ratio * 1000)
  const cached = _dateCache.get(bucket)
  if (cached !== undefined) return cached

  // Photos in newer years (before this one in newest-first order)
  let beforeCount = 0
  for (let i = 0; i < yearIdx; i++) beforeCount += counts[i]

  // Progress within this year's scroll section
  const yearStart = ys[yearIdx].ratio
  const yearEnd = yearIdx + 1 < ys.length ? ys[yearIdx + 1].ratio : 1
  const yearProgress = yearEnd > yearStart ? Math.max(0, Math.min(1, (ratio - yearStart) / (yearEnd - yearStart))) : 0
  const yearOffset = Math.min(counts[yearIdx] - 1, Math.floor(yearProgress * counts[yearIdx]))

  // Find which day this offset falls in by iterating densities within the target year
  const targetYear = ys[yearIdx].year
  let acc = 0
  let result = ''
  for (const dd of densities) {
    const yr = parseInt(dd.date.substring(0, 4))
    if (yr !== targetYear) continue
    acc += dd.count
    if (acc > yearOffset) { result = dd.date; break }
  }
  // Fallback to last day of the year
  if (!result) {
    for (const dd of densities) {
      const yr = parseInt(dd.date.substring(0, 4))
      if (yr === targetYear) result = dd.date
    }
  }

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
      <PhotoGridToolbar :count="tl.totalPhotos.value">
        <template #left>
          <el-button v-if="!selStore.enabled" text size="small" @click="selStore.enable('timeline')">
            <el-icon><Select /></el-icon>选择
          </el-button>
          <BatchToolbar v-else @batch-hide="onBatchHide" />
        </template>
      </PhotoGridToolbar>
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
            <div style="padding:6px 16px 4px 16px;display:flex;align-items:center;gap:8px;flex-wrap:wrap">
              <el-tag type="info" size="large">{{ (rows[vItem.index] as any).label }}</el-tag>
              <el-button v-if="selStore.enabled" text size="small" @click="selectDay((rows[vItem.index] as any).date)">{{ isDayAllSelected((rows[vItem.index] as any).date) ? '全不选' : '全选' }}</el-button>
            </div>
          </template>
          <template v-else>
            <div :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'4px', paddingBottom:'4px' }">
              <template v-if="(rows[vItem.index] as any).loading">
                <div v-for="i in ((rows[vItem.index] as any).estimatedCount || columns)" :key="'s-' + i" class="thumb-cell skeleton" />
              </template>
              <template v-else>
                <div
                  v-for="p in (rows[vItem.index] as any).photos"
                  :key="p.id"
                  class="thumb-cell"
                  :class="{ 'thumb-cell--selected': selStore.enabled && selStore.selectedIds.has(p.id) }"
                  @click="selStore.enabled ? selStore.toggle(p.id) : onPhotoClick(p.id, $event)"
                  @touchstart.passive="selStore.enabled ? undefined : onLpTouchStart($event, p.id)"
                  @touchmove="selStore.enabled ? undefined : onLpTouchMove"
                  @touchend="selStore.enabled ? undefined : onLpTouchEnd"
                >
                  <div v-if="selStore.enabled" class="thumb-cell-check" :class="{ 'thumb-cell-check--on': selStore.selectedIds.has(p.id) }">
                    <svg v-if="selStore.selectedIds.has(p.id)" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                      <polyline points="5,12 10,17 19,8" />
                    </svg>
                  </div>
                  <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
                </div>
              </template>
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

/* Skeleton cells for unloaded days — same color as thumb-cell fallback */
.thumb-cell.skeleton {
  border-radius: 4px;
}
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
