/** 照片网格布局常量 */
export const CELL_GAP = 4
export const ROW_GAP = 4
export const HEADER_HEIGHT = 46

/** 根据容器宽度和列数估算单行高度 */
export function estimateGridRowSize(containerWidth: number, columns: number): number {
  if (!containerWidth || columns < 1) return 100
  return Math.floor((containerWidth - (columns - 1) * CELL_GAP) / columns) + ROW_GAP
}
