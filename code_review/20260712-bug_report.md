# GalleryCloud 代码审计报告

> 审计范围：后端 .NET 10 (83 个 .cs 文件) + 前端 Vue 3 / TypeScript (34 个 .vue 文件 + 22 个 .ts 文件)
> 审计日期：2026-07-12
> 审计方式：逐行静态代码分析

---

## 🔴 严重级别（CRITICAL）

---

### CRIT-1：硬编码管理员密码 + 默认 JWT Secret 可被远程利用

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Core/Settings/AuthOptions.cs` L5-7、`src/GalleryCloud.Api/appsettings.json` L7-11 |
| **问题** | `JwtSecret = "change-me-to-a-random-string-at-least-32-chars!!"` 是静态已知的硬编码密钥且已提交至 Git。管理员默认密码 `admin` 同样硬编码。 |
| **原因** | JWT 签发和验签使用同一个对称密钥。任何知道密钥的人都可以伪造任意用户的 token。此密钥以明文存在 git 历史中。 |
| **修复** | ① 将 `JwtSecret` 移到环境变量 / 用户机密（`dotnet user-secrets set`）；② `Program.cs` 启动时检查密钥是否仍为默认值，若是则抛出异常并阻止启动；③ 强制管理员首次登录时修改密码。 |
| **复现** | 克隆仓库 → `git log -p` 查看密钥 → 用任意 JWT 库+该密钥签发 `sub=admin` 的 token → 请求任意 `[AdminOnly]` API endpoint → 获得完全管理权限 |
| **严重性** | 🔴 严重 |

---

### CRIT-2：JWT Token 通过 URL 查询参数传输，完全暴露

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Middleware/AuthMiddleware.cs` L88-90、`src/GalleryCloud.Web/src/composables/useThumbnailUrl.ts` L7 |
| **问题** | 缩略图 URL 格式为 `/api/photos/{id}/thumbnail?token={jwt}`，JWT 直接出现在 URL 中。 |
| **原因** | 后端 `AuthMiddleware.ExtractToken()` 支持从 `?token=` 查询参数读取 JWT，前端 `thumbUrl()` 函数将 token 拼接进 URL。 |
| **修复** | ① 用 `SameSite=Lax` 的 httpOnly Cookie 替代 URL token；② 或使用 Service Worker 拦截 img 请求自动附加 Authorization header；③ 若保留 URL token 方案，必须在请求日志中脱敏 token（当前 `_logger.LogInformation` 输出完整 URL）。 |
| **复现** | 浏览器打开任意页面 → F12 网络面板 → 查看任何缩略图请求 URL → token 完整暴露 |
| **严重性** | 🔴 严重 |

---

### CRIT-3：增量扫描按钮实际执行全量扫描

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Controllers/AdminController.cs` L157-167 |
| **问题** | `POST /api/admin/scan/trigger-incremental` 调用 `TriggerFullScanForAllUsersAsync()` 而非增量扫描方法。 |
| **原因** | 复制粘贴错误——方法名和注释都是 "incremental"，但实际调用了全量扫描代码。 |
| **修复** | 改为调用增量扫描方法，或移除该 endpoint 并提示"增量扫描尚未实现"。 |
| **复现** | 前端点"增量扫描"按钮 → 后端日志显示全量枚举所有文件 |
| **严重性** | 🔴 严重 |

---

### CRIT-4：`Task.Run` 火后即忘——异常被静默吞噬 + 请求上下文泄漏

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Controllers/AdminController.cs` L153、L165、L207、L219 等；`UserPanelController.cs` L46、L61、L98、L158、L170 |
| **问题** | `_ = Task.Run(() => _scanService.TriggerFullScanForAllUsersAsync())` 在 ASP.NET 请求管道中直接 `Task.Run` 但不 `await`。 |
| **原因** | ① `Task.Run` 中抛出的异常会被 .NET 运行时吞噬，永远无法被记录或处理；② 后台任务执行时 ASP.NET 的 `HttpContext` 可能已释放。 |
| **修复** | 使用 `IHostedService` + `Channel<T>` 模式处理后台任务；或包装 `BackgroundTaskQueue`（.NET 内置模式），而非直接 `Task.Run`。 |
| **复现** | 触发扫描 → 扫描过程中发生异常 → `_logger.LogError` 永远不会被调用 |
| **严重性** | 🔴 严重 |

