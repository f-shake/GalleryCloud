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
.\publish.ps1
```

输出目录 `publish/`，运行 `./GalleryCloud.Api`，浏览器打开 http://localhost:5000。

## 技术栈

| 层级 | 技术 |
|------|------|
| 后端 | .NET 10 + EF Core + SQLite |
| 图像 | SixLabors.ImageSharp 3.x |
| 前端 | Vue 3 + TypeScript + Element Plus |
| 认证 | JWT |

## 初始配置

1. `admin / admin` 登录
2. 管理后台 → 添加用户，设置照片根目录
3. 管理后台 → 触发全量扫描
4. 开始浏览

## 特性

- 时间线浏览（按天/月/平铺，缩放联动）
- 文件夹树浏览
- 地图视图（GPS 聚类，管理员可配底图）
- 多维度搜索
- 标签与收藏
- 缩略图按需生成 + 磁盘缓存
- 文件系统实时监控增量更新
- 多用户隔离 + 管理员后台
- 暗色模式（跟随系统）
- 手机端适配

## 配置

启动后所有运行时可调参数在管理后台 → 设置中修改，无需重启。

## 许可证

MIT
