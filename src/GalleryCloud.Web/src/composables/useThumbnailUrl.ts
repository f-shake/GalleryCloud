import { API_BASE } from '../api/client'

const retryInterval = 1200 // ms

export function thumbUrl(photoId: string, size = 'grid', width = 400): string {
  const token = localStorage.getItem('token') || ''
  return `${API_BASE}/photos/${photoId}/thumbnail?size=${size}&w=${width}&token=${token}`
}

export function publicThumbUrl(shareToken: string, photoId: string, size = 'grid', width = 400): string {
  return `${API_BASE}/public/shares/${shareToken}/photos/${photoId}/thumbnail?size=${size}&w=${width}`
}

export function publicFileUrl(shareToken: string, photoId: string): string {
  return `${API_BASE}/public/shares/${shareToken}/photos/${photoId}/file`
}

// Fetch a thumbnail URL, retrying if backend returns 202 (pending generation).
// Resolves to a blob URL, or rejects after max retries.
export async function fetchThumbnail(photoId: string, size = 'grid', width = 400, maxRetries = 120): Promise<string> {
  const url = thumbUrl(photoId, size, width)
  const token = localStorage.getItem('token') || ''

  for (let attempt = 0; attempt < maxRetries; attempt++) {
    const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } })
    if (res.ok) {
      const blob = await res.blob()
      return URL.createObjectURL(blob)
    }
    if (res.status === 202) {
      // Pending — wait and retry
      await new Promise(r => setTimeout(r, retryInterval))
      continue
    }
    // Real error
    throw new Error(`Thumbnail fetch failed: ${res.status}`)
  }
  throw new Error('Thumbnail timeout')
}

/** 公开分享专用：获取缩略图（无需 auth header） */
export async function fetchPublicThumbnail(shareToken: string, photoId: string, size = 'grid', width = 400, maxRetries = 120): Promise<string> {
  const url = publicThumbUrl(shareToken, photoId, size, width)
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    const res = await fetch(url)
    if (res.ok) {
      const blob = await res.blob()
      return URL.createObjectURL(blob)
    }
    if (res.status === 202) {
      await new Promise(r => setTimeout(r, retryInterval))
      continue
    }
    throw new Error(`Thumbnail fetch failed: ${res.status}`)
  }
  throw new Error('Thumbnail timeout')
}