---

## 🟠 高危级别（HIGH）

---

### HIGH-1：CORS 策略允许任意来源

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Program.cs` L82-86 |
| **问题** | `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` 允许任何域名的网页访问 API。 |
| **原因** | 为开发方便设置的宽松 CORS 策略，但在生产环境中未更改。内网其他恶意站点可以读取用户数据。 |
| **修复** | 指定具体的前端域名；或对非生产环境使用 `AllowAnyOrigin` 但配合 `SetIsOriginAllowed` 做动态校验。 |
| **复现** | 内网恶意站点 → 构造跨域请求 → API 返回数据 |
| **严重性** | 🟠 高 |

---

### HIGH-2：密码使用 HMACSHA256（单次迭代）存储

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/AuthService.cs` L54-74 |
| **问题** | `HashPassword()` 使用 `HMACSHA256`（单次迭代 HMAC）而非慢速哈希函数。 |
| **原因** | 开发者选择简单的 HMAC 方案，但缺乏密码学安全考虑。现代 GPU 可以每秒进行数十亿次 SHA-256 运算。 |
| **修复** | 使用 `BCrypt.Net` 或 ASP.NET Core 内置的 `PasswordHasher<TUser>`。 |
| **复现** | 获取数据库中的密码哈希 → 用 hashcat 以 GPU 加速破解 → 每秒数十亿次尝试 |
| **严重性** | 🟠 高 |

---

### HIGH-3：管理员身份完全基于硬编码字符串 "admin"

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/AuthService.cs` L31-37、`src/GalleryCloud.Api/Middleware/AdminOnlyAttribute.cs` L22、`src/GalleryCloud.Api/Services/UserContext.cs` L17 |
| **问题** | 全系统通过 `UserId == "admin"` 判断管理员权限。这个逻辑贯穿认证、授权、路由等多个模块。 |
| **原因** | 管理员不存储在数据库中，而是通过特殊判断逻辑硬编码处理。系统没有真正的 RBAC 模型。 |
| **修复** | ① 在 `User` 实体中添加 `IsAdmin` 字段；② 管理员也存数据库；③ 删除特殊的 "admin" 字符串判断逻辑。 |
| **复现** | 只要 CRIT-1 的 JWT Secret 被破解，设置 `sub=admin` 即可获得完全管理员权限 |
| **严重性** | 🟠 高 |

---

### HIGH-4：分享 API 不验证添加的照片是否属于分享者

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ShareService.cs` L72-93 |
| **问题** | `AddPhotosToShareAsync()` 只验证分享属于当前用户，但**不验证** `photoIds` 中的照片是否属于该用户。 |
| **原因** | 代码只做了 `db.Shares.FirstOrDefaultAsync(s => s.Id == shareId && s.UserId == userId)` 的检查，没有联表查询 photos 的 UserId。 |
| **修复** | 添加：`var ownedPhotos = await db.Photos.Where(p => photoIds.Contains(p.Id) && p.UserId == userId).Select(p => p.Id).ToListAsync()` 并在循环中检查。 |
| **复现** | 用户 A 分享链接 → 用户 A 尝试将用户 B 的照片（如果知道 photoId）添加到分享中 |
| **严重性** | 🟠 高 |

---

