<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

const props = defineProps<{
  totalItems: number
  earliestYear: number
  latestYear: number
  yearPositions: { year: number; ratio: number }[]
  getScrollTop: () => number
  getTotalHeight: () => number
  getDateAt: (scrollTop: number) => string
  onJumpToDate: (date: string) => void
  showScrubber?: boolean
}>()

const hoverY = ref(-1)
const hoverDate = ref('')
const scrubberEl = ref<HTMLElement | null>(null)
const scrubberHeight = ref(500)
let ro: ResizeObserver | null = null

onMounted(() => {
  if (scrubberEl.value) {
    ro = new ResizeObserver(entries => {
      for (const e of entries) scrubberHeight.value = e.contentRect.height
    })
    ro.observe(scrubberEl.value)
  }
})
onUnmounted(() => ro?.disconnect())

const LABEL_PX = 14 // 10px font + border + padding
const MIN_SEC_PX = 14 // sections smaller than this get hidden (keeps scrubber clean)

// Use density-based year positions from parent, fall back to equal spacing
const sections = computed(() => {
  const positions = props.yearPositions
  if (positions && positions.length > 0) {
    const h = scrubberHeight.value
    let lastShownPx = -Infinity
    return positions.map((p, i) => {
      const nextRatio = i + 1 < positions.length ? positions[i + 1].ratio : 1
      const secHeight = (nextRatio - p.ratio) * h
      const visible = secHeight >= MIN_SEC_PX || i === 0 // always show first year
      const px = p.ratio * h
      const showLabel = visible && (lastShownPx < 0 || (px - lastShownPx) >= LABEL_PX)
      if (showLabel) lastShownPx = px
      return {
        year: p.year,
        ratio: p.ratio,
        nextRatio,
        visible,
        showLabel,
      }
    })
  }
  // Fallback: equal spacing from earliest/latest year (thin out labels)
  if (props.earliestYear <= 0 || props.latestYear <= 0) return []
  const count = props.latestYear - props.earliestYear + 1
  if (count <= 0 || count > 100) return []
  const step = Math.max(1, Math.ceil(count / 15))
  return Array.from({ length: count }, (_, i) => {
    const year = props.latestYear - i
    return {
      year,
      ratio: i / count,
      nextRatio: (i + 1) / count,
      showLabel: i % step === 0,
    }
  })
})

function onHover(e: MouseEvent) {
  const el = scrubberEl.value; if (!el) return
  const rect = el.getBoundingClientRect()
  hoverY.value = e.clientY - rect.top
  const ratio = Math.max(0, Math.min(1, hoverY.value / rect.height))
  hoverDate.value = props.getDateAt(ratio * props.getTotalHeight())
}

function onLeave() { hoverY.value = -1; hoverDate.value = '' }

function onStart(e: MouseEvent | TouchEvent) {
  const y = 'touches' in e ? e.touches[0].clientY : e.clientY
  doJump(y)
  // Mouse: need window-level listeners (mouse events don't track elements)
  if (!('touches' in e)) {
    window.addEventListener('mousemove', onMove)
    window.addEventListener('mouseup', onEnd)
  }
}

function onMove(e: MouseEvent) { doJump(e.clientY) }

function onEnd() {
  window.removeEventListener('mousemove', onMove)
  window.removeEventListener('mouseup', onEnd)
}

// Touch: handled via element-level @touchmove / @touchend / @touchcancel
// (no window listeners — avoids leaking into thumbnail taps)
function onTouchMove(e: TouchEvent) { doJump(e.touches[0].clientY) }
function onTouchEnd() { /* cleanup if needed */ }

function doJump(clientY: number) {
  const el = scrubberEl.value; if (!el) return
  const rect = el.getBoundingClientRect()
  hoverY.value = clientY - rect.top
  const ratio = Math.max(0, Math.min(1, hoverY.value / rect.height))
  const targetScrollTop = ratio * props.getTotalHeight()
  const date = props.getDateAt(targetScrollTop)
  hoverDate.value = date
  if (date) props.onJumpToDate(date)
}

const thumbStyle = computed(() => {
  const totalH = props.getTotalHeight()
  if (totalH <= 0) return { top: '0%' }
  const scrollTop = props.getScrollTop()
  const ratio = Math.min(1, Math.max(0, scrollTop / totalH))
  return { top: `${ratio * 100}%` }
})

onUnmounted(() => {
  window.removeEventListener('mousemove', onMove)
  window.removeEventListener('mouseup', onEnd)
})
</script>

<template>
  <div
    ref="scrubberEl"
    :class="['scrubber', (showScrubber ?? true) ? '' : 'scrubber--hidden']"
    @mousedown.prevent="onStart"
    @mousemove="onHover"
    @mouseleave="onLeave"
    @touchstart.prevent="onStart"
    @touchmove.prevent="onTouchMove"
    @touchend="onTouchEnd"
    @touchcancel="onTouchEnd"
  >
    <!-- Year sections positioned by actual scroll density -->
    <div
      v-for="s in sections"
      :key="s.year"
      class="scrubber-year"
      :style="{
        top: `${s.ratio * 100}%`,
        height: `${(s.nextRatio - s.ratio) * 100}%`,
      }"
    >
      <span v-if="s.showLabel" class="scrubber-label">{{ s.year }}</span>
      <div class="scrubber-bar" />
    </div>

    <!-- Track line -->
    <div class="scrubber-track" />

    <!-- Thumb (blue dot) -->
    <div class="scrubber-thumb" :style="thumbStyle" />

    <!-- Hover tooltip -->
    <div v-if="hoverDate" class="scrubber-tooltip" :style="{ top: hoverY + 'px' }">
      {{ hoverDate }}
    </div>
  </div>
</template>

<style scoped>
.scrubber {
  position: fixed;
  right: 0;
  top: 52px;
  bottom: 0;
  width: 48px;
  z-index: 50;
  cursor: pointer;
  transition: opacity .3s ease;
  padding: 4px 0;
}
@media (max-width: 767px) {
  .scrubber { width: 32px; }
}
.scrubber--hidden { opacity: 0; pointer-events: none; }

/* Track — thin line on the right */
.scrubber-track {
  position: absolute;
  right: 6px;
  top: 4px;
  bottom: 4px;
  width: 2px;
  background: var(--el-border-color);
  border-radius: 1px;
  pointer-events: none;
}

/* Year section */
.scrubber-year {
  position: absolute;
  left: 0;
  right: 12px;
  display: flex;
  align-items: flex-end;
  justify-content: flex-end;
  gap: 3px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  min-height: 4px;
}

.scrubber-label {
  font-size: 10px;
  color: var(--el-text-color-secondary);
  line-height: 1;
  white-space: nowrap;
  flex-shrink: 0;
}

.scrubber-bar {
  flex: 1;
  max-width: 10px;
  min-height: 3px;
  border-radius: 1px;
  background: var(--el-color-primary-light-7);
  align-self: flex-end;
  margin-bottom: 1px;
}

/* Thumb — blue dot on track */
.scrubber-thumb {
  position: absolute;
  right: 3px;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--el-color-primary);
  box-shadow: 0 0 4px rgba(0,0,0,.15);
  transform: translateY(-50%);
  z-index: 51;
  pointer-events: none;
}

/* Tooltip */
.scrubber-tooltip {
  position: absolute;
  right: 52px;
  transform: translateY(-50%);
  background: var(--el-color-primary);
  color: #fff;
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
  white-space: nowrap;
  z-index: 52;
  pointer-events: none;
}
</style>
