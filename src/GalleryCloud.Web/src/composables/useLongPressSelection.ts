export function useLongPressSelection() {
  function onTouchStart(_e: TouchEvent, _photoId?: string) {}
  function onTouchMove() {}
  function onTouchEnd() {}
  return { onTouchStart, onTouchMove, onTouchEnd }
}
