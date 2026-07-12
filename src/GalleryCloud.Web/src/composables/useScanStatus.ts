import { ref } from 'vue'
import client from '../api/client'

const isScanning = ref(false)
let started = false
let intervalId: ReturnType<typeof setInterval> | null = null

export function useScanStatus() {
  if (!started) {
    started = true
    poll()
    intervalId = setInterval(poll, 5000)
  }

  function stop() {
    if (intervalId !== null) {
      clearInterval(intervalId)
      intervalId = null
      started = false
    }
  }

  return { isScanning, stop }
}

async function poll() {
  try { const r = await client.get('/user/scan/status'); isScanning.value = r.data.isRunning }
  catch { /* */ }
}
