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

## 🔵 低级别 & ℹ️ 信息性（等待评估）

MED / LOW / INFO 级别尚未逐条核实回应。初步判断：

| 级别 | 总数 | 初步评估 |
|------|------|----------|
| 🟡 MED | 13 条 | 部分为真实问题（MED-10 DbContext 线程安全、MED-1 信号量泄漏），部分影响有限 |
| 🔵 LOW | 10 条 | 多为代码异味，私有部署无实际影响 |
| ⚪ INFO | 4 条 | 架构性建议 |

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
