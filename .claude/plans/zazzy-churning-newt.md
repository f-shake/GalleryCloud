# 优化 /api/photos/ids 的 8MB 大数据量问题

## Context

`/api/photos/ids` 返回 10 万条 `PhotoIdentity`（`{id, takenAt}`），序列化后约 8MB JSON。耗时主要来自：网络传输（尤其是低带宽/高延迟）、前端 JSON 解析（10 万对象）、内存占用。该数据被 `useTimeline.init()` 加载，用于构建虚拟滚动行和照片导航。

## 方案一：启用 HTTP 响应压缩（gzip/brotli）

`Program.cs` 中未配置任何响应压缩。ASP.NET Core 内置支持。压缩后 8MB → 约 500KB-1MB，传输时间减少 5-10 倍。

**改动：`Program.cs`**
- `builder.Services.AddResponseCompression(...)` 配置 Brotli + Gzip
- `app.UseResponseCompression()` 放在 `app.UseCors()` 之前

**优点**：一行配置，所有 API 受益，不仅仅是 ids 端点

## 方案二：优化 DTO 格式为并行扁平数组

当前格式（JSON 数组包对象）：
```json
[{"id":"123...","takenAt":"2026-01-01T..."}, ...]
```

改为两个并行数组，消除重复 key 开销：
```json
{"i":["id1","id2",...],"d":[20260101,null,20260102,...]}
```
- `takenAt` 改为 `int?`（YYYYMMDD 整数），空时传 0 或 null
- 前端仍可组合为 `PhotoIdItem[]`

**改动：`ApiDtos.cs`** — 新增 `PhotoIdsResponse` 记录类型
**改动：`GalleryCloudJsonContext.cs`** — 注册新类型
**改动：`PhotosController.cs`** — `GetIds` 返回新格式
**改动：`useTimeline.ts`** — 适配新响应格式
**改动：`photoViewStore.ts`** — 适配（若使用）

## 组合效果预估

| 措施 | 传输大小 | 备注 |
|------|----------|------|
| 当前 | ~8MB | |
| + 压缩 | ~800KB-1.2MB | 文本压缩率高 |
| + 扁平数组 | ~5-6MB（未压缩） | 去掉 key 节省 ~30% |
| + 压缩 + 扁平 | ~400-600KB | 最佳效果 |

## 实施顺序

1. Program.cs — 添加响应压缩（影响全局，最大收益）
2. ApiDtos.cs + PhotosController.cs — 返回并行扁平数组
3. 前端 useTimeline.ts — 解析新格式
4. 前端 photoViewStore.ts — 若需要适配

## 验证

- 启动后端
- 浏览器打开 `/api/photos/ids`，检查响应头 `Content-Encoding: br` 或 `gzip`
- 比较压缩前后 payload 大小
- 时间线页面加载流畅，无卡顿