### HIGH-5：文件夹 API 全量加载所有照片到内存

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Controllers/FoldersController.cs` L66-76（`GetFolderPhotos`）、`TimelineController.cs` L109-116（`GetMonthGroups`） |
| **问题** | 这两个 endpoint 使用 `.ToListAsync()` 将用户的所有照片加载到内存，而非在数据库中进行分页和过滤。 |
| **原因** | SQLite 的 `LIKE` / 路径匹配在 EF Core 中无法轻易翻译，开发者选择了"加载全部再过滤"的方案。 |
| **修复** | 对文件路径过滤可以使用 EF.Functions.Like；或对 `GetMonthGroups` 使用 SQL 分组。对于十万级照片，内存分配可达数百 MB。 |
| **复现** | 用户有 50 万张照片 → 访问文件夹树 → 服务器内存飙升 |
| **严重性** | 🟠 高 |

---

## 🟡 中等级别（MEDIUM）

---

### MED-1：缩略图去重锁可能引发内存泄漏

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ThumbnailService.cs` L25、L124、L152 |
| **问题** | `_photoLocks.GetOrAdd(photoId, _ => new SemaphoreSlim(1, 1))` 在 `ConcurrentDictionary` 中为每张照片创建一个 `SemaphoreSlim`，但**永远不会被移除**。 |
| **原因** | `_photoLocks` 只有添加操作没有移除操作，导致随着处理照片数量的增加而无限增长。 |
| **修复** | 在 `finally` 块中添加 `_photoLocks.TryRemove(...)` 清理已处理的照片锁。或使用弱引用字典。 |
| **严重性** | 🟡 中 |

---

### MED-2：FileWatcher debounce 逻辑会丢失事件

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/FileWatcherService.cs` L115-162 |
| **问题** | debounce 收集期使用 `GetEventKey`（`{userId}|{rootId}|{fullPath}`）作为字典 key、用覆盖的方式保留最后一个事件。如果同一文件先被删除再被创建，最后一个事件（created）会覆盖删除事件，而正确的语义应该是先处理删除再处理创建。 |
| **原因** | debounce 字典设计为"每个文件只保留最后一个事件"但未处理事件顺序语义。 |
| **修复** | 对同一文件的事件队列维护一个列表而非覆盖，或对 deleted+created 序列做特殊处理。 |
| **复现** | 用户快速删除并重新创建同路径图片文件 → 数据库状态不一致 |
| **严重性** | 🟡 中 |

---

### MED-3：地图聚类算法 O(n²) + 纬度畸变

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Controllers/MapController.cs` L95-135 |
| **问题** | `ClusterPoints` 使用 O(n²) 暴力算法、聚类半径使用经纬度度数差值而非实际距离。在赤道 `0.005° ≈ 500m`，但在莫斯科（北纬 55°）`0.005° 经度 ≈ 300m`，导致聚类在不同纬度表现不一致。 |
| **原因** | 简单粗暴的最近邻聚类算法，未使用 Haversine 公式或 grid-based 优化。 |
| **修复** | ① 使用四叉树（quadtree）或网格哈希将复杂度降至 O(n log n)；② 使用 `cos(纬度)` 校正经度半径。 |
| **复现** | 在高纬度地区添加带 GPS 的照片 → 聚类结果异常密集 |
| **严重性** | 🟡 中 |

---

### MED-4：SettingService 静态缓存永不失效

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/SettingService.cs` L14 |
| **问题** | `private static readonly ConcurrentDictionary<string, string?> _cache` 使用静态字典且没有任何过期/淘汰策略。 |
| **原因** | 只缓存设置值但不考虑外部修改。虽然在多实例部署中 `SetAsync` 会更新缓存，但在分布式场景下其他实例不会同步。 |
| **修复** | 添加 `MemoryCacheEntryOptions` 设置滑动过期时间（如 5 分钟）；或使用 `IOptionsMonitor<T>` + `ChangeToken` 模式。 |
| **严重性** | 🟡 中 |

---

### MED-5：缩略图通道消费者与批量再生共享 `_inProgressCount`

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ThumbnailService.cs` L26、L123、L615、L686 |
| **问题** | 字段 `_inProgressCount` 被 `ConsumeChannelAsync`（长期后台消费者）和 `FillMissingAsync` / `RegenerateAllAsync`（一次性批量）共享使用。 |
| **原因** | 这两个路径可能同时运行（如通道消费者在处理队列，管理员同时触发重新生成）。 |
| **修复** | 分别维护两个计数器，或确保调用入口互斥。 |
| **严重性** | 🟡 中 |

