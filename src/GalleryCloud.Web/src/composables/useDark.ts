import { ref } from 'vue'

// Default: system preference, fallback to light
function getDefault(): boolean {
  const stored = localStorage.getItem('dark')
  if (stored !== null) return stored === 'true'
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

const isDark = ref(getDefault())

function initDark() {
  document.documentElement.classList.toggle('dark', isDark.value)
}

function toggleDark() {
  isDark.value = !isDark.value
  localStorage.setItem('dark', String(isDark.value))
  document.documentElement.classList.toggle('dark', isDark.value)
}

initDark()

export function useDark() {
  return { isDark, toggleDark }
}
