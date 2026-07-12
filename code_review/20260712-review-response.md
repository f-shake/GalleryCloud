# GalleryCloud 代码审计回应报告

> 回应日期：2026-07-12
> 基于原始报告：`20260712-bug_report.md`
> 部署场景：私有部署，非公有云多租户服务

---

## 🔴 严重级别（CRITICAL）

---

### CRIT-1：硬编码管理员密码 + 默认 JWT Secret

| 条目 | 内容 |
|------|------|
| **判定** | ✅ **确认，已修复** |
| **评估** | 真实问题。JWT Secret 和默认密码均以明文存在于 git 中，任何接触仓库的人可伪造任意 token。 |
| **修复** | ① `AuthOptions.cs` 移除不安全默认值，`JwtSecret` 和 `AdminDefaultPassword` 改为空字符串 + `[Required]` 验证注解；② `Program.cs` 从 `Configure<>` 改为 `AddOptions<>().Bind().ValidateDataAnnotations().ValidateOnStart()`，启动时配置不完整直接崩溃，杜绝静默回退。 |

---

### CRIT-2：JWT Token 通过 URL 查询参数传输

| 条目 | 内容 |
|------|------|
| **判定** | ⚠️ **确认有日志泄露风险，已修复** |
| **评估** | 报告中"浏览器历史记录泄露"不成立（`<img>` 子资源请求不进历史），"网络抓包"在 HTTPS 下不成立。**真实风险是 token 写入服务器访问日志。** |
| **修复** | 采用 cookie 回退方案：① 后端 `AuthMiddleware.ExtractToken` 增加 `Request.Cookies["token"]` 读取（放 query param 之前，保留其作为最后兼容）；② 前端 `authStore.login()` 成功后写 `SameSite=Lax` cookie，`logout()` 清 cookie；③ `useThumbnailUrl.thumbUrl()` 和 `useThumbnailQueue.fetchThumbnailImage()` 移除 URL 中的 `?token=`。`<img>` 标签请求靠浏览器自动发送 cookie 认证，JS fetch 仍使用 `Authorization: Bearer` header。 |

---

### CRIT-3：增量扫描按钮实际执行全量扫描

| 条目 | 内容 |
|------|------|
| **判定** | ⚠️ **代码错误确认，但实际无影响，已删除** |
| **评估** | `TriggerIncrementalScan` 确实调用了 `TriggerFullScanForAllUsersAsync()`（第 165 行），典型的复制粘贴 bug。但经代码审查发现，`ScanRootAsync` 中**全量和增量的跳过逻辑完全一致**（均检查 `FileSize + FileModifiedAt`，均执行软删除检测），`ScanMode` 参数仅在写日志标签时使用。两个模式**行为无差异**。 |
| **修复** | 删除了增量扫描的完整链路：`IScanService.TriggerIncrementalScanAsync` 接口、`ScanService` 中两个增量方法、`AdminController` 和 `UserPanelController` 的 `trigger-incremental` endpoint。前端保留扫描历史中 `incremental` 标签的显示映射以兼容旧日志。 |

---

### CRIT-4：`Task.Run` 火后即忘——异常被吞没

| 条目 | 内容 |
|------|------|
| **判定** | ⚠️ **部分属实，有选择地修复** |
| **评估** | 逐条核查报告的三个子项：① **HttpContext 已释放** — ❌ 不成立。所有后台任务均通过 `IServiceScopeFactory` 创建独立 DI scope，不访问 `HttpContext`；② **异常被静默吞噬** — ✅ 部分成立。扫描和 EXIF 刷新方法缺少外层 `catch`；③ **CancellationToken 无效** — ⚠️ 成立但无影响。HTTP 请求超时不取消后台任务是正确设计（不应因用户关浏览器中断长时间扫描），系统有独立 `/scan/cancel` 取消机制。 |
| **修复** | 修补了 3 处真正缺少异常处理的位置：`AdminController.TriggerFullScan`、`UserPanelController.TriggerFullScan`、`UserPanelController.RefreshExif` 的 `Task.Run` lambda 添加 `try-catch` + `LogError`。其他 4 处（缩略图相关）内部已有 `catch { }` 处理，不做修改。 |

---

## 🟠 高危级别（HIGH）

---

### HIGH-1：CORS 策略允许任意来源

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不构成漏洞，不修复** |
| **评估** | 该问题不理解 Bearer token 鉴权的工作方式。`AllowAnyOrigin` 在 CORS 中仅控制浏览器是否暴露响应体给跨域 JS。攻击链：恶意网站 → 浏览器发跨域请求 → 由于 token 在 `localStorage`（跨域 JS 不可读），请求不带 `Authorization` header → 服务器返回 `401` → 攻击者拿到一条"未授权"的 JSON 响应。没有有效的 token，CORS 开放访问没有任何价值。`AllowCredentials()` 才是危险组合，本项目未使用。 |

---

