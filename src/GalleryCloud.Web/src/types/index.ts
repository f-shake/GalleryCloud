export interface User {
  id: string
  username: string
  displayName: string | null
  isAdmin: boolean
  rootPath: string
}

export interface PhotoSummary {
  id: string
  fileName: string
  fileFormat: string
  width: number | null
  height: number | null
  orientation: number
  takenAt: string | null
  deviceModel: string | null
  latitude: number | null
  longitude: number | null
  fileSize: number
  createdAt: string
}

export interface PhotoDetail extends PhotoSummary {
  filePath: string
  md5Hash: string | null
  updatedAt: string
}

export interface TimelineGroup {
  label?: string
  cursor: string
  photos: PhotoSummary[]
}

export interface TimelineResponse {
  groups: TimelineGroup[]
  nextCursor: string | null
  hasMore: boolean
}

export interface ScanStatus {
  isRunning: boolean
  mode: string | null
  userId: string | null
  startedAt: string | null
  processedFiles: number
  totalFiles: number
  estimatedPercent: number
}

export interface ScanLog {
  id: string
  userId: string
  startedAt: string
  finishedAt: string | null
  totalFound: number
  newAdded: number
  softDeleted: number
  mode: string
}

export interface SystemSettingsMap {
  [key: string]: string
}
