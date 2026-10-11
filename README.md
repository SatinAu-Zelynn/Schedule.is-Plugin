<div align="center">

<img src="logo.png" width="120" height="120" align="center">

# Schedule.is 插件端

### ClassIsland 课表同步与授时服务插件

<p>
  <a href="https://github.com/SatinAu-Zelynn/Schedule.is-Extension">🔗 前往配套浏览器扩展端仓库</a>
</p>

</div>

## 项目简介

**Schedule.is 插件端** 是专为 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 开发的轻量级后台服务插件。

本插件在 ClassIsland 运行时于本地（`127.0.0.1:23850`）建立低延迟广播服务，实时将当前的课表状态、上课/课间科目及倒计时数据输出，供浏览器端 [Schedule.is 扩展端](https://github.com/SatinAu-Zelynn/Schedule.is-Extension) 在 `time.is/clock` 页面中同步大屏渲染。

## 功能特性

- **零配置后台常驻**：随 ClassIsland 启动自动激活本地轻量 HTTP 广播，无感极低资源占用
- **精准状态提取**：无缝对接 ClassIsland 核心服务，智能识别「上课中 / 课间休息 / 准备上课 / 放学空闲」
- **秒级倒计时推算**：自动根据当前状态计算距离下课或上课的剩余时间
- **跨域安全支持**：自带 CORS 规范响应头，方便外部浏览器环境调用

## 配套组件

> ⚠️ 本插件需搭配浏览器扩展使用，以实现大屏完整联动效果：
> - **网页端配套扩展**：[Schedule.is-Extension](https://github.com/SatinAu-Zelynn/Schedule.is-Extension)

## 安装与使用

### 方式一：从官方商店安装（推荐）

暂未发布

### 方式二：从本地 CIPX 安装

1. 前往 [Releases](https://github.com/SatinAu-Zelynn/Schedule.is-Plugin/releases) 下载最新版本的 `ScheduleIsPlugin.cipx` 插件包。
2. 打开 ClassIsland 软件，依次点击 **设置** -> **插件**。
3. 点击页面右上角的 **「更多选项（三个点）」**，选择 **从本地安装**。
4. 选取下载好的 `.cipx` 文件，确认安装后**重启 ClassIsland** 即可。

<div align="right">
<table><td>
<a href="#scheduleis-插件端">👆 返回顶部</a>
</td></table>
</div>