### HIGH-2：密码使用 HMACSHA256 单次迭代

| 条目 | 内容 |
|------|------|
| **判定** | ⚠️ **技术上正确，但对本场景风险极低，不修复** |
| **评估** | HMACSHA256 确实不是慢速密码哈希函数（`bcrypt`/`argon2` 慢约 10⁵ 倍）。但 attack vector 是攻击者必须先获取 SQLite 数据库文件——能读到 `App_Data/gallerycloud.db` 的攻击者大概率也能直接读照片文件。私有部署场景下此风险可接受。 |

---

### HIGH-3：管理员身份完全基于硬编码字符串 "admin"

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不是独立漏洞，不修复** |
| **评估** | 该问题实质是 CRIT-1（JWT Secret 泄露）的复述。报告的攻击路径为"JWT secret 泄露 → 伪造 `sub=admin` token → 管理员权限"。`UserId == "admin"` 的判断方式本身不是安全缺陷。其他所述攻击路径均不可行：① 无法创建 GUID 为 `"admin"` 的普通用户（`User.cs:5` 自动生成随机 GUID）；② 无法注册 `username = "admin"` 的用户获得特权（`LoginAsync` 硬编码拦截且 `[AdminOnly]` 保护创建用户接口）。 |

---

### HIGH-4：分享 API 不验证添加的照片是否属于分享者

| 条目 | 内容 |
|------|------|
| **判定** | ✅ **逻辑缺陷确认，已修复** |
| **评估** | `AddPhotosToShareAsync` 确实未验证 `photoIds` 中的照片归属权。利用条件：用户 A 必须知道用户 B 的 `photoId`（GUID，不可枚举）。私有部署场景下攻击面极小，但修复成本极低（3 行），顺手修复。 |
| **修复** | `ShareService.AddPhotosToShareAsync` 在添加照片前增加归属验证：`db.Photos.CountAsync(p => photoIds.Contains(p.Id) && p.UserId == userId)`，计数不匹配则抛 `InvalidOperationException`。 |

---

### HIGH-5：文件夹 API 全量加载所有照片到内存

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **性能问题，非安全漏洞，不修复** |
| **评估** | `FoldersController.GetFolderPhotos` 和 `TimelineController.GetMonthGroups` 确实使用 `.ToListAsync()` 全量加载后内存过滤，在照片数量极大（十万级以上）时可能导致内存压力。这是性能问题，非安全问题。修复需重构查询逻辑（改用 `EF.Functions.Like`），工作量与风险不匹配当前场景，待实测有性能瓶颈时再处理。 |

---

## 🟡 中等级别（MEDIUM）

---

### MED-1：缩略图去重锁可能引发内存泄漏

| 条目 | 内容 |
|------|------|
| **判定** | ✅ **已修复** |
| **评估** | `_photoLocks` 字典在 `GetOrAdd` 后没有 `TryRemove`，每张照片的 `SemaphoreSlim` 永远不会释放。长期运行后内存无限增长。 |
| **修复** | `ProcessChannelItemAsync` 和 `GenerateOneAsync` 的 `finally` 块中添加 `_photoLocks.TryRemove(photoId, out _)`。 |

---

### MED-2：FileWatcher debounce 逻辑会丢失事件

| 条目 | 内容 |
|------|------|
| **判定** | ✅ **已修复** |
| **评估** | debounce 字典使用覆盖（`batch[key] = nextEvent`），同一文件快速删除+重建时最后一个事件覆盖前者，正确语义应先删除再创建。 |
| **修复** | 将 `Dictionary<string, FileEvent>` 改为 `Dictionary<string, List<FileEvent>>`，每个文件保留事件列表而非覆盖。处理时 `SelectMany` 展开保持顺序。 |

---

### MED-3：地图聚类算法 O(n²) + 纬度畸变

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | 算法重写工程量大。私有部署用户不在高纬度地区，无实际影响。 |

---

### MED-4：SettingService 静态缓存永不失效

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | 单实例部署，`SetAsync` 直接更新缓存，不存在分布式同步问题。 |

---

### MED-5：缩略图通道消费者与批量再生共享 `_inProgressCount`

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | 两个入口在实际代码路径中有互斥检查（`RegenerationStatus.IsRunning`），不会同时运行。 |

---

### MED-6：定时扫描 cron 表达式每次循环都重新解析

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | ~24 小时循环一次的解析开销可忽略不计。 |

---

### MED-7：`PhotoDetail` 响应中包含完整文件路径

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | 自用系统不存在"泄露给谁"的问题。前端展示需要文件路径信息。 |

---

### MED-8：`UserPanelController` 中对 admin 用户返回 `Unauthorized` 而非 `Forbidden`

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | HTTP 状态码语义差异，不影响功能。 |

---

### MED-9：`HashService.ComputeMd5Async` 不支持取消

