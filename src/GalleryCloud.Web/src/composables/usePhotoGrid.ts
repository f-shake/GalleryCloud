import { ref, computed } from 'vue'

export function usePhotoGrid() {
  const columns = ref(5) // 3-12

  const groupLevel = computed<'day' | 'month' | 'none'>(() => {
    if (columns.value <= 5) return 'day'
    if (columns.value <= 9) return 'month'
    return 'none'
  })

  function zoomIn() { columns.value = Math.max(3, columns.value - 1) }
  function zoomOut() { columns.value = Math.min(12, columns.value + 1) }
  function setColumns(n: number) { columns.value = Math.max(3, Math.min(12, n)) }

  return { columns, groupLevel, zoomIn, zoomOut, setColumns }
}