---

### MED-6：定时扫描 cron 表达式每次循环都重新解析

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/BackgroundJobs/ScheduledScanJob.cs` L27-38 |
| **问题** | 每次循环迭代都调用 `CronExpression.Parse()` 和 `GetCronExpressionAsync()`（从 DB 读取）。 |
| **原因** | `CronExpression.Parse` 内部有解析开销和内存分配，频繁调用不必要。 |
| **修复** | 缓存 `cron` 对象和 `next` 计算时间，仅在配置更改时重新读取。 |
| **严重性** | 🟡 中 |

---

### MED-7：`PhotoDetail` 响应中包含完整文件路径

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Controllers/PhotosController.cs` L71-80 |
| **问题** | `GET /api/photos/{id}` 返回 `PhotoDetail` 中包含 `FilePath`（照片的相对路径），可泄露用户目录结构。 |
| **原因** | 前端展示需要路径信息，但可能返回过多信息。 |
| **修复** | 考虑匿名化路径或仅在共享上下文中有路径信息时返回。 |
| **严重性** | 🟡 中 |

---

### MED-8：`UserPanelController` 中对 admin 用户返回 `Unauthorized` 而非 `Forbidden`

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Controllers/UserPanelController.cs` L38、L53、L88、L90、L126 |
| **问题** | admin 用户访问用户面板 API 时返回 `401 Unauthorized`（`if (!_userContext.IsAuthenticated || _userContext.IsAdmin) return Unauthorized()`）。 |
| **原因** | 逻辑是"非认证或管理员 → 返回 401"。应该对认证但无权限的请求返回 `403 Forbidden`。 |
| **修复** | 返回 `403`（`Forbid()`）而非 `401`（`Unauthorized()`）。 |
| **严重性** | 🟡 中 |

---

### MED-9：`HashService.ComputeMd5Async` 不支持取消

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/HashService.cs` L7-13 |
| **问题** | `ComputeMd5Async` 方法不接受 `CancellationToken` 参数，在扫描被取消时无法中止大文件的哈希计算。 |
| **原因** | MD5 计算是 CPU 密集型操作（特别是大文件），不支持取消。 |
| **修复** | 添加 `CancellationToken` 参数并在循环中检查。 |
| **严重性** | 🟡 中 |

---

### MED-10：`ScanService.RunScanAsync` 中每张照片更新缩略图 - 频繁 SaveChanges

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ScanService.cs` L315-325 |
| **问题** | 在 `Parallel.ForEachAsync` 循环内部，当文件已存在且内容变化时，调用 `thumbDb.SaveChangesAsync(ct)`。这是 EF Core 中的并发问题——多个线程同时写入 `ThumbnailDbContext`（不是线程安全的！）。 |
| **原因** | `Parallel.ForEachAsync` 中的处理线程共享同一个 `ThumbnailDbContext` 实例，EF Core DbContext 不是线程安全的。 |
| **修复** | 在并行循环中移除 `SaveChangesAsync`，改为在循环结束后批量处理。 |
| **严重性** | 🟡 中 |

---

### MED-11：前端 `useScanStatus` 使用 setInterval 轮询但未清理

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Web/src/composables/useScanStatus.ts` L11 |
| **问题** | 模块级 `setInterval(poll, 5000)` 在组件卸载后不会停止，持续发送 API 请求。 |
| **原因** | 使用模块级变量但没有提供 `stop()` 清理函数。 |
| **修复** | 返回 `stop()` 函数；或改用 `onUnmounted` 清理。 |
| **严重性** | 🟡 中 |

---

