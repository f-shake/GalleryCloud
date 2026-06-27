export function thumbUrl(photoId: string, size = 'grid', width = 400): string {
  const token = localStorage.getItem('token') || ''
  return `/api/photos/${photoId}/thumbnail?size=${size}&w=${width}&token=${token}`
}
