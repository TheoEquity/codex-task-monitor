# 规范 GitHub 仓库地址设计

## 目标

将整个 Codex Task Monitor 项目明确关联到同一个 GitHub 仓库：

- 对外仓库页面：`https://github.com/TheoEquity/codex-task-monitor`
- Git 拉取和推送地址：`https://github.com/TheoEquity/codex-task-monitor.git`

用户、构建产物、Windows 安装包和开发工具都应能从各自的标准位置找到该仓库。完成修改和验证后，将当前本地 `main` 的全部提交正常推送到该仓库的 `origin/main`，不使用强制推送。

## 当前状态

- 本地 `origin` 的 fetch 和 push 地址已经是规范 Git 地址。
- 本地 `main` 当前领先 `origin/main`，尚未把完整 Windows 11 适配和后续修复推送到 GitHub。
- README 没有醒目的仓库、问题反馈或发布页面链接。
- Windows `.csproj` 没有 `RepositoryUrl`、`RepositoryType` 或 `PackageProjectUrl`。
- Inno Setup 安装包没有发布者、支持或更新网址。
- 现有包装验证不会阻止这些地址缺失或漂移到别的仓库。

## 地址模型

项目只维护一个规范仓库根地址，并按使用场景派生标准链接：

| 用途 | 地址 |
| --- | --- |
| 浏览仓库 / 项目主页 | `https://github.com/TheoEquity/codex-task-monitor` |
| Git fetch / push / RepositoryUrl | `https://github.com/TheoEquity/codex-task-monitor.git` |
| 问题反馈 | `https://github.com/TheoEquity/codex-task-monitor/issues` |
| 版本与下载 | `https://github.com/TheoEquity/codex-task-monitor/releases` |

`.git` 后缀只用于 Git 远端和构建元数据中的克隆地址；用户可见链接不带 `.git`。

## 项目关联位置

### Git

`origin` 的 fetch 和 push 地址必须精确等于规范 Git 地址。实施时先只读核对；只有发生漂移时才用 `git remote set-url origin ...` 修正。最终推送使用普通 `git push origin main`，禁止 `--force` 和历史重写。

### README

在根 README 的项目介绍附近增加“仓库”链接，并在 Windows/macOS 共用的文档层提供问题反馈和版本下载链接。README 是两个平台共同的对外入口，不在每个历史设计文档中重复插入链接。

### .NET 项目

新增 `windows/Directory.Build.props`，让 `windows/` 下的 Core、Windows 应用和测试项目共享以下标准 MSBuild 元数据：

- `RepositoryType=git`
- `RepositoryUrl=https://github.com/TheoEquity/codex-task-monitor.git`
- `PackageProjectUrl=https://github.com/TheoEquity/codex-task-monitor`

使用共享 props 而不是在三个 `.csproj` 中复制，避免以后只更新其中一个项目。

### Windows 安装包

在 Inno Setup 脚本中定义一个仓库根 URL 常量，并设置：

- `AppPublisherURL` 指向仓库根页面；
- `AppSupportURL` 指向该仓库的 Issues；
- `AppUpdatesURL` 指向该仓库的 Releases。

这些字段会进入 Windows“已安装的应用”元数据，便于用户找到源码、反馈问题和下载版本。

### 自动校验

扩展 `windows/Scripts/verify_windows_packaging.ps1`，以结构化 XML 读取 `windows/Directory.Build.props`，并校验 README 与 Inno Setup 中的规范链接。CI 继续运行现有包装验证，因此任何地址缺失、拼写错误或改到其他仓库都会使构建失败。

校验脚本不修改 Git 远端，也不在 CI 中假定 checkout 的本地 remote 配置；本机推送前单独验证 `remote.origin.url`。

## 推送流程

1. 在隔离分支中按 TDD 添加仓库元数据验证，再实现 README、MSBuild 和安装包链接。
2. 运行聚焦验证、完整 Release 测试、warnings-as-errors 构建和包装验证。
3. 因安装包元数据发生变化，重新生成一次单文件应用和安装包，记录新 SHA-256，并静默升级本机安装。
4. 将隔离分支本地合并回 `main`，在合并结果上重新验证。
5. 确认 `origin` 精确指向规范 Git 地址，工作树干净，且远端没有需要先整合的新提交。
6. 使用普通 `git push origin main` 推送全部本地提交；不创建另一个仓库、不强推、不删除远端分支。
7. 推送后核对本地 `main` 与 `origin/main` 指向同一提交。

## 测试与验收

- RED：包装验证在缺少共享 MSBuild 仓库元数据时失败。
- GREEN：验证脚本确认共享 props、README 和 Inno Setup 的所有链接来自规范仓库。
- 运行完整 Windows Release 测试和 warnings-as-errors 构建。
- 发布目录仍只包含单文件应用；安装包仍为 per-user、未签名，其他安装行为不变。
- 包装验证以新安装包的精确 SHA-256 通过。
- 静默升级后已安装程序、HKCU Run 值和单实例存活检查通过。
- 推送后 `git rev-parse main` 与 `git rev-parse origin/main` 必须相同。

## 范围边界

- 不批量改写历史设计、实施计划或验收记录；它们是不可变的过程证据。
- 不向 Swift Package 添加不存在的主页字段；跨平台关联由 Git remote 和根 README 提供。
- 不更改 GitHub 仓库权限、可见性、默认分支、标签、Release 或 Actions secrets。
- 不创建 Pull Request，因为用户明确要求把当前完整 `main` 推送到该仓库。
- 不强制推送，不重写远端历史。
- 本变更不实现“随 Codex 启动”；该功能在仓库关联完成后继续单独设计。

## 验收标准

- 本地 Git、README、所有 Windows `.NET` 项目和安装包都能追溯到 `TheoEquity/codex-task-monitor`。
- CI 能阻止仓库地址缺失或漂移。
- 当前本地 `main` 的全部提交安全推送到规范 GitHub 仓库。
- 推送后本地与远端 `main` 完全同步，且没有使用强制推送。
