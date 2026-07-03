# GalleryCloud

私有化智能相册管理系统。数据存于本地，通过浏览器跨设备访问。

## 快速开始

```bash
# 后端
cd src/GalleryCloud.Api
dotnet run

# 前端（新终端）
cd src/GalleryCloud.Web
npm install
npm run dev
```

浏览器打开 http://localhost:5175，用 `admin / admin` 登录。

## 一键发布

```powershell
.\publish.ps1                        # 全量构建（前端 + 后端 + 复制静态资源）
.\publish.ps1 -BackendOnly           # 只构建后端
.\publish.ps1 -FrontendOnly          # 只构建前端
.\publish.ps1 -NoCopy                # 全量构建但跳过 wwwroot 复制
```

输出目录 `publish/`，运行 `./GalleryCloud.Api.exe`，浏览器打开 http://localhost:5000。

## 数据库迁移

```bash
dotnet ef migrations add <名称>
```

## 技术栈

| 层级 | 技术 |
|------|------|
| 后端 | .NET 10 + EF Core + SQLite |
| 图像 | SixLabors.ImageSharp 3.x / Magick.NET（可切换） |
| 元数据 | MetadataExtractor |
| 前端 | Vue 3 + TypeScript + Element Plus |
| 地图 | ArcGIS JS API |
| 认证 | JWT |

## 初始配置

1. `admin / admin` 登录
2. 管理后台 → 添加用户，设置照片根目录
3. 管理后台 → 触发全量扫描
4. 开始浏览

## 特性

- 时间线浏览（按天/月/平铺，缩放联动）
- 文件夹树浏览（多根目录支持）
- 地图视图（GPS 聚类/缩略图散点，可切换底图）
- 多维度搜索
- 标签与收藏
- 缩略图按需生成 + 磁盘缓存
- 文件系统实时监控增量更新
- 多用户隔离 + 管理员后台
- 暗色模式（跟随系统）
- 手机端适配

## 配置

启动后所有运行时可调参数在管理后台 → 设置中修改，图片处理引擎（ImageSharp / Magick.NET）也在此切换。

## 下一步计划

- [ ] **Service 层拆分** — 将 AdminController/PhotosController 中的业务逻辑抽取到独立的 Service 类中
- [ ] **查询辅助** — 提炼通用查询方法，消除各 Controller 中重复的过滤条件
- [ ] **多用户共用根目录** — 同一物理路径被多个用户使用时，图片记录和缩略图去重
- [ ] **缩略图清理** — 照片被软删除或根目录移除时，清理对应的缩略图缓存数据
- [ ] **照片回收站** — 软删除的照片保留一段可恢复期，过期后自动清理
- [ ] **WebSocket 推送** — 扫描进度、缩略图生成状态、文件变更实时推送到前端，替代轮询
- [x] **FileWatcherService 未初始化** — `InitializeAsync()` 在 `Program.cs` 中未被调用，导致文件监控功能完全未启动
- [x] **FileWatcherService 时区 Bug** — `HandleAddOrUpdate` 中 `TakenAt` 仍在使用 `.ToUniversalTime()`（`ScanService` 已修复）
- [x] **FileWatcherService 时间兜底** — `HandleAddOrUpdate` 调用 `ExifService.Extract` 时未传入 `fallbackTime`，无 EXIF 日期的文件不拿文件修改时间兜底

## 许可证

MIT