| 条目 | 内容 |
|------|------|
| **判定** | ✅ **已修复** |
| **评估** | 该方法目前未被调用，但缺少 `CancellationToken` 参数。 |
| **修复** | 添加 `CancellationToken ct = default` 参数并传递给 `md5.ComputeHashAsync(stream, ct)`。 |

---

### MED-10：`ScanService.RunScanAsync` 中每张照片更新缩略图 - 频繁 SaveChanges

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **误报，不修复** |
| **评估** | 报告声称 `SaveChangesAsync` 在 `Parallel.ForEachAsync` 内部，但实际代码中它在**顺序 `foreach` 循环内**（并行循环只负责收集数据到 `ConcurrentBag`，所有数据库操作在并行结束后串行执行）。不存在线程安全问题。每个文件单独 `SaveChanges` 有性能开销，但不会导致数据损坏。 |

---

### MED-11：前端 `useScanStatus` 使用 `setInterval` 轮询但未清理

| 条目 | 内容 |
|------|------|
| **判定** | ✅ **已修复** |
| **评估** | 模块级 `setInterval` 没有提供清理途径，组件卸载后持续发送 API 请求。 |
| **修复** | 保存 `intervalId`，返回 `stop()` 函数，调用者可主动停止轮询。 |

---

### MED-12：`MainLayout` 桌面端和移动端的 `router-view` 重复渲染

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **不修复** |
| **评估** | 切换布局时重建 `router-view` 不影响功能，视觉上无感知。 |

---

### MED-13：`ExifService` 中 `MagickImageInfo` 未释放

| 条目 | 内容 |
|------|------|
| **判定** | ❌ **误报，不修复** |
| **评估** | 该版本 Magick.NET 的 `MagickImageInfo` 不实现 `IDisposable`，编译器已确认。无需 `using`。 |

---

## 🔵 低级别（LOW）

全部 10 条 LOW 均为代码异味或功能缺口，私有部署无安全或性能影响，**均不修复**。

简要评估：
- **LOW-1** EXIF 日期解析 → 兼容标准 EXIF 格式，非标文件即使解析失败也不影响使用
- **LOW-2** CreatedAt 被当更新时间用 → 不影响任何功能逻辑
- **LOW-3** 默认值不一致 → `SettingService` 是实际生效的配置源，`ThumbnailOptions` 仅作静态类型定义
- **LOW-4** KeepAlive 缓存 → 有 `onActivated` 可用，当前无数据过期导致的 bug
- **LOW-5** 忙等 → 有超时保护（最长 10s），功能正确
- **LOW-6** 缺少 `CreatedAt` → 不影响现有标签功能
- **LOW-7** 软删除验证 → 查询已带有 `!sp.IsDeleted` 过滤器，逻辑正确
- **LOW-8** 路由可见性 → 后端有 `[AdminOnly]` 保护，前端路由仅为 UX
- **LOW-9** 每次创建新 scope → 正确模式，singleton 服务需要独立 scope
- **LOW-10** `IgnoreQueryFilters` → 不影响功能

## ⚪ 信息性（INFO）

全部 4 条 INFO 为架构建议，**均不修复**。

---

## 修复汇总

| 编号 | 判定 | 状态 |
|------|------|------|
| CRIT-1 | 真实漏洞 | ✅ 已修复 |
| CRIT-2 | 部分真实（日志泄露） | ✅ 已修复 |
| CRIT-3 | 代码错误（无行为差异） | ✅ 已删除死代码 |
| CRIT-4 | 部分真实（异常处理） | ✅ 已修复 3 处 |
| HIGH-1 | 误解 | ❌ 不修复 |
| HIGH-2 | 技术正确/场景不适用 | ❌ 不修复 |
| HIGH-3 | CRIT-1 复述 | ❌ 不修复 |
| HIGH-4 | 真实逻辑缺陷 | ✅ 已修复 |
| HIGH-5 | 性能问题 | ❌ 不修复 |
| MED-1 | 内存泄漏 | ✅ 已修复 |
| MED-2 | 事件顺序丢失 | ✅ 已修复 |
| MED-3 | 算法优化 | ❌ 不修复 |
| MED-4 | 多实例同步 | ❌ 不修复 |
| MED-5 | 计数器争用 | ❌ 不修复 |
| MED-6 | 重复解析 | ❌ 不修复 |
| MED-7 | 路径泄露 | ❌ 不修复 |
| MED-8 | HTTP 状态码 | ❌ 不修复 |
| MED-9 | 缺少 CancellationToken | ✅ 已修复 |
| MED-10 | 线程安全（误报） | ❌ 不修复 |
| MED-11 | 轮询未清理 | ✅ 已修复 |
| MED-12 | 布局重建 | ❌ 不修复 |
| MED-13 | 资源未释放（误报） | ❌ 不修复 |
| LOW-1~10 | 代码异味 | ❌ 不修复 |
| INFO-1~4 | 架构建议 | ❌ 不修复 |