### MED-12：`MainLayout` 桌面端和移动端的 `router-view` 重复渲染

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Web/src/views/MainLayout.vue` L133-168 |
| **问题** | 模板中同时存在两个 `<router-view>`（一个在移动端 `<el-container v-else>` 下 L133-137，一个在桌面端 L163-167），通过 `v-if` / `v-else` 切换。但它们是**两个不同的渲染分支**导致路由视图在切换响应式布局时被重建。 |
| **原因** | 使用 `v-if` / `v-else` 布局切换，但 router-view 在不同分支中重建。 |
| **修复** | 将 `router-view` 提取到条件布局外，或使用 CSS 控制显示隐藏代替 v-if。 |
| **严重性** | 🟡 中 |

---

### MED-13：`ExifService.ExtractWithMetadataExtractor` 的 `Image.Identify` 回退路径中有 `MagickImageInfo` 未释放

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ExifService.cs` L53 |
| **问题** | `var info = new MagickImageInfo(filePath)`——`MagickImageInfo` 实现了 `IDisposable`，但没有使用 `using` 语句。 |
| **原因** | 缺少 `using`，但 `MagickImageInfo` 是轻量级对象只读文件头部信息。 |
| **修复** | 添加 `using` 或 `using var info = new MagickImageInfo(filePath)`。 |
| **严重性** | 🟡 中 |

---

## 🔵 低级别（LOW）

---

### LOW-1：EXIF 日期解析脆弱的字符串操作

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ExifService.cs` L361-369 |
| **问题** | `dateStr[..4] + "-" + dateStr[5..7] + "-" + dateStr[8..]` 假定 EXIF 日期字符串格式始终为 `"YYYY:MM:DD HH:mm:ss"`。 |
| **修复** | 使用更健壮的解析方案或正则表达式。 |
| **严重性** | 🔵 低 |

---

### LOW-2：ThumbnailCache CreatedAt 被用于更新时间

| 位置** | `src/GalleryCloud.Api/Services/ThumbnailService.cs` L284 |
| **问题** | 更新已有缓存记录时使用 `record.CreatedAt = DateTime.UtcNow`。 |
| **修复** | 添加 `UpdatedAt` 字段。 |
| **严重性** | 🔵 低 |

---

### LOW-3：SettingService 默认值与 ThumbnailOptions 不一致

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Core/Settings/SettingKeys.cs` vs `ThumbnailOptions.cs` |
| **问题** | `SettingService` 默认缩略图格式是 `"jpeg"`（L23），`ThumbnailOptions` 默认是 `"webp"`（L5）。 |
| **修复** | 统一默认值。 |
| **严重性** | 🔵 低 |

---

### LOW-4：Frontend KeepAlive 缓存导致数据过时

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Web/src/views/MainLayout.vue` L135、L165 |
| **问题** | `<KeepAlive>` 缓存的页面不会自动刷新。 |
| **修复** | 添加 `onActivated` 钩子或使用 `key` 强制重建。 |
| **严重性** | 🔵 低 |

---

### LOW-5：`UserService.CancelTasksForUserAsync` 使用忙等

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/UserService.cs` L243-259 |
| **问题** | `for (var i = 0; i < 100 && ...; i++) await Task.Delay(100)`——最多等待 10 秒，但没有超时保护。 |
| **修复** | 添加总超时时间或使用 `CancellationToken` + `Task.WhenAny`。 |
| **严重性** | 🔵 低 |

---

### LOW-6：`PhotoTag` 缺少 `CreatedAt` 字段

| 位置 | `src/GalleryCloud.Core/Entities/PhotoTag.cs` |
| **问题** | `PhotoTag` 实体没有 `CreatedAt` 字段，无法追踪标签添加时间。 |
| **修复** | 添加 `DateTime CreatedAt` 字段。 |
| **严重性** | 🔵 低 |

---

### LOW-7：`FileWatcher.RemovePhotoFromShareAsync` 删除时没有软删除验证

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ShareService.cs` L104-105 |
| **问题** | `FirstOrDefaultAsync(sp => sp.ShareId == shareId && sp.PhotoId == photoId && !sp.IsDeleted)` 可能查询到已软删除的记录。 |
| **修复** | 考虑是否需要使用 `IgnoreQueryFilters` 或直接移除。 |
| **严重性** | 🔵 低 |

---

### LOW-8：前端路由 /manage 对非管理员不可见但无后端保护

| 条目 | 内容 |
|------|------|
| **位置** | 所有 `/api/user/**` endpoint |
| **问题** | 用户面板（`UserPanelController`）使用 `if (!_userContext.IsAuthenticated || _userContext.IsAdmin) return Unauthorized()` 来判断，但管理员也会得到 `401`。 |
| **修复** | 使用 `[AdminOnly]` 的逆逻辑或检查角色属性。 |
| **严重性** | 🔵 低 |

---

### LOW-9：`ShareService.ListSharesAsync` 每次调用创建新 scope

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ShareService.cs` L117 |
| **问题** | share service 的每个方法都创建独立的 DI scope。 |
| **修复** | 考虑注入 `AppDbContext` 直接使用（但需注意 scope 生命周期）。 |
| **严重性** | 🔵 低 |

---

### LOW-10：`ShareService` 缺少 `IgnoreQueryFilters` 处理过期分享

| 条目 | 内容 |
|------|------|
| **位置** | `src/GalleryCloud.Api/Services/ShareService.cs` L60-70 |
| **问题** | `GetPublicShareAsync` 检查 `!s.IsDeleted` 但 `Share` 实体有 `HasQueryFilter(p => !p.IsDeleted)`，所以不需要手动加 `!s.IsDeleted`。但更严重的问题是 `ExpiresAt` 检查在 `IgnoreQueryFilters` 方面存在不一致。 |
| **修复** | 简化查询条件——查询过滤器已经自动添加。 |
| **严重性** | 🔵 低 |

---

## ⚪ 信息性 / 代码异味（INFO）

---

### INFO-1：`_scopeFactory` 在许多服务中反复创建 scope

| 位置 | `ScanService`、`SettingsService`、`ShareService`、`FileWatcherService` 等 |
| **问题** | 由于 EF Core DbContext 是 scoped 服务，但后台服务是 singleton，开发者使用了 `IServiceScopeFactory` 模式。这在 .NET 中是正确的做法，但代码重复高。 |

### INFO-2：空 catch 块

| 位置 | 多处，例如 `ThumbnailService.cs` L617 `catch { }` |
| **问题** | 完全空白的 catch 块吞噬异常。 |

### INFO-3：`StreamHelper.ReadFullyAsync` 冗余复制

| 位置 | `src/GalleryCloud.Api/Helpers/StreamHelper.cs` |
| **问题** | 传入的 `Stream` 如果是 `MemoryStream` 可以强制转换，无需新建 MemoryStream 再复制。 |

### INFO-4：`ThumbnailService` 中 `ProcessChannelItemAsync` 和 `GenerateOneAsync` 代码几乎完全相同

| 位置 | `src/GalleryCloud.Api/Services/ThumbnailService.cs` L121-174 |
| **问题** | 两个方法逻辑几乎一致，应该提取公共方法。 |

---

## 汇总统计

| 级别 | 计数 |
|------|------|
| 🔴 严重 | 4 |
| 🟠 高 | 5 |
| 🟡 中 | 13 |
| 🔵 低 | 10 |
| ⚪ 信息 | 4 |
| **合计** | **36** |

## 紧急修复优先级排序

1. **CRIT-1** → 替换默认 JWT Secret + 配置环境变量（立刻阻止远程管理员伪造 token）
2. **CRIT-2** → 从 URL 中移除 token（阻止 token 泄露到日志和 Referer）
3. **CRIT-3** → 修复增量扫描按钮（阻止错误的全量扫描）
4. **HIGH-2** → 替换密码哈希算法（阻止 offline 暴力破解）
5. **HIGH-1** → 限制 CORS 策略（阻止内网跨站攻击）
6. **CRIT-4** → 修复后台任务异常处理（防止任务静默失败）
7. **HIGH-4** → 验证分享照片的归属权（防止越权添加照片）
8. **MED-10** → 修复并行扫描中的 DbContext 线程安全问题
